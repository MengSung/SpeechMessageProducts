// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/TestHelpers/MockLoggerFactory.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class MockLoggerFactory
// 主要成員：CreateNonGenericMock
// 引用命名空間：Microsoft.Extensions.Logging、Moq
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
