/*
檔案整體說明
目的與定位：F5 雙目標固定組織／產品的租戶及會友驗證，不建立推播或讀取 Channel 金鑰。
主要功能與流程：WhoAmI 後沿 account→已配發名額→可服務 Channel 驗證範圍，完整名單分成
最多十組 IN 查詢並完整分頁。每筆返回列再驗租戶、啟用旗標及正式 contact ID，最後才回 true。
使用邊界與維護：資料只留在本次方法局部，最多 5000 個收件人／正式列、每頁 500 列，
分頁 cookie 有界且不得重複。沒有共用快取、Task.Run 或背景查詢；同步 I/O 前後核對取消，
Dispose 只清除借用 SDK，主機依 DataversePushBoundary 契約提供期限及排空，不能提前關閉 SDK。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.LineRecipients;

/// <summary>固定身分的借用 SDK adapter，不讀取秘密，不回落舊租戶 Channel 欄位。</summary>
public sealed class DataverseRecipientMembership : IRecipientMembership, IDisposable
{
    private readonly DataversePushBoundary boundary;
    private const int Yes = 100000001;

    /// <summary>借用已授權且有 transport deadline 的 SDK，立即驗證實際組織；不接管 Dispose。</summary>
    public DataverseRecipientMembership(IOrganizationService service, Guid organizationId, string product) =>
        boundary = new DataversePushBoundary(service, organizationId, product);

    /// <summary>完整核對權威名額鏈；已滿 Channel 仍可服務原租戶，但停用與退役不得使用。</summary>
    public Task<TenantPushFacts?> ReadTenantAsync(PushScope scope, CancellationToken cancellationToken) =>
        Task.FromResult(ReadTenant(scope, cancellationToken));

    /// <summary>只接受啟用租戶的明確「是」選項，null／否／非租戶均為 false。</summary>
    public Task<bool> IsTestTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var row = boundary.Read("account", tenantId, new ColumnSet("statecode", "new_istenant", "new_istesttenant"), cancellationToken);
        return Task.FromResult(DataversePushBoundary.Active(row)
            && row.GetAttributeValue<OptionSetValue>("new_istenant")?.Value == Yes
            && row.GetAttributeValue<OptionSetValue>("new_istesttenant")?.Value == Yes);
    }

    /// <summary>
    /// 先驗租戶 Channel，再逐批完整讀回；contact 同名或同 userId 但不同正式 ID 不被靜默刪掉。
    /// foundUsers 只是「是否存在合法會員」的布林集合，所有列仍先驗正式 ID 及範圍，沒有發布聯絡人清單。
    /// </summary>
    public Task<bool> AreAllMembersAsync(PushScope scope, IReadOnlyList<string> recipientUserIds, CancellationToken cancellationToken)
    {
        var users = CopyUserIds(recipientUserIds);
        if (ReadTenant(scope, cancellationToken) is null) return Task.FromResult(false);
        var rowIds = new HashSet<Guid>();
        var foundUsers = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < users.Length; offset += 500)
        {
            var batch = users.Skip(offset).Take(500).ToArray();
            var expected = new HashSet<string>(batch, StringComparer.Ordinal);
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("new_lineid", "parentcustomerid", "statecode"),
                PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
            };
            query.Criteria.AddCondition("parentcustomerid", ConditionOperator.Equal, scope.TenantId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("new_lineid", ConditionOperator.In, batch.Cast<object>().ToArray());
            query.AddOrder("contactid", OrderType.Ascending);
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                if (query.PageInfo.PageNumber > 20) throw new InvalidOperationException("Membership page bound exceeded.");
                var rows = boundary.Query(query, cancellationToken);
                if (rows.Entities.Count > 500) throw new InvalidOperationException("Membership page exceeded its declared size.");
                foreach (var row in rows.Entities)
                {
                    DataversePushBoundary.RequireIdentity(row, "contact", row.Id);
                    if (!rowIds.Add(row.Id) || rowIds.Count > 5000)
                        throw new InvalidOperationException("Membership row identity conflict or size limit.");
                    var user = row.GetAttributeValue<string>("new_lineid");
                    if (user is null || !expected.Contains(user))
                        throw new InvalidOperationException("Membership query returned an unexpected identity.");
                    if (!DataversePushBoundary.Active(row)
                        || DataversePushBoundary.Lookup(row, "parentcustomerid", "account") != scope.TenantId)
                        return Task.FromResult(false);
                    foundUsers.Add(user);
                }
                if (!rows.MoreRecords) break;
                if (rows.Entities.Count == 0 || string.IsNullOrEmpty(rows.PagingCookie)
                    || rows.PagingCookie.Length > 65536 || !cookies.Add(rows.PagingCookie))
                    throw new InvalidOperationException("Membership pagination did not make bounded progress.");
                query.PageInfo.PagingCookie = rows.PagingCookie;
                query.PageInfo.PageNumber++;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(foundUsers.Count == users.Length);
    }

    /// <summary>scope 顯示值不參與輸出；lookup 及產品任一不符就整個範圍拒絕。</summary>
    private TenantPushFacts? ReadTenant(PushScope scope, CancellationToken ct)
    {
        _ = boundary.GetService();
#if NETFRAMEWORK
        if (scope is null) throw new ArgumentNullException(nameof(scope));
#else
        ArgumentNullException.ThrowIfNull(scope);
#endif
        if (scope.TenantId == Guid.Empty || !string.Equals(scope.Product, boundary.Product, StringComparison.Ordinal)) return null;
        var account = boundary.Read("account", scope.TenantId,
            new ColumnSet("name", "new_logourl", "statecode", "new_istenant", "new_istesttenant", "new_channelslot"), ct);
        if (!DataversePushBoundary.Active(account) || account.GetAttributeValue<OptionSetValue>("new_istenant")?.Value != Yes) return null;
        var slotId = DataversePushBoundary.Lookup(account, "new_channelslot", "new_channelslot");
        if (slotId == Guid.Empty) return null;
        var slot = boundary.Read("new_channelslot", slotId, new ColumnSet("statecode", "new_tenant", "new_channel", "new_slotstatus"), ct);
        if (!DataversePushBoundary.Active(slot) || slot.GetAttributeValue<OptionSetValue>("new_slotstatus")?.Value != Yes
            || DataversePushBoundary.Lookup(slot, "new_tenant", "account") != scope.TenantId) return null;
        var channelId = DataversePushBoundary.Lookup(slot, "new_channel", "new_linechannel");
        if (channelId == Guid.Empty) return null;
        var channel = boundary.Read("new_linechannel", channelId,
            new ColumnSet("statecode", "new_product", "new_channelid", "new_channelstatus"), ct);
        if (!DataversePushBoundary.Active(channel)
            || !string.Equals(channel.GetAttributeValue<string>("new_product"), boundary.Product, StringComparison.Ordinal)
            || !string.Equals(channel.GetAttributeValue<string>("new_channelid"), scope.ChannelKey, StringComparison.Ordinal)
            || channel.GetAttributeValue<OptionSetValue>("new_channelstatus")?.Value is not (100000001 or 100000002)) return null;
        var name = account.GetAttributeValue<string>("name");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new InvalidOperationException("Authoritative tenant name is invalid.");
        return new TenantPushFacts(name, account.GetAttributeValue<string>("new_logourl"),
            account.GetAttributeValue<OptionSetValue>("new_istesttenant")?.Value == Yes);
    }

    /// <summary>獨立公開 adapter 入口也須防巨大、重複或非法 userId；格式有效不代表授權。</summary>
    private static string[] CopyUserIds(IReadOnlyList<string> users)
    {
#if NETFRAMEWORK
        if (users is null) throw new ArgumentNullException(nameof(users));
#else
        ArgumentNullException.ThrowIfNull(users);
#endif
        if (users.Count is < 1 or > 5000) throw new ArgumentException("A bounded recipient list is required.", nameof(users));
        var result = new string[users.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < result.Length; index++)
        {
            var user = users[index];
            if (user is null || user.Length != 33 || user[0] != 'U' || !unique.Add(user))
                throw new ArgumentException("Invalid or duplicate recipient identity.", nameof(users));
            for (var character = 1; character < user.Length; character++)
                if (user[character] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                    throw new ArgumentException("Invalid recipient identity.", nameof(users));
            result[index] = user;
        }
        return result;
    }

    /// <summary>解除借用參照，不釋放或關閉主機 SDK；主機仍須先排空所有同步呼叫。</summary>
    public void Dispose() => boundary.Dispose();
}
