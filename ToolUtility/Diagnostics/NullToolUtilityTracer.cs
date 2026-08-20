// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/NullToolUtilityTracer.cs
// 檔案責任：提供 Trace 停用時的零副作用 IToolUtilityTracer 實作。
// 生命週期責任：本型別不持有 stream、writer、listener、timer、task 或使用者狀態，
//               Dispose 只為 DI/測試生命週期提供冪等的空操作。
// 安全邊界：任何 request、Session、tenant 或 credential 訊息都不會被保存或輸出。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System.Diagnostics;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// Trace 停用時的零配置、零輸出追蹤器。
    /// </summary>
    public sealed class NullToolUtilityTracer : IToolUtilityTracer, System.IDisposable
    {
        /// <summary>
        /// 忽略追蹤事件；不讀取訊息、不建立檔案、不觸碰全域 listener。
        /// </summary>
        /// <param name="totalLevel">呼叫端總層級，停用時不使用。</param>
        /// <param name="qualifiedLevel">呼叫端門檻，停用時不使用。</param>
        /// <param name="message">可能含敏感資料的訊息，停用時不讀取。</param>
        /// <param name="callerFrame">呼叫端堆疊，停用時不讀取。</param>
        public void Write(int totalLevel, int qualifiedLevel, string message, StackFrame callerFrame)
        {
        }

        /// <summary>釋放空追蹤器；方法冪等且沒有任何資源需要清理。</summary>
        public void Dispose()
        {
        }
    }
}
