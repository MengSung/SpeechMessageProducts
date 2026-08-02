using System.Diagnostics;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 一個 official Worker generation 的 allocation-light 回收狀態機。
/// Policy 只保存 immutable options、單調起點與有限 scalar counters；它不擁有 Process、Pipe、
/// Stream、Timer、Task、CancellationTokenSource、collection、Profile、Credential、Session 或 caller 資料。
/// </summary>
/// <remarks>
/// 完整 response 在作業結束後才呼叫 <see cref="RecordCompletedResponse"/>，因此到達 count、memory 或
/// complete-timeout 門檻的作業可正常完成；門檻一旦記錄，下一次 admission 取得相同 immutable reason。
/// Supervisor timeout、cancellation 與 fatal frame interruption 則表示 response framing 不完整，必須立即退休。
/// </remarks>
public sealed class OfficialWorkerRecyclePolicy
{
    private readonly OfficialWorkerRecyclePolicyOptions _options;
    private readonly long _startedTimestamp;
    private readonly ulong _maximumWorkerAgeTimestampDelta;
    private long _completedOperationCount;
    private int _consecutiveCompleteWorkerTimeouts;
    private int _recycleReason;

    /// <summary>
    /// 建立一個 generation-local Policy。<paramref name="startedTimestamp"/> 必須來自與後續評估相同的
    /// <see cref="Stopwatch.GetTimestamp"/> 單調來源；Policy 不讀取 wall clock，避免校時或時區變更改寫 age 判斷。
    /// </summary>
    /// <param name="options">已驗證且不可變的部署回收門檻。</param>
    /// <param name="startedTimestamp">Worker generation 成功建立時擷取的單調時間戳。</param>
    public OfficialWorkerRecyclePolicy(
        OfficialWorkerRecyclePolicyOptions options,
        long startedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _startedTimestamp = startedTimestamp;
        _maximumWorkerAgeTimestampDelta = ConvertToStopwatchTicks(options.MaximumWorkerAge);
    }

    /// <summary>取得第一個成功記錄的 sanitized reason；後續並行原因不能覆寫。</summary>
    public OfficialWorkerRecycleReason RecycleReason =>
        (OfficialWorkerRecycleReason)Volatile.Read(ref _recycleReason);

    /// <summary>取得是否已停止此 Worker 的下一次 admission。</summary>
    public bool IsRecycleRequired => RecycleReason != OfficialWorkerRecycleReason.None;

    /// <summary>取得已完整結束的 bounded 作業計數。</summary>
    public long CompletedOperationCount => Interlocked.Read(ref _completedOperationCount);

    /// <summary>取得目前連續完整 Worker timeout response 次數。</summary>
    public int ConsecutiveCompleteWorkerTimeouts =>
        Volatile.Read(ref _consecutiveCompleteWorkerTimeouts);

    /// <summary>
    /// 在下一次 admission 前，以固定優先序評估 READY、health、protocol、資源、age 與既有 counters。
    /// 優先序為 NotReady、Health、Protocol、Resource Observation、Age、Completed Count、
    /// Private Bytes、Working Set、Complete Timeout Streak；方法不配置 managed object。
    /// </summary>
    /// <param name="observedTimestamp">與建構起點相同來源的目前單調時間戳。</param>
    /// <param name="isReady">Worker 是否仍具有可發布 READY 狀態。</param>
    /// <param name="isHealthy">最近健康證據是否完整成功。</param>
    /// <param name="protocolViolation">是否已發現 protocol contract 違反。</param>
    /// <param name="resourceObservation">同一次讀取的 Private Bytes／Working Set 純量快照。</param>
    /// <returns>None 代表可繼續 admission；其他值是此 generation 的 immutable retirement reason。</returns>
    public OfficialWorkerRecycleReason EvaluateForNextAdmission(
        long observedTimestamp,
        bool isReady,
        bool isHealthy,
        bool protocolViolation,
        OfficialWorkerResourceObservation? resourceObservation)
    {
        var existing = RecycleReason;
        if (existing != OfficialWorkerRecycleReason.None)
        {
            return existing;
        }

        return RecordFirstReason(DetermineReason(
            observedTimestamp,
            isReady,
            isHealthy,
            protocolViolation,
            resourceObservation,
            Interlocked.Read(ref _completedOperationCount),
            Volatile.Read(ref _consecutiveCompleteWorkerTimeouts)));
    }

    /// <summary>
    /// 在完整 Worker response 已被驗證並交還 caller 後，更新 completed count 與 timeout streak，
    /// 再以相同固定優先序決定下一次 admission。完整非 timeout response 必定先把 streak 歸零；
    /// counters 採飽和遞增，避免異常重複呼叫造成 integer wraparound 而錯誤恢復為低於門檻。
    /// </summary>
    /// <param name="completedTimestamp">完整 response 結束時的同來源單調時間戳。</param>
    /// <param name="isCompleteWorkerTimeout">完整 response 是否明確回報 Worker timeout outcome。</param>
    /// <param name="resourceObservation">response 結束後同一次讀取的 process 資源快照。</param>
    /// <returns>完成後的 immutable retirement reason；None 代表下一次 admission 仍可評估。</returns>
    public OfficialWorkerRecycleReason RecordCompletedResponse(
        long completedTimestamp,
        bool isCompleteWorkerTimeout,
        OfficialWorkerResourceObservation? resourceObservation)
    {
        var completedOperationCount = IncrementSaturating(ref _completedOperationCount);
        int timeoutStreak;
        if (isCompleteWorkerTimeout)
        {
            timeoutStreak = IncrementSaturating(ref _consecutiveCompleteWorkerTimeouts);
        }
        else
        {
            Interlocked.Exchange(ref _consecutiveCompleteWorkerTimeouts, 0);
            timeoutStreak = 0;
        }

        var existing = RecycleReason;
        if (existing != OfficialWorkerRecycleReason.None)
        {
            return existing;
        }

        return RecordFirstReason(DetermineReason(
            completedTimestamp,
            isReady: true,
            isHealthy: true,
            protocolViolation: false,
            resourceObservation,
            completedOperationCount,
            timeoutStreak));
    }

