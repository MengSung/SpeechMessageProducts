// ============================================================================
// 檔案：ChurchReport/Services/LegacyToolUtilityDrainController.cs
// 目的：提供 ChurchReport legacy ToolUtility 受控入口的本機停止接收與排空契約。
//
// 重要邊界：本型別只計量已註冊的 operation-level legacy work，不是 Dynamics CRM
// client pool、跨主機容量 authority、ToolUtilityFactory replacement 或全域流量證明。
// 同步 CRM 呼叫無法接受本型別的 cancellation，因此 drain timeout 或同步 overrun
// 絕不能被解讀成 Organization-level drained；deployment validator 必須另外取得
// legacy coverage 與 durable non-overlap 證據。
//
// 生命週期：controller 只保留固定 enum、計數器、狀態與短期 wait signal，不保存 request、
// session、principal、CRM Entity、credential、endpoint、response 或例外。每個 lease
// 由呼叫端唯一擁有並以 Interlocked exactly-once 釋放；controller 不啟動背景執行緒、
// queue 或長壽命 timer。等待 timeout 使用 framework 的 bounded wait，完成後不保留 timer。
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Services;

/// <summary>
/// 由 server composition root 選定的有限 legacy workload 分類。
/// 不接受呼叫端傳入的 profile、Organization、endpoint 或 credential。
/// </summary>
public enum LegacyToolUtilityWorkload
{
    /// <summary>Package01 奉獻費用查詢的受控 operation-level ingress。</summary>
    Package01FeeRead = 1
}

/// <summary>
/// legacy drain read-back 的固定分類；分類不包含上游例外、識別資料或 routing 值。
/// </summary>
public enum LegacyToolUtilityDrainResult
{
    /// <summary>所有已註冊 lease 已確定釋放。</summary>
    Drained = 1,

    /// <summary>期限內仍有 active lease，不能宣稱已排空。</summary>
    DrainTimeout = 2,

    /// <summary>等待者被取消；intake 仍維持停止，不能宣稱已排空。</summary>
    Cancelled = 3,

    /// <summary>controller 已進入 disposed terminal state。</summary>
    Disposed = 4
}

