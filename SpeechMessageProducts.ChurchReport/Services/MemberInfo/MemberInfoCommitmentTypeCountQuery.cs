using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// 把 SDK 產生、已包含搜尋／在籍／無小組範圍條件的 FetchXML，轉成
    /// customertypecode group-by 計數查詢。所有使用者條件都保留在既有 XML 節點中，
    /// 本類別只做結構化 DOM 變更，不拼接任何輸入文字。
    /// </summary>
    public static class MemberInfoCommitmentTypeCountQuery
    {
        public const string ValueAlias = "commitmenttype";
        public const string CountAlias = "rowcount";

        /// <summary>
        /// 建立「每個非空 OptionSet 值有幾筆」的 aggregate FetchXML。
        /// FetchXML aggregate 不計入 null，因此真正空白會由 Controller 另做一次 null count。
        /// </summary>
        public static string CreateValueCountsFetch(string fetchXml)
        {
            if (string.IsNullOrWhiteSpace(fetchXml))
            {
                throw new ArgumentException("FetchXML must not be blank.", nameof(fetchXml));
            }

            var document = XDocument.Parse(fetchXml, LoadOptions.PreserveWhitespace);
            var root = document.Root
                ?? throw new InvalidOperationException("FetchXML must contain a root element.");
            var entity = root.Elements()
                .Single(element => element.Name.LocalName == "entity");

            root.SetAttributeValue("aggregate", "true");
            root.Attributes()
                .Where(attribute => attribute.Name.LocalName is
                    "page" or "count" or "paging-cookie" or "returntotalrecordcount")
                .Remove();

            // 原查詢的 filter／link-entity 必須保留；只替換直接投影欄位及 order。
            entity.Elements()
                .Where(element => element.Name.LocalName is "attribute" or "order")
                .Remove();
            entity.Add(
                new XElement(
                    entity.Name.Namespace + "attribute",
                    new XAttribute("name", "customertypecode"),
                    new XAttribute("alias", ValueAlias),
                    new XAttribute("groupby", "true")),
                new XElement(
                    entity.Name.Namespace + "attribute",
                    new XAttribute("name", "contactid"),
                    new XAttribute("alias", CountAlias),
                    new XAttribute("aggregate", "countcolumn")));
            return document.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// 將 CRM aggregate rows 轉成 raw OptionSet value → count；raw value 在此只作為
        /// metadata 分段的識別鍵，不拿來比較大小。重複值會合計，缺少 alias 的異常列會略過。
        /// </summary>
        public static IReadOnlyDictionary<int, int> ReadValueCounts(
            EntityCollection rows)
        {
            var result = new Dictionary<int, int>();
            foreach (var row in rows?.Entities ?? Enumerable.Empty<Entity>())
            {
                var valueObject = Unwrap(row, ValueAlias);
                var countObject = Unwrap(row, CountAlias);
                var value = valueObject is OptionSetValue optionSetValue
                    ? optionSetValue.Value
                    : valueObject is int integerValue
                        ? integerValue
                        : (int?)null;
                if (!value.HasValue || countObject == null)
                {
                    continue;
                }

                var count = Math.Max(0, Convert.ToInt32(countObject));
                result[value.Value] = result.GetValueOrDefault(value.Value) + count;
            }
            return result;
        }

        private static object Unwrap(Entity row, string alias)
        {
            if (row == null || !row.Attributes.TryGetValue(alias, out var value))
            {
                return null;
            }

            return value is AliasedValue aliasedValue
                ? aliasedValue.Value
                : value;
        }
    }
}
