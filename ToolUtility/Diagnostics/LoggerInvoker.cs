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
