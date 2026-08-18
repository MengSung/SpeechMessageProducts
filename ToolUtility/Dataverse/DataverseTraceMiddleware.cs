using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 在驗證完成後建立 Dataverse Trace request 範圍的 HTTP middleware。它只暫時保存當前 HttpContext 的
/// TraceIdentifier 與已 HMAC 假名化的使用者關聯；不保存 HttpContext、Session、Claims 或任何 CRM 資料，
/// 並在 response 結束時以 using 確定性還原 AsyncLocal，防止跨 request 身分或 lease 關聯洩漏。
/// </summary>
public sealed class DataverseTraceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DataverseTrace _trace;

    /// <summary>
    /// 建立 middleware。Trace 為 singleton 資源擁有者，僅由其背景工作負責檔案 I/O；
    /// middleware 不配置或釋放任何 pooled client、lease 或 request scope。
    /// </summary>
    public DataverseTraceMiddleware(RequestDelegate next, DataverseTrace trace)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    /// <summary>
    /// 在開關關閉時立即委派下一個 middleware，不讀取 User、Session 或建立 trace 物件；
    /// 開啟時於 Authentication 之後建立範圍，確保 user 假名以已驗證的伺服器端身分為來源。
    /// </summary>
    public Task Invoke(HttpContext context)
    {
        if (!_trace.Enabled)
            return _next(context);

        return InvokeWithTrace(context);
    }

    private async Task InvokeWithTrace(HttpContext context)
    {
        using (_trace.BeginRequest(context))
            await _next(context).ConfigureAwait(false);
    }
}
