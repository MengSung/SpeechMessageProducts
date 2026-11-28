/*
檔案整體說明
目的與定位：部署工具的表單 XML 唯讀分頁邊界，讓已／未發布來源都取得原始完整屬性，
避免 D365 9.1 一般 formxml 投影依讀取訊息調整語系標籤而製造假的未發布差異。
主要流程：僅接受兩個 UI reader 的固定 systemform／objecttypecode 範圍、欄位與主鍵排序，
先由同組織官方轉換訊息把 EntityName 條件轉成正確數字型別，再以相同 FetchXML alias 讀兩來源；
每頁多一次有界唯讀轉換，不自行猜測 ObjectTypeCode，也不跨組織快取對應。
驗證正式 ID 與 AliasedValue 來源後複製回操作區域 formxml。
信任與隔離：不忽略、移除、排序或正規化任何表單 XML／label；原始文字仍由上層逐位元比較。
錯誤 alias、缺值、重複身份、超量或無效 UTF-8 一律拒絕，不回退到會受語系影響的一般投影。
生命週期：SDK 由命令 lease 擁有，本檔不 Dispose 它；query 不變動，頁面候選全部驗證後才回傳。
每頁最多256列、每欄一 MiB、頁面合計十六 MiB；完整跨頁／兩來源預算仍由兩個呼叫端累計。
合作取消在 SDK 前後及逐列檢查，沒有背景工作、快取、訂閱、檔案或跨組織保留狀態。
驗證方式：UiFormXmlReaderTests 重現真 InvalidDataException 的語系投影差異並保護真正 label 變更、
查詢範圍、cookie、alias 來源、取消與候選不發布；實機診斷另以 SQL 原始 XML 雜湊確認來源。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>只讀原始表單 XML 的操作區域適配器，保持正式發布護欄及 SDK 租約所有權不變。</summary>
internal static class UiFormXmlReader
{
    private const int MaximumXmlBytes = 1024 * 1024;
    private const int MaximumCookieBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] RequiredColumns =
        ["formid", "objecttypecode", "name", "type", "formactivationstate", "ismanaged", "formxml"];

    /// <summary>用相同 alias 契約讀一頁，全部候選驗證後才回傳，不做任何 XML 內容正規化。</summary>
    /// <param name="service">目前命令獨占且由外層釋放的 SDK。</param>
    /// <param name="query">固定欄位／scope／排序／頁面條件，不可改寫。</param>
    /// <param name="unpublished">只切換 SDK 的已／未發布訊息，查詢內容完全相同。</param>
    /// <param name="ct">命令 owner 合作取消。</param><returns>本次操作獨立擁有的完整頁面。</returns>
    internal static EntityCollection ReadPage(IOrganizationService service, QueryExpression query,
        bool unpublished, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        var scope = ValidateQuery(query);
        var request = ConvertQuery(service, query, scope.Length, ct);
        var source = unpublished
            ? (service.Execute(new RetrieveUnpublishedMultipleRequest { Query = request }) as RetrieveUnpublishedMultipleResponse)?.EntityCollection
            : service.RetrieveMultiple(request);
        ct.ThrowIfCancellationRequested();
        return CopyPage(source, query.PageInfo.Count, scope, ct);
    }

    /// <summary>
    /// 使用目前已核對組織的官方唯讀轉換，避免把 QueryExpression 的 EntityName 字串直接放進
    /// FetchXML 的整數 objecttypecode。轉換 XML 嚴限六十四 KiB，禁止 DTD／外部解析與多實體，
    /// 核對原欄位、單一範圍及唯一排序後僅加原始 XML alias；分頁及 cookie 依原 query 保真。
    /// </summary>
    private static FetchExpression ConvertQuery(IOrganizationService service, QueryExpression query,
        int scopeCount, CancellationToken ct)
    {
        var response = service.Execute(new QueryExpressionToFetchXmlRequest { Query = query }) as QueryExpressionToFetchXmlResponse;
        ct.ThrowIfCancellationRequested();
        var text = response?.FetchXml;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Missing raw systemform query conversion.");
        _ = Utf8Bytes(text, 64 * 1024);
        using var source = new StringReader(text);
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024
        });
        XElement fetch;
        try { fetch = XElement.Load(reader); }
        catch (XmlException) { throw new InvalidDataException("Invalid raw systemform query conversion XML."); }
        var entities = fetch.Elements("entity").ToArray();
        if (fetch.Name != "fetch" || entities.Length != 1 ||
            fetch.Elements().Count() != 1 || (string?)entities[0].Attribute("name") != "systemform")
            throw new InvalidDataException("Invalid converted systemform entity.");
        var entity = entities[0];
        var columns = entity.Elements("attribute").ToArray();
        var orders = entity.Elements("order").ToArray();
        var filters = entity.Elements("filter").ToArray();
        if (columns.Length != query.ColumnSet.Columns.Count || columns.Any(column => column.Attribute("alias") is not null) ||
            !columns.Select(column => (string?)column.Attribute("name")).Order(StringComparer.Ordinal)
                .SequenceEqual(query.ColumnSet.Columns.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            orders.Length != 1 || (string?)orders[0].Attribute("attribute") != "formid" ||
            (string?)orders[0].Attribute("descending") is not (null or "false") ||
            filters.Length != 1 || filters[0].Elements().Count() != 1 ||
            entity.Elements().Count() != columns.Length + 2)
            throw new InvalidDataException("Invalid converted systemform projection or ordering.");
        var condition = filters[0].Elements("condition").SingleOrDefault();
        var values = condition?.Elements("value").ToArray();
        if ((string?)condition?.Attribute("attribute") != "objecttypecode" ||
            (string?)condition?.Attribute("operator") != "in" || values is null || values.Length != scopeCount ||
            values.Any(value => !int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var code) || code <= 0))
            throw new InvalidDataException("Invalid converted systemform numeric scope.");
        columns.Single(column => (string?)column.Attribute("name") == "formxml").SetAttributeValue("alias", "rawformxml");
        fetch.SetAttributeValue("count", query.PageInfo.Count.ToString(CultureInfo.InvariantCulture));
        fetch.SetAttributeValue("page", query.PageInfo.PageNumber.ToString(CultureInfo.InvariantCulture));
        fetch.SetAttributeValue("paging-cookie", query.PageInfo.PagingCookie);
        return new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>
    /// 只允許兩個 reader 的固定表單欄位、單一實體範圍與正式主鍵排序；拒絕不能無損翻譯的查詢，
    /// 防止新呼叫端增加 filter／join／top 等條件卻被適配器靜默丟失。原 query 及 paging cookie 不變動。
    /// </summary>
    private static string[] ValidateQuery(QueryExpression query)
    {
        var columns = query.ColumnSet?.Columns;
        if (query.EntityName != "systemform" || query.ColumnSet is null || query.ColumnSet.AllColumns ||
            columns is null || RequiredColumns.Any(column => !columns.Contains(column)) ||
            columns.Any(column => !RequiredColumns.Contains(column, StringComparer.Ordinal) && column != "description") ||
            columns.Distinct(StringComparer.Ordinal).Count() != columns.Count ||
            query.LinkEntities.Count != 0 || query.TopCount is not null || query.Distinct ||
            query.Orders.Count != 1 || query.Orders[0].AttributeName != "formid" ||
            query.Orders[0].OrderType != OrderType.Ascending || !string.IsNullOrEmpty(query.Orders[0].EntityName) ||
            query.Criteria is null || query.Criteria.FilterOperator != LogicalOperator.And ||
            query.Criteria.Filters.Count != 0 || query.Criteria.Conditions.Count != 1 ||
            query.PageInfo is null || query.PageInfo.Count is < 1 or > 256 ||
            query.PageInfo.PageNumber is < 1 or > 16 || query.PageInfo.ReturnTotalRecordCount)
            throw new InvalidDataException("Unsupported raw systemform query.");
        CheckCookie(query.PageInfo.PagingCookie);
        var condition = query.Criteria.Conditions[0];
        if (condition.AttributeName != "objecttypecode" || condition.Operator != ConditionOperator.In ||
            !string.IsNullOrEmpty(condition.EntityName) || condition.Values.Count is < 1 or > 4096)
            throw new InvalidDataException("Invalid raw systemform entity scope.");
        var scope = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in condition.Values)
        {
            if (value is not string name || name.Length is < 1 or > 128 ||
                name[0] is < 'a' or > 'z' ||
                name.Any(character => character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_') ||
                !scope.Add(name))
                throw new InvalidDataException("Invalid or duplicate raw systemform entity scope.");
        }
        return scope.ToArray();
    }

    /// <summary>
    /// 驗證整頁的正式身份、範圍、alias 原始屬性來源與嚴格 UTF-8 預算後才發布候選。
    /// 原回應不修改；OptionSetValue 複製數值，沒有來源 Entity 字典或可變選項的共用引用。
    /// 一般 formxml 即使碰巧相同也不能混入 alias 回應，避免讀取邊界再次退回語系投影。
    /// </summary>
    private static EntityCollection CopyPage(EntityCollection? source, int pageSize, string[] scope, CancellationToken ct)
    {
        if (source is null || source.Entities.Count > pageSize ||
            !string.IsNullOrEmpty(source.EntityName) && source.EntityName != "systemform")
            throw new InvalidDataException("Invalid raw systemform page.");
        CheckCookie(source.PagingCookie);
        var result = new EntityCollection
        {
            EntityName = source.EntityName, MoreRecords = source.MoreRecords, PagingCookie = source.PagingCookie,
            TotalRecordCount = source.TotalRecordCount, TotalRecordCountLimitExceeded = source.TotalRecordCountLimitExceeded,
            MinActiveRowVersion = source.MinActiveRowVersion
        };
        var identities = new HashSet<Guid>();
        long totalBytes = 0;
        foreach (var row in source.Entities)
        {
            ct.ThrowIfCancellationRequested();
            if (row is null || row.LogicalName != "systemform" || row.Id == Guid.Empty || !identities.Add(row.Id) ||
                row.Attributes.TryGetValue("formid", out var primary) && (primary is not Guid id || id != row.Id) ||
                !row.Attributes.TryGetValue("objecttypecode", out var entity) || entity is not string name ||
                !scope.Contains(name, StringComparer.Ordinal) || row.Attributes.ContainsKey("formxml") ||
                !row.Attributes.TryGetValue("rawformxml", out var raw) || raw is not AliasedValue alias ||
                alias.EntityLogicalName != "systemform" || alias.AttributeLogicalName != "formxml" ||
                alias.Value is not string xml)
                throw new InvalidDataException("Invalid raw systemform identity, scope or alias.");
            var bytes = Utf8Bytes(xml, MaximumXmlBytes);
            totalBytes += bytes;
            if (totalBytes > 16L * MaximumXmlBytes)
                throw new InvalidDataException("Raw systemform page exceeds its XML budget.");
            var candidate = new Entity("systemform", row.Id);
            foreach (var pair in row.Attributes)
            {
                if (pair.Key == "rawformxml") continue;
                candidate[pair.Key] = pair.Value switch
                {
                    null or string or Guid or bool or int => pair.Value,
                    OptionSetValue option => new OptionSetValue(option.Value),
                    _ => throw new InvalidDataException("Invalid raw systemform attribute value.")
                };
            }
            candidate["formxml"] = xml;
            result.Entities.Add(candidate);
        }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>先限制字元數再驗嚴格 UTF-8 位元組，拒絕未配對 surrogate，且錯誤不含 XML 或 cookie 原文。</summary>
    private static int Utf8Bytes(string value, int maximum)
    {
        if (value.Length > maximum) throw new InvalidDataException("Raw systemform text exceeds its budget.");
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw new InvalidDataException("Invalid raw systemform text encoding."); }
        return bytes <= maximum ? bytes : throw new InvalidDataException("Raw systemform text exceeds its budget.");
    }

    /// <summary>請求與回應 cookie 都限十六 KiB；不解析、正規化或保存到下次操作。</summary>
    private static void CheckCookie(string? cookie)
    {
        if (cookie is not null) _ = Utf8Bytes(cookie, MaximumCookieBytes);
    }
}
