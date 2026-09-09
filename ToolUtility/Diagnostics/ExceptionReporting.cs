using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Diagnostics;

/// <summary>
/// 舊式非 DI 呼叫端的程序級轉接器，只保存一個診斷 owner、不保存 request／user 狀態。
/// 每個 Host 以 using 註冊並解除；禁止同程序同時綁定多個產品或收件人而任意選路。
/// 共用模組應在最終失敗邊界呼叫，重試中的可恢復失敗不在此上報。
/// </summary>
public static class ExceptionReporting
{
    private static ExceptionDiagnostics _current;

    /// <summary>取得目前 owner 是否可用，供 legacy 告警在未啟動 Host 的工具中明確辨識狀態。</summary>
    public static bool IsActive => Volatile.Read(ref _current) != null;

    /// <summary>綁定唯一 owner 並註冊全域終端失敗；回傳的 registration 必須先於 owner 釋放。</summary>
    public static IDisposable Attach(ExceptionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (Interlocked.CompareExchange(ref _current, diagnostics, null) != null)
            throw new InvalidOperationException("An exception diagnostics owner is already attached.");
        return new Registration(diagnostics);
    }

    /// <summary>
    /// 上報可行動失敗，CallerMemberName 只提供編譯期方法名稱；不得傳入動態個資作 operation。
    /// 未綁定時只印固定碼，不自行建立未受控 writer／LINE client。
    /// </summary>
    public static void Report(Exception exception, [CallerMemberName] string operation = "",
        CancellationToken cancellationToken = default)
    {
        var current = Volatile.Read(ref _current);
        if (current != null) current.Report(exception, operation, cancellationToken);
        else
        {
            try { Console.Error.WriteLine("[ExceptionDiagnostics] OwnerNotAttached"); } catch { }
        }
    }

    /// <summary>全域事件生命週期 owner；不呼叫 SetObserved，不改寫 CLR 的未處理例外政策。</summary>
    private sealed class Registration : IDisposable
    {
        private ExceptionDiagnostics _owner;

        /// <summary>owner 綁定成功後才註冊；實例最長只存續至 Host 結束。</summary>
        public Registration(ExceptionDiagnostics owner)
        {
            _owner = owner;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }

        /// <summary>程序即將終止只保證盡力同步落檔；LINE 入列不等於可在程序結束前送達。</summary>
        private void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
        {
            Volatile.Read(ref _owner)?.Report(args.ExceptionObject as Exception, "Process.UnhandledException");
        }

        /// <summary>未被 await 的 task fault 是可行動失敗；不保存 task 或例外以外的執行狀態。</summary>
        private void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Volatile.Read(ref _owner)?.Report(args.Exception, "Task.UnobservedException");
        }

        /// <summary>先解除 static event，再移除同一 owner；重複 Dispose 不影響後續 Host 的綁定。</summary>
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
            Interlocked.CompareExchange(ref _current, null, owner);
        }
    }
}
