/*
 * 檔案整體說明
 *
 * 目的與定位：提供舊文字檔ILogger provider、logger及設定，將啟用層級的訊息交給檔案輸出；必須與正式Exception.log告警owner區分，
 * 檔案路徑與writer生命週期由主機設定及provider管理。
 *
 * 主要功能與流程：
 * 本檔宣告 FileLoggerConfiguration、FileLogger、FileLoggerProvider。
 * 主要操作入口包含 IsEnabled、CreateLogger、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Program.cs 追查目前工作樹中的合作／使用位置。
 */
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
