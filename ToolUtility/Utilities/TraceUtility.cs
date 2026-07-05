// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Utilities/TraceUtility.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class TraceUtility
// 主要成員：TraceByLevel、TraceByLevelLegacy
// 引用命名空間：System、System.Diagnostics、System.Linq
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Diagnostics;
using System.Linq;

namespace ToolUtilityNameSpace.Utilities
{
    public static class TraceUtility
    {
        public static void TraceByLevel(object logger, int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            if (totalLevel < qualifiedLevel) return;

            // Try to invoke ILogger.Log<TState>(...) via reflection to avoid compile-time dependency
            try
            {
                if (logger != null)
                {
                    var loggerType = logger.GetType();
                    var logMethod = loggerType.GetMethods()
                        .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                    if (logMethod != null)
                    {
                        var genericMethod = logMethod.MakeGenericMethod(typeof(object));

                        // Try to get LogLevel enum type from Microsoft.Extensions.Logging.Abstractions
                        var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                        object debugLevel = null;
                        if (logLevelType != null)
                        {
                            debugLevel = Enum.Parse(logLevelType, "Debug");
                        }

                        // Create EventId instance if available
                        object eventId = null;
                        var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                        if (eventIdType != null)
                        {
                            // EventId has constructors: EventId(int id) and EventId(int id, string name)
                            eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                        }

                        // state object - use anonymous object or plain message
                        object state = stringToProcess ?? string.Empty;

                        // formatter delegate: Func<object, Exception, string>
                        Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;

                        // If we couldn't resolve logLevel or eventId types, attempt to call with nulls
                        var args = new object[] { debugLevel, eventId, state, null, formatter };

                        genericMethod.Invoke(logger, args);
                        return;
                    }
                }
            }
            catch
            {
                // ignore and fallback
            }

            // Fallback to Debug output
            Debug.WriteLine($"Time: {DateTime.Now}");
            Debug.WriteLine($"Message: {stringToProcess}");
            Debug.WriteLine($"StackTrace: {new StackTrace(new StackFrame(1, true))}");
        }

        // Legacy fallback using Debug (for backward compatibility)
        public static void TraceByLevelLegacy(int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            if (totalLevel < qualifiedLevel) return;

            Debug.WriteLine($"Time: {DateTime.Now}");
            Debug.WriteLine($"Message: {stringToProcess}");
            Debug.WriteLine($"StackTrace: {new StackTrace(new StackFrame(1, true))}");
        }
    }
}
