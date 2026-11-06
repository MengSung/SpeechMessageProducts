/*
 * 檔案整體說明
 *
 * 目的與定位：保護EntityQueryService的既有行為與相容性。受測元件的責任為：不保存查詢結果的純 SDK 實體讀取器；授權、組織與服務身分必須由 host 先驗證。 每次建立操作局部候選，整頁穩定 ID 
 * 驗證後才回傳，不以名稱去重或發布部分結果。
 *
 * 主要功能與流程：
 * 本檔宣告 EntityQueryServiceTests。
 * 測試案例入口包含 RetrieveEntity_WhenEntityExists_ShouldReturnEntity、
 * RetrieveMultiple_WhenQueryValid_ShouldReturnCollection。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using System;

namespace ToolUtility.Tests.EntityOperations
{
    public class EntityQueryServiceTests
    {
        [Fact]
        public void RetrieveEntity_WhenEntityExists_ShouldReturnEntity()
        {
            var expected = TestEntityFactory.CreateContact("U123", "測試");
            var mockClient = MockOrganizationServiceFactory.CreateMockWithEntity(expected);

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new EntityQueryService(mockLogger.Object, mockClient.Object);

            var result = service.RetrieveEntity("contact", expected.Id);

            result.Should().NotBeNull();
            result.Id.Should().Be(expected.Id);
        }

        [Fact]
        public void RetrieveMultiple_WhenQueryValid_ShouldReturnCollection()
        {
            var collection = new EntityCollection(new[]
            {
                TestEntityFactory.CreateContact("U123", "測試1"),
                TestEntityFactory.CreateContact("U456", "測試2")
            });

            var mockClient = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new EntityQueryService(mockLogger.Object, mockClient.Object);

            var query = new QueryByAttribute("contact");

            var result = service.RetrieveMultiple(query);

            result.Entities.Count.Should().Be(2);
        }
    }
}
