/*
 * 檔案整體說明
 *
 * 目的與定位：在 EntityOperations 搬移前鎖定三個代表方法的完整 SDK 請求與結果語義。 所有 ID、名稱與資料為合成值；只替換外部 SDK，不建立網路、快取或使用者狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 EntityQueryCharacterizationTests。
 * - 單筆查詢必須保留實體名稱、資料庫 ID 及 AllColumns；不得意外走 host cache。 回傳同一操作擁有的 Entity，原有欄位不遺失。
 * - 欄位查詢保留 TopCount=1、完整欄位、業務欄位與啟用條件；特殊文字必為型別化值， 不能因搬移變成 XML 插入或把顯示名稱當唯一識別碼。
 * - 工作查詢保留 100 筆上限、原欄位投影、createdon倒序、主旨與statecode條件。 完整 XML 與獨立黃金檔比較；同名不同資料庫 ID 兩筆工作均保留。
 * 測試案例入口包含 RetrieveEntity_PreservesIdColumnsAndResult、RetrieveEntityByField_PreservesTypedConditions、
 * RetrieveTaskBySubject_MatchesGoldenAndPreservesSameNameRows。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 在 EntityOperations 搬移前鎖定三個代表方法的完整 SDK 請求與結果語義。
    /// 所有 ID、名稱與資料為合成值；只替換外部 SDK，不建立網路、快取或使用者狀態。
    /// </summary>
    public sealed class EntityQueryCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 單筆查詢必須保留實體名稱、資料庫 ID 及 AllColumns；不得意外走 host cache。
        /// 回傳同一操作擁有的 Entity，原有欄位不遺失。
        /// </summary>
        [Fact]
        public void RetrieveEntity_PreservesIdColumnsAndResult()
        {
            var expected = new Entity("contact", RecordId) { ["fullname"] = "Synthetic Contact" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns))).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntity("contact", RecordId));
            client.VerifyAll();
        }

        /// <summary>
        /// 欄位查詢保留 TopCount=1、完整欄位、業務欄位與啟用條件；特殊文字必為型別化值，
        /// 不能因搬移變成 XML 插入或把顯示名稱當唯一識別碼。
        /// </summary>
        [Fact]
        public void RetrieveEntityByField_PreservesTypedConditions()
        {
            const string value = "Synthetic <&\"' value";
            var expected = new Entity("contact", RecordId);
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("contact", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.True(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "fullname", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { value, 0 }, actual.Values);
                }).Returns(new EntityCollection(new[] { expected }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntityByField("contact", "fullname", value));
            client.VerifyAll();
        }

        /// <summary>
        /// 工作查詢保留 100 筆上限、原欄位投影、createdon倒序、主旨與statecode條件。
        /// 完整 XML 與獨立黃金檔比較；同名不同資料庫 ID 兩筆工作均保留。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = new EntityCollection(new[]
            {
                new Entity("task", RecordId) { ["subject"] = "Synthetic Task" },
                new Entity("task", new Guid("22222222-2222-4222-8222-222222222222")) { ["subject"] = "Synthetic Task" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => GoldenQueryAssertions.Equal("entity-task.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = expected;
                    return response;
                });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var actual = service.RetrieveTaskBySubject("Synthetic Task");
                Assert.Same(expected, actual);
                Assert.Equal(2, actual.Entities.Count);
                Assert.NotEqual(actual.Entities[0].Id, actual.Entities[1].Id);
            }
        }
    }
}
