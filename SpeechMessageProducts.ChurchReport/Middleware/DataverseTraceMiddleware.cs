using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using ToolUtilityNameSpace.Dataverse;

namespace ChurchReport.Middleware;

/// <summary>
/// 將 ChurchReport 的 ASP.NET Core request 邊界轉接至共用 Dataverse Trace。此產品層 middleware
/// 是唯一讀取 <see cref="HttpContext"/>、Claims 與 Session 的位置；它不保存 HttpContext、
/// Session、原始身分或 CRM 資料，也不擁有 Trace 的背景佇列、檔案、lease 或 pooled client。
/// 啟用時建立的 AsyncLocal 範圍最長只存續至下一個 middleware 完成，並由 using 確定性還原，
/// 防止 request、使用者、profile 或 tenant 關聯洩漏到後續工作。
/// </summary>
public sealed class DataverseTraceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DataverseTrace _trace;

    /// <summary>
    /// 建立產品層 Trace 轉接器。下一個 middleware 與 singleton Trace 由 ASP.NET Core 容器提供；
    /// 本型別不建立或釋放共享資源，因此短命 request 不會 Dispose 長命 Trace 或 connection pool。
    /// </summary>
    /// <param name="next">目前 request 管線中的下一個委派。</param>
    /// <param name="trace">集中負責 HMAC 假名化、事件佇列與檔案生命週期的共用 Trace。</param>
    public DataverseTraceMiddleware(RequestDelegate next, DataverseTrace trace)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    /// <summary>
    /// 處理單一 HTTP request。Trace 關閉時直接委派下一層，不讀取 User、Session 或配置物件；
    /// 開啟時才擷取 TraceIdentifier、已驗證名稱與 Session Id，並原樣交給 ToolUtility 集中假名化。
    /// </summary>
    /// <param name="context">目前 ASP.NET Core request；僅在本次呼叫期間讀取，不被任何長命物件保存。</param>
    /// <returns>代表後續管線與 Trace 範圍皆已完成的工作。</returns>
    public Task Invoke(HttpContext context)
    {
        if (!_trace.Enabled)
            return _next(context);

        return InvokeWithTrace(context);
    }

    /// <summary>
    /// 在已啟用 Trace 的慢路徑建立 request 範圍。Session feature 採 null-safe 讀取，避免未啟用
    /// Session 的 Host 發生例外；using 會在成功、失敗或取消時一致還原 AsyncLocal。
    /// </summary>
    private async Task InvokeWithTrace(HttpContext context)
    {
        var sessionId = context.Features.Get<ISessionFeature>()?.Session?.Id;
        using (_trace.BeginRequest(context.TraceIdentifier, context.User?.Identity?.Name, sessionId))
            await _next(context).ConfigureAwait(false);
    }
}
