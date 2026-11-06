/*
 * 檔案整體說明
 *
 * 目的與定位：保護舊host相容入口確實沿原SDK查詢，結果與資料庫身份不被adapter或快取改寫。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactServiceTests。
 * - SDK注入一筆合成LINE資料；斷言單筆非null及原LINE值，不能用空實作通過。
 * 測試案例入口包含 RetrieveByLineId_WhenContactExists_ShouldReturnEntity、RetrieveCollectionByName_ShouldReturnCollection。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtility.Tests.ContactOperations
{
    /// <summary>保護舊host相容入口確實沿原SDK查詢，結果與資料庫身份不被adapter或快取改寫。</summary>
    public class ContactServiceTests
    {
        /// <summary>SDK注入一筆合成LINE資料；斷言單筆非null及原LINE值，不能用空實作通過。</summary>
        [Fact]
        public void RetrieveByLineId_WhenContactExists_ShouldReturnEntity()
        {
            var expected = TestEntityFactory.CreateContact("U123456", "測試聯絡人");

            // ContactService 直接以 IOrganizationService.RetrieveMultiple 查詢，故替身建立在該邊界上。
            var mockQueryService = MockOrganizationServiceFactory.CreateMockWithCollection(
                new EntityCollection(new[] { expected }));

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new ContactService(mockLogger.Object, mockQueryService.Object);

            var result = service.RetrieveByLineId("U123456");

            result.Should().NotBeNull();
            Assert.NotNull(result);
            result["new_lineid"].Should().Be("U123456");
        }

        [Fact]
        public void RetrieveCollectionByName_ShouldReturnCollection()
        {
            var collection = new EntityCollection(new[]
            {
                TestEntityFactory.CreateContact("U123", "A"),
                TestEntityFactory.CreateContact("U456", "B")
            });

            // 同上：受測邊界是 IOrganizationService，不是已淘汰的 IEntityQueryService。
            var mockQueryService = MockOrganizationServiceFactory.CreateMockWithCollection(collection);

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new ContactService(mockLogger.Object, mockQueryService.Object);

            var result = service.RetrieveCollectionByName("A");

            result.Entities.Count.Should().Be(2);
        }
    }
}
