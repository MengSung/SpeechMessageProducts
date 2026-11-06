/*
 * 檔案整體說明
 *
 * 目的與定位：搬移前於原 host 執行的集合特性；假 SDK 僅替代網路，實際服務產生的條件與回傳由測試斷言。 三份人工黃金檔鎖定欄位啟用條件、週報日期／排序及批次字典刻意沒有 statecode 的差異。 合成 ID 
 * 不對應正式記錄；每個案例只保存自己的查詢，沒有共享可變狀態或外部資源。
 *
 * 主要功能與流程：
 * 本檔宣告 CollectionCharacterizationTests。
 * - 合成資料庫 ID，並非依姓名或測試列位置生成的產品識別。
 * - 同名但身份不同的第二筆正式記錄，用以防止按顯示文字合併。
 * - 同步及 Task 包裝都必須保留 QueryByAttribute 的原條件與 AllColumns，不增加背景排程。
 * - 週報維持 Execute/RetrieveMultiple、四條件及日期升冪；固定合成主日避免機器時間干擾。
 * 測試案例入口包含 FieldQuery_PreservesGoldenAndOwnsEachResult、WeeklyQuery_PreservesGoldenAndExecuteRoute、
 * DictionaryBatch_PreservesGoldenAndSameNameRecords、Page、Serialize。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ToolUtility.Tests/DataOperations/CollectionLifecycleTests.cs、
 * ToolUtility.Tests/DataOperations/CollectionSafetyTests.cs、
 * ToolUtility.Tests/DataOperations/OptimizedQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/OptimizedQuerySafetyTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.CollectionOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 搬移前於原 host 執行的集合特性；假 SDK 僅替代網路，實際服務產生的條件與回傳由測試斷言。
    /// 三份人工黃金檔鎖定欄位啟用條件、週報日期／排序及批次字典刻意沒有 statecode 的差異。
    /// 合成 ID 不對應正式記錄；每個案例只保存自己的查詢，沒有共享可變狀態或外部資源。
    /// </summary>
    public sealed class CollectionCharacterizationTests
    {
        /// <summary>合成資料庫 ID，並非依姓名或測試列位置生成的產品識別。</summary>
        internal static readonly Guid FirstId = new Guid("11111111-1111-1111-1111-111111111111");
        /// <summary>同名但身份不同的第二筆正式記錄，用以防止按顯示文字合併。</summary>
        internal static readonly Guid SecondId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>同步及 Task 包裝都必須保留 QueryByAttribute 的原條件與 AllColumns，不增加背景排程。</summary>
        [Fact]
        public async Task FieldQuery_PreservesGoldenAndOwnsEachResult()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryByAttribute>(input);
                GoldenQueryAssertions.Equal("collection-field.xml", new XElement("query",
                    new XAttribute("entity", query.EntityName), new XAttribute("all", query.ColumnSet.AllColumns),
                    query.Attributes.Select((field, index) => new XElement("condition",
                        new XAttribute("field", field), new XAttribute("value", query.Values[index])))).ToString());
                Assert.Null(query.TopCount);
                return Page("contact");
            });
            var service = new CollectionQueryService(new object(), sdk.Object);
            var sync = service.RetrieveEntityCollectionByField("contact", "new_scope", "synthetic");
            var pending = service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", "synthetic");
            Assert.True(pending.IsCompleted);
            var asyncResult = await pending;
            Assert.Equal(new[] { FirstId, SecondId }, sync.Entities.Select(row => row.Id));
            Assert.Equal(sync.Entities.Select(row => row.Id), asyncResult.Entities.Select(row => row.Id));
            sync.Entities.Clear();
            Assert.Equal(2, asyncResult.Entities.Count);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
        }

        /// <summary>週報維持 Execute/RetrieveMultiple、四條件及日期升冪；固定合成主日避免機器時間干擾。</summary>
        [Fact]
        public void WeeklyQuery_PreservesGoldenAndExecuteRoute()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                var query = Assert.IsType<QueryExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query);
                GoldenQueryAssertions.Equal("collection-weekly.xml", Serialize(query).ToString());
                return new RetrieveMultipleResponse { Results = { ["EntityCollection"] = Page(query.EntityName) } };
            });
            var result = new CollectionQueryService(new object(), sdk.Object)
                .QueryWeeklyReportBeforeTowMonthOfSunday(new DateTime(2026, 11, 1), FirstId);
            Assert.Equal(new[] { FirstId, SecondId }, result.Entities.Select(row => row.Id));
            sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>字典批次只含正式 ID 的 IN 條件，不偷偷加入停用過濾；同名不同 ID 全部回傳。</summary>
        [Fact]
        public async Task DictionaryBatch_PreservesGoldenAndSameNameRecords()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                GoldenQueryAssertions.Equal("collection-dictionary.xml", Serialize(query).ToString());
                Assert.Equal(5000, query.PageInfo.Count);
                Assert.Equal(1, query.PageInfo.PageNumber);
                return Page("contact");
            });
            var result = await new CollectionQueryService(new object(), sdk.Object)
                .RetrieveBatchByIdsAsync("contact", new List<Guid> { FirstId, SecondId });
            Assert.Equal(2, result.Count);
            Assert.Equal("same-name", result[FirstId]["fullname"]);
            Assert.Equal("same-name", result[SecondId]["fullname"]);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>SDK 假替身每次回新頁，正式 ID 不變；呼叫端可修改自己的結果，來源不保存返回物件。</summary>
        internal static EntityCollection Page(string name) => new EntityCollection(new[]
        {
            new Entity(name, FirstId) { ["fullname"] = "same-name" },
            new Entity(name, SecondId) { ["fullname"] = "same-name" }
        });

        /// <summary>只把實際 query 的穩定語義序列化；黃金檔另以手工固定值建立，不呼叫產品建構器產預期值。</summary>
        private static XElement Serialize(QueryExpression query) => new XElement("query",
            new XAttribute("entity", query.EntityName), new XAttribute("all", query.ColumnSet.AllColumns),
            new XAttribute("filter", query.Criteria.FilterOperator),
            query.Criteria.Conditions.Select(condition => new XElement("condition",
                new XAttribute("field", condition.AttributeName), new XAttribute("op", condition.Operator),
                condition.Values.Select(value => new XElement("value", value)))),
            query.Orders.Select(order => new XElement("order", new XAttribute("field", order.AttributeName),
                new XAttribute("direction", order.OrderType))));
    }
}
