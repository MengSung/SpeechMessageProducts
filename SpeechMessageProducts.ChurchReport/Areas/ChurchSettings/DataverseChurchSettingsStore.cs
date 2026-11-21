/*
 * 檔案整體說明
 *
 * 目的與定位：將 GoodShepherd.Core 的教會設定儲存 port 接到目前 ChurchReport request 的 Dataverse service。
 * 主要流程：所有覆寫、奉獻類別與稽核查詢均先加 account lookup 條件；新增類別使用 schema 核定的
 * 教會＋代碼 alternate key；覆寫使用教會、穩定鍵、語系 alternate key；寫入後由核心服務更新文字版本。
 * 使用邊界與維護：IOrganizationService 由 ToolUtility request scope 擁有，本 adapter 僅借用且不 Dispose。
 * Entity 僅在單一方法內存在，輸出轉為核心純值；查詢固定最多 200 筆，不保存 request、Session、快取或連線。
 * 所有方法在進入 SDK 前檢查取消；Dataverse SDK 呼叫同步完成後才回傳，任何 SDK 例外原樣傳遞，不把失敗轉成空結果。
 * 失敗與驗證：教會識別只由已授權核心服務傳入；查詢結果若跨教會或缺正式 ID，交由核心服務拒絕整批發布。
 * 離線測試以合成 IOrganizationService 驗證 query 條件與映射，不宣稱任何實機稽核結果。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Settings;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>借用單一 ChurchReport request 的 Dataverse 服務實作教會設定儲存契約。</summary>
internal sealed class DataverseChurchSettingsStore : IChurchSettingsStore, IChurchSettingsTermReader
{
    private const int MaximumRows = 200;
    private const int MaximumAuditValueLength = 2000;
    private static readonly HashSet<string> AuditedFields = new(StringComparer.Ordinal)
    {
        "new_text",
        "new_name",
        "new_sortorder",
        "new_accountingcode",
        "new_legacyoptionvalue",
        "statecode"
    };
    private readonly IOrganizationService organizationService;

    /// <summary>接收 Host scope 所有的 Dataverse facade；adapter 不接管 facade 的釋放責任。</summary>
    /// <param name="organizationService">目前 request 的 CRM facade。</param>
    public DataverseChurchSettingsStore(IOrganizationService organizationService) =>
        this.organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));

    /// <inheritdoc />
    public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        var target = new Entity("new_localizedtext");
        target.KeyAttributes["new_accountid"] = new EntityReference("account", value.ChurchId);
        target.KeyAttributes["new_key"] = value.Key;
        target.KeyAttributes["new_culture"] = value.Culture;
        target["new_accountid"] = new EntityReference("account", value.ChurchId);
        target["new_key"] = value.Key;
        target["new_culture"] = value.Culture;
        target["new_name"] = value.Key + "|" + value.Culture;
        target["new_text"] = value.Text;
        organizationService.Execute(new UpsertRequest { Target = target });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("new_localizedtext")
        {
            ColumnSet = new ColumnSet("new_localizedtextid"),
            TopCount = 2
        };
        query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
        query.Criteria.AddCondition("new_key", ConditionOperator.Equal, key);
        query.Criteria.AddCondition("new_culture", ConditionOperator.Equal, culture);
        var rows = organizationService.RetrieveMultiple(query).Entities;
        if (rows.Count > 1) throw new InvalidOperationException("The church text alternate key is not unique.");
        if (rows.Count == 1) organizationService.Delete("new_localizedtext", rows[0].Id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changedOnUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(changedOnUtc));
        organizationService.Update(new Entity("account", churchId)
        {
            ["new_textchangedon"] = changedOnUtc
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("new_offeringcategory")
        {
            ColumnSet = new ColumnSet("new_accountid", "new_code", "new_name", "new_sortorder",
                "new_accountingcode", "new_legacyoptionvalue", "statecode"),
            TopCount = MaximumRows
        };
        query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
        query.AddOrder("new_sortorder", OrderType.Ascending);
        query.AddOrder("new_offeringcategoryid", OrderType.Ascending);
        var result = organizationService.RetrieveMultiple(query).Entities
            .Select(entity => new OfferingCategory(
                entity.Id,
                entity.GetAttributeValue<EntityReference>("new_accountid")?.Id ?? Guid.Empty,
                entity.GetAttributeValue<string>("new_code") ?? string.Empty,
                entity.GetAttributeValue<string>("new_name") ?? string.Empty,
                entity.GetAttributeValue<int?>("new_sortorder") ?? 0,
                entity.GetAttributeValue<string>("new_accountingcode"),
                entity.GetAttributeValue<int?>("new_legacyoptionvalue"),
                entity.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0))
            .ToArray();
        return Task.FromResult<IReadOnlyList<OfferingCategory>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChurchSettingsTermOverride>> ReadTermOverridesAsync(
        Guid churchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (churchId == Guid.Empty) throw new ArgumentException("A church identity is required.", nameof(churchId));
        var query = new QueryExpression("new_localizedtext")
        {
            ColumnSet = new ColumnSet(
                "new_localizedtextid", "new_accountid", "new_key", "new_culture", "new_text"),
            TopCount = MaximumRows
        };
        query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
        query.AddOrder("new_localizedtextid", OrderType.Ascending);

        var rows = organizationService.RetrieveMultiple(query).Entities;
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException("Dataverse returned more church text overrides than requested.");

        var identities = new HashSet<Guid>();
        var keys = new HashSet<(string Key, string Culture)>();
        var candidate = new List<ChurchSettingsTermOverride>(rows.Count);
        foreach (var row in rows)
        {
            var owner = row.GetAttributeValue<EntityReference>("new_accountid");
            var key = row.GetAttributeValue<string>("new_key");
            var culture = row.GetAttributeValue<string>("new_culture");
            var text = row.GetAttributeValue<string>("new_text");
            if (row.Id == Guid.Empty || !identities.Add(row.Id)
                || owner?.Id != churchId
                || !string.Equals(owner.LogicalName, "account", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(culture)
                || text is null
                || !keys.Add((key, culture)))
                throw new InvalidOperationException("Dataverse returned an invalid church-scoped text override.");
            candidate.Add(new ChurchSettingsTermOverride(row.Id, churchId, key, culture, text));
        }
        return Task.FromResult<IReadOnlyList<ChurchSettingsTermOverride>>(candidate.ToArray());
    }

    /// <inheritdoc />
    public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("new_offeringcategory")
        {
            ColumnSet = new ColumnSet("new_offeringcategoryid"),
            TopCount = 1
        };
        query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
        query.Criteria.AddCondition("new_code", ConditionOperator.Equal, code);
        return Task.FromResult(organizationService.RetrieveMultiple(query).Entities.Count > 0);
    }

    /// <inheritdoc />
    public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
        string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = new Entity("new_offeringcategory");
        target.KeyAttributes["new_accountid"] = new EntityReference("account", churchId);
        target.KeyAttributes["new_code"] = code;
        target["new_accountid"] = new EntityReference("account", churchId);
        target["new_code"] = code;
        target["new_name"] = name;
        target["new_sortorder"] = sortOrder;
        SetOptional(target, "new_accountingcode", accountingCode);
        SetOptional(target, "new_legacyoptionvalue", legacyOptionValue);
        var response = organizationService.Execute(new UpsertRequest { Target = target }) as UpsertResponse
            ?? throw new InvalidOperationException("Dataverse category upsert returned no UpsertResponse.");
        var id = response.Target?.Id ?? Guid.Empty;
        if (id == Guid.Empty)
            throw new InvalidOperationException("Dataverse category upsert returned no stable Target identity.");
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
        string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = new Entity("new_offeringcategory", id)
        {
            ["new_accountid"] = new EntityReference("account", churchId),
            ["new_name"] = name,
            ["new_sortorder"] = sortOrder,
            ["statecode"] = new OptionSetValue(isActive ? 0 : 1)
        };
        SetOptional(target, "new_accountingcode", accountingCode);
        SetOptional(target, "new_legacyoptionvalue", legacyOptionValue);
        organizationService.Update(target);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(
        Guid churchId, int maximum, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximum is < 1 or > MaximumRows) throw new ArgumentOutOfRangeException(nameof(maximum));

        var relatedRecordIds = ReadAuditedRecordIds(churchId, "new_localizedtext",
                "new_localizedtextid", cancellationToken)
            .Concat(ReadAuditedRecordIds(churchId, "new_offeringcategory",
                "new_offeringcategoryid", cancellationToken))
            .Distinct()
            .ToArray();
        if (relatedRecordIds.Length == 0)
            return Task.FromResult<IReadOnlyList<ChurchSettingsAuditRecord>>(
                Array.Empty<ChurchSettingsAuditRecord>());

        var query = new QueryExpression("audit")
        {
            ColumnSet = new ColumnSet("objectid", "createdon", "userid"),
            TopCount = maximum
        };
        query.Criteria.AddCondition("objectid", ConditionOperator.In,
            relatedRecordIds.Cast<object>().ToArray());
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("auditid", OrderType.Descending);

        var auditRows = organizationService.RetrieveMultiple(query).Entities;
        if (auditRows.Count > maximum)
            throw new InvalidOperationException("Dataverse returned more audit rows than requested.");

        var relatedIds = new HashSet<Guid>(relatedRecordIds);
        var auditIdentities = new HashSet<Guid>();
        var pending = new List<PendingAuditChange>();
        foreach (var auditRow in auditRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (auditRow.Id == Guid.Empty || !auditIdentities.Add(auditRow.Id))
                throw new InvalidOperationException("Dataverse returned a duplicate or empty audit identity.");

            var objectReference = auditRow.GetAttributeValue<EntityReference>("objectid");
            if (objectReference is null || !relatedIds.Contains(objectReference.Id))
                continue;
            var changedOn = auditRow.GetAttributeValue<DateTime?>("createdon");
            if (changedOn is null)
                throw new InvalidOperationException("Dataverse audit row has no change timestamp.");
            var changedOnUtc = changedOn.Value.Kind == DateTimeKind.Utc
                ? changedOn.Value
                : DateTime.SpecifyKind(changedOn.Value, DateTimeKind.Utc);
            var actor = auditRow.GetAttributeValue<EntityReference>("userid");
            var detailResponse = organizationService.Execute(
                new RetrieveAuditDetailsRequest { AuditId = auditRow.Id }) as RetrieveAuditDetailsResponse
                ?? throw new InvalidOperationException("Dataverse audit detail request returned an unexpected response.");
            if (detailResponse.AuditDetail is not AttributeAuditDetail attributeDetail)
                continue;

            foreach (var field in GetChangedAuditedFields(attributeDetail))
            {
                var oldValue = attributeDetail.OldValue?.Attributes.TryGetValue(field, out var oldCandidate) == true
                    ? FormatAuditValue(oldCandidate)
                    : string.Empty;
                var newValue = attributeDetail.NewValue?.Attributes.TryGetValue(field, out var newCandidate) == true
                    ? FormatAuditValue(newCandidate)
                    : string.Empty;
                if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    continue;

                pending.Add(new PendingAuditChange(
                    CreateAuditChangeId(auditRow.Id, field),
                    churchId,
                    objectReference.Id,
                    changedOnUtc,
                    field,
                    oldValue,
                    newValue,
                    actor?.Id ?? Guid.Empty));
            }
        }

        var selected = pending
            .OrderByDescending(change => change.ChangedOnUtc)
            .ThenBy(change => change.Id)
            .Take(maximum)
            .ToArray();
        var actorIds = selected.Select(change => change.ActorId).Where(id => id != Guid.Empty).Distinct().ToArray();
        var actorNames = ReadActorDisplayNames(actorIds, cancellationToken);
        var result = selected.Select(change => new ChurchSettingsAuditRecord(
                change.Id,
                change.ChurchId,
                change.TargetRecordId,
                change.ChangedOnUtc,
                change.Field,
                change.Before,
                change.After,
                actorNames.TryGetValue(change.ActorId, out var displayName) ? displayName : string.Empty))
            .ToArray();
        return Task.FromResult<IReadOnlyList<ChurchSettingsAuditRecord>>(result);
    }

    /// <summary>
    /// 先以 account 範圍查詢可稽核資料列的正式 ID，並再次驗證 SDK 回傳的 owner。
    /// 這兩階段邊界確保 Audit 的 objectid 篩選只可能引用本教會資料，不依賴畫面或 caller 傳入的 ID 清單。
    /// </summary>
    private IReadOnlyList<Guid> ReadAuditedRecordIds(
        Guid churchId, string entityName, string primaryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(primaryId, "new_accountid"),
            TopCount = MaximumRows
        };
        query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
        var rows = organizationService.RetrieveMultiple(query).Entities;
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException("Dataverse returned more church records than requested.");

        var identities = new HashSet<Guid>();
        var candidate = new List<Guid>(rows.Count);
        foreach (var row in rows)
        {
            var owner = row.GetAttributeValue<EntityReference>("new_accountid");
            if (row.Id == Guid.Empty || owner?.Id != churchId
                || !string.Equals(owner.LogicalName, "account", StringComparison.Ordinal)
                || !identities.Add(row.Id))
                throw new InvalidOperationException("Dataverse returned an invalid church-scoped audit source row.");
            candidate.Add(row.Id);
        }
        return candidate;
    }

    /// <summary>只將允許公開的欄位名稱投影成排序穩定的 audit detail 欄位集合。</summary>
    private static IReadOnlyList<string> GetChangedAuditedFields(AttributeAuditDetail detail)
    {
        var fields = (detail.OldValue?.Attributes.Keys ?? Enumerable.Empty<string>())
            .Concat(detail.NewValue?.Attributes.Keys ?? Enumerable.Empty<string>())
            .Where(AuditedFields.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
        return fields;
    }

    /// <summary>
    /// 將最多 200 筆候選所涉及的 actor 以單一有界查詢轉成顯示名稱，不輸出使用者 ID 或 Entity。
    /// </summary>
    private Dictionary<Guid, string> ReadActorDisplayNames(
        IReadOnlyList<Guid> actorIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        if (actorIds.Count == 0) return result;

        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("fullname"),
            TopCount = MaximumRows
        };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.In,
            actorIds.Cast<object>().ToArray());
        var rows = organizationService.RetrieveMultiple(query).Entities;
        if (rows.Count > MaximumRows)
            throw new InvalidOperationException("Dataverse returned more audit actors than requested.");
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || !result.TryAdd(
                    row.Id, row.GetAttributeValue<string>("fullname") ?? string.Empty))
                throw new InvalidOperationException("Dataverse returned a duplicate or empty audit actor identity.");
        }
        return result;
    }

    /// <summary>僅格式化稽核白名單欄位支援的純值型別；未知 SDK 值拒絕發布，避免洩露物件內容。</summary>
    private static string FormatAuditValue(object? value)
    {
        if (value is null) return string.Empty;
        var formatted = value switch
        {
            string text => text,
            OptionSetValue option => option.Value.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            long longInteger => longInteger.ToString(CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            DateTime dateTime => (dateTime.Kind == DateTimeKind.Utc
                ? dateTime
                : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Dataverse audit detail contains an unsupported field value.")
        };
        if (formatted.Length > MaximumAuditValueLength)
            throw new InvalidOperationException("Dataverse audit value exceeds the bounded display contract.");
        return formatted;
    }

    /// <summary>以不可變 Audit ID 與欄位名稱組成穩定的變更列 identity，涵蓋單一事件多欄位情境。</summary>
    private static Guid CreateAuditChangeId(Guid auditId, string field)
    {
        var fieldBytes = Encoding.UTF8.GetBytes(field);
        var input = new byte[16 + fieldBytes.Length];
        auditId.TryWriteBytes(input);
        fieldBytes.CopyTo(input, 16);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    private sealed record PendingAuditChange(
        Guid Id,
        Guid ChurchId,
        Guid TargetRecordId,
        DateTime ChangedOnUtc,
        string Field,
        string Before,
        string After,
        Guid ActorId);

    private static void SetOptional(Entity target, string attribute, object? value)
    {
        if (value is null) target.Attributes.Remove(attribute);
        else target[attribute] = value;
    }
}
