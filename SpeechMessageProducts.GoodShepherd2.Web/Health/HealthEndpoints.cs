/*
檔案整體說明
目的與定位：提供好牧人 2.0 F2 的存活及 D365 WhoAmI 就緒端點；不洩露組織或使用者識別，也不代表平台 SQL／LINE 已就緒。
主要功能與流程：live 無 I/O 固定 200。ready 以本次呼叫 token 取得短期 SDK 租約，驗證 WhoAmI 三個非空身分，再完整歸還後發布 200。任何取得、SDK、標記或釋放故障均回安全 503 並交共用診斷；不確定連線先標記淘汰。
使用邊界與維護：管理器只使用啟動時驗證的固定產品／組織鍵，HTTP 不提供憑證、租戶或路由權限。本端點只借用 manager／diagnostics，單次 lease 在 finally 唯一歸還；沒有共享回應快取、識別保留或額外 task。同步 SDK 無可取消介面，因此不以 Task.Run／提前逾時冒充排空，不在 SDK 尚使用時釋放租約；只在操作前後檢查 caller 取消。測試故障、正常取消及清理順序；離線結果不是實機連線證據。
*/
using System.Runtime.ExceptionServices;
using Microsoft.Crm.Sdk.Messages;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Health;

/// <summary>F2 狹義健康入口；所有成功資訊僅為 HTTP 狀態，不回傳 WhoAmI 或連線設定。</summary>
public static class HealthEndpoints
{
    /// <summary>映射無 I/O 的 live 與每次重新借用 SDK 的 ready，不使用任何 request 身分作連線鍵。</summary>
    /// <param name="endpoints">當次主機擁有的端點集合。</param>
    /// <returns>原端點集合。</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/health/live", () => Results.Ok());
        endpoints.MapGet("/health/ready", CheckReady);
        return endpoints;
    }

    /// <summary>完成 WhoAmI 及租約釋放才發布 ready；失敗只回 503，正常 caller 取消保留原取消例外。</summary>
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