    /// <summary>記錄 incomplete response 的 supervisor deadline timeout，立即停止下一次 admission。</summary>
    public OfficialWorkerRecycleReason RecordSupervisorTimeout()
        => RecordFirstReason(OfficialWorkerRecycleReason.SupervisorTimeout);

    /// <summary>記錄 incomplete response 的 supervisor-side cancellation，立即停止下一次 admission。</summary>
    public OfficialWorkerRecycleReason RecordSupervisorCancellation()
        => RecordFirstReason(OfficialWorkerRecycleReason.SupervisorCancellation);

    /// <summary>記錄致命 frame interruption，立即停止下一次 admission。</summary>
    public OfficialWorkerRecycleReason RecordFatalFrameInterruption()
        => RecordFirstReason(OfficialWorkerRecycleReason.FatalFrameInterruption);

    /// <summary>
    /// 以固定順序從同一份 scalar snapshot 選出單一候選原因。無效資源觀測在任何 memory 比較前
    /// fail closed；單調時間倒退同樣是 health failure，不能讓 age 門檻因時間來源錯誤而被略過。
    /// </summary>
    private OfficialWorkerRecycleReason DetermineReason(
        long observedTimestamp,
        bool isReady,
        bool isHealthy,
        bool protocolViolation,
        OfficialWorkerResourceObservation? resourceObservation,
        long completedOperationCount,
        int consecutiveCompleteWorkerTimeouts)
    {
        if (!isReady)
        {
            return OfficialWorkerRecycleReason.NotReady;
        }

        if (!isHealthy || observedTimestamp < _startedTimestamp)
        {
            return OfficialWorkerRecycleReason.HealthFailure;
        }

        if (protocolViolation)
        {
            return OfficialWorkerRecycleReason.ProtocolViolation;
        }

        if (!resourceObservation.HasValue || !resourceObservation.GetValueOrDefault().IsValid)
        {
            return OfficialWorkerRecycleReason.ResourceObservationFailure;
        }

        var observation = resourceObservation.GetValueOrDefault();
        var elapsedTimestampDelta = unchecked((ulong)(observedTimestamp - _startedTimestamp));
        if (elapsedTimestampDelta >= _maximumWorkerAgeTimestampDelta)
        {
            return OfficialWorkerRecycleReason.MaximumWorkerAge;
        }

        if (completedOperationCount >= _options.MaximumCompletedOperations)
        {
            return OfficialWorkerRecycleReason.MaximumCompletedOperations;
        }

        if (observation.PrivateBytes >= _options.MaximumPrivateBytes)
        {
            return OfficialWorkerRecycleReason.MaximumPrivateBytes;
        }

        if (observation.WorkingSetBytes >= _options.MaximumWorkingSet)
        {
            return OfficialWorkerRecycleReason.MaximumWorkingSet;
        }

        return consecutiveCompleteWorkerTimeouts >=
            _options.MaximumConsecutiveCompleteWorkerTimeouts
            ? OfficialWorkerRecycleReason.MaximumConsecutiveCompleteWorkerTimeouts
            : OfficialWorkerRecycleReason.None;
    }

    /// <summary>
    /// 以 CompareExchange 記錄第一個非 None 原因。競爭失敗者讀回已存在的 winner，
    /// 因此不需要 lock、queue、subscription 或 background arbiter，且 reason 永不變更。
    /// </summary>
    private OfficialWorkerRecycleReason RecordFirstReason(OfficialWorkerRecycleReason candidate)
    {
        if (candidate == OfficialWorkerRecycleReason.None)
        {
            return RecycleReason;
        }

        var existing = Interlocked.CompareExchange(
            ref _recycleReason,
            (int)candidate,
            (int)OfficialWorkerRecycleReason.None);
        return existing == (int)OfficialWorkerRecycleReason.None
            ? candidate
            : (OfficialWorkerRecycleReason)existing;
    }

    /// <summary>以 CAS 飽和遞增 long counter，避免越過 long.MaxValue 後回繞為負值。</summary>
    private static long IncrementSaturating(ref long location)
    {
        while (true)
        {
            var current = Interlocked.Read(ref location);
            if (current == long.MaxValue)
            {
                return current;
            }

            var next = current + 1;
            if (Interlocked.CompareExchange(ref location, next, current) == current)
            {
                return next;
            }
        }
    }

    /// <summary>以 CAS 飽和遞增 int counter，避免越過 int.MaxValue 後回繞為負值。</summary>
    private static int IncrementSaturating(ref int location)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (current == int.MaxValue)
            {
                return current;
            }

            var next = current + 1;
            if (Interlocked.CompareExchange(ref location, next, current) == current)
            {
                return next;
            }
        }
    }

    /// <summary>
    /// 將已受三十日上限保護的 TimeSpan 精確向上換算為 Stopwatch ticks。
    /// 向上取整確保 sub-tick 設定不會提前回收；換算只在 Policy 建構時執行，不進入 admission hot path。
    /// </summary>
    private static ulong ConvertToStopwatchTicks(TimeSpan duration)
    {
        var scaledTicks = decimal.Ceiling(
            (decimal)duration.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        return checked((ulong)scaledTicks);
    }
}
