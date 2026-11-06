/*
 * 檔案整體說明
 *
 * 目的與定位：原host八入口全投影特性；固定查詢黃金值不靠新實作產生，兩種UtcNow界線另以呼叫前後時間窗驗證。 Query與Fetch仍保持不同SDK入口，結果原單頁metadata與同名異ID完整保存。
 *
 * 主要功能與流程：
 * 本檔宣告 PresentQueryCharacterizationTests。
 * - 每入口都鎖定條件順序/型別、欄位/排序/link；動態時間僅替換已驗證的單一值，不放寬其餘樹。
 * - 固定八個來源API及合成ID/日期，名稱與結構參數只供安全測試變體，不取代伺服器授權。
 * 測試案例入口包含 OriginalEntrypoints_PreserveFullQueryAndMetadata、Read。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 原host八入口全投影特性；固定查詢黃金值不靠新實作產生，兩種UtcNow界線另以呼叫前後時間窗驗證。
    /// Query與Fetch仍保持不同SDK入口，結果原單頁metadata與同名異ID完整保存。
    /// </summary>
    public sealed class PresentQueryCharacterizationTests
    {
        /// <summary>每入口都鎖定條件順序/型別、欄位/排序/link；動態時間僅替換已驗證的單一值，不放寬其餘樹。</summary>
        [Theory]
        [InlineData("weeks")]
        [InlineData("sorted")]
        [InlineData("fetch")]
        [InlineData("in-weekly")]
        [InlineData("date")]
        [InlineData("weekly")]
        [InlineData("months")]
        [InlineData("list")]
        public void OriginalEntrypoints_PreserveFullQueryAndMetadata(string entry)
        {
            var before = DateTime.UtcNow;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            EntityCollection Rows(string table) => new EntityCollection(new[]
            {
                new Entity(table, ContactCharacterizationTests.FirstId) { ["new_name"] = "same-name" },
                new Entity(table, ContactCharacterizationTests.SecondId) { ["new_name"] = "same-name" }
            }) { MoreRecords = true, PagingCookie = "synthetic-cookie", TotalRecordCount = 37 };
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                var projection = XElement.Parse(RelationshipQueryCharacterizationTests.Describe(query));
                if (entry == "sorted" || entry == "date")
                {
                    var condition = entry == "sorted" ? query.Criteria.Conditions[2]
                        : Assert.Single(query.LinkEntities).LinkCriteria.Conditions[0];
                    var time = Assert.IsType<DateTime>(Assert.Single(condition.Values));
                    Assert.Equal(DateTimeKind.Utc, time.Kind);
                    var days = entry == "sorted" ? -32 : -7;
                    Assert.InRange(time, before.AddDays(days), DateTime.UtcNow.AddDays(days));
                    projection.Descendants("condition").Single(node =>
                        (string?)node.Attribute("field") == (entry == "sorted" ? "new_sunday_date" : "new_class_end_date"))
                        .Element("value")!.Value = "CURRENT_UTC";
                }
                GoldenQueryAssertions.Equal("present-" + entry + ".xml", projection.ToString());
                return Rows(query.EntityName);
            });
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                GoldenQueryAssertions.Equal("present-fetch.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query);
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = Rows("new_present_record");
                return response;
            });
            var result = Read(new PresentRecordQueryService(new object(), sdk.Object), entry);
            Assert.Equal(2, result.Entities.Count);
            Assert.All(result.Entities, row => Assert.Equal("same-name", row["new_name"]));
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-cookie", result.PagingCookie);
            Assert.Equal(37, result.TotalRecordCount);
        }

        /// <summary>固定八個來源API及合成ID/日期，名稱與結構參數只供安全測試變體，不取代伺服器授權。</summary>
        internal static EntityCollection Read(IPresentRecordQueryService service, string entry,
            string id = "11111111-1111-4111-8111-111111111111", string name = "Synthetic",
            int weeks = 3, string parent = "new_disciple_lessons", string idField = "new_disciple_lessonsid",
            string association = "new_parent", string child = "new_present_record") => entry switch
        {
            "weeks" => service.QueryPresentRecordByContactIdAndSunday(Guid.Parse(id), ContactCharacterizationTests.SecondId, weeks),
            "sorted" => service.QueryPresentRecordSortBySunday(parent, idField, id, association, child),
            "fetch" => service.QueryPresentRecordSortBySundayFetchXml(weeks, name, id),
            "in-weekly" => service.QueryPresentRecordInWeeklyReportByContactId(Guid.Parse(id), ContactCharacterizationTests.SecondId),
            "date" => service.QueryEntityListByDate(parent, idField, id, association, child),
            "weekly" => service.QueryWeeklyReportBySunday(new DateTime(2027, 3, 14), Guid.Parse(id)),
            "months" => service.QueryWeeklyReportBeforeTwoMonthOfSunday(new DateTime(2027, 3, 14), Guid.Parse(id)),
            "list" => service.QueryListByContactId(Guid.Parse(id), association),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}
