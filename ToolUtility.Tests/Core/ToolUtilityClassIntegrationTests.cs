// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ToolUtilityClassIntegrationTests
// 主要成員：RetrieveContactByLineId_ShouldDelegateToContactService、SetEntityBoolAttribute_ShouldDelegateToAttributeService
// 引用命名空間：Xunit、FluentAssertions、Moq、System、Microsoft.Xrm.Sdk、ToolUtilityNameSpace.Core、ToolUtility.Tests.TestHelpers、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Xunit;
using FluentAssertions;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Core;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtility.Tests.Core
{
    public class ToolUtilityClassIntegrationTests
    {
        [Fact]
        public void RetrieveContactByLineId_ShouldDelegateToContactService()
        {
            // Arrange
            var expected = TestEntityFactory.CreateContact("U123", "測試");
            var collection = new EntityCollection(new[] { expected });

            var mockCrm = MockCrmClientFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            // Act
            var result = facade.RetrieveContactByLineId("U123");

            // Assert
            result.Should().NotBeNull();
            result["new_lineid"].Should().Be("U123");
        }

        [Fact]
        public void SetEntityBoolAttribute_ShouldDelegateToAttributeService()
        {
            // Arrange
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            var entity = new Entity("contact");

            // Act
            facade.SetEntityBoolAttribute(ref entity, "new_testflag", true);

            // Assert
            facade.GetEntityBoolAttribute(entity, "new_testflag").Should().BeTrue();
        }
    }
}
