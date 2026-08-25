// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SaveIntegrateBackgroundUploadRunner.cs
// 檔案責任：執行 SaveIntegrate 已接受的背景上傳，明確擁有背景 DI/ambient/trace scope，並只寫入安全的結構化結果。
// 隔離與生命週期：本 runner 不持有 Controller、HttpContext、Session 或來源週報圖；所有輸入都是已隔離副本或短命委派，
// 且 DI/ambient/trace scope 皆由 using 在成功與失敗路徑確定釋放，避免跨使用者或跨 request 保留。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ServiceModel;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers;

/// <summary>
/// 執行已接受的 SaveIntegrate 背景作業，並以固定 schema 紀錄每個生命週期階段的結果。
/// </summary>
/// <remarks>
/// runner 只接受程式碼建立的 operationId 與不含 request 狀態的委派。它不保存任何輸入；工作完成後
/// closure、背景 scope 與隔離週報副本均失去擁有者。<c>bg.end</c> 只表示 trace scope 已釋放，真正的
/// CRM 成功證據只能是 <c>stage=upload</c>、<c>outcome=succeeded</c> 的 <c>bg.outcome</c> 事件。
/// </remarks>
internal static class SaveIntegrateBackgroundUploadRunner
{
    /// <summary>
    /// 依序建立背景 scope、解析 ToolUtility、執行上傳並清理隔離副本。
    /// </summary>
    /// <param name="operationId">由 request 端建立的 opaque 關聯識別碼，不得含帳密、身分或 CRM 資料。</param>
    /// <param name="trace">目前程序的診斷 trace；停用時可為 null，工作語意不受影響。</param>
    /// <param name="createScope">建立背景工作唯一擁有的 DI scope。</param>
    /// <param name="beginAmbientScope">將 legacy CRM 解析綁定到該背景 scope，離開時必須還原。</param>
    /// <param name="resolveProvider">由背景 scope 解析 ToolUtility provider。</param>
    /// <param name="uploadAsync">只操作隔離週報副本的 CRM 上傳工作。</param>
    /// <param name="cleanup">只清理隔離週報副本的短暫記憶體收尾動作。</param>
    /// <param name="safeDiagnostic">只接收固定例外型別名稱的診斷接收器，不得將例外文字寫入一般 trace。</param>
    internal static async Task RunAsync(
        string operationId,
        DataverseTrace trace,
        Func<IServiceScope> createScope,
        Func<IServiceProvider, IDisposable> beginAmbientScope,
        Func<IServiceProvider, IToolUtilityProvider> resolveProvider,
        Func<Task> uploadAsync,
        Action cleanup,
        Action<string> safeDiagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(createScope);
        ArgumentNullException.ThrowIfNull(beginAmbientScope);
        ArgumentNullException.ThrowIfNull(resolveProvider);
        ArgumentNullException.ThrowIfNull(uploadAsync);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(safeDiagnostic);

        var stage = "scope-create";
        using var traceScope = trace?.BeginBackgroundOperation("SaveIntegrate.Upload");
        try
        {
            using var scope = createScope();
            using var ambientScope = beginAmbientScope(scope.ServiceProvider);

            stage = "provider-resolve";
            var provider = resolveProvider(scope.ServiceProvider);
            stage = "toolutility-resolve";
            _ = provider.GetToolUtility();

            stage = "upload";
            await uploadAsync().ConfigureAwait(false);
            trace?.RecordBackgroundOutcome(operationId, stage, "succeeded", string.Empty);

            stage = "cleanup";
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                trace?.RecordBackgroundOutcome(operationId, stage, "failed", GetErrorClass(exception));
                safeDiagnostic(exception.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            trace?.RecordBackgroundOutcome(operationId, stage, "failed", GetErrorClass(exception));
            safeDiagnostic(exception.GetType().Name);
        }
    }

    /// <summary>
    /// 將例外映射成固定且不含敏感內容的背景 errorClass。
    /// </summary>
    /// <remarks>
    /// 不得回傳 exception.Message、stack trace 或任何 CRM payload。分類完成後 runner 不保存例外參考，
    /// 因此例外與可能含敏感內容的物件會在目前背景工作結束後由 GC 回收。
    /// </remarks>
    /// <param name="exception">目前階段捕捉到的例外。</param>
    /// <returns>DataverseTrace 所允許的固定 errorClass。</returns>
    internal static string GetErrorClass(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "canceled",
            TimeoutException => "timeout",
            CommunicationException => "crm-fault",
            InvalidOperationException => "dependency-resolution",
            _ => "unexpected"
        };
    }
}
