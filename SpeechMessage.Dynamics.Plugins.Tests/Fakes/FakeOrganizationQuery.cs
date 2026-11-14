/*
 * 檔案整體說明
 *
 * 目的與定位：實作 FakeOrganizationService 的嚴格離線查詢子集；QueryExpression 與
 * FetchXML 共用同一驗證及求值路徑，避免兩種格式得到相反的合成測試結論。
 * 主要功能與流程：先驗證整棵查詢，再連結、套用條件、排序與 TopCount，最後投影並
 * 深層複製。支援 And／Or、常見值比較及 inner／left outer 巢狀連結；未知功能具名拒絕。
 * 使用邊界與維護：不模擬權限、metadata、collation、aggregate、distinct、分頁、相對日期、
 * intersect、連結排序或主鍵特殊命名。字串以 ordinal ignore case 比較；空值只有 Null／
 * NotNull 命中，其他運算不命中。排序 null 在前，再以正式 GUID 穩定排序。
 * 資源與驗證：呼叫者持有服務鎖，候選僅存活於該次呼叫；最多 4096 候選、16 層查詢及
 * 128KiB XML，禁用 DTD／外部解析。重複主列識別拒絕發布，不按顯示值刪除或合併；
 * 一對多 fan-out 若造成重複主列須由情境重新設計，不能冒充唯一列結果。
 * 本檔沒有連線、背景工作或自行擁有的 SDK 資源；測試覆蓋空表拒絕及兩語法等價性。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>查詢 partial 僅存放無共享狀態的解析器；所有候選都屬於這次 RetrieveMultiple。</summary>
internal sealed partial class FakeOrganizationService
{
    /// <summary>完整驗證之後才讀表；輸出主列身分唯一且所有屬性 detached。</summary>
    private EntityCollection Query(QueryBase input)
    {
        var query = input switch
        {
            QueryExpression expression => expression,
            FetchExpression fetch => ParseFetch(fetch.Query),
            _ => throw Unsupported("QueryType." + (input?.GetType().Name ?? "null"))
        };
        var aliases = new Dictionary<LinkEntity, string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", Name(query.EntityName) };
        ValidateQuery(query, aliases, names);
        var candidates = new List<Dictionary<string, Entity?>>();
        foreach (var entity in Table(query.EntityName))
        {
            var context = new Dictionary<string, Entity?>(StringComparer.OrdinalIgnoreCase) { [""] = entity, [query.EntityName] = entity };
            foreach (var expanded in Expand(new[] { context }, query.LinkEntities, "", aliases))
            {
                if (!Matches(query.Criteria, expanded, "")) continue;
                if (candidates.Count >= Capacity) throw Unsupported("Query candidate capacity");
                candidates.Add(expanded);
            }
        }
        candidates.Sort((left, right) => CompareRows(left, right, query.Orders));
        var result = new EntityCollection { EntityName = query.EntityName };
        var identities = new HashSet<Guid>();
        // 驗證完整候選而非 Top 後的片段，避免 Top 掩蓋 join 帶來的重複身分。
        foreach (var context in candidates)
            if (!identities.Add(context[""]!.Id)) throw new InvalidOperationException("Query result duplicate identity: LinkEntity fan-out");
        foreach (var context in candidates.Take(query.TopCount ?? Capacity))
        {
            var projected = Project(context[""]!, query.ColumnSet);
            foreach (var pair in aliases)
            {
                var entity = context[pair.Value];
                if (entity is null) continue;
                foreach (var attribute in Project(entity, pair.Key.Columns).Attributes)
                    projected.Attributes.Add(pair.Value + "." + attribute.Key, new AliasedValue(entity.LogicalName, attribute.Key, CloneValue(attribute.Value, 0)));
            }
            result.Entities.Add(projected);
        }
        return result;
    }

    /// <summary>只枚舉本實例的指定邏輯表；呼叫者已持有服務鎖。</summary>
    private IEnumerable<Entity> Table(string name) => rows.Where(pair => pair.Key.Name == Name(name)).Select(pair => pair.Value);

    /// <summary>欄位白名單投影並複製，正式主鍵不受 ColumnSet 排除；缺欄位不虛構 null 屬性。</summary>
    private static Entity Project(Entity source, ColumnSet columns)
    {
        ValidateColumns(columns);
        var result = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
        foreach (var attribute in source.Attributes)
            if (columns.AllColumns || columns.Columns.Contains(attribute.Key) || attribute.Key == Name(source.LogicalName) + "id")
            {
                result[attribute.Key] = CloneValue(attribute.Value, 0);
                if (source.FormattedValues.TryGetValue(attribute.Key, out var formatted)) result.FormattedValues.Add(attribute.Key, formatted);
            }
        return result;
    }

    /// <summary>拒絕會改變查詢語意的進階旗標；NoLock 不影響離線已序列化的讀取。</summary>
    private static void ValidateQuery(QueryExpression query, Dictionary<LinkEntity, string> aliases, HashSet<string> names)
    {
        if (query.Distinct) throw Unsupported("QueryExpression.Distinct");
        if (query.SubQueryExpression is not null) throw Unsupported("QueryExpression.SubQueryExpression");
        if (!string.IsNullOrEmpty(query.QueryHints) || !string.IsNullOrEmpty(query.ForceSeek) || !string.IsNullOrEmpty(query.DataSource)) throw Unsupported("QueryExpression.HintsOrDataSource");
        if (query.ExtensionData is not null) throw Unsupported("QueryExpression.ExtensionData");
        var page = query.PageInfo;
        if (page is not null && (page.Count != 0 || (page.PageNumber < 0 || page.PageNumber > 1) || page.ReturnTotalRecordCount || !string.IsNullOrEmpty(page.PagingCookie) || page.ExtensionData is not null)) throw Unsupported("QueryExpression.PageInfo");
        if (query.TopCount is < 0) throw new ArgumentOutOfRangeException(nameof(query), "TopCount must be nonnegative");
        ValidateColumns(query.ColumnSet);
        RegisterLinks(query.LinkEntities, query.EntityName, aliases, names, 0);
        ValidateFilter(query.Criteria, names, 0);
        foreach (var order in query.Orders)
        {
            Name(order.AttributeName);
            if (!string.IsNullOrEmpty(order.Alias) || order.ExtensionData is not null) throw Unsupported("OrderExpression.AliasOrExtensionData");
            if (order.OrderType != OrderType.Ascending && order.OrderType != OrderType.Descending) throw Unsupported("OrderType." + order.OrderType);
            if (!string.IsNullOrEmpty(order.EntityName) && !names.Contains(order.EntityName)) throw Unsupported("OrderExpression.EntityName." + order.EntityName);
        }
    }

    /// <summary>先收集所有 alias，防止空表漏掉未知連結；深度限制同時切斷循環查詢圖。</summary>
    private static void RegisterLinks(IEnumerable<LinkEntity> links, string parent, Dictionary<LinkEntity, string> aliases, HashSet<string> names, int depth)
    {
        if (depth > 16) throw Unsupported("LinkEntity.Depth");
        foreach (var link in links)
        {
            if (aliases.Count >= 64) throw Unsupported("LinkEntity.Count");
            if (link.JoinOperator != JoinOperator.Inner && link.JoinOperator != JoinOperator.LeftOuter) throw Unsupported("LinkEntity.JoinOperator." + link.JoinOperator);
            if (!string.Equals(link.LinkFromEntityName, parent, StringComparison.OrdinalIgnoreCase)) throw Unsupported("LinkEntity.LinkFromEntityName");
            Name(link.LinkFromAttributeName); Name(link.LinkToAttributeName); Name(link.LinkToEntityName);
            if (link.Orders.Count != 0 || !string.IsNullOrEmpty(link.ForceSeek) || link.ExtensionData is not null) throw Unsupported("LinkEntity.OrdersOrHints");
            var alias = string.IsNullOrEmpty(link.EntityAlias) ? Name(link.LinkToEntityName) + (aliases.Count + 1).ToString(CultureInfo.InvariantCulture) : link.EntityAlias;
            if (!names.Add(alias)) throw Unsupported("LinkEntity.DuplicateAlias." + alias);
            aliases.Add(link, alias);
            ValidateColumns(link.Columns);
            // ON 條件只允許該連結自身欄位；跨 alias 條件應放在根 Criteria，避免模擬錯誤時序。
            ValidateFilter(link.LinkCriteria, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alias }, 0);
            RegisterLinks(link.LinkEntities, link.LinkToEntityName, aliases, names, depth + 1);
        }
    }

    /// <summary>拒絕 lazy file 與 aggregate expression，避免投影靜默忽略欄位功能。</summary>
    private static void ValidateColumns(ColumnSet columns)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (columns.AttributeExpressions.Count != 0 || columns.HasLazyFileAttribute || columns.ExtensionData is not null || columns.LazyFileAttributeValue is not null ||
            !string.IsNullOrEmpty(columns.LazyFileAttributeEntityName) || !string.IsNullOrEmpty(columns.LazyFileAttributeKey) || columns.LazyFileAttributeSizeLimit != 0)
            throw Unsupported("ColumnSet.AttributeExpressionsOrLazyFile");
        foreach (var column in columns.Columns) Name(column);
    }

    /// <summary>遞迴驗證所有運算子、參數數量及 alias；不依賴資料存在或短路條件命中。</summary>
    private static void ValidateFilter(FilterExpression filter, HashSet<string> aliases, int depth)
    {
        if (depth > 16) throw Unsupported("FilterExpression.Depth");
        if (filter.FilterOperator != LogicalOperator.And && filter.FilterOperator != LogicalOperator.Or) throw Unsupported("FilterOperator." + filter.FilterOperator);
        if (filter.IsQuickFindFilter || !string.IsNullOrEmpty(filter.FilterHint) || filter.AnyAllFilterLinkEntity is not null || filter.ExtensionData is not null) throw Unsupported("FilterExpression.Advanced");
        foreach (var condition in filter.Conditions)
        {
            Name(condition.AttributeName);
            if (condition.CompareColumns || condition.ExtensionData is not null) throw Unsupported("ConditionExpression.CompareColumnsOrExtensionData");
            if (!string.IsNullOrEmpty(condition.EntityName) && !aliases.Contains(condition.EntityName)) throw Unsupported("ConditionExpression.EntityName." + condition.EntityName);
            var count = condition.Operator switch
            {
                ConditionOperator.Null or ConditionOperator.NotNull => 0,
                ConditionOperator.In or ConditionOperator.NotIn => -1,
                ConditionOperator.Between or ConditionOperator.NotBetween => 2,
                ConditionOperator.Equal or ConditionOperator.NotEqual or ConditionOperator.GreaterThan or ConditionOperator.GreaterEqual or
                ConditionOperator.LessThan or ConditionOperator.LessEqual or ConditionOperator.Like or ConditionOperator.NotLike or
                ConditionOperator.BeginsWith or ConditionOperator.EndsWith => 1,
                _ => throw Unsupported("ConditionOperator." + condition.Operator)
            };
            if ((count >= 0 && condition.Values.Count != count) || (count < 0 && condition.Values.Count == 0)) throw Unsupported("ConditionValueCount." + condition.Operator);
            foreach (var value in condition.Values)
            {
                var scalar = Scalar(value);
                if (scalar is null) throw Unsupported("ConditionValue.Null");
                if ((condition.Operator == ConditionOperator.Like || condition.Operator == ConditionOperator.NotLike) &&
                    (Text(scalar).IndexOf('[') >= 0 || Text(scalar).IndexOf(']') >= 0)) throw Unsupported("Like.BracketPattern");
            }
        }
        foreach (var child in filter.Filters) ValidateFilter(child, aliases, depth + 1);
    }

    /// <summary>按 join 順序建立操作區域 context；左連結無匹配時包含整棵子樹的 null alias。</summary>
    private List<Dictionary<string, Entity?>> Expand(IEnumerable<Dictionary<string, Entity?>> source, IEnumerable<LinkEntity> links, string parentAlias, Dictionary<LinkEntity, string> aliases)
    {
        var current = source.ToList();
        foreach (var link in links)
        {
            var next = new List<Dictionary<string, Entity?>>();
            foreach (var context in current)
            {
                var left = Value(context[parentAlias], link.LinkFromAttributeName);
                var matched = false;
                foreach (var target in Table(link.LinkToEntityName))
                {
                    var right = Value(target, link.LinkToAttributeName);
                    if (left is null || right is null || Compare(left, right) != 0) continue;
                    var joined = new Dictionary<string, Entity?>(context, StringComparer.OrdinalIgnoreCase) { [aliases[link]] = target };
                    if (!Matches(link.LinkCriteria, joined, aliases[link])) continue;
                    foreach (var child in Expand(new[] { joined }, link.LinkEntities, aliases[link], aliases))
                    {
                        matched = true;
                        if (next.Count >= Capacity) throw Unsupported("LinkEntity candidate capacity");
                        next.Add(child);
                    }
                }
                if (!matched && link.JoinOperator == JoinOperator.LeftOuter)
                {
                    var outer = new Dictionary<string, Entity?>(context, StringComparer.OrdinalIgnoreCase);
                    NullAliases(outer, link, aliases);
                    if (next.Count >= Capacity) throw Unsupported("LinkEntity candidate capacity");
                    next.Add(outer);
                }
            }
            current = next;
        }
        return current;
    }

    /// <summary>左連結未命中時明確標記整棵子樹，不把上一列匹配資料殘留在新 context。</summary>
    private static void NullAliases(Dictionary<string, Entity?> context, LinkEntity link, Dictionary<LinkEntity, string> aliases)
    {
        context[aliases[link]] = null;
        foreach (var child in link.LinkEntities) NullAliases(context, child, aliases);
    }

    /// <summary>空 filter 視為不限制；AND／OR 遞迴求值，驗證已在讀表前完成。</summary>
    private static bool Matches(FilterExpression filter, Dictionary<string, Entity?> context, string alias)
    {
        var tests = filter.Conditions.Select(condition => Condition(condition, context, alias))
            .Concat(filter.Filters.Select(child => Matches(child, context, alias))).ToArray();
        return filter.FilterOperator == LogicalOperator.And ? tests.All(value => value) : tests.Length == 0 || tests.Any(value => value);
    }

    /// <summary>常用比較遵循明確 null 契約；SDK 包裝值先解開，Like 僅支援百分比與底線。</summary>
    private static bool Condition(ConditionExpression condition, Dictionary<string, Entity?> context, string alias)
    {
        var actual = Scalar(Value(context[string.IsNullOrEmpty(condition.EntityName) ? alias : condition.EntityName], condition.AttributeName));
        if (condition.Operator == ConditionOperator.Null) return actual is null;
        if (condition.Operator == ConditionOperator.NotNull) return actual is not null;
        if (actual is null) return false;
        var values = condition.Values.Select(Scalar).ToArray();
        return condition.Operator switch
        {
            ConditionOperator.Equal => Compare(actual, values[0]) == 0,
            ConditionOperator.NotEqual => Compare(actual, values[0]) != 0,
            ConditionOperator.GreaterThan => Compare(actual, values[0]) > 0,
            ConditionOperator.GreaterEqual => Compare(actual, values[0]) >= 0,
            ConditionOperator.LessThan => Compare(actual, values[0]) < 0,
            ConditionOperator.LessEqual => Compare(actual, values[0]) <= 0,
            ConditionOperator.In => values.Any(value => Compare(actual, value) == 0),
            ConditionOperator.NotIn => values.All(value => Compare(actual, value) != 0),
            ConditionOperator.Between => Compare(actual, values[0]) >= 0 && Compare(actual, values[1]) <= 0,
            ConditionOperator.NotBetween => Compare(actual, values[0]) < 0 || Compare(actual, values[1]) > 0,
            ConditionOperator.BeginsWith => Text(actual).StartsWith(Text(values[0]), StringComparison.OrdinalIgnoreCase),
            ConditionOperator.EndsWith => Text(actual).EndsWith(Text(values[0]), StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Like => Like(Text(actual), Text(values[0])),
            ConditionOperator.NotLike => !Like(Text(actual), Text(values[0])),
            _ => throw Unsupported("ConditionOperator." + condition.Operator)
        };
    }

    /// <summary>讀取屬性或慣例主鍵；缺欄位即 null，不以空字串冒充。</summary>
    private static object? Value(Entity? entity, string attribute) => entity is null ? null :
        string.Equals(attribute, entity.LogicalName + "id", StringComparison.OrdinalIgnoreCase) ? entity.Id : entity.Attributes.TryGetValue(attribute, out var value) ? value : null;
    /// <summary>比較僅接受純量或 SDK 純量包裝，巢狀實體查詢必須明確擴充。</summary>
    private static object? Scalar(object? value) => value switch
    {
        null => null, EntityReference reference => reference.Id, Money money => money.Value, OptionSetValue option => option.Value,
        AliasedValue alias => Scalar(alias.Value), string or Guid or bool or byte or short or int or long or float or double or decimal or DateTime or DateTimeOffset => value,
        _ => throw Unsupported("QueryValueType." + value.GetType().Name)
    };
    /// <summary>不依主機文化轉換 XML 字面值；不同型別若無可靠比較規則則拒絕。</summary>
    private static int Compare(object? first, object? second)
    {
        var left = Scalar(first); var right = Scalar(second);
        if (left is null || right is null) return left is null ? right is null ? 0 : -1 : 1;
        if (left is Guid guid) return guid.CompareTo(right is Guid other ? other : Guid.Parse(Text(right)));
        if (left is DateTime date) return date.CompareTo(right is DateTime other ? other : DateTime.Parse(Text(right), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        if (left is DateTimeOffset offset) return offset.CompareTo(right is DateTimeOffset other ? other : DateTimeOffset.Parse(Text(right), CultureInfo.InvariantCulture));
        if (left is bool flag) return flag.CompareTo(right is bool other ? other : Text(right) == "1" || (Text(right) != "0" && bool.Parse(Text(right))));
        if (left is string text) return StringComparer.OrdinalIgnoreCase.Compare(text, Text(right));
        return Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
    }
    /// <summary>查詢字面值使用 invariant culture；只供已驗證純量，不呼叫任意物件 ToString。</summary>
    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    /// <summary>SQL 百分比／底線轉成錨定正規式，有限時且其餘符號一律 escape，避免任意 regex 注入。</summary>
    private static bool Like(string actual, string pattern) => Regex.IsMatch(actual, "\\A" + Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    /// <summary>多欄排序之後以主鍵固定同值次序；無排序亦固定主鍵，避免 hash 表列舉造成假差異。</summary>
    private static int CompareRows(Dictionary<string, Entity?> left, Dictionary<string, Entity?> right, IEnumerable<OrderExpression> orders)
    {
        foreach (var order in orders)
        {
            var alias = string.IsNullOrEmpty(order.EntityName) ? "" : order.EntityName;
            var comparison = Compare(Value(left[alias], order.AttributeName), Value(right[alias], order.AttributeName));
            if (comparison != 0) return order.OrderType == OrderType.Descending ? -Math.Sign(comparison) : comparison;
        }
        return left[""]!.Id.CompareTo(right[""]!.Id);
    }

    /// <summary>以禁 DTD、無外部 resolver 的有界讀取器解析 FetchXML，未知屬性及元素先拒絕。</summary>
    private static QueryExpression ParseFetch(string xml)
    {
        if (xml is null || xml.Length > 131072) throw Unsupported("FetchXML.Size");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 131072 });
        XElement root;
        try { root = XDocument.Load(reader).Root ?? throw Unsupported("FetchXML.Root"); }
        catch (XmlException error) { throw new NotSupportedException("FakeOrganizationService.Unsupported: FetchXML.MalformedOrDtd", error); }
        CheckXml(root, "fetch", "top", "version", "mapping");
        if (root.Attribute("version") is { Value: not "1.0" }) throw Unsupported("FetchXML.version");
        if (root.Attribute("mapping") is { Value: not "logical" }) throw Unsupported("FetchXML.mapping");
        if (root.Elements().Count() != 1 || root.Elements().Single().Name != "entity") throw Unsupported("FetchXML.entity");
        var entity = root.Elements().Single();
        CheckXml(entity, "entity", "name");
        var query = new QueryExpression(Required(entity, "name")) { ColumnSet = new ColumnSet(false) };
        if (root.Attribute("top") is { } top) query.TopCount = int.Parse(top.Value, NumberStyles.None, CultureInfo.InvariantCulture);
        ParseBody(entity, query.ColumnSet, query.Criteria, query.LinkEntities, query.Orders, query.EntityName, 0);
        return query;
    }

    /// <summary>將 XML entity／link 的子節點翻成共用 QueryExpression，不另外實作第二套求值器。</summary>
    private static void ParseBody(XElement source, ColumnSet columns, FilterExpression filter, DataCollection<LinkEntity> links,
        DataCollection<OrderExpression> orders, string entityName, int depth)
    {
        if (depth > 16) throw Unsupported("FetchXML.Depth");
        // FetchXML 未列 attribute 時回傳全部欄位；QueryExpression 空 ColumnSet 仍只回主鍵。
        if (!source.Elements("attribute").Any() && !source.Elements("all-attributes").Any()) columns.AllColumns = true;
        foreach (var node in source.Elements())
        {
            switch (node.Name.LocalName)
            {
                case "attribute": CheckXml(node, "attribute", "name"); Leaf(node); columns.AddColumn(Required(node, "name")); break;
                case "all-attributes": CheckXml(node, "all-attributes"); Leaf(node); columns.AllColumns = true; break;
                case "filter": filter.Filters.Add(ParseFilter(node, depth + 1)); break;
                case "order":
                    CheckXml(node, "order", "attribute", "descending", "entityname"); Leaf(node);
                    var order = new OrderExpression(Required(node, "attribute"), Bool(node, "descending") ? OrderType.Descending : OrderType.Ascending) { EntityName = (string?)node.Attribute("entityname") };
                    orders.Add(order); break;
                case "link-entity":
                    CheckXml(node, "link-entity", "name", "from", "to", "alias", "link-type");
                    var kind = (string?)node.Attribute("link-type") ?? "inner";
                    if (kind != "inner" && kind != "outer") throw Unsupported("FetchXML.link-type." + kind);
                    var link = new LinkEntity(entityName, Required(node, "name"), Required(node, "to"), Required(node, "from"), kind == "inner" ? JoinOperator.Inner : JoinOperator.LeftOuter)
                    { EntityAlias = (string?)node.Attribute("alias"), Columns = new ColumnSet(false) };
                    ParseBody(node, link.Columns, link.LinkCriteria, link.LinkEntities, link.Orders, link.LinkToEntityName, depth + 1);
                    links.Add(link); break;
                default: throw Unsupported("FetchXML.Element." + node.Name);
            }
        }
    }

    /// <summary>翻譯巢狀條件並保留字面值供同一比較器轉型；只允許明列運算子與 value 節點。</summary>
    private static FilterExpression ParseFilter(XElement source, int depth)
    {
        if (depth > 16) throw Unsupported("FetchXML.FilterDepth");
        CheckXml(source, "filter", "type");
        var type = (string?)source.Attribute("type") ?? "and";
        if (type != "and" && type != "or") throw Unsupported("FetchXML.filter.type." + type);
        var result = new FilterExpression(type == "and" ? LogicalOperator.And : LogicalOperator.Or);
        foreach (var node in source.Elements())
        {
            if (node.Name == "filter") { result.Filters.Add(ParseFilter(node, depth + 1)); continue; }
            CheckXml(node, "condition", "attribute", "operator", "value", "entityname");
            var operation = Required(node, "operator");
            var op = operation switch
            {
                "eq" => ConditionOperator.Equal, "ne" => ConditionOperator.NotEqual,
                "gt" => ConditionOperator.GreaterThan, "ge" => ConditionOperator.GreaterEqual,
                "lt" => ConditionOperator.LessThan, "le" => ConditionOperator.LessEqual,
                "null" => ConditionOperator.Null, "not-null" => ConditionOperator.NotNull,
                "in" => ConditionOperator.In, "not-in" => ConditionOperator.NotIn,
                "between" => ConditionOperator.Between, "not-between" => ConditionOperator.NotBetween,
                "like" => ConditionOperator.Like, "not-like" => ConditionOperator.NotLike,
                "begins-with" => ConditionOperator.BeginsWith, "ends-with" => ConditionOperator.EndsWith,
                _ => throw Unsupported("FetchXML.operator." + operation)
            };
            var condition = new ConditionExpression(Required(node, "attribute"), op) { EntityName = (string?)node.Attribute("entityname") };
            if (node.Attribute("value") is { } single)
            {
                if (node.Elements().Any()) throw Unsupported("FetchXML.condition.MixedValues");
                condition.Values.Add(single.Value);
            }
            foreach (var value in node.Elements())
            {
                CheckXml(value, "value"); Leaf(value); condition.Values.Add(value.Value);
            }
            result.Conditions.Add(condition);
        }
        return result;
    }

    /// <summary>只允許無 namespace 的明列 XML 屬性；未知功能一律具名拒絕，不忽略。</summary>
    private static void CheckXml(XElement element, string expected, params string[] allowed)
    {
        if (element.Name != expected) throw Unsupported("FetchXML.Element." + element.Name);
        foreach (var attribute in element.Attributes())
            if (attribute.Name.NamespaceName.Length != 0 || !allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal)) throw Unsupported("FetchXML.Attribute." + attribute.Name);
        if (element.Name != "value" && element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) throw Unsupported("FetchXML.Text");
    }
    /// <summary>葉節點不接受隱藏子元素，避免丟掉內嵌功能而假成功。</summary>
    private static void Leaf(XElement node) { if (node.Elements().Any()) throw Unsupported("FetchXML.Nested." + node.Name); }
    /// <summary>缺少必填 schema 屬性時具名拒絕，錯誤不附原查詢字串。</summary>
    private static string Required(XElement node, string name) => (string?)node.Attribute(name) is { Length: > 0 } value ? value : throw Unsupported("FetchXML.Required." + name);
    /// <summary>XML 布林僅接受 true／false／1／0；其他值不可靜默降為 false。</summary>
    private static bool Bool(XElement node, string name) => (string?)node.Attribute(name) switch
    { null or "false" or "0" => false, "true" or "1" => true, _ => throw Unsupported("FetchXML.Boolean." + name) };
}
