/*
檔案整體說明
目的與定位：F5 c124 非測試租戶拒絕案例的暫時旗標切換接縫，不屬於產品管理 API 或通用租戶管理工具。
主要功能與流程：先 WhoAmI 核對固定組織，再以正式 account 主鍵重讀本輪完整所有權與版本；
保存純值復原候選成功後，僅用 IfRowVersionMatches 更新 new_istesttenant 一欄，同步等 SDK 返回再驗回所有權及新版本。
輸入與信任邊界：固定組織必須由外層測試 catalog 提供；只允許 c124、canonical 本輪 marker 及非空正式 ID，
名稱、代碼或測試旗標單獨都不是授權。寫前委派必須由外層實作先落盤 flush；此接縫不接收 LINE userId、訊息或憑證。
資源與隔離：只借用外層有 transport timeout 的單輪 SDK，必須由外層先持 D365／LINE 鎖並在全部作業排空後關閉；
本檔不建立連線、快取、CTS 或背景 task，局部 Entity 只活到本次同步方法返回，不對呼叫端發布可變 Entity。
取消與失敗：SDK 送出前尊重取消；送出後一定同步排空並驗回，不以晚到取消隱藏已確認結果。
落盤失敗零寫入；CAS、未知提交與驗回故障保留原例外及外層清冊，不重送、不盲目補償或直接啟動 F12 清理。
驗證方式：合成 SDK 的錯組織／正式 ID／旗標、落盤、CAS、提交後逾時與取消故障先 RED 再 GREEN，
驗每次只有一個單欄 CAS 與寫前候選。此檔仍只是完整 Live 入口的離線準備，不代表已在 D365 切換或清理。
*/
using System;
using System.IO;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>只含本輪安全復原欄位的候選；不含顯示名稱、收件人或秘密。</summary>
/// <param name="TenantId">資料庫正式租戶主鍵。</param>
/// <param name="Code">僅允許本案 c124。</param>
/// <param name="Marker">本輪 canonical GUID。</param>
/// <param name="Previous">寫前測試旗標。</param>
/// <param name="Requested">要求的新測試旗標。</param>
/// <param name="RowVersion">寫前伺服器版本。</param>
internal sealed record F5LiveFlagWrite(Guid TenantId, string Code, string Marker, bool Previous, bool Requested, string RowVersion);

/// <summary>讀回後的純值確認，不保留 SDK Entity 或連線。</summary>
/// <param name="TenantId">確認的正式租戶主鍵。</param>
/// <param name="IsTest">讀回的測試旗標。</param>
/// <param name="RowVersion">讀回的伺服器版本。</param>
internal sealed record F5LiveTenantFlagState(Guid TenantId, bool IsTest, string RowVersion);

/// <summary>單輪借用 SDK 的 c124 旗標 CAS；外層唯一擁有連線、鎖、持久化清冊與共用診斷。</summary>
internal static class F5PushLiveTenantFlag
{
    /// <summary>每次讀取建立獨立欄位容器，SDK 若修改參數亦不污染下一次或其他租戶；只投影所有權必要欄位。</summary>
    private static ColumnSet Columns() => new("accountid", "name", "new_tenantcode", "new_istenant", "new_istesttenant");

    /// <summary>
    /// 先驗本輪所有權並保存候選，再以讀到的 RowVersion 切換與驗回；previous 與 requested 必須相反。
    /// beforeWrite 失敗直接保留原例外且不送寫入；SDK 一旦入場，直到返回及讀回結束才把控制權還給外層。
    /// </summary>
    internal static F5LiveTenantFlagState SetFlag(IOrganizationService service, Guid organizationId, Guid tenantId,
        string code, string marker, bool previous, bool requested, Action<F5LiveFlagWrite> beforeWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        if (organizationId == Guid.Empty || tenantId == Guid.Empty || code != "c124" || previous == requested ||
            !Guid.TryParseExact(marker, "N", out var parsed) || parsed == Guid.Empty || marker != parsed.ToString("N"))
            throw Rejected();
        cancellationToken.ThrowIfCancellationRequested();
        if (service.Execute(new WhoAmIRequest()) is not WhoAmIResponse who || who.OrganizationId != organizationId)
            throw Rejected();
        cancellationToken.ThrowIfCancellationRequested();
        var before = service.Retrieve("account", tenantId, Columns());
        var version = RequireOwned(before, tenantId, code, marker, previous);
        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite(new F5LiveFlagWrite(tenantId, code, marker, previous, requested, version));
        // 清冊可能已保存但 caller 同時取消：候選仍保留供核對，確定尚未送出的 SDK 不入場。
        cancellationToken.ThrowIfCancellationRequested();
        var update = new Entity("account", tenantId)
        {
            RowVersion = version,
            ["new_istesttenant"] = new OptionSetValue(requested ? 100000001 : 100000000)
        };
        if (service.Execute(new UpdateRequest
        {
            Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
        }) is not UpdateResponse) throw Rejected();
        // 已確認寫入後不再用 caller token 中斷驗回；SDK transport deadline 仍由外層 owner 控制。
        var after = service.Retrieve("account", tenantId, Columns());
        var nextVersion = RequireOwned(after, tenantId, code, marker, requested);
        if (nextVersion == version) throw Rejected();
        return new F5LiveTenantFlagState(tenantId, requested, nextVersion);
    }

    /// <summary>
    /// 正式 Entity ID、同列主鍵欄及本輪完整名稱都須一致；兩個旗標只接受明確 OptionSetValue。
    /// 版本必須由 SDK 列提供且有界，不以 caller 值補缺；確認結果只發布純值版本，原列不逸出。
    /// </summary>
    private static string RequireOwned(Entity? row, Guid id, string code, string marker, bool isTest)
    {
        if (row is null || row.LogicalName != "account" || row.Id != id ||
            !row.Attributes.TryGetValue("accountid", out var primary) || primary is not Guid key || key != id ||
            !row.Attributes.TryGetValue("name", out var name) || name is not string title || title != "測試F5-" + marker + "-" + code ||
            !row.Attributes.TryGetValue("new_tenantcode", out var tenantCode) || tenantCode is not string actualCode || actualCode != code ||
            !row.Attributes.TryGetValue("new_istenant", out var tenantFlag) || tenantFlag is not OptionSetValue tenant || tenant.Value != 100000001 ||
            !row.Attributes.TryGetValue("new_istesttenant", out var testFlag) || testFlag is not OptionSetValue test ||
            test.Value != (isTest ? 100000001 : 100000000) ||
            string.IsNullOrWhiteSpace(row.RowVersion) || row.RowVersion.Length > 64)
            throw Rejected();
        return row.RowVersion;
    }

    /// <summary>只提供固定安全訊息；真正 SDK／落盤故障仍由原例外交給外層同一共用診斷 owner。</summary>
    private static InvalidDataException Rejected() => new("F5 live tenant flag ownership or confirmation failed.");
}
