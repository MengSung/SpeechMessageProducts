// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs
// 目的：私有 Web API 連線器設定（含 admission capacity）。
//
// 保母教學：
// - 只有 Gateway / Embedded 會用到這個設定。
// - 密碼、token、client secret 只能用秘密參考名稱。
// - Admission 管的是 CRM 併發與排隊，不是 per-user session pool。
// ============================================================================

using System.ComponentModel.DataAnnotations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 私有 no-SDK Web API 連線器設定。
/// </summary>
public sealed class DynamicsWebApiOptions
{
    public const string SectionName = "DynamicsWebApi";

    /// <summary>
    /// 組織根 URI，例如 https://crm.example.internal/Contoso/
    /// </summary>
    public string? OrganizationBaseUri { get; set; }

    /// <summary>
    /// 相容欄位：完整 Web API root（含 api/data/vX.Y/）。
    /// </summary>
    public string? OrganizationWebApiBaseUri { get; set; }

    /// <summary>
    /// CE 主版本標籤：8.2 或 9.1。
    /// </summary>
    [Required]
    public string CeVersion { get; set; } = "9.1";

    /// <summary>
    /// 驗證模式。
    /// </summary>
    public DynamicsAuthMode AuthMode { get; set; } = DynamicsAuthMode.Windows;

    /// <summary>
    /// Windows 憑證來源。
    /// </summary>
    public DynamicsCredentialSource CredentialSource { get; set; } = DynamicsCredentialSource.HostIdentity;

    public string? SecretReference { get; set; }
    public string? UserNameSecretName { get; set; }
    public string? PasswordSecretName { get; set; }
    public string? DomainSecretName { get; set; }
    public string? AuthoritySecretName { get; set; }
    public string? ClientIdSecretName { get; set; }
    public string? CredentialReferenceName { get; set; }
    public string? FeasibilityEvidenceId { get; set; }

    /// <summary>
    /// ADFS authority URI, e.g. https://sts.example.com/adfs
    /// </summary>
    public string? AuthorityUri { get; set; }

    /// <summary>
    /// OAuth resource / audience for CRM, e.g. https://jesus.example.com/
    /// </summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// ADFS application (client) id. Not a secret.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Optional client secret name for confidential clients.
    /// </summary>
    public string? ClientSecretName { get; set; }

    /// <summary>
    /// Local-dev only: allow username/password grant against ADFS.
    /// Production should use non-password service flow / pre-issued bearer.
    /// </summary>
    public bool AllowLocalDevPasswordGrant { get; set; }

    /// <summary>
    /// Optional refresh-token secret name (env / secret store).
    /// </summary>
    public string? RefreshTokenSecretName { get; set; }

    /// <summary>
    /// Local-dev only JSON path for access/refresh tokens obtained via authorization_code.
    /// </summary>
    public string? LocalDevTokenStorePath { get; set; }

    /// <summary>
    /// OAuth redirect URI used by authorization_code exchange (must match ADFS client registration).
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// HTTP 逾時秒數。
    /// </summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 每個 server 的最大連線數。
    /// </summary>
    [Range(1, 128)]
    public int MaxConnectionsPerServer { get; set; } = 4;

    /// <summary>
    /// HTTP 連線池生命週期（分鐘）。
    /// </summary>
    [Range(1, 240)]
    public int PooledConnectionLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Hard cap for one uncompressed CRM JSON response. Responses are streamed
    /// into a pooled buffer and rejected before parsing when this limit is
    /// exceeded.
    /// </summary>
    [Range(1024, 67_108_864)]
    public int MaxResponseBytes { get; set; } = 2_097_152;

    /// <summary>
    /// Number of bounded retries for idempotent read operations after HTTP
    /// 429 or 503. The original attempt is not included in this count.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Maximum delay for one Retry-After or exponential-backoff interval.
    /// The overall operation remains bounded by <see cref="TimeoutSeconds"/>.
    /// </summary>
    [Range(0, 30)]
    public int MaxRetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Organization 級 admission / capacity 設定。
    /// </summary>
    public OrganizationAdmissionOptions Admission { get; set; } = new();
}
