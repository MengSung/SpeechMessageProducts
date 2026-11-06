using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Extensions;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>只讀結構投影器；所有資料先在本次候選完成且驗證正式ID，才發布純值snapshot。借用單命令SDK，不接管或保留連線。</summary>
internal static class SchemaMetadata
{
    /// <summary>一次只讀完整metadata與註冊定義；SDK同步I/O前後驗caller取消，不建立未觀測Task或提前歸還正在入場的client。</summary>
    /// <param name="service">由命令lease擁有且有transport timeout的獨占服務。</param>
    /// <param name="cancellationToken">命令owner取消訊號。</param>
    /// <param name="retrieveAsIfPublished">true盤點含未發布候選；false核對已發布結構，避免apply順便發布其他變更或把未發布當完成。</param>
    /// <returns>最多4096實體／100000欄、各註冊集合5000列的純值完整快照。</returns>
    internal static SchemaSnapshot Read(IOrganizationService service, CancellationToken cancellationToken, bool retrieveAsIfPublished = true)
    {
        ArgumentNullException.ThrowIfNull(service);
        cancellationToken.ThrowIfCancellationRequested();
        var current = Execute<RetrieveCurrentOrganizationResponse>(service,
            new RetrieveCurrentOrganizationRequest { AccessType = EndpointAccessType.Default }, cancellationToken).Detail;
        if (current is null || current.OrganizationId == Guid.Empty
            || !string.Equals(current.UniqueName, "goodshepherdback", StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(current.OrganizationVersion, out _))
            throw new InvalidDataException("Organization metadata does not match the authorized deployment.");
        var organization = service.Retrieve("organization", current.OrganizationId, new ColumnSet("languagecode"));
        cancellationToken.ThrowIfCancellationRequested();
        if (organization is null || organization.Id != current.OrganizationId || organization.LogicalName != "organization"
            || !organization.Attributes.TryGetValue("languagecode", out var rawLanguage) || rawLanguage is not int language || language is <= 0 or > 65535)
            throw new InvalidDataException("Organization language metadata is incomplete.");
        var metadata = Execute<RetrieveAllEntitiesResponse>(service,
            new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.All, RetrieveAsIfPublished = retrieveAsIfPublished }, cancellationToken).EntityMetadata;
        if (metadata is null || metadata.Length is < 1 or > 4096)
            throw new InvalidDataException("Entity metadata is incomplete or exceeds its bounded budget.");
        var entities = new List<SchemaEntityState>(metadata.Length);
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var attributeCount = 0;
        foreach (var entity in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entity is null || !Identity(entity.MetadataId, entity.LogicalName, ids, names)
                || entity.Attributes is null || entity.Attributes.Length > 2048 || entity.Keys?.Length > 32)
                throw new InvalidDataException("Entity metadata identity or budget is invalid.");
            attributeCount = checked(attributeCount + entity.Attributes.Length);
            if (attributeCount > 100_000) throw new InvalidDataException("Attribute metadata exceeds its total budget.");
            var fieldIds = new HashSet<Guid>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<SchemaFieldState>(entity.Attributes.Length);
            foreach (var attribute in entity.Attributes)
            {
                if (attribute is null || !Identity(attribute.MetadataId, attribute.LogicalName, fieldIds, fieldNames))
                    throw new InvalidDataException("Attribute metadata identity is invalid.");
                fields.Add(ProjectField(attribute));
            }
            var keys = new List<SchemaKeyState>();
            var keyIds = new HashSet<Guid>();
            var keyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in entity.Keys ?? Array.Empty<EntityKeyMetadata>())
            {
                if (key is null || !Identity(key.MetadataId, key.LogicalName, keyIds, keyNames)
                    || key.KeyAttributes is null || key.KeyAttributes.Length is < 1 or > 16
                    || key.KeyAttributes.Distinct(StringComparer.Ordinal).Count() != key.KeyAttributes.Length
                    || key.KeyAttributes.Any(name => !fieldNames.Contains(name)))
                    throw new InvalidDataException("Alternate key metadata identity is invalid.");
                keys.Add(new SchemaKeyState(key.MetadataId!.Value, key.LogicalName,
                    Array.AsReadOnly(key.KeyAttributes.ToArray()), key.EntityKeyIndexStatus.ToString()));
            }
            entities.Add(new SchemaEntityState(entity.MetadataId!.Value, entity.LogicalName,
                entity.PrimaryIdAttribute ?? "", entity.PrimaryNameAttribute ?? "", entity.OwnershipType?.ToString() ?? "Unknown",
                entity.IsAuditEnabled?.Value, entity.IsOptimisticConcurrencyEnabled, entity.IsManaged,
                Array.AsReadOnly(fields.OrderBy(field => field.LogicalName, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(keys.OrderBy(key => key.LogicalName, StringComparer.Ordinal).ToArray())));
        }
        var solutions = ReadRows(service, "solution", new ColumnSet("solutionid", "uniquename", "ismanaged", "publisherid"), cancellationToken,
            query => query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, "SpeechMessageCore"))
            .Select(row => new SchemaSolutionState(row.Id, Text(row, "uniquename"), Required<bool>(row, "ismanaged"), Lookup(row, "publisherid"))).ToArray();
        if (solutions.Length > 1) throw new InvalidDataException("Solution identity is ambiguous.");
        var publishers = ReadRows(service, "publisher", new ColumnSet("publisherid", "uniquename", "customizationprefix"), cancellationToken)
            .Select(row => new SchemaPublisherState(row.Id, Text(row, "uniquename"), Text(row, "customizationprefix"))).ToArray();
        if (publishers.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count() != publishers.Length)
            throw new InvalidDataException("Publisher identity is ambiguous.");
        var components = solutions.Length == 0 ? Array.Empty<SchemaComponentState>()
            : ReadRows(service, "solutioncomponent", new ColumnSet("solutioncomponentid", "componenttype", "objectid"), cancellationToken,
                query => query.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutions[0].Id))
                .Select(row => new SchemaComponentState(row.Id, Required<OptionSetValue>(row, "componenttype").Value, Required<Guid>(row, "objectid"))).ToArray();
        if (components.Any(component => component.ObjectId == Guid.Empty)
            || components.Select(component => (component.Type, component.ObjectId)).Distinct().Count() != components.Length)
            throw new InvalidDataException("Solution component identity is ambiguous.");
        var filters = ReadRows(service, "sdkmessagefilter", new ColumnSet("sdkmessagefilterid", "sdkmessageid", "primaryobjecttypecode", "secondaryobjecttypecode"), cancellationToken)
            .Select(row => new SchemaFilterState(row.Id, Lookup(row, "sdkmessageid"), OptionalText(row, "primaryobjecttypecode"), OptionalText(row, "secondaryobjecttypecode"))).ToArray();
        var steps = ReadRows(service, "sdkmessageprocessingstep", new ColumnSet("sdkmessageprocessingstepid", "sdkmessageid", "sdkmessagefilterid", "eventhandler", "stage", "mode", "rank", "statecode", "filteringattributes"), cancellationToken)
            .Select(row => new SchemaStepState(row.Id, Lookup(row, "sdkmessageid"), Lookup(row, "sdkmessagefilterid", optional: true),
                Lookup(row, "eventhandler"), Required<OptionSetValue>(row, "stage").Value, Required<OptionSetValue>(row, "mode").Value,
                Required<int>(row, "rank"), Required<OptionSetValue>(row, "statecode").Value, OptionalText(row, "filteringattributes"))).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new SchemaSnapshot
        {
            OrganizationId = current.OrganizationId, Organization = current.UniqueName, Version = current.OrganizationVersion, LanguageCode = language,
            Entities = Array.AsReadOnly(entities.OrderBy(entity => entity.LogicalName, StringComparer.Ordinal).ToArray()),
            Solutions = Array.AsReadOnly(solutions), Publishers = Array.AsReadOnly(publishers.OrderBy(publisher => publisher.Name, StringComparer.Ordinal).ToArray()),
            Components = Array.AsReadOnly(components.OrderBy(component => component.Type).ThenBy(component => component.ObjectId).ToArray()),
            Filters = Array.AsReadOnly(filters.OrderBy(filter => filter.Id).ToArray()), Steps = Array.AsReadOnly(steps.OrderBy(step => step.Id).ToArray())
        };
    }

    /// <summary>只映射schema功能約束；未知型別保留其正式名稱供匯出，不能在plan中假裝等於任一支援型別。</summary>
    private static SchemaFieldState ProjectField(AttributeMetadata field)
    {
        var type = field switch
        {
            StringAttributeMetadata => "string", MemoAttributeMetadata => "memo", IntegerAttributeMetadata => "integer",
            LookupAttributeMetadata => "lookup", PicklistAttributeMetadata => "choice", DateTimeAttributeMetadata => "datetime",
            _ => field.AttributeType?.ToString() ?? "Unknown"
        };
        var targets = field is LookupAttributeMetadata lookup ? lookup.Targets ?? Array.Empty<string>() : Array.Empty<string>();
        var options = field is PicklistAttributeMetadata choice
            ? choice.OptionSet?.Options.Select(option => option.Value ?? throw new InvalidDataException("Option metadata has no stable identity.")).ToArray()
                ?? throw new InvalidDataException("Choice metadata is incomplete.") : Array.Empty<int>();
        // 這是完整組織metadata而非P1單目標lookup清單。D365 regardingobjectid合法含數百表，
        // 唯讀實測379個目標；上限沿用全組織4096表預算，仍拒重複／非法名稱，不能靜默刪掉多型目標。
        if (options.Length > 4096 || options.Distinct().Count() != options.Length || targets.Length > 4096
            || targets.Any(target => string.IsNullOrWhiteSpace(target) || target.Length > 128)
            || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new InvalidDataException("Attribute metadata contains duplicate identities or exceeds its budget.");
        return new SchemaFieldState(field.MetadataId!.Value, field.LogicalName, type,
            field is StringAttributeMetadata text ? text.MaxLength : field is MemoAttributeMetadata memo ? memo.MaxLength : null,
            field.RequiredLevel?.Value.ToString() ?? "Unknown", field.IsSecured,
            Array.AsReadOnly(targets.OrderBy(target => target, StringComparer.Ordinal).ToArray()),
            field is DateTimeAttributeMetadata date ? date.DateTimeBehavior?.Value ?? "Unknown" : "",
            field is DateTimeAttributeMetadata formatted ? formatted.Format?.ToString() ?? "Unknown" : "",
            Array.AsReadOnly(options.Order().ToArray()), field.IsManaged, field.IsAuditEnabled?.Value);
    }

    /// <summary>重用Core完整頁／正式ID/cookie守門，最多5000列；任何超量或後頁故障都不回傳部分metadata。</summary>
    private static List<Entity> ReadRows(IOrganizationService service, string table, ColumnSet columns,
        CancellationToken cancellationToken, Action<QueryExpression>? configure = null)
    {
        var query = new QueryExpression(table) { ColumnSet = columns };
        query.AddOrder(table + "id", OrderType.Ascending);
        configure?.Invoke(query);
        return service.RetrieveAllAsync(query, cancellationToken: cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>SDKmessage前後檢查取消與強型別回應，不把錯型別回應當空成功。</summary>
    private static T Execute<T>(IOrganizationService service, OrganizationRequest request, CancellationToken cancellationToken) where T : OrganizationResponse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = service.Execute(request);
        cancellationToken.ThrowIfCancellationRequested();
        return response as T ?? throw new InvalidDataException("Metadata message returned an unexpected response.");
    }

    /// <summary>正式metadata ID及邏輯身份都必須存在且在同一消費集合唯一；不同顯示名不是替代身份。</summary>
    private static bool Identity(Guid? id, string? name, HashSet<Guid> ids, HashSet<string> names) =>
        id is { } value && value != Guid.Empty && ids.Add(value) && !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && names.Add(name);
    /// <summary>固定欄位型別缺失即拒絕，不以SDK預設false/0掩蓋不完整回應。</summary>
    private static T Required<T>(Entity row, string field) => row.Attributes.TryGetValue(field, out var raw) && raw is T value
        ? value : throw new InvalidDataException("Metadata record has a missing or invalid required attribute.");
    /// <summary>必填metadata純文字有界，不回顯錯值。</summary>
    private static string Text(Entity row, string field)
    {
        var value = Required<string>(row, field);
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 256 ? value : throw new InvalidDataException("Metadata identity text is invalid.");
    }
    /// <summary>選填定義文字仍有限制，避免設定本文意外流入metadata快照。</summary>
    private static string OptionalText(Entity row, string field) => !row.Attributes.ContainsKey(field) ? ""
        : Required<string>(row, field) is { Length: <= 8192 } value ? value : throw new InvalidDataException("Metadata definition text exceeds its budget.");
    /// <summary>只複製lookup正式ID，不保存SDK EntityReference或DisplayName。</summary>
    private static Guid Lookup(Entity row, string field, bool optional = false)
    {
        if (optional && !row.Attributes.ContainsKey(field)) return Guid.Empty;
        var value = Required<EntityReference>(row, field).Id;
        return value != Guid.Empty ? value : throw new InvalidDataException("Metadata lookup identity is missing.");
    }
}
