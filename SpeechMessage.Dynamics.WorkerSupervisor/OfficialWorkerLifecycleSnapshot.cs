namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 提供官方 Worker generation 的有限生命週期 ownership 計數，供 drain、recycle、startup rollback
/// 與 soak/no-leak 驗證使用。Snapshot 只公開純量計數，不公開 Process、Pipe、StreamReader、Task、
/// CancellationTokenSource、SemaphoreSlim、要求內容、Credential、Token 或 Session reference。
/// </summary>
public sealed record OfficialWorkerLifecycleSnapshot(
    bool IsReady,
    int OwnedProcessCount,
    int OwnedPipeCount,
    int OwnedBackgroundTaskCount,
    int ActiveOperationCount)
{
    /// <summary>
    /// 取得 Executor 仍負責 Dispose 的 operation gate 數量；generation 存活或 cleanup 未確認完成時為一，
    /// gate 已在所有 entrant 離開後成功 Dispose 才能歸零。
    /// </summary>
    public int OwnedOperationGateCount { get; init; }

    /// <summary>
    /// 取得仍由 Executor 強引用並負責關閉的 stdout/stderr reader 數量；不得因 parent process 先退出而
    /// 提前假設 descendant 已關閉 inherited output handle。
    /// </summary>
    public int OwnedOutputReaderCount { get; init; }

    /// <summary>
    /// 取得仍由 Executor 觀察至 terminal 的 stdout/stderr discard task 數量；此值亦是
    /// <see cref="OwnedBackgroundTaskCount"/> 的明確 output-task 分解。
    /// </summary>
    public int OwnedOutputTaskCount { get; init; }

    /// <summary>
    /// 取得仍由 Executor 擁有的 output-read cancellation source 數量；只有兩個 reader task 都 terminal
    /// 且 source 成功 Dispose 後才歸零。
    /// </summary>
    public int OwnedOutputCancellationSourceCount { get; init; }

    /// <summary>
    /// 取得目前由唯一 cleanup attempt 擁有的 Process exit wait 數量。此 owner 同時涵蓋
    /// <c>WaitForExitAsync</c> 的 OS event subscription、timeout timer 與 cancellation registration；
    /// 並行 <c>DisposeAsync</c> caller 共用同一 attempt，因此值只能為零或一。每次 attempt 無論成功、
    /// timeout 或失敗都必須在返回前歸零，避免 retry 累積不可見 Task、registration 或 Process reference。
    /// 此計數不包含 stdout/stderr reader task；後者由 <see cref="OwnedOutputTaskCount"/> 分別呈現。
    /// </summary>
    public int OwnedProcessExitWaitCount { get; init; }

    /// <summary>
    /// 取得已進入 <c>ExecuteAsync</c> 且尚未離開的 caller 數量，包含等待 operation gate 與已持有 gate
    /// 的要求；cleanup 必須等此值歸零後才可 Dispose gate。
    /// </summary>
    public int OperationEntrantCount { get; init; }
}
