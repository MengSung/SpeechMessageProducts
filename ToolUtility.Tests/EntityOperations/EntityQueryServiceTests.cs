// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class EntityQueryServiceTests
// 主要成員：RetrieveEntity_WhenEntityExists_ShouldReturnEntity、RetrieveMultiple_WhenQueryValid_ShouldReturnCollection
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.EntityOperations、ToolUtility.Tests.TestHelpers、ToolUtilityNameSpace.Interfaces、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Moq
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;
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
            var mockClient = MockCrmClientFactory.CreateMockWithEntity(expected);

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

            var mockClient = MockCrmClientFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new EntityQueryService(mockLogger.Object, mockClient.Object);

            var query = new QueryByAttribute("contact");

            var result = service.RetrieveMultiple(query);

            result.Entities.Count.Should().Be(2);
        }
    }
}
