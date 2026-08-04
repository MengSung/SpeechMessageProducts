// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Configuration/ConfigurationProfileResolver.cs
// 用途：將部署端可變組態複製為大小寫不敏感、不可變且 fail-closed 的 Profile 快照。
// ============================================================================

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.ControlPlane.Configuration;

/// <summary>
/// 從已繫結設定建立 Profile Catalog 的純記憶體 Resolver。建構子會完整驗證並複製所有必要
/// scalar 值，之後不再保存原始 Dictionary、IConfiguration 或 options 參考；這可防止組態
/// 被修改後讓既有請求跨 Profile/Organization 共用錯誤的 Connector、Credential 參考或 Pool
/// 政策。此類別不配置任何 I/O、Session、Worker、Permit、Timer 或背景資源，無需 Dispose。
/// </summary>
public sealed class ConfigurationProfileResolver : IProfileResolver
{
    private const int MaximumAliasLength = 128;
    private const int MaximumCredentialReferenceLength = 256;
    private static readonly Regex SafeAliasPattern = new(
        "^[A-Za-z0-9._-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly ImmutableDictionary<string, ResolutionEntry> _entries;

    /// <summary>
    /// 建立一份新的不可變設定快照。輸入 collections 僅在建構期間讀取，完成後呼叫端可安全
    /// 釋放或替換其設定物件；每筆無效 Profile 都會保留 fail-closed 錯誤，而不會讓其他
    /// Alias 退回到它或建立不完整 runtime。
    /// </summary>
    /// <param name="profiles">以 Profile Alias 索引的部署端 Profile 設定。</param>
    /// <param name="organizationCatalog">以 Organization Alias 索引的已確認 Catalog。</param>
    /// <param name="generationId">此次設定快照的單調 generation；必須為正值。</param>
    /// <exception cref="ArgumentNullException">任一設定集合為 null 時擲回。</exception>
    /// <exception cref="ArgumentOutOfRangeException">generation 非正值時擲回，避免 Pool key 漂移。</exception>
    public ConfigurationProfileResolver(
        IReadOnlyDictionary<string, DynamicsProfileOptions> profiles,
        IReadOnlyDictionary<string, OrganizationCatalogEntry> organizationCatalog,
        long generationId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(organizationCatalog);
        if (generationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generationId), "Generation ID must be positive.");
        }

