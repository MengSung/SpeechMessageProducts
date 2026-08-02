namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 表示官方 Dynamics Worker 在 READY 發布前失敗，並明確承接該次未發布 generation 的全部
/// cleanup ownership。例外只公開固定 sanitized 訊息、原始失敗作為 <see cref="Exception.InnerException"/>
/// 與 bounded lifecycle counters；它不公開 Process、Pipe、Task、Nonce、路徑、Credential、Token 或 Session。
/// 呼叫邊界必須保留並 <see cref="DisposeAsync"/> 此例外，直到 snapshot 歸零；若一次 bounded cleanup
/// 尚未完成，同一例外仍是唯一 owner，可由下一次 factory 建立或 host shutdown 確定性重試。
/// </summary>
public sealed class OfficialWorkerStartupException : InvalidOperationException, IAsyncDisposable
{
    private const string StartupFailureMessage =
        "The official Dynamics worker startup did not complete.";

    private readonly OfficialWorkerProfileExecutor _owner;

    /// <summary>
    /// 建立 startup ownership transfer。<paramref name="startupFailure"/> 保留 timeout、cancellation、
    /// READY protocol 或 process-start 的原始型別；<paramref name="owner"/> 是唯一持有未完成資源的 executor。
    /// </summary>
    /// <param name="startupFailure">已 sanitized 的原始 startup 失敗。</param>
    /// <param name="owner">從 pipe 建立開始即擁有完整 lifecycle graph 的未發布 executor。</param>
    internal OfficialWorkerStartupException(
        Exception startupFailure,
        OfficialWorkerProfileExecutor owner)
        : base(StartupFailureMessage, startupFailure)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// 取得不暴露 handle 或 mutable resource 的 bounded counters，供 factory readiness、測試與 host
    /// shutdown 判斷 ownership 是否已歸零。即使 parent process 已退出，只要 reader 尚未 terminal，
    /// snapshot 仍保留 Process 與背景 owner，避免假的零基線。
    /// </summary>
    /// <returns>目前未發布 generation 的 lifecycle snapshot。</returns>
    public OfficialWorkerLifecycleSnapshot GetLifecycleSnapshot() =>
        _owner.GetLifecycleSnapshot();

    /// <summary>
    /// 委派到相同 executor 的共享、可重試 cleanup attempt。方法完整 await cleanup，不建立
    /// fire-and-forget task；若 deadline 內仍無法確認 process exit 或 reader completion，會回傳固定
    /// cleanup failure，而 owner reference 保持在本例外中供後續重試。
    /// </summary>
    public ValueTask DisposeAsync() => _owner.DisposeAsync();
}
