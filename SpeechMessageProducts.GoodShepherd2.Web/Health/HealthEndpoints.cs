/*
檔案整體說明
目的與定位：提供好牧人2.0的無IO存活與F11四項就緒端點，只發布固定失敗名稱，不洩組織、使用者、連線字串或秘密；舊F2同步WhoAmI方法保留來源相容但不再映射HTTP。
主要功能與流程：live無I/O固定200。HTTP ready等待中立四probe逐一完成D365 WhoAmI、平台SQL select1、queue容量與Channel狀態檢查，只有皆成功才200；失敗503只列固定項目並禁止快取。舊同步CheckReady仍完整保留F2租約歸還與安全告警契約，但不是HTTP映射。
使用邊界與維護：管理器只使用啟動時驗證的固定產品／組織鍵，HTTP 不提供憑證、租戶或路由權限。本端點只借用 manager／diagnostics，單次 lease 在 finally 唯一歸還；沒有共享回應快取、識別保留或額外 task。同步 SDK 無可取消介面，因此不以 Task.Run／提前逾時冒充排空，不在 SDK 尚使用時釋放租約；只在操作前後檢查 caller 取消。測試故障、正常取消及清理順序；離線結果不是實機連線證據。
*/
using System.Runtime.ExceptionServices;
using SpeechMessage.Hosting.Observability;
using Microsoft.Crm.Sdk.Messages;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Health;

/// <summary>F11四項健康HTTP入口及舊F2來源相容方法；回應不含WhoAmI、識別或連線設定。</summary>
public static class HealthEndpoints
{
    /// <summary>映射無I/O的live與每次重新執行四probe的ready，不以request身分選擇部署或連線鍵。</summary>
    /// <param name="endpoints">當次主機擁有的端點集合。</param>
    /// <returns>原端點集合。</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/health/live", () => Results.Ok());
        endpoints.MapGet("/health/ready", CheckReadyAsync);
        return endpoints;
    }

    /// <summary>F11實際HTTP入口：四項皆健康才200；失敗只列固定名稱，HTTP正常取消仍原樣傳播。</summary>
    public static async Task<IResult> CheckReadyAsync(ReadinessChecks checks, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var failed = await checks.CheckAsync(cancellationToken).ConfigureAwait(false);
        return failed.Count == 0 ? Results.Ok() : Results.Json(new { failed }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>舊F2狹義WhoAmI相容入口，非HTTP路由；完成租約釋放才回結果，正常caller取消保留原例外。</summary>
    /// <param name="manager">已依固定產品及組織註冊的主機 manager，只借用不關閉。</param>
    /// <param name="diagnostics">共用安全告警入口，只傳真實例外與固定 operation。</param>
    /// <param name="cancellationToken">HTTP caller 的取消訊號；不是 SDK 內部逾時的替代分類。</param>
    /// <returns>所有操作及清理成功才回 200，真正失敗回不帶身分資料的 503。</returns>
    public static IResult CheckReady(IDataverseConnectionManager manager,
        ExceptionDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        IClientLease? lease = null;
        Exception? primary = null;
        try
        {
            lease = manager.Acquire(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.Service.Execute(new WhoAmIRequest()) is not WhoAmIResponse identity ||
                identity.UserId == Guid.Empty || identity.OrganizationId == Guid.Empty ||
                identity.BusinessUnitId == Guid.Empty)
                throw new InvalidOperationException("WhoAmI returned an incomplete identity.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            primary = exception;
            diagnostics.Report(exception, "GoodShepherd2.Health.Ready", cancellationToken);
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception failure) { diagnostics.Report(failure, "GoodShepherd2.Health.Ready.Fault"); }
            }
        }
        finally
        {
            if (lease is not null)
            {
                try { lease.Dispose(); }
                catch (Exception failure)
                {
                    diagnostics.Report(failure, "GoodShepherd2.Health.Ready.Release");
                    primary ??= failure;
                }
            }
        }
        if (primary is OperationCanceledException && cancellationToken.IsCancellationRequested)
            ExceptionDispatchInfo.Capture(primary).Throw();
        return primary is null ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
