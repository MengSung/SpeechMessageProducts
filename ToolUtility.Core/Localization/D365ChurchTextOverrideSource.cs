using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Extensions;

namespace ToolUtilityNameSpace.Localization;

/// <summary>由 Host 提供經完整範圍驗證的操作 lease；不得從 caller 路由推測組織或跨教會重用帶狀態的 SDK。</summary>
public interface IChurchTextOverrideServiceFactory
{
    /// <summary>同步取得一次有界 lease；Host 必須設定 SDK transport timeout，來源會在入場 I/O 排空後歸還。</summary>
    /// <param name="context">已授權的完整範圍；工廠需驗證實際連線所屬組織／產品。</param>
    /// <returns>本次操作獨佔的 lease；連線本身是否池化由工廠 owner 的隔離契約決定。</returns>
    /// <param name="cancellationToken">操作 owner 取消，也必須傳入 pool 等待，不能只取消後面的資料查詢。</param>
    IChurchTextOverrideServiceLease Open(TextContext context, CancellationToken cancellationToken);
}

/// <summary>一次讀取的借用邊界。Dispose 歸還 lease，不代表來源取得了外部共享連線的擁有權。</summary>
public interface IChurchTextOverrideServiceLease : IDisposable
{
    /// <summary>工廠驗證後的實際範圍；來源再次與請求核對，禁止錯組織、產品及教會。</summary>
    TextContext Context { get; }
    /// <summary>僅在 lease 內有效、已設定有界逾時的 SDK；不得由結果或快取保存。</summary>
    IOrganizationService Service { get; }
    /// <summary>讀取故障／取消或不確定狀態時標記不可重用；Host lease 必須在歸還前讓 pool 淘汰該 client。</summary>
    void MarkFaulted();
}

/// <summary>
/// new_localizedtext 的雙目標純 SDK 來源；僅保存 Host 擁有的工廠，不保存連線、請求或資料列。
/// 同步 SDK 使用既有完整分頁轉接，無 Task.Run、額外執行緒或無主背景工作；取消後仍等入場 I/O 返回。
/// </summary>
public sealed class D365ChurchTextOverrideSource : IChurchTextOverrideSource
{
    /// <summary>借用無 request 狀態的工廠，Host 必須讓其存活至所有 ReadAsync 排空。</summary>
    private readonly IChurchTextOverrideServiceFactory factory;

    /// <summary>指定唯一 lease 取得入口；本來源不建立或關閉 Host 的服務提供者與連線池。</summary>
    /// <param name="factory">Host 授權及管理的 lease 工廠。</param>
    public D365ChurchTextOverrideSource(IChurchTextOverrideServiceFactory factory) =>
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// QueryExpression 只選純文字及父教會，固定 account 條件與主鍵排序；委派既有完整頁／重複 ID／cookie 預算守門。
    /// 全部轉換完成才封裝唯讀候選；後頁失敗不發布前頁，finally 歸還 lease，原 SDK 例外交最終 Host 告警。
    /// </summary>
    /// <param name="context">已授權範圍，不因語系不同改查另一教會。</param>
    /// <param name="cancellationToken">操作 owner 取消，I/O 前後檢查，不能提前釋放仍入場的 SDK。</param>
    /// <returns>最多五千列且符合 A.6 schema 邊界的純值集合。</returns>
    public async Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = factory.Open(context, cancellationToken) ?? throw new InvalidOperationException("Override service lease is unavailable.");
        try
        {
            if (!context.HasSameScope(lease.Context))
                throw new InvalidOperationException("Override service lease does not match the authorized scope.");
            var query = new QueryExpression("new_localizedtext")
            {
                ColumnSet = new ColumnSet("new_accountid", "new_key", "new_culture", "new_text")
            };
            query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, context.ChurchId);
            query.AddOrder("new_localizedtextid", OrderType.Ascending);
            var rows = await lease.Service.RetrieveAllAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
            var candidate = new List<ChurchTextOverride>(rows.Count);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var account = row.GetAttributeValue<EntityReference>("new_accountid");
                if (account is null || account.Id != context.ChurchId || !string.Equals(account.LogicalName, "account", StringComparison.Ordinal))
                    throw new InvalidOperationException("Override row does not match the authorized church.");
                var key = RequireText(row, "new_key", 200);
                var culture = RequireText(row, "new_culture", 16);
                var text = RequireText(row, "new_text", 2000, allowEmpty: true);
                candidate.Add(new ChurchTextOverride(row.Id, context.ChurchId, new TextKey(key), culture, text));
            }
            return candidate.AsReadOnly();
        }
        catch
        {
            // 取消／逾時或候選異常不能證明 transport 狀態可重用；先標不可重用，再由 using 歸還。
            // 這是 cleanup 邊界不是終端通知邊界：保留原例外交給 cache／Host，共用層不能另建通知器。
            lease.MarkFaulted();
            throw;
        }
    }

    /// <summary>讀取 A.6 指定字串型別及長度，不以缺欄位默認空值，錯誤不回顯資源本文或教會識別。</summary>
    /// <param name="row">本次操作獨佔的 SDK 列。</param>
    /// <param name="attribute">固定 schema 欄位。</param>
    /// <param name="maximum">正式 schema 字元預算。</param>
    /// <param name="allowEmpty">只有文字本文允許明確空字串，缺欄位仍拒絕。</param>
    /// <returns>不保留 SDK 參照的不可變字串。</returns>
    private static string RequireText(Entity row, string attribute, int maximum, bool allowEmpty = false)
    {
        if (!row.Attributes.TryGetValue(attribute, out var value) || value is not string text
            || text.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
            throw new InvalidOperationException("Override row does not conform to the bounded schema.");
        return text;
    }
}
