// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/Utilities/TraceUtilityTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class TraceUtilityTests
// 主要成員：TraceByLevel_WhenLoggerProvided_ShouldUseILogger、TraceByLevel_WhenLevelTooLow_ShouldNotLog
// 引用命名空間：Xunit、Moq、Microsoft.Extensions.Logging、ToolUtilityNameSpace.Utilities、ToolUtility.Tests.TestHelpers、System
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace.Utilities;
using ToolUtility.Tests.TestHelpers;
using System;

namespace ToolUtility.Tests.Utilities
{
    public class TraceUtilityTests
    {
        [Fact]
        public void TraceByLevel_WhenLoggerProvided_ShouldUseILogger()
        {
            // Arrange
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            int totalLevel = 5;
            int qualifiedLevel = 3;
            string message = "測試訊息";

            // Act
            TraceUtility.TraceByLevel(mockLogger.Object, totalLevel, qualifiedLevel, message);

            // Assert - verify LogDebug was called at least once
            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Debug),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void TraceByLevel_WhenLevelTooLow_ShouldNotLog()
        {
            // Arrange
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            // Act
            TraceUtility.TraceByLevel(mockLogger.Object, totalLevel: 2, qualifiedLevel: 5, "不應記錄");

            // Assert
            mockLogger.Verify(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }
    }
}
