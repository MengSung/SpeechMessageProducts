// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/LoggerInvoker.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class LoggerInvoker、class LoggerAccessor
// 主要成員：LogError、LogWarning、Log、Create、Invoke
// 引用命名空間：System、System.Collections.Concurrent、System.Linq、System.Reflection、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 統一封裝不同型別 logger 的呼叫方式。
    /// 這裡會優先使用已知的 <see cref="ILogger"/>，
    /// 若呼叫端傳入的是其他 logger 實作，則快取反射結果後再重複使用，
    /// 避免在高頻錯誤記錄路徑上每次重新做反射掃描。
    /// </summary>
    internal static class LoggerInvoker
    {
        private static readonly Func<string, Exception, string> StringFormatter =
            static (state, _) => state ?? string.Empty;

        private static readonly ConcurrentDictionary<Type, LoggerAccessor> Accessors = new();

        /// <summary>
        /// 以 Error 等級寫入訊息與例外。
        /// </summary>
        public static void LogError(object logger, Exception exception, string message)
        {
            Log(logger, LogLevel.Error, exception, message);
        }

        /// <summary>
        /// 以 Warning 等級寫入訊息。
        /// </summary>
        public static void LogWarning(object logger, string message)
        {
            Log(logger, LogLevel.Warning, null, message);
        }

        /// <summary>
        /// 實際執行 logger 呼叫。
        /// 若是標準 <see cref="ILogger"/> 直接走型別安全路徑，
        /// 否則改走快取後的反射呼叫。
        /// </summary>
        private static void Log(object logger, LogLevel level, Exception exception, string message)
        {
            if (logger == null)
            {
                return;
            }

            if (logger is ILogger typedLogger)
            {
                typedLogger.Log(level, exception, "{Message}", message);
                return;
            }

            Accessors.GetOrAdd(logger.GetType(), static type => LoggerAccessor.Create(type))
                .Invoke(logger, level, exception, message);
        }

        private sealed class LoggerAccessor
        {
            private readonly MethodInfo _method;
            private readonly object _errorLevel;
            private readonly object _warningLevel;
            private readonly object _eventId;

            public static readonly LoggerAccessor Noop = new(null, null, null, null);

            private LoggerAccessor(MethodInfo method, object errorLevel, object warningLevel, object eventId)
            {
                _method = method;
                _errorLevel = errorLevel;
                _warningLevel = warningLevel;
                _eventId = eventId;
            }

            /// <summary>
            /// 建立特定 logger 型別對應的反射存取器。
            /// 成功後會被快取，下次同型別可直接重用。
            /// </summary>
            public static LoggerAccessor Create(Type loggerType)
            {
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.IsGenericMethodDefinition && m.GetParameters().Length == 5);

                if (logMethod == null)
                {
                    return Noop;
                }

                var constructedMethod = logMethod.MakeGenericMethod(typeof(string));
                var parameters = constructedMethod.GetParameters();

                if (parameters.Length != 5)
                {
                    return Noop;
                }

                object errorLevel;
                object warningLevel;
                object eventId;

                try
                {
                    errorLevel = Enum.Parse(parameters[0].ParameterType, nameof(LogLevel.Error));
                    warningLevel = Enum.Parse(parameters[0].ParameterType, nameof(LogLevel.Warning));
                }
                catch
                {
                    return Noop;
                }

                try
                {
                    eventId = Activator.CreateInstance(parameters[1].ParameterType, 0, string.Empty)
                        ?? Activator.CreateInstance(parameters[1].ParameterType);
                }
                catch
                {
                    try
                    {
                        eventId = Activator.CreateInstance(parameters[1].ParameterType);
                    }
                    catch
                    {
                        return Noop;
                    }
                }

                return new LoggerAccessor(constructedMethod, errorLevel, warningLevel, eventId);
            }

            /// <summary>
            /// 呼叫已快取的反射方法。
            /// </summary>
            public void Invoke(object logger, LogLevel level, Exception exception, string message)
            {
                if (_method == null)
                {
                    return;
                }

                try
                {
                    _method.Invoke(
                        logger,
                        new object[]
                        {
                            level == LogLevel.Error ? _errorLevel : _warningLevel,
                            _eventId,
                            message ?? string.Empty,
                            exception,
                            StringFormatter
                        });
                }
                catch
                {
                    // 忽略日誌失敗，避免反過來拖垮主要交易路徑。
                }
            }
        }
    }
}
