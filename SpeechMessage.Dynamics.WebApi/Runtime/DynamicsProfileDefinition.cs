// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileDefinition.cs
// 目的：把部署設定快照封裝成不可變的 Profile 定義，並在每次建立 Generation 時提供全新深複本，
//       防止 Options reload 或其他 Profile 修改同一個 mutable 物件而造成跨 Generation 狀態污染。
// ============================================================================

using System.Text.RegularExpressions;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 不可變的 Dynamics Profile 定義。
/// Constructor 只驗證 Alias、時間上限與深複製設定，不解析 Secret、不建立 Client／Handler、不取得 Host Slot；
/// 因此定義可安全存放在 Runtime Manager 的 Alias Catalog，而不會長期保留 Credential、Token、Session 或網路資源。
/// 每次 <see cref="CreateOptionsSnapshot"/> 都回傳新的 mutable Options graph，確保 crm82、crm91、Active 與 Draining
/// Generation 之間不會透過同一個 Options／Admission 物件互相修改 Transport 或容量設定。
/// </summary>
public sealed class DynamicsProfileDefinition
{
    private const string AliasPattern = "^[A-Za-z0-9._-]{1,128}$";
    private readonly DynamicsWebApiOptions _optionsSnapshot;

    /// <summary>
    /// 建立一份 Profile Definition。
    /// </summary>
    /// <param name="profileAlias">伺服器端核准 Alias；只允許英數、點、底線與連字號，禁止 URI／Path／Query 語法。</param>
    /// <param name="options">部署設定快照；方法會立即深複製，呼叫端之後的修改不會影響此定義。</param>
    /// <param name="warmUpOnActivation">是否在發布前執行有界 WhoAmI Warm-up；測試與未完成驗證的正式 Profile 可關閉。</param>
    /// <param name="drainTimeout">等待既有 Execution Lease 自然完成的上限；未指定時使用 ShutdownDrainTimeoutSeconds。</param>
    /// <param name="cancellationGracePeriod">強制取消後等待 Lease 釋放的上限；未指定時使用最大工作生命週期加 expiry fence 與五秒清理餘裕。</param>
    public DynamicsProfileDefinition(
        string profileAlias,
        DynamicsWebApiOptions options,
        bool warmUpOnActivation,
        TimeSpan? drainTimeout = null,
        TimeSpan? cancellationGracePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(profileAlias) ||
            !string.Equals(profileAlias, profileAlias.Trim(), StringComparison.Ordinal) ||
            !Regex.IsMatch(
                profileAlias,
                AliasPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
        {
            throw new ArgumentException(
                "Profile alias must be 1-128 characters using only letters, digits, '.', '_' or '-'.",
                nameof(profileAlias));
        }

        _optionsSnapshot = CloneOptions(options);
        ProfileAlias = profileAlias;
        WarmUpOnActivation = warmUpOnActivation;
        DrainTimeout = drainTimeout
            ?? TimeSpan.FromSeconds(Math.Max(1, _optionsSnapshot.Admission.ShutdownDrainTimeoutSeconds));
        CancellationGracePeriod = cancellationGracePeriod
            ?? TimeSpan.FromSeconds(
                Math.Max(1, _optionsSnapshot.Admission.MaximumOutboundWorkLifetimeSeconds) +
                Math.Max(1, _optionsSnapshot.Admission.RuntimeHostSlotExpiryFenceSeconds) +
                5);

        if (DrainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout), "Drain timeout must be positive.");
        }

        if (CancellationGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cancellationGracePeriod),
                "Cancellation grace period must be positive.");
        }
    }

    /// <summary>取得通過嚴格語法驗證的伺服器端 Profile Alias。</summary>
    public string ProfileAlias { get; }

    /// <summary>取得是否在發布前執行受 Admission 與 Cancellation 約束的 WhoAmI Warm-up。</summary>
    public bool WarmUpOnActivation { get; }

    /// <summary>取得停止新 Lease 後等待既有工作自然完成的有界時間。</summary>
    public TimeSpan DrainTimeout { get; }

    /// <summary>取得強制取消後等待租約與 finally 清理完成的有界時間。</summary>
    public TimeSpan CancellationGracePeriod { get; }

    /// <summary>
    /// 建立一份全新的 DynamicsWebApiOptions 與 OrganizationAdmissionOptions 深複本。
    /// Factory 可自由將此複本交給 Options.Create、Transport、Token Provider 與 Client，
    /// 但不得把複本再分享給其他 Generation；秘密值仍只以 Reference Name 表達，實際 Credential 由 Secret Resolver 即時取得。
    /// </summary>
    public DynamicsWebApiOptions CreateOptionsSnapshot()
        => CloneOptions(_optionsSnapshot);

    /// <summary>
    /// 深複製所有目前已知的 Profile 與 Admission 欄位。
    /// 這裡刻意逐欄複製，而不是 JSON round-trip，避免序列化設定、未知欄位或未來 Converter 意外保留秘密／型別資訊；
    /// 新增 Options 欄位時必須同步更新此方法與測試，否則新的 Generation 不可宣稱為完整不可變快照。
    /// </summary>
    private static DynamicsWebApiOptions CloneOptions(DynamicsWebApiOptions source)
        => new()
        {
            OrganizationBaseUri = source.OrganizationBaseUri,
            OrganizationWebApiBaseUri = source.OrganizationWebApiBaseUri,
            CeVersion = source.CeVersion,
            AuthMode = source.AuthMode,
            CredentialSource = source.CredentialSource,
            SecretReference = source.SecretReference,
            UserNameSecretName = source.UserNameSecretName,
            PasswordSecretName = source.PasswordSecretName,
            DomainSecretName = source.DomainSecretName,
            AuthoritySecretName = source.AuthoritySecretName,
            ClientIdSecretName = source.ClientIdSecretName,
            CredentialReferenceName = source.CredentialReferenceName,
            FeasibilityEvidenceId = source.FeasibilityEvidenceId,
            AuthorityUri = source.AuthorityUri,
            ResourceUri = source.ResourceUri,
            ClientId = source.ClientId,
            ClientSecretName = source.ClientSecretName,
            AllowLocalDevPasswordGrant = source.AllowLocalDevPasswordGrant,
            RefreshTokenSecretName = source.RefreshTokenSecretName,
            RedirectUri = source.RedirectUri,
            TimeoutSeconds = source.TimeoutSeconds,
            MaxConnectionsPerServer = source.MaxConnectionsPerServer,
            PooledConnectionLifetimeMinutes = source.PooledConnectionLifetimeMinutes,
            MaxResponseBytes = source.MaxResponseBytes,
            MaxRetryAttempts = source.MaxRetryAttempts,
            MaxRetryDelaySeconds = source.MaxRetryDelaySeconds,
            Admission = CloneAdmissionOptions(source.Admission)
        };

    /// <summary>
    /// 深複製 Organization Admission 設定，使 Profile-local Options graph 不會共用可變 Admission 物件。
    /// </summary>
    private static OrganizationAdmissionOptions CloneAdmissionOptions(OrganizationAdmissionOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new OrganizationAdmissionOptions
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
}
