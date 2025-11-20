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