/// <summary>
/// 管理已註冊 legacy ingress 的 bounded lease、stop-intake 與 drain lifecycle。
/// 這是每個 ChurchReport host 的本機計量器；未註冊的 ToolUtility 呼叫不會被本型別攔截。
/// </summary>
public sealed class LegacyToolUtilityDrainController : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DefaultDisposeDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _drainedSignal;
    private int _activeLeaseCount;
    private int _package01FeeReadLeaseCount;
    private bool _intakeOpen = true;
    private bool _disposed;

    /// <summary>取得目前是否仍接受新的受控 legacy lease。</summary>
    public bool IsIntakeOpen
    {
        get
        {
            lock (_sync)
            {
                return _intakeOpen && !_disposed;
            }
        }
    }

    /// <summary>
    /// 取得目前尚未釋放的受控 lease 數量。
    /// 此值只用於本機 lifecycle/read-back，不代表跨 host 的 Organization capacity。
    /// </summary>
    public int ActiveLeaseCount
    {
        get
        {
            lock (_sync)
            {
                return _activeLeaseCount;
            }
        }
    }

    /// <summary>取得指定 workload 的本機 active lease 數量。</summary>
    public int GetActiveLeaseCount(LegacyToolUtilityWorkload workload)
    {
        ValidateWorkload(workload);
        lock (_sync)
        {
            return workload == LegacyToolUtilityWorkload.Package01FeeRead
                ? _package01FeeReadLeaseCount
                : 0;
        }
    }

    /// <summary>
    /// 嘗試取得一個受控 legacy lease。
    /// intake 已停止或 controller 已 disposed 時回傳 null；取消只會影響本次等待者。
    /// </summary>
    /// <param name="workload">由 server code 固定選定的 workload 分類。</param>
    /// <param name="cancellationToken">本次等待者的取消權杖。</param>
    /// <returns>呼叫端唯一擁有的 lease；拒絕時為 null。</returns>
    public Task<LegacyToolUtilityDrainLease?> AcquireAsync(
        LegacyToolUtilityWorkload workload,
        CancellationToken cancellationToken)
    {
        ValidateWorkload(workload);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_intakeOpen || _disposed)
            {
                return Task.FromResult<LegacyToolUtilityDrainLease?>(null);
            }

            if (_activeLeaseCount == 0)
            {
                _drainedSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            checked
            {
                _activeLeaseCount++;
                if (workload == LegacyToolUtilityWorkload.Package01FeeRead)
                {
                    _package01FeeReadLeaseCount++;
                }
            }

            return Task.FromResult<LegacyToolUtilityDrainLease?>(
                new LegacyToolUtilityDrainLease(this, workload));
        }
    }

    /// <summary>
    /// 停止新的受控 ingress 並等待目前已取得的 lease 釋放。
    /// timeout、取消與 disposed 都回傳固定分類；不會把未知工作轉成成功。
    /// </summary>
    /// <param name="timeout">本次 drain 的 bounded 等待期限。</param>
    /// <param name="cancellationToken">只取消本次 drain waiter。</param>
    public async Task<LegacyToolUtilityDrainResult> StopIntakeAndDrainAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateTimeout(timeout);

        Task? drainTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return LegacyToolUtilityDrainResult.Disposed;
            }

            _intakeOpen = false;
            if (_activeLeaseCount == 0)
            {
                return LegacyToolUtilityDrainResult.Drained;
            }

            drainTask = GetDrainSignalLocked().Task;
        }

        try
        {
            await drainTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return LegacyToolUtilityDrainResult.Drained;
        }
        catch (TimeoutException)
        {
            return LegacyToolUtilityDrainResult.DrainTimeout;
        }
        catch (OperationCanceledException)
        {
            return LegacyToolUtilityDrainResult.Cancelled;
        }
    }

    /// <summary>
    /// 停止 intake 並以固定期限嘗試排空後釋放 controller。
    /// 未排空時拋出 bounded cleanup exception，避免把不確定 cleanup 回報為成功。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Task? drainTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _intakeOpen = false;
            _disposed = true;
            drainTask = _activeLeaseCount == 0 ? null : GetDrainSignalLocked().Task;
        }

        if (drainTask is null)
        {
            return;
        }

        try
        {
            await drainTask.WaitAsync(DefaultDisposeDrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                "Legacy drain cleanup did not reach a known baseline.",
                exception);
        }
    }

    /// <summary>同步釋放入口；本 controller 不進行外部 I/O，等待只受 bounded cleanup deadline 限制。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void Release(LegacyToolUtilityWorkload workload)
    {
        lock (_sync)
        {
            if (_activeLeaseCount <= 0)
            {
                return;
            }

            _activeLeaseCount--;
            if (workload == LegacyToolUtilityWorkload.Package01FeeRead &&
                _package01FeeReadLeaseCount > 0)
            {
                _package01FeeReadLeaseCount--;
            }

            if (_activeLeaseCount == 0)
            {
                _drainedSignal?.TrySetResult(true);
            }
        }
    }

    private TaskCompletionSource<bool> GetDrainSignalLocked()
    {
        return _drainedSignal ??= new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static void ValidateWorkload(LegacyToolUtilityWorkload workload)
    {
        if (workload != LegacyToolUtilityWorkload.Package01FeeRead)
        {
            throw new ArgumentOutOfRangeException(nameof(workload));
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    /// <summary>代表一次受控 legacy work 的唯一 lease owner。</summary>
    public sealed class LegacyToolUtilityDrainLease : IAsyncDisposable, IDisposable
    {
        private readonly LegacyToolUtilityDrainController _owner;
        private readonly LegacyToolUtilityWorkload _workload;
        private int _released;

        internal LegacyToolUtilityDrainLease(
            LegacyToolUtilityDrainController owner,
            LegacyToolUtilityWorkload workload)
        {
            _owner = owner;
            _workload = workload;
        }

        /// <summary>此 lease 的固定 workload 分類，不包含任何 request 或 subject 資料。</summary>
        public LegacyToolUtilityWorkload Workload => _workload;

        /// <summary>以 exactly-once 語意釋放本次 lease。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_workload);
            }
        }

        /// <summary>同步釋放後回傳已完成的 ValueTask，不建立 fire-and-forget cleanup。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
