// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ContactServiceTests
// 主要成員：RetrieveByLineId_WhenContactExists_ShouldReturnEntity、RetrieveCollectionByName_ShouldReturnCollection
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.ContactOperations、ToolUtility.Tests.TestHelpers、ToolUtilityNameSpace.EntityOperations、Moq、System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    public class ContactServiceTests
    {
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
