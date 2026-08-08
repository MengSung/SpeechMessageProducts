// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ContactProfileFixtureStores.cs
// 用途：P7.2 Slice B live evidence 的兩個 task-owned read/store adapter。
//       它們只持有單一 request scope 的 IOrganizationService，並在 Dispose
//       時 deterministic cleanup；不向產品層暴露 SDK 或任意 query API。
// ============================================================================

using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>只讀取及還原 B1 三個 LINE profile 欄位的 Data8 fixture store。</summary>
internal sealed class P72Data8ContactLineProfileFixtureStore : IP72ContactLineProfileFixtureStore
{
    private IOrganizationService? _service;

    /// <summary>建立單一 owner 的 fixture store。</summary>
    public P72Data8ContactLineProfileFixtureStore(IOrganizationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    public P72ContactLineProfileSnapshot Read(Guid contactId)
    {
        var service = _service ?? throw new ObjectDisposedException(nameof(P72Data8ContactLineProfileFixtureStore));
        var entity = service.Retrieve(
            "contact",
            contactId,
            new ColumnSet("new_line_picture_url", "new_line_status_message", "new_line_displayname"));
        if (entity is null || entity.LogicalName != "contact" || entity.Id != contactId)
        {
            throw new InvalidOperationException("The B1 contact read-back identity is invalid.");
        }

        return new P72ContactLineProfileSnapshot(
            ReadOptionalString(entity, "new_line_picture_url"),
            ReadOptionalString(entity, "new_line_status_message"),
            ReadOptionalString(entity, "new_line_displayname"));
    }

    /// <inheritdoc />
    public void Restore(Guid contactId, P72ContactLineProfileSnapshot baseline)
    {
        var service = _service ?? throw new ObjectDisposedException(nameof(P72Data8ContactLineProfileFixtureStore));
        ArgumentNullException.ThrowIfNull(baseline);
        var entity = new Entity("contact", contactId)
        {
            ["new_line_picture_url"] = baseline.PictureUrl,
            ["new_line_status_message"] = baseline.StatusMessage,
            ["new_line_displayname"] = baseline.DisplayName
        };
        service.Update(entity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var service = Interlocked.Exchange(ref _service, null);
        (service as IDisposable)?.Dispose();
    }

    /// <summary>將 SDK attribute 投影為有限的 string/null union。</summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string
            ?? throw new InvalidOperationException("The B1 contact profile attribute is invalid.");
    }
}

/// <summary>以固定 connector-owned 規則產生 B2 legacy parity projection。</summary>
internal sealed class P72Data8UngroupedCommitmentParityStore : IP72UngroupedCommitmentParityStore
{
    private const int MaximumListCount = 500;
    private const int MaximumGroupedContactCount = 4096;
    private const int MaximumPages = 8;
    private IOrganizationService? _service;

    /// <summary>建立單一 request-scoped legacy read owner。</summary>
    public P72Data8UngroupedCommitmentParityStore(IOrganizationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    public IReadOnlyList<UngroupedCommitmentCountDto> ReadLegacyCounts(string? search)
    {
        var service = _service ?? throw new ObjectDisposedException(nameof(P72Data8UngroupedCommitmentParityStore));
        var closedStatus = ResolveClosedStatus(service);
        var groupedIds = ReadGroupedContactIds(service, closedStatus);
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "customertypecode"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var current = new FilterExpression(LogicalOperator.Or);
        current.AddCondition("customertypecode", ConditionOperator.Null);
        current.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus);
        query.Criteria.Filters.Add(current);
        AddSearchFilter(query, search);
        if (groupedIds.Count > 0)
        {
            query.Criteria.AddCondition(
                "contactid",
                ConditionOperator.NotIn,
                groupedIds.Select(static id => (object)id).ToArray());
        }

        var rows = RetrieveAll(service, query);
        return rows
            .Select(static row => row.GetAttributeValue<OptionSetValue>("customertypecode")?.Value)
            .Where(static value => value.HasValue)
            .GroupBy(static value => value!.Value)
            .OrderBy(static group => group.Key)
            .Select(static group => new UngroupedCommitmentCountDto
            {
                Value = group.Key,
                Count = group.Count()
            })
            .ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var service = Interlocked.Exchange(ref _service, null);
        (service as IDisposable)?.Dispose();
    }

