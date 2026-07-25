// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs
// 目的：私有 Web API 連線器設定。
//
// 保母教學：
// - 只有 Gateway / Embedded 會用到這個設定。
// - 產品 JSON 不應直接暴露這層完整秘密。
// - 密碼、token、client secret 只能用秘密參考名稱，不能寫明文。
// - OrganizationBaseUri 是組織根路徑；ApprovedWebApiRoot 由程式推導。
// - 若你只填 OrganizationWebApiBaseUri（舊欄位），也允許，便於現有測試與過渡。
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 私有 no-SDK Web API 連線器設定。
/// </summary>
public sealed class DynamicsWebApiOptions
{
    public const string SectionName = "DynamicsWebApi";

    /// <summary>
    /// 組織根 URI，例如 https://crm.example.internal/Contoso/
    /// 不可含 user-info / query / fragment。
    /// </summary>
    public string? OrganizationBaseUri { get; set; }

    /// <summary>
    /// 相容欄位：若已是完整 Web API root（含 api/data/vX.Y/），可直接使用。
    /// 新設定請優先填 OrganizationBaseUri + CeVersion。
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
    /// Windows 憑證來源。AuthMode=Windows 時有效。
    /// </summary>
    public DynamicsCredentialSource CredentialSource { get; set; } = DynamicsCredentialSource.HostIdentity;

    /// <summary>
    /// 舊版相容秘密參考。Windows/SecretReference 或測試用 bearer 可沿用。
    /// </summary>
    public string? SecretReference { get; set; }

    /// <summary>
    /// Windows/SecretReference：使用者名稱秘密參考。
    /// </summary>
    public string? UserNameSecretName { get; set; }

    /// <summary>
    /// Windows/SecretReference：密碼秘密參考。
    /// </summary>
    public string? PasswordSecretName { get; set; }

    /// <summary>
    /// Windows/SecretReference：網域秘密參考（可選）。
    /// </summary>
    public string? DomainSecretName { get; set; }

    /// <summary>
    /// AdfsOAuth：authority 秘密參考。
    /// </summary>
    public string? AuthoritySecretName { get; set; }

    /// <summary>
    /// AdfsOAuth：client id 秘密參考。
    /// </summary>
    public string? ClientIdSecretName { get; set; }

    /// <summary>
    /// AdfsOAuth：已證明可行的服務憑證/token 秘密參考。
    /// Phase 1 先直接解析成 bearer access token，後續可換成正式 token provider。
    /// </summary>
    public string? CredentialReferenceName { get; set; }

    /// <summary>
    /// AdfsOAuth 可行性證據 ID（設定層記錄用）。
    /// </summary>
    public string? FeasibilityEvidenceId { get; set; }

    /// <summary>
    /// HTTP 逾時秒數。
    /// </summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 每個 server 的最大連線數（SocketsHttpHandler.MaxConnectionsPerServer）。
    /// </summary>
    [Range(1, 128)]
    public int MaxConnectionsPerServer { get; set; } = 4;

    /// <summary>
    /// HTTP 連線池生命週期（分鐘），用來處理 DNS/網路變更。
    /// </summary>
    [Range(1, 240)]
    public int PooledConnectionLifetimeMinutes { get; set; } = 15;
}
