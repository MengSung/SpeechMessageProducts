/*
 * 檔案整體說明
 *
 * 目的與定位：以假logger驗證追蹤層級過濾及ILogger委派，避免低於門檻的診斷仍產生輸出。
 *
 * 主要功能與流程：
 * 本檔宣告 TraceUtilityTests。
 * 測試案例入口包含 TraceByLevel_WhenLoggerProvided_ShouldUseILogger、TraceByLevel_WhenLevelTooLow_ShouldNotLog。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
