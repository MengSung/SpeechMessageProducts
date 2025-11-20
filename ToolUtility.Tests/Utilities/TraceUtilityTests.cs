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
