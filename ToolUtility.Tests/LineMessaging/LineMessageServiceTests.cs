// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessageServiceTests
// 主要成員：CreatePushMessage_ShouldCallCreateEntity
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.LineMessaging、ToolUtility.Tests.TestHelpers、Moq、ToolUtilityNameSpace.EntityOperations、System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtility.Tests.TestHelpers;
using Moq;
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.LineMessaging
{
    public class LineMessageServiceTests
    {
        [Fact]
        public void CreatePushMessage_ShouldCallCreateEntity()
        {
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new LineMessageService(mockLogger.Object, mockCrm.Object);

            service.CreatePushMessage("U123", "sub", "hello");

            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
        }
    }
}
