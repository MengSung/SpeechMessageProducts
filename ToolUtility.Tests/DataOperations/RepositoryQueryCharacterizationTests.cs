/*
 * 檔案整體說明
 *
 * 目的與定位：五個舊Repository查詢的完整投影及回傳特性，先在原host執行；只替代外部repository，不重建產品條件。 特別保留舊present使用本地時間、忽略list條件的差異，
 * 不在抽取時冒充教會時鐘或授權修正。
 *
 * 主要功能與流程：
 * 本檔宣告 RepositoryQueryCharacterizationTests、FixedClock。
 * - 完整黃金查詢保留欄位、條件型別、排序、top與原Fetch字串；動態本地日期另驗呼叫前後界線。
 * - 注入固定LocalNow可穩定重現舊DateTime.Now.AddMonths條件，時鐘不得改變原本的Local型別。
 * - 原始Fetch逐字保留，不能在安全解析後重新序列化而改變caller已簽章或診斷比較的查詢。
 * - 固定五入口及合成scope，參數只供獨立拒絕測試變體，不作伺服器授權。
 * 測試案例入口包含 OriginalQueries_PreserveProjectionAndResult、
 * PresentQuery_UsesInjectedLocalClockWithoutChangingDateSemantics、Read。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.QueryOperations;
using ToolUtilityNameSpace.TimeOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 五個舊Repository查詢的完整投影及回傳特性，先在原host執行；只替代外部repository，不重建產品條件。
    /// 特別保留舊present使用本地時間、忽略list條件的差異，不在抽取時冒充教會時鐘或授權修正。
    /// </summary>
    public sealed class RepositoryQueryCharacterizationTests
    {
        /// <summary>完整黃金查詢保留欄位、條件型別、排序、top與原Fetch字串；動態本地日期另驗呼叫前後界線。</summary>
        [Theory]
        [InlineData("single")] [InlineData("collection")] [InlineData("fetch")] [InlineData("blood")] [InlineData("present")]
        public void OriginalQueries_PreserveProjectionAndResult(string entry)
        {
            var before = DateTime.Now;
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            var expected = new EntityCollection();
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                string table;
                if (entry == "fetch")
                {
                    Assert.Equal(Fetch, Assert.IsType<FetchExpression>(input).Query);
                    table = "contact";
                }
                else
                {
                    var query = Assert.IsType<QueryExpression>(input);
                    table = query.EntityName;
                    var projection = XElement.Parse(RelationshipQueryCharacterizationTests.Describe(query));
                    if (entry == "present")
                    {
                        var date = Assert.IsType<DateTime>(Assert.Single(query.Criteria.Conditions[1].Values));
                        Assert.Equal(DateTimeKind.Local, date.Kind);
                        Assert.InRange(date, before.AddMonths(-3), DateTime.Now.AddMonths(-3));
                        projection.Descendants("condition").Last().Element("value")!.Value = "CURRENT_LOCAL";
                    }
                    GoldenQueryAssertions.Equal("repository-query-" + entry + ".xml", projection.ToString());
                }
                expected.Entities.Add(new Entity(table, ContactCharacterizationTests.FirstId) { ["fullname"] = "same-name" });
                if (entry != "single") expected.Entities.Add(new Entity(table, ContactCharacterizationTests.SecondId) { ["fullname"] = "same-name" });
                expected.MoreRecords = true; expected.PagingCookie = "synthetic"; expected.TotalRecordCount = 41;
                return expected;
            });
            var result = Read(new QueryService(repository.Object, new Mock<IOrganizationService>(MockBehavior.Strict).Object), entry);
            Assert.Same(entry == "single" || entry == "blood" ? expected.Entities[0] : expected, result);
            Assert.Equal(entry == "single" ? 1 : 2, expected.Entities.Count);
            Assert.True(expected.MoreRecords);
            Assert.Equal("synthetic", expected.PagingCookie);
            Assert.Equal(41, expected.TotalRecordCount);
            repository.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>注入固定LocalNow可穩定重現舊DateTime.Now.AddMonths條件，時鐘不得改變原本的Local型別。</summary>
        [Fact]
        public void PresentQuery_UsesInjectedLocalClockWithoutChangingDateSemantics()
        {
            var localNow = new DateTime(2026, 11, 6, 9, 30, 0, DateTimeKind.Local);
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                Assert.Equal(localNow.AddMonths(-3), Assert.IsType<DateTime>(
                    Assert.Single(query.Criteria.Conditions[1].Values)));
                Assert.Equal(DateTimeKind.Local, Assert.IsType<DateTime>(
                    Assert.Single(query.Criteria.Conditions[1].Values)).Kind);
                return new EntityCollection();
            });
            var service = new QueryService(repository.Object, new Mock<IOrganizationService>(MockBehavior.Strict).Object,
                new FixedClock(localNow));
            Assert.Empty(service.QueryPresentRecordByContactIdAndSunday(ContactCharacterizationTests.SecondId,
                ContactCharacterizationTests.FirstId, 3).Entities);
        }

        /// <summary>原始Fetch逐字保留，不能在安全解析後重新序列化而改變caller已簽章或診斷比較的查詢。</summary>
        internal const string Fetch = "<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>";

        /// <summary>固定五入口及合成scope，參數只供獨立拒絕測試變體，不作伺服器授權。</summary>
        internal static object? Read(QueryService service, string entry, Guid? id = null, string entity = "contact",
            string field = "fullname", string fetch = Fetch, int months = 3) => entry switch
        {
            "single" => service.RetrieveEntityByField(entity, field, "synthetic-private"),
            "collection" => service.RetrieveEntityCollectionByField(entity, field, "synthetic-private"),
            "fetch" => service.QueryByFetchXml(fetch),
            "blood" => service.QueryBloodReportByContactId(id ?? ContactCharacterizationTests.FirstId),
            "present" => service.QueryPresentRecordByContactIdAndSunday(ContactCharacterizationTests.SecondId,
                id ?? ContactCharacterizationTests.FirstId, months),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };

        /// <summary>測試用不可變時鐘不捕捉request或使用者scope，Utc與Local明確保持Kind。</summary>
        private sealed class FixedClock : IClock
        {
            /// <summary>建立不變的UTC及本地測試瞬間。</summary>
            public FixedClock(DateTime localNow) => LocalNow = localNow;

            /// <summary>固定有效UTC瞬間，避免依賴測試程序時區。</summary>
            public DateTime UtcNow => new DateTime(2026, 11, 6, 1, 30, 0, DateTimeKind.Utc);

            /// <summary>固定且不變的本地測試瞬間。</summary>
            public DateTime LocalNow { get; }
        }
    }
}
