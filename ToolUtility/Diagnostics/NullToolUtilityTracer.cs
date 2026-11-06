/*
 * 檔案整體說明
 *
 * 目的與定位：Trace 停用時的零配置、零輸出追蹤器。
 *
 * 主要功能與流程：
 * 本檔宣告 NullToolUtilityTracer。
 * - 忽略追蹤事件；不讀取訊息、不建立檔案、不觸碰全域 listener。
 * 主要操作入口包含 Write、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/ActivityHostOperations/ActivityCallerRouteTests.cs、
 * ToolUtility.Tests/AttachmentOperations/AttachmentCallerRouteTests.cs、
 * ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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
