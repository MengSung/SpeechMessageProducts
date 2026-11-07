/*
檔案整體說明

目的與定位：提供 SpeechMessage.Hosting 對既有 ToolUtility.Diagnostics owner 的唯一
相容轉接，讓 Host／middleware／legacy 呼叫端共用同一個 ExceptionDiagnostics，而不
複製 Exception.log writer、LINE queue 或靜態事件訂閱。

主要功能與流程：
本檔宣告 ExceptionReportingAdapter。建構時以既有 owner 綁定
ExceptionReporting；Dispose 時先解除 static registration，再由 DI／組合根依既有
順序釋放 diagnostics。adapter 只保存 owner 參考，不保存 HttpContext、Session、
tenant、credential、logger scope、request exception 或使用者輸入。

使用邊界與維護：
呼叫端必須以 Host／DI 擁有 adapter，且 adapter 的生命週期不可短於任何使用
ExceptionReporting 的 middleware 或 logger。重複綁定會 fail closed；重複 Dispose
不會解除其他 Host 的 owner。此檔不擁有被注入的 diagnostics，除非由同一個 DI
container 依註冊順序負責其最終 Dispose。
*/
using System;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting;

/// <summary>
/// 將 Host 已擁有的 <see cref="ExceptionDiagnostics"/> 綁定至既有
/// <see cref="ExceptionReporting"/> 靜態相容入口。
/// </summary>
/// <remarks>
/// 本類別只保存 owner 參考與 registration，不保存 request、Session、tenant、
/// credential、logger state 或例外本文。Dispose 先解除 registration；diagnostics
/// 的 writer、queue、CTS、mutex 與 sender 由其唯一 owner 按既有生命週期清理。
/// </remarks>
public sealed class ExceptionReportingAdapter : IDisposable
{
    private readonly ExceptionDiagnostics _diagnostics;
    private IDisposable? _registration;

    /// <summary>建立唯一相容綁定；若程序已有其他 owner，立即拒絕第二個 owner。</summary>
    /// <param name="diagnostics">由 Host／DI 擁有的 diagnostics owner。</param>
    public ExceptionReportingAdapter(ExceptionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
        _registration = ExceptionReporting.Attach(diagnostics);
    }

    /// <summary>取得被借用的唯一 diagnostics owner，不轉移其 Dispose 所有權。</summary>
    public ExceptionDiagnostics Diagnostics => _diagnostics;

    /// <summary>
    /// 先解除程序級事件與靜態 owner，再允許容器釋放 diagnostics；重複呼叫安全。
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }
}