        var catalog = BuildCatalogSnapshot(organizationCatalog);
        var entries = ImmutableDictionary.CreateBuilder<string, ResolutionEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profiles)
        {
            var profileAlias = pair.Key;
            if (!IsValidIdentifier(profileAlias) || pair.Value is null)
            {
                if (!string.IsNullOrWhiteSpace(profileAlias))
                {
                    entries[profileAlias] = ResolutionEntry.Failure("profile.not-found");
                }

                continue;
            }

            entries[profileAlias] = BuildResolutionEntry(
                profileAlias,
                pair.Value,
                catalog,
                generationId);
        }

        _entries = entries.ToImmutable();
    }

    /// <summary>
    /// 以不區分大小寫的 Alias 取得既有 immutable snapshot。此方法只做字串驗證與 dictionary
    /// 查詢，不會建立 Connector/Worker/Permit 或觸碰 CredentialProvider；未知或不合法輸入
    /// 因而沒有連線、Session、記憶體或 handle 的遺留風險。
    /// </summary>
    public bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error)
    {
        profile = null;
        error = "profile.not-found";
        if (!IsValidIdentifier(profileAlias) || !_entries.TryGetValue(profileAlias, out var entry))
        {
            return false;
        }

        if (entry.Profile is null)
        {
            error = entry.Error;
            return false;
        }

        profile = entry.Profile;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 將 Catalog 複製為 immutable 字典。Catalog key 不合法或 entry 為 null 時略過，因為對應
    /// Profile 之後會明確得到 organization.not-found；這比保留半成品或猜測目標更安全。
    /// </summary>
    private static ImmutableDictionary<string, CatalogSnapshot> BuildCatalogSnapshot(
        IReadOnlyDictionary<string, OrganizationCatalogEntry> organizationCatalog)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, CatalogSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in organizationCatalog)
        {
            if (!IsValidIdentifier(pair.Key) || pair.Value is null)
            {
                continue;
            }

            var entry = pair.Value;
            builder[pair.Key] = new CatalogSnapshot(
                entry.OrganizationId,
                entry.State,
                IsValidServiceUri(entry.ServiceUri));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 建立一個 Alias 的成功或失敗快照。錯誤依規格固定且不包含敏感資料；檢查順序保證
    /// Organization 狀態與 GUID 在 Connector 相容性前被驗證，避免為未知實體選取任何 runtime。
    /// </summary>
    private static ResolutionEntry BuildResolutionEntry(
        string profileAlias,
        DynamicsProfileOptions profile,
        ImmutableDictionary<string, CatalogSnapshot> catalog,
        long generationId)
    {
        if (!IsValidIdentifier(profile.OrganizationAlias) ||
            !catalog.TryGetValue(profile.OrganizationAlias, out var organization))
        {
            return ResolutionEntry.Failure("organization.not-found");
        }

        if (organization.State != OrganizationState.Enabled)
        {
            return ResolutionEntry.Failure("organization.disabled");
        }

        if (IsPlaceholderOrganizationId(organization.OrganizationId))
        {
            return ResolutionEntry.Failure("organization.identity-placeholder");
        }

        if (!organization.HasValidServiceUri)
        {
            return ResolutionEntry.Failure("organization.not-found");
        }

        if (!IsCompatible(profile.CeVersion, profile.ConnectorKind))
        {
            return ResolutionEntry.Failure("profile.connector-incompatible");
        }

        if (!IsValidCredentialReference(profile.CredentialReference))
        {
            return ResolutionEntry.Failure("credential.unresolvable");
        }

        if (!TryCreatePoolPolicy(profile.Pool, out var pool) ||
            !TryCreateOperationPolicy(profile.Operation, out var operation))
        {
            return ResolutionEntry.Failure("profile.invalid-policy");
        }

        return ResolutionEntry.Success(new ResolvedProfile(
            profileAlias,
            profile.OrganizationAlias,
            organization.OrganizationId,
            profile.CeVersion,
            profile.ConnectorKind,
            profile.CredentialReference.Trim(),
            pool,
            operation,
            generationId));
    }

    /// <summary>確認官方 Worker 僅服務同一個 CE 版本；Data8 目前明確支援兩個已知版本。</summary>
    private static bool IsCompatible(CeVersion ceVersion, ConnectorKind connectorKind)
        => connectorKind switch
        {
            ConnectorKind.Data8 => ceVersion is CeVersion.Ce82 or CeVersion.Ce91,
            ConnectorKind.OfficialCrm82Worker => ceVersion == CeVersion.Ce82,
            ConnectorKind.OfficialCrm91Worker => ceVersion == CeVersion.Ce91,
            _ => false
        };

    /// <summary>
    /// 驗證並複製 Pool 政策。所有上限均刻意有限；未來 Pool 可據此保證 lease 等待、閒置處置
    /// 與資源數量都受限，且不會因錯誤組態建立無界 queue 或永久保留連線。
    /// </summary>
    private static bool TryCreatePoolPolicy(PoolPolicy? source, out ResolvedPoolPolicy policy)
    {
        policy = null!;
        if (source is null || source.MinSize < 0 || source.MaxSize is < 1 or > 64 ||
            source.MinSize > source.MaxSize || source.IdleTimeoutMinutes is < 1 or > 60 ||
            source.AcquireTimeoutSeconds is < 1 or > 600)
        {
            return false;
        }

        policy = new ResolvedPoolPolicy(
            source.MinSize,
            source.MaxSize,
            TimeSpan.FromMinutes(source.IdleTimeoutMinutes),
            TimeSpan.FromSeconds(source.AcquireTimeoutSeconds),
            source.HealthCheckOnAcquire);
        return true;
    }

    /// <summary>驗證並複製單次操作策略，防止無界 retry 或永不取消的延遲資源。</summary>
    private static bool TryCreateOperationPolicy(OperationPolicy? source, out ResolvedOperationPolicy policy)
    {
        policy = null!;
        if (source is null || source.TimeoutSeconds is < 1 or > 600 || source.MaxRetries is < 0 or > 5 ||
            source.RetryBaseDelayMs is < 1 or > 10_000)
        {
            return false;
        }

        policy = new ResolvedOperationPolicy(
            TimeSpan.FromSeconds(source.TimeoutSeconds),
            source.MaxRetries,
            TimeSpan.FromMilliseconds(source.RetryBaseDelayMs));
        return true;
    }

    /// <summary>驗證 Alias/OrganizationAlias 不會成為 URI、檔案路徑或無界 cache key。</summary>
    private static bool IsValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumAliasLength &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
           SafeAliasPattern.IsMatch(value);

    /// <summary>Credential 只允許受限的參考名稱，防止將 secret 值或空白直接放入 Profile。</summary>
    private static bool IsValidCredentialReference(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumCredentialReferenceLength &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) && IsValidIdentifierLike(value);

    /// <summary>Credential reference 可含路徑分隔以配合受信任 secret store 的命名，但不含空白。</summary>
    private static bool IsValidIdentifierLike(string value)
        => value.All(static character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or '/');

    /// <summary>驗證部署端 Service URI 為無 credential/query/fragment 的 HTTPS Organization Service URL。</summary>
    private static bool IsValidServiceUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
           string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
           uri.AbsolutePath.EndsWith("/XRMServices/2011/Organization.svc", StringComparison.OrdinalIgnoreCase);

    /// <summary>拒絕全零或全 f GUID，避免樣板 placeholder 成為容量或 Pool 身分。</summary>
    private static bool IsPlaceholderOrganizationId(Guid organizationId)
    {
        var bytes = organizationId.ToByteArray();
        return organizationId == Guid.Empty || bytes.All(static value => value == byte.MaxValue);
    }

    /// <summary>保存 Catalog 中 Resolver 真正需要的不可變 scalar 值，不保留組態物件或 URI 實例。</summary>
    private sealed record CatalogSnapshot(
        Guid OrganizationId,
        OrganizationState State,
        bool HasValidServiceUri);

    /// <summary>保存單筆成功 Profile 或穩定失敗碼；兩者皆無資源所有權與可變 request 狀態。</summary>
    private sealed record ResolutionEntry(ResolvedProfile? Profile, string Error)
    {
        /// <summary>建立成功 entry。</summary>
        public static ResolutionEntry Success(ResolvedProfile profile) => new(profile, string.Empty);

        /// <summary>建立 fail-closed entry。</summary>
        public static ResolutionEntry Failure(string error) => new(null, error);
    }
}
