/*
檔案整體說明
目的與定位：FS 的離線檢視表產生器，將分層 schema 中已核准的 SchemaView 轉為公用
savedquery 所需的 FetchXml、LayoutXml 與 QueryType 純值候選。
主要流程：先以實體／欄位白名單核對檢視，再固定產生 QueryType 0 的公用檢視；特殊
QueryType 只能由既有資料庫元件更新，不能由本檔新建。所有 XML 均由 XDocument 建立，
不拼接未驗證的 XML 標記。
信任邊界與生命週期：輸入是單次分層 parser 已驗證的純值，不包含業務資料、使用者輸入、
憑證或 SDK；輸出只存於呼叫端候選，沒有快取、背景工作或外部資源。
驗證方式：ViewDefinitionTests 驗證欄位白名單、FetchXml／LayoutXml 欄位及特殊檢視拒絕；
這些離線結果不代表任何組織的 savedquery 已建立、啟用或發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>一次產生的公用檢視 XML 候選，不保存 SDK 或組織可變狀態。</summary>
internal sealed record GeneratedView(int QueryType, string FetchXml, string LayoutXml);

/// <summary>由分層 schema 產生安全、可重現的公用檢視定義。</summary>
internal static class ViewDefinitions
{
    /// <summary>驗證每個實體最多一個預設公用檢視。</summary>
    internal static bool ValidateDefaults(IReadOnlyList<SchemaView> views)
    {
        ArgumentNullException.ThrowIfNull(views);
        var defaults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in views)
            if (view is null || view.IsDefault && !defaults.Add(view.Entity))
                throw new InvalidDataException("An entity has more than one default public view.");
        return true;
    }

    /// <summary>
    /// 產生 QueryType 0 的公用檢視；queryType 非零代表特殊檢視，只有既有 ID 更新路徑可用，
    /// 因此在產生新候選時直接拒絕。
    /// </summary>
    /// <param name="schema">已由 SchemaFiles.LoadForOrganization 驗證的組織 schema。</param>
    /// <param name="view">本次要產生的檢視純值。</param>
    /// <param name="queryType">D365 savedquery QueryType；新建只允許 0。</param>
    /// <param name="primaryId">正式 SDK metadata 的主鍵；純產生器測試未提供時使用標準邏輯主鍵名稱。</param>
    /// <param name="objectTypeCode">正式 SDK 物件類型；未提供的純 XML 模板省略，不硬編 account 代碼。</param>
    /// <returns>不含未驗證 XML 的純值檢視候選。</returns>
    internal static GeneratedView Generate(SchemaDefinition schema, SchemaView view, int queryType = 0,
        string? primaryId = null, int? objectTypeCode = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(view);
        if (queryType != 0)
            throw new InvalidDataException("Special savedquery types may only update an existing component.");

        var entity = schema.Entities.SingleOrDefault(candidate => candidate.LogicalName == view.Entity)
            ?? throw new InvalidDataException("View entity is not in the approved schema.");
        var fields = entity.Fields.Select(field => field.LogicalName).ToHashSet(StringComparer.Ordinal);
        if (view.Columns is null || view.Columns.Count == 0 ||
            view.Columns.Any(column => !fields.Contains(column)) ||
            view.Columns.Distinct(StringComparer.Ordinal).Count() != view.Columns.Count ||
            view.Order is null || view.Order.Any(column => !fields.Contains(column)) ||
            view.Filter is null || view.Filter.Any(condition => !fields.Contains(condition)))
            throw new InvalidDataException("View contains an unknown or invalid attribute.");

        var fetchEntity = new XElement("entity", new XAttribute("name", entity.LogicalName));
        foreach (var column in view.Columns)
            fetchEntity.Add(new XElement("attribute", new XAttribute("name", column)));
        if (view.Filter.Count != 0)
        {
            var filter = new XElement("filter", new XAttribute("type", "and"));
            foreach (var attribute in view.Filter)
                filter.Add(new XElement("condition", new XAttribute("attribute", attribute), new XAttribute("operator", "not-null")));
            fetchEntity.Add(filter);
        }
        foreach (var attribute in view.Order)
            fetchEntity.Add(new XElement("order", new XAttribute("attribute", attribute)));

        var fetch = new XDocument(new XElement("fetch", fetchEntity));
        var row = new XElement("row", new XAttribute("name", "result"), new XAttribute("id", primaryId ?? entity.LogicalName + "id"));
        foreach (var column in view.Columns)
            row.Add(new XElement("cell", new XAttribute("name", column), new XAttribute("width", "150")));
        var layout = new XDocument(new XElement("grid",
            new XAttribute("name", "resultset"),
            objectTypeCode is { } code ? new XAttribute("object", code) : null,
            new XAttribute("select", "1"),
            new XAttribute("preview", "1"),
            row));
        return new GeneratedView(
            0,
            fetch.ToString(SaveOptions.DisableFormatting),
            layout.ToString(SaveOptions.DisableFormatting));
    }
}
