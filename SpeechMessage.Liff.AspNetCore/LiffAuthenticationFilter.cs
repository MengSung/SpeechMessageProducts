/*
檔案整體說明
目的與定位：F6共用的端點驗證filter，把單次Authorization ID Token轉成伺服器已驗租戶actor；不使用登入Cookie或Session。
主要功能與流程：唯一Bearer標頭經官方verifier後交產品scoped resolver；有效actor只在next執行期間放入Items，所有成功、拒絕、取消與故障路徑finally移除。預期token拒絕401，缺actor403，真正服務故障交共用diagnostics後503。
使用邊界與維護：Login Channel與Messaging Channel來自Host部署快照，Items中既有未驗身分先清除，不接受caller聲稱的tenant、角色或Channel。filter只借用DI服務，不關閉client、manager或diagnostics；無共享身分或token快取，原始錯誤、姓名及token不得回傳或寫log。測試確認next期間scope有效且完成後歸零，端點自身例外仍交外層Host處理。
*/
using Microsoft.AspNetCore.Http;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>單次HTTP的LIFF驗證與actor發布邊界；不建立Session、Cookie或背景工作。</summary>
/// <param name="verifier">只借用DI管理的官方token驗證器，不保留當次token。</param>
/// <param name="resolver">單一request scope的產品授權解析器。</param>
/// <param name="options">伺服器部署的公開Channel映射快照。</param>
/// <param name="diagnostics">Host唯一安全故障入口；不由filter處置。</param>
public sealed class LiffAuthenticationFilter(
    LiffIdTokenVerifier verifier, ILiffActorResolver resolver, LiffOptions options,
    ExceptionDiagnostics diagnostics) : IEndpointFilter
{
    /// <summary>本請求端點消費的唯一actor鍵；不是跨請求快取索引。</summary>
    public const string ActorItemKey = "LiffActor";

    /// <summary>單次驗證與借入身分的擁有範圍；next結束或失敗時都必須解除Items中的actor。</summary>
    /// <param name="context">本次端點呼叫，不保存在任何欄位。</param>
    /// <param name="next">借用本次actor的後續端點，不能把actor保存到跨請求服務。</param>
    /// <returns>端點結果或固定安全拒絕；不回傳token或供應商錯誤本文。</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        http.Items.Remove(ActorItemKey);
        try
        {
            http.RequestAborted.ThrowIfCancellationRequested();
            var headers = http.Request.Headers.Authorization;
            if (headers.Count != 1 || headers[0] is not { } header ||
                !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            var token = header[7..];
            if (token.Length is 0 or > 16384 || token.Any(c => c <= ' ' || c > '~' || c == ','))
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            if (!options.IsConfigured) return Results.StatusCode(StatusCodes.Status403Forbidden);

            LiffIdentity identity;
            try
            {
                identity = await verifier.VerifyAsync(token, options.LoginChannelId, http.RequestAborted).ConfigureAwait(false);
            }
            catch (LiffVerificationException exception)
            {
                if (!exception.IsServiceFailure) return Results.StatusCode(StatusCodes.Status401Unauthorized);
                diagnostics.Report(exception.InnerException ?? exception, "Liff.Authentication.Verify", http.RequestAborted);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            LiffActor? actor;
            try
            {
                actor = await resolver.ResolveAsync(identity, http.RequestAborted).ConfigureAwait(false);
                http.RequestAborted.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                diagnostics.Report(exception, "Liff.Authentication.Resolve", http.RequestAborted);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            if (actor is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            http.Items[ActorItemKey] = actor;
            return await next(context).ConfigureAwait(false);
        }
        finally { http.Items.Remove(ActorItemKey); }
    }
}
