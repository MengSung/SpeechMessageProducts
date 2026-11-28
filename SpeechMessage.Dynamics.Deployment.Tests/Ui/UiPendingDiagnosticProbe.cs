/*
檔案整體說明
目的與定位：FS 套用前的唯讀 UI 差異診斷投影，只供測試工具使用，不參與產品發布決策。
主要流程：借入正式 reader 完成的兩來源 scope 及本次 schema 實體白名單，先驗完整身份，
再將有差異的元件轉成固定格式；輸出正式管理 ID、白名單類型／實體、差異欄名及 XML 空值／等值布林。
保密與隔離：不輸出表單／FetchXML／LayoutXML／顯示名稱或其內容，也不輸出任何秘密與業務值。
SDK fault 僅回傳數字錯碼與固定關鍵字的存在布林，原始 Message／InnerFault／TraceText 不得輸出。
候選字串只活到單次呼叫結束，沒有 SDK、連線、writer、全域快取、檔案或背景工作所有權。
XML 比較每份上限一 MiB 字元，禁止 DTD／外部 resolver；解析器及 StringReader 由 helper 確定關閉。
失敗與驗證：未知實體、空身份、不完整兩來源或重複身份必須拒絕；輸出全部完成才回傳。
UiPendingDiagnosticProbeTests 使用合成敏感哨兵、身份故障及相同範圍反例驗證純值契約；
通過只代表診斷格式安全，不能當作實機已發布或允許略過計畫外變更檢查的證據。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>單次 scope 的純值安全摘要器；不保留來源、XML 或任何外部資源。</summary>
internal static class UiPendingDiagnosticProbe
{
    /// <summary>唯讀核對官方查詢轉換是否將固定平台實體範圍轉為 FetchXML 的數字型別，不輸出原始 XML。</summary>
    /// <param name="execute">本案 SDK，回應不可跨組織保留。</param>
    /// <returns>只有固定格式與型別布林的診斷結果。</returns>
    internal static string DescribeConvertedScope(Func<OrganizationRequest, OrganizationResponse> execute)
    {
        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "formxml"),
            PageInfo = new PagingInfo { Count = 256, PageNumber = 1 }
        };
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.In, "new_linechannel");
        query.AddOrder("formid", OrderType.Ascending);
        var response = execute(new QueryExpressionToFetchXmlRequest { Query = query }) as QueryExpressionToFetchXmlResponse;
        var text = response?.FetchXml;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64 * 1024)
            throw new InvalidDataException("Invalid query conversion diagnostic response.");
        using var source = new StringReader(text);
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024
        });
        var fetch = XElement.Load(reader);
        var condition = fetch.Descendants("condition").Single();
        var values = condition.Elements("value").Select(item => item.Value).ToArray();
        return "FS-CONVERSION numericScope=" + (values.Length == 1 &&
            int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code > 0);
    }

    /// <summary>將 SDK fault 投影成固定分類，最多掃描四 KiB 文字，不回傳任意訊息內容或巢狀資料。</summary>
    /// <param name="fault">本次 SDK 回應的 fault；只借用、不保存其引用。</param>
    /// <returns>數字錯碼與固定關鍵字布林，不含來源資料。</returns>
    internal static string DescribeFault(OrganizationServiceFault fault)
    {
        var message = fault.Message ?? "";
        if (message.Length > 4096) message = message[..4096];
        var words = new[] { "integer", "Int32", "String", "format", "order", "alias", "paging", "entity", "attribute", "query", "convert", "type" };
        return $"FS-FAULT code=0x{fault.ErrorCode:X8} " + string.Join(" ",
            words.Select(word => word + "=" + message.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>完整驗證後回傳差異欄位名稱；不回傳欄位值或部分候選。</summary>
    /// <param name="scope">同一次已／未發布完整盤點，不可跨組織混用。</param>
    /// <param name="allowedEntities">呼叫端由正式 schema 取得的精確實體白名單。</param>
    /// <returns>僅供診斷的固定字串集合，不含來源物件引用。</returns>
    internal static IReadOnlyList<string> Describe(UiViewScope scope, IReadOnlyCollection<string> allowedEntities)
    {
        if (scope.Published.Count > 8192 || scope.Unpublished.Count > 8192 ||
            allowedEntities.Count is < 1 or > 4096)
            throw new InvalidDataException("UI diagnostic scope exceeds its bound.");
        var allowed = allowedEntities.ToHashSet(StringComparer.Ordinal);
        var before = new Dictionary<(string Type, Guid Id), UiViewState>();
        var after = new HashSet<(string Type, Guid Id)>();
        foreach (var row in scope.Published.Concat(scope.Unpublished))
        {
            if (row.Id == Guid.Empty || row.Type is not ("systemform" or "savedquery") ||
                !allowed.Contains(row.Entity) || row.Entity.Length > 128 ||
                row.Entity.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
                throw new InvalidDataException("UI diagnostic identity is invalid.");
        }
        foreach (var row in scope.Published)
            if (!before.TryAdd((row.Type, row.Id), row))
                throw new InvalidDataException("UI diagnostic source repeats an identity.");
        foreach (var row in scope.Unpublished)
            if (!after.Add((row.Type, row.Id)))
                throw new InvalidDataException("UI diagnostic source repeats an identity.");
        // 沿用正式兩來源完整性與 hash 差異定義；摘要不能重新定義發布授權。
        var pending = UiViewDeployment.Pending(scope).ToHashSet();
        var result = new List<string>();
        foreach (var row in scope.Unpublished.Where(row => pending.Contains(row.Id))
                     .OrderBy(row => row.Type, StringComparer.Ordinal).ThenBy(row => row.Id))
        {
            var old = before[(row.Type, row.Id)];
            var fields = new List<string>();
            if (old.Name != row.Name) fields.Add("name");
            if (old.QueryType != row.QueryType) fields.Add("type");
            if (old.IsDefault != row.IsDefault) fields.Add("isdefault");
            if (old.State != row.State) fields.Add("state");
            if (old.Managed != row.Managed) fields.Add("ismanaged");
            if (old.FetchXml != row.FetchXml) fields.Add(row.Type == "systemform" ? "formxml" : "fetchxml");
            if (old.LayoutXml != row.LayoutXml) fields.Add("layoutxml");
            if (fields.Count == 0) fields.Add("hash");
            result.Add($"FS-PENDING type={row.Type} entity={row.Entity} id={row.Id:D} fields={string.Join(",", fields)} " +
                DescribeXmlShape(old.FetchXml, row.FetchXml));
        }
        return result.AsReadOnly();
    }

    /// <summary>只回報兩份 XML 的空值／解析／等值布林，不輸出內容；不改正式 pending 決策。</summary>
    /// <param name="before">已發布 XML，只活到本次有界解析結束。</param>
    /// <param name="after">未發布 XML，不從中解析或載入外部資源。</param>
    /// <returns>固定欄名與布林，不能作為發布授權。</returns>
    internal static string DescribeXmlShape(string before, string after)
    {
        var equal = false;
        if (before.Length is > 0 and <= 1_048_576 && after.Length is > 0 and <= 1_048_576)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true,
                    MaxCharactersInDocument = 1_048_576
                };
                using var oldText = new StringReader(before);
                using var newText = new StringReader(after);
                using var oldReader = XmlReader.Create(oldText, settings);
                using var newReader = XmlReader.Create(newText, settings);
                equal = XNode.DeepEquals(XElement.Load(oldReader), XElement.Load(newReader));
            }
            catch (XmlException)
            {
                // 非法 XML 是這個觀察器預期要辨識的輸入；只回報不可證實等值，不放行或輸出錯誤本文。
            }
        }
        return $"publishedEmpty={before.Length == 0} unpublishedEmpty={after.Length == 0} parsedEqual={equal}";
    }

    /// <summary>只列第一個結構差異的固定類別與白名單欄名，不輸出任意節點名／屬性值。</summary>
    /// <param name="before">已發布且不超過一 MiB 字元的 XML。</param>
    /// <param name="after">未發布 XML，拒絕外部實體與超量內容。</param>
    /// <returns>不含任意內容的診斷摘要，非發布許可。</returns>
    internal static string DescribeXmlStructure(string before, string after)
    {
        if (before.Length is < 1 or > 1_048_576 || after.Length is < 1 or > 1_048_576)
            return "kind=unavailable";
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true,
                MaxCharactersInDocument = 1_048_576
            };
            using var oldText = new StringReader(before);
            using var newText = new StringReader(after);
            using var oldReader = XmlReader.Create(oldText, settings);
            using var newReader = XmlReader.Create(newText, settings);
            var oldNodes = XElement.Load(oldReader).DescendantsAndSelf().ToArray();
            var newNodes = XElement.Load(newReader).DescendantsAndSelf().ToArray();
            if (oldNodes.Length != newNodes.Length)
            {
                var names = oldNodes.Concat(newNodes).Select(KnownElement).ToHashSet(StringComparer.Ordinal);
                var changes = names.Order(StringComparer.Ordinal).Select(name =>
                    (Name: name, Delta: newNodes.Count(node => KnownElement(node) == name) -
                        oldNodes.Count(node => KnownElement(node) == name))).Where(item => item.Delta != 0);
                return $"kind=element-count published={oldNodes.Length} unpublished={newNodes.Length} " +
                    $"changes={string.Join(",", changes.Select(item => item.Name + ":" + item.Delta))}";
            }
            for (var index = 0; index < oldNodes.Length; index++)
            {
                var old = oldNodes[index];
                var next = newNodes[index];
                if (old.Name != next.Name) return "kind=element-name";
                var attributes = old.Attributes().Select(item => item.Name)
                    .Union(next.Attributes().Select(item => item.Name))
                    .Where(name => (string?)old.Attribute(name) != (string?)next.Attribute(name)).ToArray();
                if (attributes.Length != 0)
                {
                    var element = old.Name.NamespaceName.Length == 0 &&
                        old.Name.LocalName is "form" or "control" or "cell" or "tab" or "row" or "section" or "label"
                        ? old.Name.LocalName : "other";
                    var names = attributes.Select(name => name.NamespaceName.Length == 0 &&
                        name.LocalName is "id" or "name" or "classid" or "controlid" or "datafieldname" or "value"
                            or "languagecode" or "visible" or "type" or "uniqueid"
                        ? name.LocalName : "other").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
                    return $"kind=attributes element={element} attributes={string.Join(",", names)}";
                }
                var oldTextNodes = string.Concat(old.Nodes().OfType<XText>().Select(node => node.Value));
                var newTextNodes = string.Concat(next.Nodes().OfType<XText>().Select(node => node.Value));
                if (oldTextNodes != newTextNodes) return "kind=text";
            }
            return "kind=other-or-equal";
        }
        catch (XmlException)
        {
            return "kind=unparseable";
        }
    }

    /// <summary>節點名稱只映射有限 FormXML 分類；自訂名稱及 namespace 一律不回顯。</summary>
    /// <param name="element">單次解析的節點，不保留引用。</param><returns>固定白名單值。</returns>
    private static string KnownElement(XElement element) => element.Name.NamespaceName.Length == 0 &&
        element.Name.LocalName is "form" or "tabs" or "tab" or "columns" or "column" or "sections" or "section"
            or "rows" or "row" or "cell" or "control" or "parameters" or "label" or "labels" or "events" or "event"
            or "Handlers" or "Handler" or "formLibraries" or "Library" or "Navigation" or "NavBar" or "NavBarItem"
            or "DisplayConditions" or "EntityRelationship" or "header" or "footer" or "hiddencontrols"
        ? element.Name.LocalName : "other";

    /// <summary>單一正式表單的全語系訊息對照；只讀一列且原 XML 僅回到本次診斷，不記錄內容。</summary>
    /// <param name="execute">借入目前租約的同步 SDK；不取得關閉所有權。</param>
    /// <param name="id">已由 reader 驗證的正式 formid。</param>
    /// <param name="ct">呼叫端合作取消，入場 SDK 返回後再釋放租約。</param>
    /// <returns>有界 XML 純值，呼叫端只比較內容，不輸出或跨案保存。</returns>
    internal static string ReadAllLabels(Func<OrganizationRequest, OrganizationResponse> execute, Guid id,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (id == Guid.Empty) throw new InvalidDataException("All-label diagnostic needs a formal form identity.");
        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formxml"),
            PageInfo = new PagingInfo { Count = 1, PageNumber = 1 }
        };
        query.Criteria.AddCondition("formid", ConditionOperator.Equal, id);
        query.AddOrder("formid", OrderType.Ascending);
        var request = new OrganizationRequest("RetrieveMultipleSystemFormsWithAllLabels");
        request.Parameters["Query"] = query;
        var response = execute(request);
        ct.ThrowIfCancellationRequested();
        if (!response.Results.TryGetValue("EntityCollection", out var value) ||
            value is not EntityCollection rows || rows.MoreRecords || rows.Entities.Count != 1)
            throw new InvalidDataException("All-label diagnostic response is incomplete.");
        var row = rows.Entities[0];
        if (row.Id != id || row.LogicalName != "systemform" ||
            row.GetAttributeValue<string>("formxml") is not { Length: > 0 and <= 1_048_576 } xml)
            throw new InvalidDataException("All-label diagnostic returned an invalid form.");
        return xml;
    }

    /// <summary>單筆未發布表單訊息對照；保留精確正式 ID 與一 MiB XML 上限，無寫入或資源所有權。</summary>
    /// <param name="execute">本次租約 SDK。</param><param name="id">正式表單 ID。</param>
    /// <param name="ct">本案取消。</param><returns>僅供本次比較的 XML，不得輸出內容。</returns>
    internal static string ReadUnpublishedSingle(Func<OrganizationRequest, OrganizationResponse> execute, Guid id,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (id == Guid.Empty) throw new InvalidDataException("Single-form diagnostic needs a formal identity.");
        var request = new OrganizationRequest("RetrieveUnpublished");
        request.Parameters["Target"] = new EntityReference("systemform", id);
        request.Parameters["ColumnSet"] = new ColumnSet("formxml");
        var response = execute(request);
        ct.ThrowIfCancellationRequested();
        if (!response.Results.TryGetValue("Entity", out var value) || value is not Entity row ||
            row.Id != id || row.LogicalName != "systemform" ||
            row.GetAttributeValue<string>("formxml") is not { Length: > 0 and <= 1_048_576 } xml)
            throw new InvalidDataException("Single-form diagnostic returned an invalid form.");
        return xml;
    }

    /// <summary>以正式 FetchXML alias 對照兩來源的原始屬性投影，單頁單表單，不能寫入或省略身份驗證。</summary>
    /// <param name="execute">本案租約的 SDK。</param><param name="id">正式 formid。</param>
    /// <param name="unpublished">是否使用正式未發布讀取訊息。</param>
    /// <param name="ct">本案合作取消。</param><returns>有界原始 XML 純值，只供本案等值比較。</returns>
    internal static string ReadAliasedForm(Func<OrganizationRequest, OrganizationResponse> execute, Guid id,
        bool unpublished, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (id == Guid.Empty) throw new InvalidDataException("Aliased-form diagnostic needs a formal identity.");
        var xml = new XElement("fetch", new XAttribute("count", 1), new XAttribute("page", 1),
            new XElement("entity", new XAttribute("name", "systemform"),
                new XElement("attribute", new XAttribute("name", "formid")),
                new XElement("attribute", new XAttribute("name", "formxml"), new XAttribute("alias", "rawformxml")),
                new XElement("order", new XAttribute("attribute", "formid")),
                new XElement("filter", new XElement("condition", new XAttribute("attribute", "formid"),
                    new XAttribute("operator", "eq"), new XAttribute("value", id.ToString("D"))))));
        var request = new OrganizationRequest(unpublished ? "RetrieveUnpublishedMultiple" : "RetrieveMultiple");
        request.Parameters["Query"] = new FetchExpression(xml.ToString(SaveOptions.DisableFormatting));
        var response = execute(request);
        ct.ThrowIfCancellationRequested();
        if (!response.Results.TryGetValue("EntityCollection", out var value) ||
            value is not EntityCollection rows || rows.MoreRecords || rows.Entities.Count != 1)
            throw new InvalidDataException("Aliased-form diagnostic response is incomplete.");
        var row = rows.Entities[0];
        if (row.Id != id || row.LogicalName != "systemform" ||
            !row.Attributes.TryGetValue("rawformxml", out var raw) ||
            raw is not AliasedValue alias || alias.EntityLogicalName != "systemform" ||
            alias.AttributeLogicalName != "formxml" || alias.Value is not string text ||
            text.Length is < 1 or > 1_048_576)
            throw new InvalidDataException("Aliased-form diagnostic returned an invalid form.");
        return text;
    }
}
