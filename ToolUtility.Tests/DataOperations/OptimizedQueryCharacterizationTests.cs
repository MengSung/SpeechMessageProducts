/*
 * 檔案整體說明
 *
 * 目的與定位：原 host 優化查詢的三個代表特性：精確欄位單筆、帶啟用條件的 top 查詢與不含啟用條件的批次。 先在未搬移實作跑綠；黃金值只使用合成資料庫 ID，假 SDK 僅替代網路、不產生預期查詢。
 *
 * 主要功能與流程：
 * 本檔宣告 OptimizedQueryCharacterizationTests。
 * - 單筆必須維持 caller 欄位與正式 ID，不能悄悄改成 AllColumns 或另一筆查詢。
 * - 保留原欄位等值、啟用條件與 top；同名但不同 ID 的結果必須完整保留。
 * - 字典版本不得偷偷加 statecode；沿用第一頁 5000 上限及 AllColumns 的完整 golden。
 * - 只正規化語義等價的 AND 巢狀節點，保留欄位／值及順序；OR 不能在 golden 中被誤扁平。 測試輸入固定小樹，不處理外來不可信查詢或產生產品預期值。
 * 測試案例入口包含 Retrieve_PreservesColumnsAndIdentity、Condition_PreservesGoldenAndSameNameRecords、
 * Batch_PreservesGoldenAndDictionaryIdentity、FlattenAnd。
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
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 原 host 優化查詢的三個代表特性：精確欄位單筆、帶啟用條件的 top 查詢與不含啟用條件的批次。
    /// 先在未搬移實作跑綠；黃金值只使用合成資料庫 ID，假 SDK 僅替代網路、不產生預期查詢。
    /// </summary>
    public sealed class OptimizedQueryCharacterizationTests
    {
        /// <summary>單筆必須維持 caller 欄位與正式 ID，不能悄悄改成 AllColumns 或另一筆查詢。</summary>
        [Fact]
        public void Retrieve_PreservesColumnsAndIdentity()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var id = CollectionCharacterizationTests.FirstId;
            sdk.Setup(item => item.Retrieve("contact", id, It.IsAny<ColumnSet>())).Returns(
                (string name, Guid key, ColumnSet columns) =>
                {
                    GoldenQueryAssertions.Equal("optimized-retrieve.xml", new XElement("retrieve",
                        new XAttribute("entity", name), new XAttribute("id", key),
                        new XAttribute("all", columns.AllColumns),
                        columns.Columns.Select(column => new XElement("column", column))).ToString());
                    return new Entity(name, key) { ["fullname"] = "synthetic" };
                });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var result = service.RetrieveEntity("contact", id, "fullname");
            Assert.Equal(id, result.Id);
            Assert.Equal("synthetic", result["fullname"]);
            sdk.VerifyAll();
        }

        /// <summary>保留原欄位等值、啟用條件與 top；同名但不同 ID 的結果必須完整保留。</summary>
        [Fact]
        public void Condition_PreservesGoldenAndSameNameRecords()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                GoldenQueryAssertions.Equal("optimized-condition.xml", new XElement("query",
                    new XAttribute("entity", query.EntityName), new XAttribute("top", query.TopCount!),
                    new XAttribute("all", query.ColumnSet.AllColumns),
                    FlattenAnd(query.Criteria).Select(condition => new XElement("condition",
                        new XAttribute("field", condition.AttributeName), new XAttribute("op", condition.Operator),
                        condition.Values.Select(value => new XElement("value", value))))).ToString());
                return CollectionCharacterizationTests.Page("contact");
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var result = service.RetrieveByFieldValue("contact", "new_scope", "synthetic", 7);
            Assert.Equal(new[] { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId },
                result.Entities.Select(row => row.Id));
            Assert.All(result.Entities, row => Assert.Equal("same-name", row["fullname"]));
        }

        /// <summary>字典版本不得偷偷加 statecode；沿用第一頁 5000 上限及 AllColumns 的完整 golden。</summary>
        [Fact]
        public void Batch_PreservesGoldenAndDictionaryIdentity()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                GoldenQueryAssertions.Equal("collection-dictionary.xml", new XElement("query",
                    new XAttribute("entity", query.EntityName), new XAttribute("all", query.ColumnSet.AllColumns),
                    new XAttribute("filter", query.Criteria.FilterOperator),
                    query.Criteria.Conditions.Select(condition => new XElement("condition",
                        new XAttribute("field", condition.AttributeName), new XAttribute("op", condition.Operator),
                        condition.Values.Select(value => new XElement("value", value))))).ToString());
                Assert.Equal(5000, query.PageInfo.Count);
                Assert.Equal(1, query.PageInfo.PageNumber);
                return CollectionCharacterizationTests.Page("contact");
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var result = service.RetrieveBatch("contact", new List<Guid>
                { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId });
            Assert.Equal(2, result.Count);
            Assert.Equal("same-name", result[CollectionCharacterizationTests.FirstId]["fullname"]);
            Assert.Equal("same-name", result[CollectionCharacterizationTests.SecondId]["fullname"]);
        }

        /// <summary>
        /// 只正規化語義等價的 AND 巢狀節點，保留欄位／值及順序；OR 不能在 golden 中被誤扁平。
        /// 測試輸入固定小樹，不處理外來不可信查詢或產生產品預期值。
        /// </summary>
        private static IEnumerable<ConditionExpression> FlattenAnd(FilterExpression filter)
        {
            Assert.Equal(LogicalOperator.And, filter.FilterOperator);
            foreach (var child in filter.Filters)
                foreach (var condition in FlattenAnd(child)) yield return condition;
            foreach (var condition in filter.Conditions) yield return condition;
        }
    }
}
