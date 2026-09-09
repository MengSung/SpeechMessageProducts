using System;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport.Logging;

/// <summary>
/// Debug／Release 共用 Error/Critical 轉接器。只借用組合根診斷 owner，不保存 logger scope、
/// state 或 formatter（可能含使用者資料），也不自行建立 category cache 或檔案 handle。
/// </summary>
public sealed class ExceptionLoggerProvider : ILoggerProvider
{
    private readonly ExceptionDiagnostics _diagnostics;

    /// <summary>診斷 owner 由 Program 管理，provider Dispose 不得提前關閉背景 sender。</summary>
    public ExceptionLoggerProvider(ExceptionDiagnostics diagnostics) =>
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    /// <summary>每個 logger 只保存受信任的程式 category，禁止以姓名／tenant／路徑實值作 category。</summary>
    public ILogger CreateLogger(string categoryName) => new ExceptionLogger(_diagnostics, categoryName);

    /// <summary>不持有需釋放的資源；Program 在所有 Host logging 結束後統一 drain。</summary>
    public void Dispose() { }

    /// <summary>即時轉接單筆錯誤，不持有 HTTP、例外或任意泛型 state。</summary>
    private sealed class ExceptionLogger : ILogger
    {
        private readonly ExceptionDiagnostics _diagnostics;
        private readonly string _category;

        /// <summary>DI logger factory 的 category 為程式擁有的型別名稱。</summary>
        public ExceptionLogger(ExceptionDiagnostics diagnostics, string category)
        {
            _diagnostics = diagnostics;
            _category = category;
        }

        /// <summary>刻意不追蹤 scope，避免將 request 的 credential 或識別資訊存入長命 logger。</summary>
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>資訊／警告不代表最終失敗；Error/Critical 無論是否附 Exception 都必須落檔。</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Error or LogLevel.Critical;

        /// <summary>不呼叫 formatter；無 Exception 的錯誤以 category 和 EventId 定位，不複製動態資料。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (IsEnabled(logLevel)) _diagnostics.Report(exception, _category + ".Event" + eventId.Id);
        }
    }
}
