// ============================================================================
// 檔案：SpeechMessage.Dynamics.Embedded/EmbeddedHostAdapter.cs
// 用途：以同一套 RequestGuard 與受控 ControlPlane executor 提供產品內 Embedded 呼叫入口。
//
// 生命週期與隔離契約：
// 1. Adapter 是無狀態 singleton；不保存 request、使用者、Session、Credential、endpoint、permit 或 client。
// 2. 每次呼叫先同步執行 RequestGuard 與固定 ProfileAlias 比對，再委派既有受控 executor。
// 3. 下游 executor 是 Admission、generation runtime 與 Data8 pool 的唯一資源 owner；Adapter 不建立 CTS、timer、
//    Task cache、HTTP handler 或第二條 Connector 路徑，因此 Embedded 僅省略 HTTP，絕不繞過控制面。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Guard;

namespace SpeechMessage.Dynamics.Embedded;

/// <summary>
/// 提供同程序 Embedded 的受控 Dynamics 操作轉接器。
/// 此型別固定於 host 啟動時選定的一個 ProfileAlias，拒絕任何 request-time alias 覆寫；所有通過
/// Guard 的請求皆完整委派給既有 ControlPlane executor，故 organization admission、profile resolver、
/// connector router 與 generation-owned Data8 pool 仍依原本順序執行。Adapter 不擁有下游 executor，
/// DI container 關閉時由組合根依其既有 owner 順序 drain 與 dispose，避免雙重釋放或跨 Session 保留。
/// </summary>
public sealed class EmbeddedHostAdapter : IDynamicsOperationExecutor
{
    private readonly IRequestGuard _requestGuard;
    private readonly IDynamicsOperationExecutor _controlledExecutor;
    private readonly string _profileAlias;

    /// <summary>
    /// 建立只使用固定部署 Profile 的 Embedded 入口。建構時只複製已驗證的 alias scalar，不讀取 Gateway endpoint、
    /// 不解析 credential，也不建立任何 transport 資源；這確保 Visual Studio F5 的 Embedded 模式不會悄悄變成
    /// Gateway fallback 或將秘密複製到 singleton graph。
    /// </summary>
    /// <param name="requestGuard">Gateway 亦使用的同步、無 I/O 請求防線。</param>
    /// <param name="controlledExecutor">
    /// 已完成 ProfileResolver、Organization Admission、IConnectorRouter 與 Pool 組合的下游 executor；
    /// 其生命週期由 composition root 擁有，Adapter 絕不可 Dispose。
    /// </param>
    /// <param name="profileAlias">此 host generation 唯一允許的部署端 ProfileAlias。</param>
    public EmbeddedHostAdapter(
        IRequestGuard requestGuard,
        IDynamicsOperationExecutor controlledExecutor,
        string profileAlias)
    {
        _requestGuard = requestGuard ?? throw new ArgumentNullException(nameof(requestGuard));
        _controlledExecutor = controlledExecutor ?? throw new ArgumentNullException(nameof(controlledExecutor));
        _profileAlias = string.IsNullOrWhiteSpace(profileAlias)
            ? throw new ArgumentException("Embedded ProfileAlias is required.", nameof(profileAlias))
            : profileAlias.Trim();
    }

    /// <summary>
    /// 先執行與 Gateway 完全相同的 RequestGuard，再確認請求 alias 不能偏離 host 的固定 Profile，最後才委派
    /// 既有受控 executor。拒絕路徑不建立 Permit、Client、CTS、Timer 或背景 Task；允許路徑也不保存 request，
    /// 而是立即透傳給下游，由其 finally／lease dispose 決定性回收 admission permit 與 Data8 client。
    /// </summary>
    /// <param name="request">已由產品業務碼建立的封閉 capability operation request。</param>
    /// <param name="cancellationToken">由 ASP.NET Core request scope 擁有的取消訊號，原樣傳遞且不被保存。</param>
    /// <returns>受控純值結果；拒絕時不回顯 request 內容、endpoint 或 credential。</returns>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guardResult = _requestGuard.Inspect(request, RequestOrigin.Embedded);
        if (!guardResult.Succeeded)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                guardResult.ErrorCode,
                "The Embedded Dynamics request was rejected by the shared request guard."));
        }

        if (!string.Equals(request.ProfileAlias, _profileAlias, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                "request.invalid-profile-alias",
                "The request ProfileAlias cannot override the Embedded host profile."));
        }

        // 不使用 async 包裝下游 Task，避免多一個 state machine 持有 request graph；下游 ControlledOperationExecutor
        // 會在第一個 async 邊界前投影 request 並在 finally 釋放 prepared dispatch 所有暫存資源。
        return _controlledExecutor.ExecuteAsync(request, cancellationToken);
    }
}
