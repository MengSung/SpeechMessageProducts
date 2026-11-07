/*
 * 檔案整體說明
 *
 * 目的與定位：P2-03 的最小 Dataverse 寫入協定，接受 CLI 已驗證的公開識別及加密封包，
 * 借用當次 OrganizationLease 的服務；不讀組態、stdin、TokenKey 或明文，也不自行連線。
 * 主要功能與流程：protect-token 用 new_channelid 替代鍵執行原子 Upsert，只送名稱及兩個
 * 加密欄位；assign-channel 用相同替代鍵 Retrieve，核對真正資料庫身分後只更新 account lookup。
 * 不以顯示名稱去重、不先 Create 再補救、不掃全表，不把操作鍵或臨時 GUID 當正式資料身分。
 * 資源擁有權與隔離：本型別沒有欄位、快取、背景工作或可變共用狀態。服務由外層 lease
 * 擁有，SDK request/response 只活於當次呼叫；同步 SDK 已入場後必須返回才可釋放連線。
 * 呼叫端取消在每次 SDK I/O 前後檢查；寫入後取消不代表回復，不能因此重建或自動刪除資料。
 * 維護與驗證：SDK 故障原樣向上傳，由 CLI 釋放 lease／寫鎖後交共用診斷；不讀錯誤本文。
 * 離線測試保護精確 SDK payload、實際 ID 與故障路徑；索引唯一性及清理仍需獨立實機驗收。
 */
using System;
using System.IO;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Channels;

/// <summary>不保留狀態的 SDK 協定；連線與資料授權仍由部署組合根及服務端負責。</summary>
internal static class ChannelProvisioningService
{
    /// <summary>以正式替代鍵原子寫入兩個密文欄位；拒絕缺少正式身分的 SDK 回應。</summary>
    /// <param name="service">當次 lease 借用的服務，不能跨命令保存。</param>
    /// <param name="channelId">已驗證、至多二十位數的 LINE Channel ID。</param>
    /// <param name="tokenEnvelope">已驗證長度的存取權杖密文，禁止輸出或記錄。</param>
    /// <param name="secretEnvelope">已驗證長度的 Channel secret 密文。</param>
    /// <param name="cancellationToken">呼叫端取消；同步寫入須返回後才檢查。</param>
    internal static void Protect(IOrganizationService service, string channelId,
        string tokenEnvelope, string secretEnvelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = new Entity("new_linechannel", "new_channelid", channelId)
        {
            ["new_name"] = "LINE Channel " + channelId,
            ["new_accesstokenencrypted"] = tokenEnvelope,
            ["new_channelsecretencrypted"] = secretEnvelope
        };
        var response = (UpsertResponse)service.Execute(new UpsertRequest { Target = candidate });
        ValidateIdentity(response.Target);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>只取 Channel 的正式 ID，再將教會的單一 lookup 指向該記錄，不觸碰其他欄位。</summary>
    /// <param name="service">外層擁有、當次獨占的 SDK 服務。</param>
    /// <param name="accountId">已驗證非空的教會資料庫 ID。</param>
    /// <param name="channelId">已驗證的替代鍵，不是資料庫 GUID。</param>
    /// <param name="cancellationToken">呼叫端取消，在讀取與寫入之間另有停止點。</param>
    internal static void Assign(IOrganizationService service, Guid accountId,
        string channelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = (RetrieveResponse)service.Execute(new RetrieveRequest
        {
            Target = new EntityReference("new_linechannel", "new_channelid", channelId),
            ColumnSet = new ColumnSet(false)
        });
        var channel = response.Entity;
        if (channel is null)
            throw new InvalidDataException("Channel identity is invalid.");
        var identity = channel.ToEntityReference();
        ValidateIdentity(identity);
        cancellationToken.ThrowIfCancellationRequested();
        service.Update(new Entity("account", accountId) { ["new_linechannelid"] = identity });
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>禁止空 ID 或不同實體回應被當成成功；只使用固定錯誤，不輸出回應內容。</summary>
    /// <param name="identity">服務端回傳的正式身分，不能由 caller 合成。</param>
    private static void ValidateIdentity(EntityReference? identity)
    {
        if (identity is null || identity.Id == Guid.Empty ||
            !string.Equals(identity.LogicalName, "new_linechannel", StringComparison.Ordinal))
            throw new InvalidDataException("Channel identity is invalid.");
    }
}