    /// <summary>以同一個 search category 建立 bounded legacy filter。</summary>
    private static void AddSearchFilter(QueryExpression query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return;
        }

        var value = search.Trim();
        if (value.Length > 256)
        {
            throw new InvalidOperationException("The B2 search exceeds its bound.");
        }

        var filter = new FilterExpression(LogicalOperator.Or);
        filter.AddCondition("fullname", ConditionOperator.Like, "%" + value + "%");
        filter.AddCondition("mobilephone", ConditionOperator.Like, "%" + value + "%");
        query.Criteria.Filters.Add(filter);
    }

    /// <summary>讀取唯一的「結案」OptionSet value；不猜測固定整數。</summary>
    private static int ResolveClosedStatus(IOrganizationService service)
    {
        var response = service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = "contact",
            LogicalName = "customertypecode",
            RetrieveAsIfPublished = true
        }) as RetrieveAttributeResponse;
        if (response?.AttributeMetadata is not PicklistAttributeMetadata picklist ||
            picklist.OptionSet?.Options is null)
        {
            throw new InvalidOperationException("The B2 commitment metadata is unavailable.");
        }

        var matches = picklist.OptionSet.Options
            .Where(static option => option?.Value is not null)
            .Where(static option =>
                option.Label?.LocalizedLabels?.Any(label =>
                    string.Equals(label.Label, "結案", StringComparison.OrdinalIgnoreCase)) == true)
            .Select(static option => option!.Value!.Value)
            .Distinct()
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("The B2 commitment metadata is ambiguous.");
    }

    /// <summary>讀取 active、app-named、小組名單的 membership contact IDs。</summary>
    private static IReadOnlySet<Guid> ReadGroupedContactIds(IOrganizationService service, int closedStatus)
    {
        var listQuery = new QueryExpression("list")
        {
            ColumnSet = new ColumnSet("listid"),
            PageInfo = new PagingInfo { Count = MaximumListCount, PageNumber = 1 }
        };
        listQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        listQuery.Criteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
        listQuery.Criteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
        var listIds = service.RetrieveMultiple(listQuery).Entities
            .Select(static entity => entity.Id)
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (listIds.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var membershipQuery = new QueryExpression("listmember")
        {
            ColumnSet = new ColumnSet("listid", "entityid"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        membershipQuery.Criteria.AddCondition(
            "listid",
            ConditionOperator.In,
            listIds.Select(static id => (object)id).ToArray());
        var contactLink = new LinkEntity("listmember", "contact", "entityid", "contactid", JoinOperator.Inner)
        {
            EntityAlias = "member",
            Columns = new ColumnSet(false)
        };
        contactLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var current = new FilterExpression(LogicalOperator.Or);
        current.AddCondition("customertypecode", ConditionOperator.Null);
        current.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus);
        contactLink.LinkCriteria.Filters.Add(current);
        membershipQuery.LinkEntities.Add(contactLink);

        var grouped = new HashSet<Guid>();
        foreach (var row in RetrieveAll(service, membershipQuery))
        {
            var id = row.GetAttributeValue<EntityReference>("entityid")?.Id ?? Guid.Empty;
            if (id != Guid.Empty)
            {
                grouped.Add(id);
                if (grouped.Count > MaximumGroupedContactCount)
                {
                    throw new InvalidOperationException("The B2 grouped contact scope exceeds its bound.");
                }
            }
        }

        return grouped;
    }

    /// <summary>以有限頁數讀取固定 projection，避免無界 CRM response retention。</summary>
    private static IReadOnlyList<Entity> RetrieveAll(IOrganizationService service, QueryExpression query)
    {
        var rows = new List<Entity>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            query.PageInfo.PageNumber = page;
            var response = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The B2 parity response is missing.");
            rows.AddRange(response.Entities);
            if (!response.MoreRecords)
            {
                return rows;
            }

            if (string.IsNullOrWhiteSpace(response.PagingCookie))
            {
                throw new InvalidOperationException("The B2 parity paging contract is incomplete.");
            }

            query.PageInfo.PagingCookie = response.PagingCookie;
        }

        throw new InvalidOperationException("The B2 parity paging contract exceeded its bound.");
    }
}
