using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace ChurchReport.Logging
{
    /// <summary>
    /// Trace Logger Provider
    /// 將 ILogger 的輸出導向 System.Diagnostics.Trace，使其能寫入 Trace.log
    /// 僅在 Debug 組態下啟用（透過 Startup.cs 的條件編譯控制）
    /// </summary>
    public class TraceLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// 建立 Logger 實例
        /// </summary>
        /// <param name="categoryName">Logger 類別名稱</param>
        /// <returns>TraceLogger 實例</returns>
        public ILogger CreateLogger(string categoryName)
        {
            return new TraceLogger(categoryName);
        }

        /// <summary>
        /// 釋放資源（無需釋放）
        /// </summary>
        public void Dispose()
        {
            // Trace 已由 Program.cs 管理，這裡不需要額外處理
        }

        /// <summary>
        /// Trace Logger 實作
        /// 將日誌訊息寫入 System.Diagnostics.Trace
        /// </summary>
        private class TraceLogger : ILogger
        {
            private readonly string _categoryName;

            public TraceLogger(string categoryName)
            {
                _categoryName = categoryName;
            }

            /// <summary>
            /// 開始邏輯作業範圍（不實作）
            /// </summary>
            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            /// <summary>
            /// 檢查是否啟用指定的日誌等級
            /// </summary>
            public bool IsEnabled(LogLevel logLevel)
            {
                // 只記錄 Information 以上等級的日誌（避免過多 Debug/Trace 訊息）
                return logLevel >= LogLevel.Information;
            }

            /// <summary>
            /// 寫入日誌
            /// </summary>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                try
                {
                    // 格式化日誌訊息
                    var message = formatter?.Invoke(state, exception);
                    if (string.IsNullOrEmpty(message))
                    {
                        return;
                    }

                    // 建立完整的日誌訊息（包含時間戳、等級、類別）
                    var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {message}";

                    // 如果有例外，附加例外資訊
                    if (exception != null)
                    {
                        logMessage += Environment.NewLine + exception.ToString();
                    }

                    // 寫入 Trace（會被 Program.cs 的 TextWriterTraceListener 捕捉並寫入 Trace.log）
                    Trace.WriteLine(logMessage);
                }
                catch
                {
                    // 避免日誌記錄失敗影響主要業務流程
                    // 靜默處理錯誤
                }
            }
        }
    }
}
