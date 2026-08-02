namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 一個 official Worker generation 的 immutable 回收門檻。
/// 所有欄位在建構時驗證為有限正值並套用部署硬上限；此型別不保存 Credential、Token、
/// Session、Profile secret、Process、Timer、Task、collection 或任何可在建構後改寫的狀態。
/// </summary>
public sealed class OfficialWorkerRecyclePolicyOptions
{
    /// <summary>未明確設定時使用的八小時 Worker age 上限。</summary>
    public static readonly TimeSpan DefaultMaximumWorkerAge = TimeSpan.FromHours(8);

    /// <summary>部署可接受的三十日 Worker age 絕對上限。</summary>
    public static readonly TimeSpan DeploymentMaximumWorkerAge = TimeSpan.FromDays(30);

    /// <summary>未明確設定時使用的十萬次完整作業上限。</summary>
    public const long DefaultMaximumCompletedOperations = 100_000;

    /// <summary>部署可接受的一億次完整作業絕對上限。</summary>
    public const long DeploymentMaximumCompletedOperations = 100_000_000;

    /// <summary>未明確設定時使用的二 GiB Private Bytes 上限。</summary>
    public const long DefaultMaximumPrivateBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>未明確設定時使用的二 GiB Working Set 上限。</summary>
    public const long DefaultMaximumWorkingSet = 2L * 1024 * 1024 * 1024;

    /// <summary>任一部署資源門檻可設定的十六 TiB 絕對上限。</summary>
    public const long DeploymentMaximumResourceBytes = 16L * 1024 * 1024 * 1024 * 1024;

    /// <summary>未明確設定時允許的三次連續完整 Worker timeout response。</summary>
    public const int DefaultMaximumConsecutiveCompleteWorkerTimeouts = 3;

    /// <summary>部署可接受的一千次連續完整 Worker timeout response 絕對上限。</summary>
    public const int DeploymentMaximumConsecutiveCompleteWorkerTimeouts = 1_000;

    /// <summary>
    /// 建立並完整驗證 immutable 回收門檻。驗證失敗時不建立 Policy、Process、Timer、
    /// 背景工作或任何可保留資源，讓 configuration 在 runtime generation 發布前 fail closed。
    /// </summary>
    /// <param name="maximumWorkerAge">Worker 自 READY generation 起可存活的最長單調時間。</param>
    /// <param name="maximumCompletedOperations">完整 response 作業數上限。</param>
    /// <param name="maximumPrivateBytes">Private Bytes 上限。</param>
    /// <param name="maximumWorkingSet">Working Set 上限。</param>
    /// <param name="maximumConsecutiveCompleteWorkerTimeouts">連續完整 Worker timeout response 上限。</param>
    public OfficialWorkerRecyclePolicyOptions(
        TimeSpan? maximumWorkerAge = null,
        long maximumCompletedOperations = DefaultMaximumCompletedOperations,
        long maximumPrivateBytes = DefaultMaximumPrivateBytes,
        long maximumWorkingSet = DefaultMaximumWorkingSet,
        int maximumConsecutiveCompleteWorkerTimeouts =
            DefaultMaximumConsecutiveCompleteWorkerTimeouts)
    {
        MaximumWorkerAge = maximumWorkerAge ?? DefaultMaximumWorkerAge;
        if (MaximumWorkerAge <= TimeSpan.Zero ||
            MaximumWorkerAge > DeploymentMaximumWorkerAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumWorkerAge),
                "Maximum worker age must be positive and no greater than thirty days.");
        }

        MaximumCompletedOperations = ValidatePositiveBounded(
            maximumCompletedOperations,
            DeploymentMaximumCompletedOperations,
            nameof(maximumCompletedOperations));
        MaximumPrivateBytes = ValidatePositiveBounded(
            maximumPrivateBytes,
            DeploymentMaximumResourceBytes,
            nameof(maximumPrivateBytes));
        MaximumWorkingSet = ValidatePositiveBounded(
            maximumWorkingSet,
            DeploymentMaximumResourceBytes,
            nameof(maximumWorkingSet));

        if (maximumConsecutiveCompleteWorkerTimeouts is < 1 or
            > DeploymentMaximumConsecutiveCompleteWorkerTimeouts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConsecutiveCompleteWorkerTimeouts),
                "Maximum consecutive complete worker timeouts must be between 1 and 1000.");
        }

        MaximumConsecutiveCompleteWorkerTimeouts = maximumConsecutiveCompleteWorkerTimeouts;
    }

    /// <summary>取得 Worker generation 的有限單調 age 上限。</summary>
    public TimeSpan MaximumWorkerAge { get; }

    /// <summary>取得完整 response 作業數的有限上限。</summary>
    public long MaximumCompletedOperations { get; }

    /// <summary>取得 Private Bytes 的有限上限。</summary>
    public long MaximumPrivateBytes { get; }

    /// <summary>取得 Working Set 的有限上限。</summary>
    public long MaximumWorkingSet { get; }

    /// <summary>取得連續完整 Worker timeout response 的有限上限。</summary>
    public int MaximumConsecutiveCompleteWorkerTimeouts { get; }

    /// <summary>驗證 long 門檻為正值且不超過其固定部署上限。</summary>
    private static long ValidatePositiveBounded(long value, long maximum, string parameterName)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Recycle threshold must be positive and within its deployment maximum.");
        }

        return value;
    }
}
