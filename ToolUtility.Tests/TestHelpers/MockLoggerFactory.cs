using Microsoft.Extensions.Logging;
using Moq;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// Mock Logger 工廠
    /// 用於產生測試用的 ILogger Mock 物件
    /// </summary>
    public static class MockLoggerFactory
    {
        /// <summary>
        /// 建立一個 Mock ILogger&lt;T&gt; 實例
        /// </summary>
        /// <typeparam name="T">Logger 的類型參數</typeparam>
        /// <returns>Mock ILogger 實例</returns>
        public static Mock<ILogger<T>> CreateMock<T>()
        {
            var mock = new Mock<ILogger<T>>();
            
            // 預設不驗證 Log 呼叫（避免測試過於嚴格）
            mock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Verifiable();
            
            return mock;
        }

        /// <summary>
        /// 建立一個 Mock ILogger 實例（非泛型）
        /// </summary>
        public static Mock<ILogger> CreateNonGenericMock()
        {
            var mock = new Mock<ILogger>();
            
            mock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Verifiable();
            
            return mock;
        }
    }
}
