// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class EntityCrudServiceTests
// 主要成員：CreateEntity_ShouldReturnGuid、UpdateEntity_ShouldCallClient、DeleteEntity_ShouldCallClient
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.EntityOperations、ToolUtility.Tests.TestHelpers、ToolUtilityNameSpace.Interfaces、Microsoft.Xrm.Sdk、Moq、System
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
using Moq;
using System;

namespace ToolUtility.Tests.EntityOperations
{
    public class EntityCrudServiceTests
    {
        [Fact]
        public void CreateEntity_ShouldReturnGuid()
        {
            var entity = TestEntityFactory.CreateEmpty("contact");
            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            var id = service.CreateEntity(entity);

            id.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public void UpdateEntity_ShouldCallClient()
        {
            var entity = TestEntityFactory.CreateEmpty("contact");
            entity["fullname"] = "new name";

            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.UpdateEntity(entity);

            mockClient.Verify(x => x.Update(It.Is<Entity>(e => e == entity)), Times.Once);
        }

        [Fact]
        public void DeleteEntity_ShouldCallClient()
        {
            var id = Guid.NewGuid();
            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.DeleteEntity("contact", id);

            mockClient.Verify(x => x.Delete("contact", id), Times.Once);
        }
    }
}
