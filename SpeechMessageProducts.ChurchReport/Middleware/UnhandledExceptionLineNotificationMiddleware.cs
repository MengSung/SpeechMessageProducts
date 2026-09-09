using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport.Middleware;

/// <summary>
/// HTTP 最終失敗轉接器；必須位於 UseExceptionHandler 的內側，先落檔與排入通知再 rethrow。
/// 只持有組合根 owner 與管線委派；所有 request 狀態僅存在本次 InvokeAsync，不流入背景工作。
/// </summary>
public sealed class UnhandledExceptionLineNotificationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ExceptionDiagnostics _diagnostics;

    /// <summary>DI 注入借用的診斷 owner，不由 middleware 釋放。</summary>
    public UnhandledExceptionLineNotificationMiddleware(RequestDelegate next, ExceptionDiagnostics diagnostics)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>正常 client 取消不報，逾時與真正故障先落檔；永遠保留同一個原始例外。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context).ConfigureAwait(false); }
        catch (Exception exception)
        {
            _diagnostics.Report(exception, "Http.UnhandledException", context.RequestAborted);
            throw;
        }
    }
}
