// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/IDynamicsProfileRuntime.cs
// 目的：定義單一不可變 Profile Generation 與其 Execution Lease 的公開生命週期契約。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// Profile Runtime 的單向生命週期狀態。
/// Active 可接受新 Lease；Draining 拒絕新 Lease但等待既有工作；Disposed 代表所有 Generation-owned 資源已完成清理。
/// 狀態不可由 Draining 回到 Active，避免已取消或已開始 Dispose 的 Token／Handler 被重新發布。
/// </summary>
public enum DynamicsProfileRuntimeState
{
    /// <summary>Generation 已發布並可取得新的 Execution Lease。</summary>
    Active,

    /// <summary>Generation 已退休，不再接受新 Lease，正在等待既有工作排空。</summary>
    Draining,

    /// <summary>所有 Execution Lease 已歸零，Token／Transport／Admission Registration 已確定性釋放。</summary>
    Disposed
}

/// <summary>
/// 一次對特定 Profile Generation 的執行引用。
/// Lease 暴露 Generation-owned 中立 Executor 與退休 CancellationToken，但不保存 Request、User、Session、JWT、Token 或 Credential。
/// 呼叫端必須在 finally／await using 釋放 Lease；只有最後一個 Lease 釋放後，Runtime 才能終止 Worker、Pipe 與 Admission Registration。
/// </summary>
public interface IDynamicsProfileExecutionLease : IAsyncDisposable, IDisposable
{
    /// <summary>取得此 Lease 所屬的非秘密 Runtime Generation Key。</summary>
    ProfileRuntimeKey RuntimeKey { get; }

    /// <summary>
    /// 取得只屬於此 Generation 的 SDK-free 操作執行器；不得快取到 Lease 生命週期以外，
    /// 也不得向下轉型取得 Worker process、Pipe、Credential 或 CRM SDK 物件。
    /// </summary>
    IDynamicsOperationExecutor Executor { get; }

    /// <summary>
    /// 取得 Generation 強制退休時會被取消的 Token；呼叫端需與 Request Cancellation 與 Host Lease-loss Token 連結。
    /// </summary>
    CancellationToken RetirementToken { get; }
}

/// <summary>
/// 一個不可變 Dynamics Profile Generation 的 Runtime。
/// Runtime 唯一擁有官方 Worker Executors、Process／Pipe 生命週期、退休 CancellationTokenSource 與 Admission Registration；
/// 它不擁有終端使用者 Session，也不允許在 Draining 後重新取得 Lease。
/// </summary>
public interface IDynamicsProfileRuntime : IAsyncDisposable, IDisposable
{
    /// <summary>取得不含秘密或 Request 身分的 Generation Key。</summary>
    ProfileRuntimeKey Key { get; }

    /// <summary>取得目前單向生命週期狀態。</summary>
    DynamicsProfileRuntimeState State { get; }

    /// <summary>取得尚未釋放的 Execution Lease 數量；只供 readiness、drain 與 bounded telemetry 使用。</summary>
    int ActiveExecutionCount { get; }

    /// <summary>取得此 Generation 對應的共用 Admission Manager；Manager 只共用容量，不共用 Client／Token／Credential。</summary>
    IOrganizationAdmissionManager AdmissionManager { get; }

    /// <summary>取得不含秘密的 Admission 即時快照。</summary>
    AdmissionMetricsSnapshot AdmissionSnapshot { get; }

    /// <summary>
    /// 取得此 Generation 已聚合的第一個 sticky Worker recycle reason。
    /// 預設為 None，讓不使用官方 Worker 的既有 Runtime fake 保持相容；正式 Worker Runtime 必須覆寫此成員。
    /// </summary>
    OfficialWorkerRecycleReason RecycleReason => OfficialWorkerRecycleReason.None;

    /// <summary>
    /// 在下一次 Admission 前同步評估此 Generation 是否必須整代替換。
    /// 實作不得保存 Request、User、Session、Credential 或 Token；任何無法可信讀取的資源觀測都必須 fail closed。
    /// </summary>
    OfficialWorkerRecycleReason EvaluateRecycleForNextAdmission() => RecycleReason;

    /// <summary>
    /// 在同一生命週期鎖內確認 State=Active 並增加執行引用。
    /// 若 Runtime 已 Draining／Disposed，回傳 false 且不建立任何 Client、Token、CTS 或背景工作。
    /// </summary>
    bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease);

    /// <summary>
    /// 使用同一 Profile Client 與共用 Admission Manager 執行有界 WhoAmI Warm-up。
    /// Warm-up 不建立 per-user Pool，也必須受 Host Lease、Deadline、Cancellation 與 Operation Registry 約束。
    /// </summary>
    Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken);

    /// <summary>原子將 Active 轉為 Draining；重複呼叫安全且不會取消仍在期限內執行的既有工作。</summary>
    void BeginDrain();

    /// <summary>
    /// 停止新 Lease、等待既有 Lease 歸零；自然排空逾時後取消 RetirementToken，再等待有界清理餘裕。
    /// 只有 Lease 歸零後才 Dispose Token、Transport 與 Admission Registration；否則拋出 TimeoutException 而不釋放使用中資源。
    /// </summary>
    Task DrainAndDisposeAsync(CancellationToken cancellationToken = default);
}
