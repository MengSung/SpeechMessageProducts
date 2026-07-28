// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs
// 目的：產品 JSON 的強型別設定模型（Gateway / Embedded 二選一）。
//
// 保母教學：
// - 這個設定只是「啟動綁定」，不是授權真相來源。
// - Gateway 模式只允許 Gateway 端點與 profile alias。
// - Embedded 模式只允許 profile 設定與 manifest/trust 來源。
// - 禁止在 JSON 放 CRM 密碼、client secret、使用者/LINE session。
// ============================================================================

using System.ComponentModel.DataAnnotations;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 產品端 Dynamics 連線模式設定。
/// 對應 appsettings / 產品 JSON 的 DynamicsAccess 區段。
/// </summary>
public sealed class ProductDynamicsOptions
{
    public const string SectionName = "DynamicsAccess";

    /// <summary>
    /// 執行模式：Gateway 或 Embedded。必須明確指定。
    /// </summary>
    [Required]
    public DynamicsExecutionMode ExecutionMode { get; set; } = DynamicsExecutionMode.Gateway;

    /// <summary>
    /// 邏輯組織/環境別名，例如 jesus-prod。不是 CRM 連線字串。
    /// </summary>
    [Required]
    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>
    /// Gateway 模式專用設定。ExecutionMode=Gateway 時必填。
    /// </summary>
    public GatewayModeOptions? Gateway { get; set; }

    /// <summary>
    /// Embedded 模式專用設定。ExecutionMode=Embedded 時必填。
    /// </summary>
    public EmbeddedModeOptions? Embedded { get; set; }
}

/// <summary>
/// Gateway 模式只需要知道「去哪裡打 Gateway」，不需要知道 CRM 密碼。
/// </summary>
public sealed class GatewayModeOptions
{
    /// <summary>
    /// 內部 Gateway base URL，例如 https://dynamics-gateway.internal/
    /// </summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// 可選的 API 路徑前綴。預設 /v1。
    /// </summary>
    public string ApiPrefix { get; set; } = "/v1";
}

/// <summary>
/// Embedded 模式在產品程序內執行受控操作。
/// 注意：仍不得把 raw CRM service client 暴露給業務程式。
/// </summary>
public sealed class EmbeddedModeOptions
{
    /// <summary>
    /// 組織 Web API 基底 URI（不含任意 query/user-info）。
    /// 例如 https://crm.example.com/api/data/v9.1/
    /// </summary>
    [Required]
    [Url]
    public string OrganizationWebApiBaseUri { get; set; } = string.Empty;

    /// <summary>
    /// CE 目標主版本標籤。目前只接受 8.2 或 9.1。
    /// </summary>
    [Required]
    public string CeVersion { get; set; } = "9.1";

    /// <summary>
    /// 秘密參考名稱（例如 KeyVault secret name），不是秘密本體。
    /// </summary>
    [Required]
    public string SecretReference { get; set; } = string.Empty;

    /// <summary>
    /// Embedded 啟動時用來驗證 registry/manifest 的來源 URI 或檔案路徑。
    /// 驗證失敗必須 fail-closed。
    /// </summary>
    [Required]
    public string ManifestOrRegistrySource { get; set; } = string.Empty;

    /// <summary>
    /// Windows credential source for Embedded mode.
    /// HostIdentity = process identity; SecretReference = resolve username/password via secret names.
    /// </summary>
    public string CredentialSource { get; set; } = "HostIdentity";

    /// <summary>
    /// Secret name for Windows username (env var name or local-dev CrmConnection bridge key).
    /// Required when CredentialSource=SecretReference.
    /// </summary>
    public string? UserNameSecretName { get; set; }

    /// <summary>
    /// Secret name for Windows password. Never put the password value itself here.
    /// </summary>
    public string? PasswordSecretName { get; set; }

    /// <summary>
    /// Optional secret name for Windows domain.
    /// </summary>
    public string? DomainSecretName { get; set; }

    /// <summary>
    /// Auth mode for Embedded: Windows or AdfsOAuth.
    /// </summary>
    public string AuthMode { get; set; } = "Windows";

    /// <summary>
    /// ADFS authority, e.g. https://speechmessagests.speechmessage.com.tw/adfs
    /// </summary>
    public string? AuthorityUri { get; set; }

    /// <summary>
    /// OAuth resource for CRM org, e.g. https://jesus.speechmessage.com.tw/
    /// </summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// ADFS client application id (public).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Optional client-id secret name if stored outside appsettings.
    /// </summary>
    public string? ClientIdSecretName { get; set; }

    /// <summary>
    /// Optional client-secret secret name for confidential clients.
    /// </summary>
    public string? ClientSecretName { get; set; }

    /// <summary>
    /// Optional pre-issued bearer token secret name.
    /// </summary>
    public string? CredentialReferenceName { get; set; }

    /// <summary>
    /// 本機 local-dev-manifest 專用：允許 ADFS username/password grant。
    /// 正式環境必須 false，改走非密碼服務流程或預先核發的 bearer token。
    /// </summary>
    public bool AllowLocalDevPasswordGrant { get; set; }

    /// <summary>
    /// Optional refresh-token secret name.
    /// </summary>
    public string? RefreshTokenSecretName { get; set; }

    /// <summary>
    /// Local-dev token store path (authorization_code / refresh_token).
    /// </summary>
    public string? LocalDevTokenStorePath { get; set; }

    /// <summary>
    /// OAuth redirect URI for local-dev authorization_code.
    /// </summary>
    public string? RedirectUri { get; set; }
}