/*
 * 檔案整體說明
 *
 * 目的與定位：執行既有 QueryService 五個入口與 MarketingListService 的真實已編譯委派鏈。 只替代外部 SDK，保護 Repository 跨 assembly 搬移的查詢形狀及整頁拒絕；
 * 不證明授權或實機 D365。
 *
 * 主要功能與流程：
 * 本檔宣告 RepositoryCallerRouteTests。
 * - 六個消費點必須抵達同一 SDK 並保留各自投影／條件；正常回應完整返回，重複 ID 拒絕而不部分發布。 借入 SDK 的 Dispose 不被轉移，沒有外部設定、憑證或網路。
 * - 只選擇真實產品方法，不在 fixture 重建查詢或模擬 repository 呼叫。
 * 測試案例入口包含 ActualCallers_PreserveQueryAndRejectDuplicatePage、Invoke。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 執行既有 QueryService 五個入口與 MarketingListService 的真實已編譯委派鏈。
    /// 只替代外部 SDK，保護 Repository 跨 assembly 搬移的查詢形狀及整頁拒絕；不證明授權或實機 D365。
    /// </summary>
    public sealed class RepositoryCallerRouteTests
    {
        private static readonly Guid ContactId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ListId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 六個消費點必須抵達同一 SDK 並保留各自投影／條件；正常回應完整返回，重複 ID 拒絕而不部分發布。
        /// 借入 SDK 的 Dispose 不被轉移，沒有外部設定、憑證或網路。
        /// </summary>
        /// <param name="route">實際產品消費點。</param>
        /// <param name="duplicate">是否注入同一消費頁的精確重複資料庫 ID。</param>
        [Theory]
        [InlineData("single", false)]
        [InlineData("collection", false)]
        [InlineData("fetch", false)]
        [InlineData("blood", false)]
        [InlineData("present", false)]
        [InlineData("list", false)]
        [InlineData("single", true)]
        [InlineData("collection", true)]
        [InlineData("fetch", true)]
        [InlineData("blood", true)]
        [InlineData("present", true)]
        [InlineData("list", true)]
        public void ActualCallers_PreserveQueryAndRejectDuplicatePage(string route, bool duplicate)
        {
            var table = route == "blood" ? "new_blood_report"
                : route == "present" ? "new_individual_meeting_and_spiritual_meditation_records"
                : route == "list" ? "listmember" : "contact";
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection();
            page.Entities.Add(new Entity(table, ContactId) { ["fullname"] = "合成同名" });
            if (duplicate) page.Entities.Add(new Entity(table, ContactId));
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Callback<QueryBase>(query =>
            {
                if (route == "fetch")
                {
                    Assert.Equal("<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>",
                        Assert.IsType<FetchExpression>(query).Query);
                    return;
                }
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal(table, expression.EntityName);
                Assert.True(expression.ColumnSet.AllColumns);
                if (route == "present")
                {
                    Assert.Equal(2, expression.Criteria.Conditions.Count);
                    Assert.Equal("new_contact_present_record", expression.Criteria.Conditions[0].AttributeName);
                    Assert.Equal(ContactId, Assert.Single(expression.Criteria.Conditions[0].Values));
                    Assert.Equal("new_sunday_date", expression.Criteria.Conditions[1].AttributeName);
                    Assert.Equal(ConditionOperator.GreaterEqual, expression.Criteria.Conditions[1].Operator);
                    Assert.IsType<DateTime>(Assert.Single(expression.Criteria.Conditions[1].Values));
                    Assert.Equal("new_sunday_date", Assert.Single(expression.Orders).AttributeName);
                }
                else
                {
                    var condition = Assert.Single(expression.Criteria.Conditions);
                    Assert.Equal(route == "blood" ? "new_blood_contact_relation" : route == "list" ? "listid" : "fullname",
                        condition.AttributeName);
                    Assert.Equal(ConditionOperator.Equal, condition.Operator);
                    Assert.Equal(route == "blood" ? (object)ContactId : route == "list" ? ListId : "合成同名",
                        Assert.Single(condition.Values));
                    if (route == "single") Assert.Equal(1, expression.TopCount);
                    if (route == "blood") Assert.Equal("createdon", Assert.Single(expression.Orders).AttributeName);
                }
            }).Returns(page);
            var repository = new EntityRepository(sdk.Object);
            var queryService = new QueryService(repository, sdk.Object);
            var listService = new MarketingListService(repository, sdk.Object);
            if (duplicate)
            {
                var failure = Assert.Throws<InvalidOperationException>(() => Invoke(route, queryService, listService));
                var repositoryFailure = Assert.IsType<InvalidOperationException>(failure.InnerException);
                Assert.Equal("The entity collection could not be retrieved.", repositoryFailure.Message);
                Assert.IsType<InvalidOperationException>(repositoryFailure.InnerException);
                Assert.Equal(2, page.Entities.Count);
            }
            else
            {
                var actual = Invoke(route, queryService, listService);
                if (route == "single" || route == "blood") Assert.Same(page.Entities[0], actual);
                else Assert.Same(page, actual);
            }
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只選擇真實產品方法，不在 fixture 重建查詢或模擬 repository 呼叫。</summary>
        private static object Invoke(string route, QueryService service, MarketingListService lists)
        {
            if (route == "single") return service.RetrieveEntityByField("contact", "fullname", "合成同名");
            if (route == "collection") return service.RetrieveEntityCollectionByField("contact", "fullname", "合成同名");
            if (route == "fetch") return service.QueryByFetchXml("<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>");
            if (route == "blood") return service.QueryBloodReportByContactId(ContactId);
            if (route == "present") return service.QueryPresentRecordByContactIdAndSunday(ListId, ContactId, 3);
            return lists.RetrieveMembersByListId(ListId);
        }
    }
}
