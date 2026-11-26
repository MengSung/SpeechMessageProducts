/*
檔案整體說明
目的與定位：F6好牧人2.0的scoped身分解析，使用固定產品與部署Messaging Channel，在goodshepherd20內逐次查Contact→租戶→名額→Channel。
主要功能與流程：完整有界讀取本人候選、驗權威ID與關聯且歸還SDK租約，再依唯一候選或SQL完整scope選擇查角色並發布actor。切換同樣重新讀本人候選才寫ID；無或失效選擇回null，不任取資料、不合併同名。
使用邊界與維護：輸入identity須先經官方驗證，Channel與產品不取自query。候選與查詢只在方法內；最後名稱快照只活於本次DI request scope，不能作跨請求快取。同步SDK入場後完整等待，取消在呼叫前後檢查，故障淘汰再歸還；清理失敗不能發布部分成功。SDK、SQL與角色來源由外部DI擁有，本類不關閉manager。F8前角色為空；所有真正例外交HTTP共用診斷，額外清理故障交既有共用入口。
*/
using System.Runtime.ExceptionServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Liff;

/// <summary>單次request的產品解析器；同scope不可並行重入，不保留token、連線或跨請求授權結果。</summary>
internal sealed class GoodShepherd2LiffActorResolver(
    IDataverseConnectionManager manager, ICurrentTenantSelectionStore selection,
    ILiffRoleResolver roles, LiffOptions options, IChurchClock clock) : ILiffActorResolver
{
    /// <summary>只屬本request最近一次完整解析的名稱與actor，重新解析前立即清除。</summary>
    private Resolved? resolved;
    /// <summary>限制同scoped實例只有一個解析或切換操作；不是跨程序資料鎖。</summary>
    private int executing;

    /// <summary>每次重新讀完整候選，唯一Contact直接用，多筆只有有效目前選擇才可發布；先完成清理及角色驗證。</summary>
    /// <param name="identity">由當次官方token驗證產生，不跨請求使用。</param>
    /// <param name="cancellationToken">caller取消，SDK不支援中途取消時必須確實排空才傳遞。</param>
    /// <returns>不可變actor，或沒有有效候選與選擇時null。</returns>
    public async Task<LiffActor?> ResolveAsync(LiffIdentity identity, CancellationToken cancellationToken)
    {
        Begin();
        try
        {
            if (!ValidateIdentity(identity, cancellationToken)) return null;
            var candidates = LoadCandidates(identity.UserId, cancellationToken);
            Candidate? chosen = candidates.Count == 1 ? candidates[0] : null;
            if (candidates.Count > 1)
            {
                var current = await selection.GetAsync(GoodShepherd2Product.Name, options.MessagingChannelKey,
                    identity.UserId, cancellationToken).ConfigureAwait(false);
                chosen = candidates.SingleOrDefault(c => c.ContactId == current);
            }
            if (chosen is null) return null;
            var roleCodes = await roles.ResolveRolesAsync(chosen.ContactId, chosen.TenantId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var actor = new LiffActor(chosen.ContactId, chosen.TenantId, roleCodes);
            resolved = new Resolved(actor, chosen.TenantName);
            return actor;
        }
        finally { Volatile.Write(ref executing, 0); }
    }

    /// <summary>切換前重新確認Contact屬於本人的固定Channel租戶；無權或失效只回false，SQL不寫入。</summary>
    /// <param name="identity">本次已驗token身分。</param>
    /// <param name="contactId">前端提出的候選ID，不是授權依據。</param>
    /// <param name="cancellationToken">原caller取消。</param>
    /// <returns>真正儲存完成才true；SDK／SQL故障保留真實例外。</returns>
    internal async Task<bool> SetSelectionAsync(LiffIdentity identity, Guid contactId, CancellationToken cancellationToken)
    {
        Begin();
        try
        {
            if (!ValidateIdentity(identity, cancellationToken) || contactId == Guid.Empty) return false;
            if (!LoadCandidates(identity.UserId, cancellationToken).Any(c => c.ContactId == contactId)) return false;
            await selection.SetAsync(GoodShepherd2Product.Name, options.MessagingChannelKey,
                identity.UserId, contactId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { Volatile.Write(ref executing, 0); }
    }

    /// <summary>只接受本scope最近一次完整解析的同一actor實例；名稱與身分同批發布，不依query重選租戶。</summary>
    /// <param name="actor">filter於本請求發布的同一actor。</param>
    /// <returns>同一候選的已驗租戶名稱。</returns>
    internal string GetTenantName(LiffActor actor) =>
        resolved is { } current && ReferenceEquals(actor, current.Actor)
            ? current.TenantName : throw new InvalidOperationException("LIFF request scope is unavailable.");

    /// <summary>先取得單一操作權，再清除舊snapshot；並行重入不得覆寫另一個操作的scope。</summary>
    private void Begin()
    {
        if (Interlocked.CompareExchange(ref executing, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent LIFF scope use is not supported.");
        resolved = null;
    }

    /// <summary>沒有部署映射、過期或非法身分均拒絕；不記錄或正規化本人識別。</summary>
    private bool ValidateIdentity(LiffIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        return options.IsConfigured && identity.ExpiresAt > clock.UtcNow &&
            !string.IsNullOrWhiteSpace(identity.UserId) && identity.UserId.Length <= 128 &&
            !identity.UserId.Any(char.IsControl);
    }

    /// <summary>完整joined查詢最多八頁、每頁128筆；候選驗證及SDK排空、租約歸還都成功後才發布。</summary>
    private List<Candidate> LoadCandidates(string lineUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IClientLease? lease = null;
        Exception? primary = null;
        var candidates = new List<Candidate>();
        try
        {
            lease = manager.Acquire(cancellationToken);
            var query = BuildQuery(lineUserId);
            var identities = new HashSet<Guid>();
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 1; page <= 8; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                query.PageInfo.PageNumber = page;
                var rows = lease.Service.RetrieveMultiple(query);
                cancellationToken.ThrowIfCancellationRequested();
                if (rows is null || rows.Entities.Count > 128)
                    throw new InvalidOperationException("LIFF candidate page is invalid.");
                foreach (var row in rows.Entities)
                {
                    if (row.LogicalName != "contact" || row.Id == Guid.Empty || !identities.Add(row.Id) ||
                        !string.Equals(row.GetAttributeValue<string>("new_lineid"), lineUserId, StringComparison.Ordinal))
                        throw new InvalidOperationException("LIFF candidate identity is invalid.");
                    var tenant = Alias<Guid>(row, "tenant.accountid");
                    var slot = Alias<Guid>(row, "slot.new_channelslotid");
                    var channel = Alias<Guid>(row, "channel.new_linechannelid");
                    if (tenant == Guid.Empty || slot == Guid.Empty || channel == Guid.Empty ||
                        !Lookup(row.GetAttributeValue<EntityReference>("parentcustomerid"), "account", tenant) ||
                        !Alias<bool>(row, "tenant.new_istenant") ||
                        !Lookup(Alias<EntityReference>(row, "tenant.new_channelslot"), "new_channelslot", slot) ||
                        !Lookup(Alias<EntityReference>(row, "slot.new_tenant"), "account", tenant) ||
                        !Lookup(Alias<EntityReference>(row, "slot.new_channel"), "new_linechannel", channel) ||
                        !string.Equals(Alias<string>(row, "channel.new_channelid"), options.MessagingChannelKey, StringComparison.Ordinal) ||
                        !string.Equals(Alias<string>(row, "channel.new_product"), GoodShepherd2Product.Name, StringComparison.Ordinal))
                        throw new InvalidOperationException("LIFF candidate scope is invalid.");
                    var name = Alias<string>(row, "tenant.name");
                    // 與whoami頁面的256字元界線一致，超界拒絕整批而不截斷權威名稱。
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
                        throw new InvalidOperationException("LIFF tenant display value is invalid.");
                    candidates.Add(new Candidate(row.Id, tenant, name));
                }
                if (!rows.MoreRecords) break;
                if (page == 8 || rows.Entities.Count == 0 || string.IsNullOrEmpty(rows.PagingCookie) ||
                    rows.PagingCookie.Length > 32768 || !cookies.Add(rows.PagingCookie))
                    throw new InvalidOperationException("LIFF candidate paging is invalid.");
                query.PageInfo.PagingCookie = rows.PagingCookie;
            }
        }
        catch (Exception exception)
        {
            primary = exception;
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception cleanup) { ExceptionReporting.Report(cleanup, "GoodShepherd2.Liff.FaultLease", CancellationToken.None); }
            }
        }
        finally
        {
            if (lease is not null)
            {
                try { lease.Dispose(); }
                catch (Exception cleanup)
                {
                    if (primary is null) primary = cleanup;
                    else ExceptionReporting.Report(cleanup, "GoodShepherd2.Liff.ReleaseLease", CancellationToken.None);
                }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return candidates;
    }

    /// <summary>只選取授權所需欄位，以固定產品及Channel限制三個inner join；不讀任何Channel密文。</summary>
    private QueryExpression BuildQuery(string lineUserId)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "new_lineid", "parentcustomerid"),
            PageInfo = new PagingInfo { Count = 128, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_lineid", ConditionOperator.Equal, lineUserId);
        query.AddOrder("contactid", OrderType.Ascending);
        var tenant = query.AddLink("account", "parentcustomerid", "accountid", JoinOperator.Inner);
        tenant.EntityAlias = "tenant";
        tenant.Columns = new ColumnSet("accountid", "name", "new_istenant", "new_channelslot");
        tenant.LinkCriteria.AddCondition("new_istenant", ConditionOperator.Equal, true);
        var slot = tenant.AddLink("new_channelslot", "new_channelslot", "new_channelslotid", JoinOperator.Inner);
        slot.EntityAlias = "slot";
        slot.Columns = new ColumnSet("new_channelslotid", "new_tenant", "new_channel");
        var channel = slot.AddLink("new_linechannel", "new_channel", "new_linechannelid", JoinOperator.Inner);
        channel.EntityAlias = "channel";
        channel.Columns = new ColumnSet("new_linechannelid", "new_product", "new_channelid");
        channel.LinkCriteria.AddCondition("new_product", ConditionOperator.Equal, GoodShepherd2Product.Name);
        channel.LinkCriteria.AddCondition("new_channelid", ConditionOperator.Equal, options.MessagingChannelKey);
        return query;
    }

    /// <summary>連接欄位必須為預期型別的非null值；缺欄位不能用default轉為授權成功。</summary>
    private static T Alias<T>(Entity row, string field)
    {
        if (row.Attributes.TryGetValue(field, out var value) && value is AliasedValue { Value: T typed })
            return typed;
        throw new InvalidOperationException("LIFF scope projection is incomplete.");
    }

    /// <summary>lookup同時驗logical name與權威ID，不接受只有顯示名稱或另一種實體。</summary>
    private static bool Lookup(EntityReference? reference, string entity, Guid id) =>
        reference is not null && reference.Id == id && string.Equals(reference.LogicalName, entity, StringComparison.Ordinal);

    /// <summary>操作內的已驗候選，沒有SDK物件或任何憑證參照。</summary>
    private sealed record Candidate(Guid ContactId, Guid TenantId, string TenantName);
    /// <summary>只供本request whoami的完整快照，沒有跨scope引用。</summary>
    private sealed record Resolved(LiffActor Actor, string TenantName);
}
