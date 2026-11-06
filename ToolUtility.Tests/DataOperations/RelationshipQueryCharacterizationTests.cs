/*
 * 檔案整體說明
 *
 * 目的與定位：六個原host關聯入口的固定完整QueryExpression投影：欄位、條件值型別、link、排序與分頁缺省逐一鎖定。 golden由已讀原碼獨立撰寫，不在測試執行時生成；替身只代替SDK網路，
 * 不幫產品驗候選或做授權。
 *
 * 主要功能與流程：
 * 本檔宣告 RelationshipQueryCharacterizationTests。
 * - 同名異ID與原單頁metadata全部保留；不因抽取而添加statecode、日期轉換或自動全表分頁。
 * - 六入口只有合成GUID與固定日期；動態結構名稱只用於獨立測試變體，不構成已授權scope。
 * - 投影實際SDK查詢而非另造產品預期；保留空根filter、全部link層級及原值CLR型別以抓出暗中改寫。
 * - 欄位順序及AllColumns屬原查詢契約，不能由排序或忽略空欄位掩蓋差異。
 * 測試案例入口包含 OriginalEntrypoints_PreserveQueryAndCompleteSdkPage、Read、Describe、Columns、Filter、Link。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ToolUtility.Tests/DataOperations/ComplexQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/CrmAsyncExtensionCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/PresentQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/RelationshipQuerySafetyTests.cs、
 * ToolUtility.Tests/DataOperations/RepositoryQueryCharacterizationTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 六個原host關聯入口的固定完整QueryExpression投影：欄位、條件值型別、link、排序與分頁缺省逐一鎖定。
    /// golden由已讀原碼獨立撰寫，不在測試執行時生成；替身只代替SDK網路，不幫產品驗候選或做授權。
    /// </summary>
    public sealed class RelationshipQueryCharacterizationTests
    {
        /// <summary>同名異ID與原單頁metadata全部保留；不因抽取而添加statecode、日期轉換或自動全表分頁。</summary>
        [Theory]
        [InlineData("plain")]
        [InlineData("ordered")]
        [InlineData("linked")]
        [InlineData("weekly")]
        [InlineData("many")]
        [InlineData("contact")]
        public void OriginalEntrypoints_PreserveQueryAndCompleteSdkPage(string entry)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var rows = new EntityCollection(new[]
                {
                    new Entity(entry == "linked" ? "contact" : "list", ContactCharacterizationTests.FirstId) { ["new_name"] = "same-name" },
                    new Entity(entry == "linked" ? "contact" : "list", ContactCharacterizationTests.SecondId) { ["new_name"] = "same-name" }
                }) { MoreRecords = true, PagingCookie = "synthetic", TotalRecordCount = 27 };
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
                {
                    GoldenQueryAssertions.Equal("relationship-" + entry + ".xml", Describe(Assert.IsType<QueryExpression>(query)));
                    return rows;
                });
                var result = Read(new RelationshipQueryService(new object(), sdk.Object), entry);
                Assert.Same(rows, result);
                Assert.Equal(2, result.Entities.Count);
                Assert.True(result.MoreRecords);
                Assert.Equal("synthetic", result.PagingCookie);
                Assert.Equal(27, result.TotalRecordCount);
                sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        /// <summary>六入口只有合成GUID與固定日期；動態結構名稱只用於獨立測試變體，不構成已授權scope。</summary>
        internal static EntityCollection Read(IRelationshipQueryService service, string entry,
            string parent = "contact", string idField = "contactid",
            string id = "11111111-1111-4111-8111-111111111111",
            string association = "new_contact_family_leader_list", string child = "list") => entry switch
        {
            "plain" => service.RetrieveManyToOneRelationship(parent, idField, id, association, child),
            "ordered" => service.QueryListsAndOrderedByListName(parent, idField, id, association, child),
            "linked" => service.RetrieveManyToOneWithLinkEntity(),
            "weekly" => service.QueryWeeklyReportBySunday(new DateTime(2027, 3, 14), parent, idField, id, association, child),
            "many" => service.QueryManyToMany("new_app_named", "list", "list", "listid", "listmember", "listid", "entityid", Guid.Parse(id)),
            "contact" => service.QueryListOfContactManyToMany(Guid.Parse(id)),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };

        /// <summary>投影實際SDK查詢而非另造產品預期；保留空根filter、全部link層級及原值CLR型別以抓出暗中改寫。</summary>
        internal static string Describe(QueryExpression query) => new XElement("query",
            new XAttribute("entity", query.EntityName), new XAttribute("distinct", query.Distinct),
            new XAttribute("noLock", query.NoLock), new XAttribute("top", query.TopCount?.ToString(CultureInfo.InvariantCulture) ?? ""),
            new XAttribute("page", query.PageInfo.PageNumber), new XAttribute("count", query.PageInfo.Count),
            new XAttribute("cookie", query.PageInfo.PagingCookie ?? ""), new XAttribute("returnTotal", query.PageInfo.ReturnTotalRecordCount),
            Columns(query.ColumnSet), Filter(query.Criteria),
            query.Orders.Select(order => new XElement("order", new XAttribute("field", order.AttributeName),
                new XAttribute("direction", order.OrderType))), query.LinkEntities.Select(Link)).ToString();

        /// <summary>欄位順序及AllColumns屬原查詢契約，不能由排序或忽略空欄位掩蓋差異。</summary>
        private static XElement Columns(ColumnSet columns) => new XElement("columns",
            new XAttribute("all", columns.AllColumns), columns.Columns.Select(name => new XElement("column", name)));

        /// <summary>保留AND/OR與完整值型別；只有固定小型測試查詢，不處理外來無界遞迴樹。</summary>
        private static XElement Filter(FilterExpression filter) => new XElement("filter",
            new XAttribute("op", filter.FilterOperator),
            filter.Conditions.Select(condition => new XElement("condition", new XAttribute("field", condition.AttributeName),
                new XAttribute("op", condition.Operator), new XAttribute("entity", condition.EntityName ?? ""),
                condition.Values.Select(value => new XElement("value", new XAttribute("type", value.GetType().Name),
                    value is bool boolean ? boolean ? "true" : "false" : Convert.ToString(value, CultureInfo.InvariantCulture))))),
            filter.Filters.Select(Filter));

        /// <summary>link from/to方向、join與alias必須原樣；欄位與link filter不與根filter混淆。</summary>
        private static XElement Link(LinkEntity link) => new XElement("link",
            new XAttribute("fromEntity", link.LinkFromEntityName), new XAttribute("fromAttribute", link.LinkFromAttributeName),
            new XAttribute("toEntity", link.LinkToEntityName), new XAttribute("toAttribute", link.LinkToAttributeName),
            new XAttribute("join", link.JoinOperator), new XAttribute("alias", link.EntityAlias ?? ""),
            Columns(link.Columns), Filter(link.LinkCriteria), link.LinkEntities.Select(Link));
    }
}
