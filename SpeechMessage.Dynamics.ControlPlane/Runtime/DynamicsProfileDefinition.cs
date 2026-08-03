// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileDefinition.cs
// 目的：把部署擁有的官方 Worker 與 Organization admission 設定凍結成不可變 Profile 定義。
// ============================================================================

using System.Text.RegularExpressions;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 官方 NuGet Worker 路徑的不可變 Dynamics Profile 定義。
/// 此型別只保存非秘密部署身分、完整限定的 Worker 執行檔位置、雜湊、package lock、有限 timeout
/// 與已驗證的 Organization admission 計畫；它不解析 endpoint secret、Credential、Token，也不建立
/// Process、Pipe、Client、Timer、CancellationTokenSource 或背景工作，因此可安全保留於 Alias Catalog。
/// </summary>
/// <remarks>
/// <see cref="WorkerProfileGenerationId"/> 是 worker-profile.xml 的精確 <c>generationId</c>，由部署擁有且
/// 原樣傳入 Worker bootstrap。它與 <see cref="ProfileRuntimeKey.Generation"/> 的程序內單調數字完全分離：
/// 前者選取 worker-local immutable profile，後者只辨識 ControlPlane replace-and-drain 世代。
/// 這項分離可避免重啟時數字重新從一開始，卻無法匹配既有 XML profile 的錯誤。
/// </remarks>
public sealed class DynamicsProfileDefinition
{
    private const string SafeIdentifierPattern = "^[A-Za-z0-9._-]{1,128}$";
    private static readonly TimeSpan MaximumConfiguredTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 建立並完整驗證一份不可變 Profile Definition。
    /// 驗證在任何 Worker process、Pipe、Admission Manager 或 cancellation registration 建立前完成；
    /// 呼叫端後續修改 <paramref name="admissionOptions"/> 不會影響此物件或任何既有 Runtime Generation。
    /// </summary>
    /// <param name="profileAlias">伺服器端核准的 profile alias；不得包含 URI、路徑或 query 語法。</param>
    /// <param name="workerProfileGenerationId">必須精確匹配 worker-profile.xml generationId 的非秘密部署識別。</param>
    /// <param name="workerVersion">明確選取 CE 8.2 或 CE 9.1 的獨立官方 Worker graph。</param>
    /// <param name="organizationBaseUri">實體 Dynamics Organization 的 HTTPS root；不得是 /api/data/ 或 Organization Service endpoint。</param>
    /// <param name="workerExecutablePath">已部署 Worker executable 的完整限定路徑；產品要求不能覆寫。</param>
    /// <param name="workerExecutableSha256">部署核准 executable 的 64 字元 SHA-256 十六進位雜湊。</param>
    /// <param name="packageLockId">Worker READY handshake 必須精確回報的 immutable package-lock ID。</param>
    /// <param name="admissionOptions">Organization 級容量與 host-slot fencing 設定；建構時轉為 immutable plan。</param>
    /// <param name="workerCount">此 profile generation 擁有的有限 process 數量，預設一。</param>
    /// <param name="maxInFlightPerWorker">每個 Worker 的在途上限；未有目標實機證據前必須精確為一。</param>
    /// <param name="warmUpOnActivation">發布前是否以服務 workload 執行受控 WhoAmI warm-up。</param>
    /// <param name="startupTimeout">Worker executable/hash/pipe/READY 建立上限；未指定時為三十秒。</param>
    /// <param name="operationTimeout">單次 Worker operation 上限；未指定時採 admission 最大 outbound lifetime。</param>
    /// <param name="drainTimeout">等待 execution lease 與 Worker graceful drain 的上限；未指定時採 admission shutdown timeout。</param>
    /// <param name="cancellationGracePeriod">發出 retirement cancellation 後等待 finally/lease cleanup 的上限。</param>
    /// <param name="recyclePolicyOptions">Worker age、完整作業數、記憶體與完整 timeout streak 的有限回收門檻。</param>
    public DynamicsProfileDefinition(
        string profileAlias,
        string workerProfileGenerationId,
        OfficialWorkerVersion workerVersion,
        string organizationBaseUri,
        string workerExecutablePath,
        string workerExecutableSha256,
        string packageLockId,
        OrganizationAdmissionOptions admissionOptions,
        int workerCount = 1,
        int maxInFlightPerWorker = 1,
        bool warmUpOnActivation = true,
        TimeSpan? startupTimeout = null,
        TimeSpan? operationTimeout = null,
        TimeSpan? drainTimeout = null,
        TimeSpan? cancellationGracePeriod = null,
        OfficialWorkerRecyclePolicyOptions? recyclePolicyOptions = null)
    {
        ArgumentNullException.ThrowIfNull(admissionOptions);
        RecyclePolicyOptions = CloneRecyclePolicyOptions(
            recyclePolicyOptions ?? new OfficialWorkerRecyclePolicyOptions());
        ProfileAlias = ValidateIdentifier(profileAlias, nameof(profileAlias));
        WorkerProfileGenerationId = ValidateIdentifier(
            workerProfileGenerationId,
            nameof(workerProfileGenerationId));
        PackageLockId = ValidateIdentifier(packageLockId, nameof(packageLockId));

        if (!Enum.IsDefined(workerVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(workerVersion), "Worker version is invalid.");
        }

        if (string.IsNullOrWhiteSpace(workerExecutablePath) ||
            !string.Equals(workerExecutablePath, workerExecutablePath.Trim(), StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(workerExecutablePath))
        {
            throw new ArgumentException(
                "Worker executable path must be a fully qualified path without outer whitespace.",
                nameof(workerExecutablePath));
        }

        if (string.IsNullOrWhiteSpace(workerExecutableSha256) ||
            workerExecutableSha256.Length != 64 ||
            workerExecutableSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Worker executable SHA-256 must contain exactly 64 hexadecimal characters.",
                nameof(workerExecutableSha256));
        }

        if (workerCount is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerCount),
                "Worker count must be between 1 and 64.");
        }

        if (maxInFlightPerWorker != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInFlightPerWorker),
                "MaxInFlightPerWorker must remain exactly 1 until target-specific concurrency proof exists.");
        }

        if (!OrganizationAdmissionPlan.TryCreate(
                organizationBaseUri,
                workerCount,
                maxInFlightPerWorker,
                CloneAdmissionOptions(admissionOptions),
                out var admissionPlan,
                out var admissionError) ||
            admissionPlan is null)
        {
            throw new ArgumentException(
                admissionError?.ErrorMessage ?? "The Dynamics organization admission plan is invalid.",
                nameof(admissionOptions));
        }

        WorkerVersion = workerVersion;
        CeVersion = workerVersion switch
        {
            OfficialWorkerVersion.Ce82 => "8.2",
            OfficialWorkerVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(workerVersion))
        };
        OrganizationBaseUri = admissionPlan.CanonicalKey.NormalizedOrganizationBaseUri;
        ExpectedOrganizationId = admissionPlan.CanonicalKey.ExpectedOrganizationId;
        WorkerExecutablePath = Path.GetFullPath(workerExecutablePath);
        WorkerExecutableSha256 = workerExecutableSha256.ToLowerInvariant();
        WorkerCount = workerCount;
        MaxInFlightPerWorker = maxInFlightPerWorker;
        WarmUpOnActivation = warmUpOnActivation;
        AdmissionPlan = admissionPlan;

        StartupTimeout = ValidateTimeout(
            startupTimeout ?? TimeSpan.FromSeconds(30),
            nameof(startupTimeout));
        OperationTimeout = ValidateTimeout(
            operationTimeout ?? admissionPlan.MaximumOutboundWorkLifetime,
            nameof(operationTimeout));
        DrainTimeout = ValidateTimeout(
            drainTimeout ?? admissionPlan.ShutdownDrainTimeout,
            nameof(drainTimeout));
        CancellationGracePeriod = ValidateTimeout(
            cancellationGracePeriod ??
            admissionPlan.MaximumOutboundWorkLifetime +
            admissionPlan.RuntimeHostSlotExpiryFence +
            TimeSpan.FromSeconds(5),
            nameof(cancellationGracePeriod));
    }

    /// <summary>取得已通過嚴格語法驗證的伺服器端 Profile Alias。</summary>
    public string ProfileAlias { get; }

    /// <summary>取得必須精確匹配 worker-profile.xml generationId 的部署擁有識別。</summary>
    public string WorkerProfileGenerationId { get; }

    /// <summary>取得此 Profile 固定使用的 CE 8.2 或 CE 9.1 Worker 版本。</summary>
    public OfficialWorkerVersion WorkerVersion { get; }

    /// <summary>取得 SDK-free runtime key 使用的明確 CE 版本字串。</summary>
    public string CeVersion { get; }

    /// <summary>取得已正規化的 HTTPS Organization root，不是直接 transport endpoint。</summary>
    public string OrganizationBaseUri { get; }

    /// <summary>取得容量與實機 identity 驗證共用的預期 Organization GUID。</summary>
    public Guid ExpectedOrganizationId { get; }

    /// <summary>取得 Supervisor 在 process start 前驗證 SHA-256 的完整限定 executable 路徑。</summary>
    public string WorkerExecutablePath { get; }

    /// <summary>取得已正規化為小寫的 executable SHA-256；此值不含 Credential 或 Token。</summary>
    public string WorkerExecutableSha256 { get; }

    /// <summary>取得 Worker READY 必須精確回報的 immutable package-lock ID。</summary>
    public string PackageLockId { get; }

    /// <summary>取得此 Runtime Generation 擁有的有限 Worker process 數量。</summary>
    public int WorkerCount { get; }

    /// <summary>取得每個 Worker 的安全在途上限；目前合約固定為一。</summary>
    public int MaxInFlightPerWorker { get; }

    /// <summary>取得是否在 Runtime 發布前執行受控服務 identity warm-up。</summary>
    public bool WarmUpOnActivation { get; }

    /// <summary>取得 Worker executable/hash/pipe/READY 建立的有限上限。</summary>
    public TimeSpan StartupTimeout { get; }

    /// <summary>取得單次 Worker operation 的有限上限。</summary>
    public TimeSpan OperationTimeout { get; }

    /// <summary>取得 Runtime execution lease 與 Worker graceful drain 的有限上限。</summary>
    public TimeSpan DrainTimeout { get; }

    /// <summary>取得 retirement cancellation 後等待 finally 與 lease cleanup 的有限餘裕。</summary>
    public TimeSpan CancellationGracePeriod { get; }

    /// <summary>
    /// 取得建構時重新驗證並複製的 immutable Worker recycle options。
    /// 其中只有非秘密有限純量，不包含 Process、Timer、Task、Credential、Session 或 mutable collection。
    /// </summary>
    public OfficialWorkerRecyclePolicyOptions RecyclePolicyOptions { get; }

    /// <summary>
    /// 取得建構時產生的 immutable admission plan。此 internal seam 只供 Factory、Runtime 與契約測試使用；
    /// plan 可跨 generation 共用，因為它不包含 Manager、Semaphore、Lease、Token、Credential 或 mutable collection。
    /// </summary>
    internal OrganizationAdmissionPlan AdmissionPlan { get; }

    /// <summary>
    /// 建立一個全新的 Supervisor options 物件。每個 Worker process 都取得自己的 immutable options instance；
    /// bootstrap identity 使用部署擁有的 <see cref="WorkerProfileGenerationId"/>，不得改用 manager numeric generation。
    /// </summary>
    internal OfficialWorkerProfileOptions CreateWorkerOptions()
        => new()
        {
            ProfileAlias = ProfileAlias,
            ProfileGenerationId = WorkerProfileGenerationId,
            WorkerVersion = WorkerVersion,
            WorkerExecutablePath = WorkerExecutablePath,
            WorkerExecutableSha256 = WorkerExecutableSha256,
            PackageLockId = PackageLockId,
            StartupTimeout = StartupTimeout,
            OperationTimeout = OperationTimeout,
            DrainTimeout = DrainTimeout,
            RecyclePolicyOptions = CloneRecyclePolicyOptions(RecyclePolicyOptions)
        };

    /// <summary>驗證 bounded ASCII identifier；避免路徑、URI、空白或控制字元進入 process bootstrap。</summary>
    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Regex.IsMatch(
                value,
                SafeIdentifierPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
        {
            throw new ArgumentException(
                "Identifier must be 1-128 characters using only letters, digits, '.', '_' or '-'.",
                parameterName);
        }

        return value;
    }

    /// <summary>驗證 timeout 為正值且不超過 Supervisor 的十分鐘硬上限。</summary>
    private static TimeSpan ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > MaximumConfiguredTimeout)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Timeout must be positive and no greater than ten minutes.");
        }

        return value;
    }

    /// <summary>
    /// 逐欄複製已驗證的 immutable recycle options，讓每個 Profile Definition 擁有自己的部署 snapshot；
    /// 複本不建立 background owner，也不保留呼叫端 options reference 供未來配置機制意外替換。
    /// </summary>
    private static OfficialWorkerRecyclePolicyOptions CloneRecyclePolicyOptions(
        OfficialWorkerRecyclePolicyOptions source)
        => new(
            source.MaximumWorkerAge,
            source.MaximumCompletedOperations,
            source.MaximumPrivateBytes,
            source.MaximumWorkingSet,
            source.MaximumConsecutiveCompleteWorkerTimeouts);

    /// <summary>
    /// 逐欄複製 mutable admission options，讓驗證與 immutable plan 建立不會受呼叫端並行修改影響。
    /// 複本只活到 constructor 結束，不會成為跨 generation 共用狀態或長生命週期 Credential 容器。
    /// </summary>
    private static OrganizationAdmissionOptions CloneAdmissionOptions(OrganizationAdmissionOptions source)
        => new()
        {
            ExpectedOrganizationId = source.ExpectedOrganizationId,
            AggregateMaxInFlight = source.AggregateMaxInFlight,
            MaximumRuntimeHosts = source.MaximumRuntimeHosts,
            LocalQueueCapacity = source.LocalQueueCapacity,
            MaxDispatchEnvelopeBytes = source.MaxDispatchEnvelopeBytes,
            QueueAdmissionTimeoutSeconds = source.QueueAdmissionTimeoutSeconds,
            MaxInFlightAndQueuedPerWorkload = source.MaxInFlightAndQueuedPerWorkload,
            AdmissionNamespaceId = source.AdmissionNamespaceId,
            LeaseNamespaceId = source.LeaseNamespaceId,
            AdmissionEpoch = source.AdmissionEpoch,
            RuntimeHostSlotLeaseTtlSeconds = source.RuntimeHostSlotLeaseTtlSeconds,
            RuntimeHostSlotRenewalIntervalSeconds = source.RuntimeHostSlotRenewalIntervalSeconds,
            RuntimeHostSlotExpiryFenceSeconds = source.RuntimeHostSlotExpiryFenceSeconds,
            MaximumOutboundWorkLifetimeSeconds = source.MaximumOutboundWorkLifetimeSeconds,
            ShutdownDrainTimeoutSeconds = source.ShutdownDrainTimeoutSeconds,
            RequireDurableHostCoordinator = source.RequireDurableHostCoordinator
        };
}
