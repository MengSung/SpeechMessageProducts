#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.DataOperations
{
    /// <summary>
    /// 純資料操作的局部 XML 建構與單頁發布契約；僅保留有限結構常數，不保存任一操作資料。
    /// 不建立連線、快取、Task、timer 或通知器；授權與 SDK 生命週期由最終 host owner 負責。
    /// </summary>
    internal static class SdkDataQuery
    {
        /// <summary>拒絕未配置 SDK；回傳借用參照，不移轉 Dispose、憑證或連線所有權。</summary>
        internal static IOrganizationService RequireService(IOrganizationService? service)
        {
#if NETFRAMEWORK
            if (service == null) throw new ArgumentNullException(nameof(service));
#else
            ArgumentNullException.ThrowIfNull(service);
#endif
            return service;
        }

        /// <summary>結構名稱只接受SDK logical name字元；XML編碼或QueryExpression本身不是結構合法性與授權證明。</summary>
        internal static void RequireLogicalName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>
        /// 僅串流讀取一次無命名空間Fetch根表；禁止DTD／resolver及外部檔案網路存取，
        /// 不建立可保留任意查詢的共享樹。錯誤訊息不含原XML，以免查詢條件進入日誌。
        /// </summary>
        internal static string ExpectedFetchEntity(string? fetchXml, string parameter)
        {
            if (string.IsNullOrWhiteSpace(fetchXml))
                throw new ArgumentException("A FetchXML query is required.", parameter);
            var xml = fetchXml!;
            try
            {
                using var text = new StringReader(xml);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = xml.Length
                });
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "fetch"
                    || reader.NamespaceURI.Length != 0)
                    throw new ArgumentException("Invalid FetchXML root.", parameter);
                string? entityName = null;
                var entityCount = 0;
                while (reader.Read())
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1
                        && reader.LocalName == "entity" && reader.NamespaceURI.Length == 0)
                    {
                        entityCount++;
                        entityName = reader.GetAttribute("name");
                    }
                if (entityCount != 1 || entityName == null)
                    throw new ArgumentException("A single FetchXML entity is required.", parameter);
                RequireLogicalName(entityName, parameter);
                return entityName;
            }
            catch (XmlException failure)
            {
                throw new ArgumentException("Invalid FetchXML query.", parameter, failure);
            }
        }

        /// <summary>建立新的根實體欄位與排序樹；輸入是各服務私有唯讀結構名稱，非 caller 授權依據。</summary>
        internal static XElement Entity(string name, IEnumerable<string> fields, string order, bool descending = false)
        {
            var entity = new XElement("entity", new XAttribute("name", name));
            foreach (var field in fields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", order),
                new XAttribute("descending", descending ? "true" : "false")));
            return entity;
        }

        /// <summary>建立操作局部 and 條件；所有 XElement 僅屬該次候選，不供跨執行緒共用。</summary>
        internal static XElement Filter(params XElement[] conditions) =>
            new XElement("filter", new XAttribute("type", "and"), conditions);

        /// <summary>條件文字只能進屬性值，自動編碼特殊字元；無值運算子不製造 value 屬性。</summary>
        internal static XElement Condition(string attribute, string operation, string? value = null)
        {
            var condition = new XElement("condition", new XAttribute("attribute", attribute),
                new XAttribute("operator", operation));
            if (value != null) condition.Add(new XAttribute("value", value));
            return condition;
        }

        /// <summary>
        /// lookup 必須是非空資料庫 GUID；顯示名稱只供舊 SDK uiname，不是權限或去重鍵。
        /// 拒絕訊息不回顯輸入；合法 GUID 以大括號 D 格式保留既有查詢語義。
        /// </summary>
        internal static XElement Lookup(string attribute, string? displayName, string entityName, string id)
        {
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
                throw new ArgumentException("A database identifier is required.", nameof(id));
            return new XElement("condition", new XAttribute("attribute", attribute), new XAttribute("operator", "eq"),
                new XAttribute("uiname", displayName ?? string.Empty), new XAttribute("uitype", entityName),
                new XAttribute("value", "{" + parsed.ToString("D") + "}"));
        }

        /// <summary>
        /// 日期條件沿用既有西曆日期語義，不受執行緒文化或泰國曆影響；不自行做時區換算。
        /// 時間轉換由上層教會時鐘決定，此處只序列化 caller 已決定的日期。
        /// </summary>
        internal static string Date(DateTime value) => value.ToString("yyyy-M-d", CultureInfo.InvariantCulture);

        /// <summary>拒絕反向日期，不能交換端點或漏掉條件而擴張 caller 的查詢範圍。</summary>
        internal static void RequireRange(DateTime start, DateTime end)
        {
            if (start.Date > end.Date) throw new ArgumentException("Invalid date range.", nameof(end));
        }

        /// <summary>
        /// 包裝原 FetchXML 設定並只送一次 SDK，完整驗證根實體與全部穩定 ID 後才發布；
        /// 原分頁欄位不變，不自動無界讀取下一頁。SDK 故障不 catch 或包裝，交回 host 通知邊界。
        /// </summary>
        internal static EntityCollection Run(IOrganizationService service, XElement entity, bool distinct = false, int? top = null,
            bool executeRequest = false)
        {
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", distinct ? "true" : "false"));
            if (top.HasValue) fetch.Add(new XAttribute("top", top.Value));
            fetch.Add(entity);
            return RunFetch(service, fetch, executeRequest);
        }

        /// <summary>
        /// 僅接受本組件操作局部建好的完整Fetch樹；維持各舊入口Execute或RetrieveMultiple的差異。
        /// 先核對response型別及top回應預算，再驗全部正式ID；不在超量候選上進行無界HashSet配置。
        /// SDK故障原樣向外傳遞；候選、XML及識別集合不存共用狀態，也不接管借入SDK。
        /// </summary>
        internal static EntityCollection RunFetch(IOrganizationService service, XElement fetch, bool executeRequest)
        {
            var expression = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting));
            EntityCollection? candidate;
            if (executeRequest)
            {
                if (service.Execute(new RetrieveMultipleRequest { Query = expression }) is not RetrieveMultipleResponse response)
                    throw new InvalidOperationException("The SDK returned an invalid query response.");
                candidate = response.EntityCollection;
            }
            else candidate = service.RetrieveMultiple(expression);
            var top = (int?)fetch.Attribute("top");
            if (top.HasValue && candidate != null && candidate.Entities.Count > top.Value)
                throw new InvalidOperationException("The SDK exceeded the requested row budget.");
            return ValidatePage(candidate, (string)fetch.Element("entity")!.Attribute("name")!, top ?? 5000);
        }

        /// <summary>
        /// 所有候選只在單次操作內存活；先驗整頁 ID 非空、無重複且根實體相符，才回原候選。
        /// 不以名稱去重、不修改原頁、不持有 Entity 或 HashSet；空頁合法但 null 頁屬 SDK 完整性故障。
        /// </summary>
        internal static EntityCollection ValidatePage(EntityCollection? candidate, string entityName, int? maximumRows = null)
        {
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            // 個別入口明確要求本地單頁預算時，先拒超量才建ID集合，不在不可信超量頁配置無界HashSet。
            if (maximumRows.HasValue && candidate.Entities.Count > maximumRows.Value)
                throw new InvalidOperationException("The SDK exceeded the requested row budget.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
                if (row == null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return candidate;
        }
    }
}
