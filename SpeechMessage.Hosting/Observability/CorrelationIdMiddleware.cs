/*
檔案整體說明
目的與定位：F11共用HTTP關聯編號middleware，供產品主機在錯誤處理及產品路由外側建立有界ILogger scope，不承擔登入或租戶判斷。
主要功能與流程：僅接受單一X-Correlation-Id值，依純值規則拒絕不合法字元及長度後新生編號，設定本次TraceIdentifier與安全回應標頭，再以CorrelationId欄位開scope並等待下一層。
使用邊界與維護：middleware只保存部署的RequestDelegate及無身分logger，不保存HttpContext、caller標頭或scope。scope由當次await所在using確定釋放，例外／取消仍傳給既有錯誤owner；不建立AsyncLocal、背景task、timer或快取。此編號是追查提示不是授權。應在所有路由之前註冊，Webhook背景事件仍以自身事件編號另建獨立scope。
*/
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SpeechMessage.Hosting.Observability;

/// <summary>只有本次HTTP請求持有scope；不讓request或可變身分逃離當次執行。</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

    /// <summary>建立並最終釋放同一關聯scope；多值標頭完整拒絕，回應僅含驗證後的安全編號。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var values = context.Request.Headers["X-Correlation-Id"];
        var id = CorrelationIds.ForHttp(values.Count == 1 ? values[0] : null);
        context.TraceIdentifier = id;
        context.Response.Headers["X-Correlation-Id"] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context).ConfigureAwait(false);
    }
}
