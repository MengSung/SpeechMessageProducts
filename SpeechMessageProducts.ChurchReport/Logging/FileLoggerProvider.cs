// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Logging/FileLoggerProvider.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 FileLoggerProvider 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class FileLoggerConfiguration、class FileLogger、class FileLoggerProvider
// 主要成員：IsEnabled、CreateLogger、Dispose、MinimumLevel、FileName
// 引用命名空間：Microsoft.Extensions.Logging、System、System.Collections.Concurrent、System.IO
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace ChurchReport.Logging
{
    public class FileLoggerConfiguration
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
        internal readonly object Lock = new object();
        public string FileName { get; set; } = "ChurchReportLog.txt";
    }

    internal class FileLogger : ILogger
    {
        private readonly string _name;
        private readonly FileLoggerConfiguration _config;

        public FileLogger(string name, FileLoggerConfiguration config)
        {
            _name = name;
            _config = config;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _config.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var logRecord = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {logLevel:u3} {_name}: {message}";
            if (exception != null)
            {
                logRecord += Environment.NewLine + exception;
            }

            try
            {
                // Determine target file path. If config.FileName is absolute, use it directly.
                var targetPath = _config.FileName ?? "log.txt";
                string filePath;
                if (Path.IsPathRooted(targetPath))
                {
                    filePath = targetPath;
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                else
                {
                    var logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDir);
                    filePath = Path.Combine(logDir, targetPath);
                }

                lock (_config.Lock)
                {
                    File.AppendAllText(filePath, logRecord + Environment.NewLine);
                }
            }
            catch
            {
                // swallow to avoid crashing app due to logging
            }
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggerConfiguration _config;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers;

        public FileLoggerProvider() : this(new FileLoggerConfiguration()) { }

        public FileLoggerProvider(string fileName) : this(new FileLoggerConfiguration { FileName = fileName }) { }

        public FileLoggerProvider(FileLoggerConfiguration config)
        {
            _config = config ?? new FileLoggerConfiguration();
            _loggers = new ConcurrentDictionary<string, FileLogger>();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _config));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }
}
