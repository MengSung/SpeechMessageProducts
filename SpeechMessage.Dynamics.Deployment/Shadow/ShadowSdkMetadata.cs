/*
 * 檔案整體說明
 *
 * 目的與定位：影子 CLI 的操作區域 metadata adapter，將情境欄位對照當下正式
 * schema 後才建立 SDK 候選，避免以 JSON 型別、欄名或 logicalname+id 猜測。
 * 主要功能與流程：唯讀載入最多六十四實體，每表最多四千九十六欄，完整驗證
 * 正式主鍵及唯一欄位；ConvertFields 檢查存在性、Create／Update 可寫性與秘密
 * 欄位限制，再交 ShadowSdkValues 處理精度、選項及日期。明確型別 marker 只作
 * 結構解包，不授權實際 lookup；resolver 仍須由 CLI 的核定別名表提供。
 * 使用邊界與維護：SDK 及 caller token 皆借入，不取得連線或 timer 所有權。
 * 不寫入、不讀業務記錄或 User Secrets；候選只存活單次命令，無 static cache、
 * Session 或跨組織共享。同步 SDK 回應後再驗 caller 取消，不宣稱可中斷已送出呼叫。
 * 組織、測試身分、lookup 範圍及舊入口盤點由 caller 負責，metadata 不是授權。
 * 失敗使用固定訊息，真正 SDK 例外保留交外層共用告警，不回顯欄值或 schema 本文；
 * 維護時跑合成 metadata、隔離及容量測試與 UTF-8／CRLF 位元組檢查。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>只屬一個已核定命令的 schema 候選；不能被跨租戶快取或當作資料授權。</summary>
internal sealed class ShadowSdkMetadata
{
    private readonly Dictionary<string, EntityMetadata> entities = new(StringComparer.Ordinal);

    /// <summary>
    /// 載入固定實體集合，完整讀取成功才返回 adapter。重複請求名稱只查一次；
    /// 回應的主鍵與欄位身分須完整唯一，無缺值預設或跨實體後備。
    /// </summary>
    /// <param name="service">目前命令租約借出的 SDK，不在此 Dispose。</param>
    /// <param name="entityNames">已由情境 reader 驗證的實體名稱，最多六十四個。</param>
    /// <param name="cancellationToken">caller 取消，SDK 呼叫前後均檢查。</param>
    internal ShadowSdkMetadata(IOrganizationService service, IReadOnlyList<string> entityNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(entityNames);
        if (entityNames.Count is < 1 or > 64 || entityNames.Any(name => !SchemaName(name))) throw Invalid();
        foreach (var name in entityNames.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = service.Execute(new RetrieveEntityRequest
            {
                LogicalName = name, EntityFilters = EntityFilters.Attributes, RetrieveAsIfPublished = true
            }) as RetrieveEntityResponse;
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = response?.EntityMetadata;
            if (metadata is null || metadata.LogicalName != name || !SchemaName(metadata.PrimaryIdAttribute) ||
                metadata.Attributes is not { Length: > 0 and <= 4096 }) throw Invalid();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in metadata.Attributes)
                if (field is null || !SchemaName(field.LogicalName) || !fields.Add(field.LogicalName) ||
                    (field.EntityLogicalName is not null && field.EntityLogicalName != name)) throw Invalid();
            var primary = metadata.Attributes.SingleOrDefault(field => field.LogicalName == metadata.PrimaryIdAttribute);
            if (primary?.AttributeType != AttributeTypeCode.Uniqueidentifier) throw Invalid();
            entities.Add(name, metadata);
        }
    }

    /// <summary>讀取已核對的正式主鍵，不從實體名稱推導，不發布可變 metadata 參考。</summary>
    internal string PrimaryId(string entity) => GetEntity(entity).PrimaryIdAttribute;

    /// <summary>關聯查詢必須是真正 lookup 且正式 Targets 包含父實體；不能把任意 GUID 或字串欄當關聯。</summary>
    internal void ValidateRelatedLookup(string entity, string attribute, string parentEntity)
    {
        var metadata = GetEntity(entity);
        if (!SchemaName(attribute) || metadata.Attributes.SingleOrDefault(field => field.LogicalName == attribute)
            is not LookupAttributeMetadata { Targets: not null } lookup ||
            !lookup.Targets.Contains(parentEntity, StringComparer.Ordinal)) throw Invalid();
    }

    /// <summary>
    /// 驗證所有指定觀察欄位存在且唯一，不要求它們可寫；回傳新的欄位陣列。
    /// 只檢查 schema，欄位讀取及權限仍由受控 observe 路徑負責。
    /// </summary>
    internal IReadOnlyList<string> ValidateObservation(string entity, IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var metadata = GetEntity(entity);
        if (fields.Count is < 1 or > 128 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Count ||
            fields.Any(name => !SchemaName(name) || !metadata.Attributes.Any(field => field.LogicalName == name)))
            throw Invalid();
        return Array.AsReadOnly(fields.ToArray());
    }

    /// <summary>
    /// 將完整欄位集合解碼成 caller 獨占的新 Entity；主鍵、秘密欄位及非明確可寫欄位拒絕。
    /// resolver 僅解析核定別名，查詢或授權失敗保留原例外；部分候選不會被返回或寫入。
    /// </summary>
    internal Entity ConvertFields(string entity, JsonElement fields, bool create,
        Func<string, string, EntityReference> resolveAlias)
    {
        ArgumentNullException.ThrowIfNull(resolveAlias);
        var metadata = GetEntity(entity);
        if (fields.ValueKind != JsonValueKind.Object || fields.EnumerateObject().Count() > 128) throw Invalid();
        var candidate = new Entity(entity);
        foreach (var property in fields.EnumerateObject())
        {
            var name = property.Name;
            var attribute = metadata.Attributes.SingleOrDefault(field => field.LogicalName == name);
            if (!SchemaName(name) || candidate.Contains(name) || name == metadata.PrimaryIdAttribute ||
                name == "new_lineid" || name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                attribute is null || (create ? attribute.IsValidForCreate : attribute.IsValidForUpdate) != true)
                throw Invalid();
            candidate[name] = ShadowSdkValues.Convert(Unwrap(property.Value, attribute), attribute, resolveAlias);
        }
        return candidate;
    }

    /// <summary>解包 reader 已允許的明確型別 marker；錯型別與額外屬性仍拒絕，不默默刪欄。</summary>
    private static JsonElement Unwrap(JsonElement value, AttributeMetadata attribute)
    {
        if (value.ValueKind != JsonValueKind.Object) return value;
        if (value.TryGetProperty("reference", out var reference))
        {
            if (attribute is not LookupAttributeMetadata || value.EnumerateObject().Count() != 1) throw Invalid();
            return reference;
        }
        var marker = attribute is MoneyAttributeMetadata ? "money" : attribute is EnumAttributeMetadata ? "option" : null;
        if (marker is not null)
        {
            if (value.EnumerateObject().Count() != 1 || !value.TryGetProperty(marker, out var inner)) throw Invalid();
            return inner;
        }
        return value;
    }

    /// <summary>未知實體不重新查詢、不採其他表的欄位，維持單次載入邊界。</summary>
    private EntityMetadata GetEntity(string entity) => entities.TryGetValue(entity, out var metadata) ? metadata : throw Invalid();

    /// <summary>正式小寫 schema 識別字，拒絕點、路徑、空白與過長名稱。</summary>
    private static bool SchemaName(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    /// <summary>固定拒絕內容，不攜帶原始 JSON、欄位或 metadata 的敏感資料。</summary>
    private static InvalidDataException Invalid() => new("Shadow metadata precondition failed.");
}
