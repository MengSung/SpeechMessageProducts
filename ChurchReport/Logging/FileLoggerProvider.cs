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
