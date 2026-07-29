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
    /// Embedded 模式的 Windows 認證來源。
    /// HostIdentity 使用目前服務行程身分；SecretReference 只保存使用者名稱/密碼的秘密名稱，實際值由秘密提供者解析。
    /// </summary>
    public string CredentialSource { get; set; } = "HostIdentity";

    /// <summary>
    /// Windows 使用者名稱的秘密名稱，可對應環境變數或 local-dev CrmConnection bridge key。
    /// CredentialSource=SecretReference 時必填；不得直接填入帳號值。
    /// </summary>
    public string? UserNameSecretName { get; set; }

    /// <summary>
    /// Windows 密碼的秘密名稱；此欄位絕對不可放入密碼本體。
    /// </summary>
    public string? PasswordSecretName { get; set; }

    /// <summary>
    /// 選用的 Windows 網域秘密名稱；未設定時由帳號格式或執行環境決定。
    /// </summary>
    public string? DomainSecretName { get; set; }

    /// <summary>
    /// Embedded 認證模式，只允許 Windows 或 AdfsOAuth；必須在啟動時固定，不能由使用者要求切換。
    /// </summary>
    public string AuthMode { get; set; } = "Windows";

    /// <summary>
    /// ADFS authority，例如 https://speechmessagests.speechmessage.com.tw/adfs；不得包含使用者資訊或動態 query。
    /// </summary>
    public string? AuthorityUri { get; set; }

    /// <summary>
    /// CRM 組織的 OAuth resource/audience，例如 https://jesus.speechmessage.com.tw/。
    /// </summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// ADFS client application ID；此識別碼本身不是秘密，但仍必須由部署設定固定。
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client ID 存放於外部設定來源時使用的選用秘密名稱；不得在此欄位放入 client secret。
    /// </summary>
    public string? ClientIdSecretName { get; set; }

    /// <summary>
    /// confidential client 使用的選用 client-secret 秘密名稱；只保存參考，不保存秘密值。
    /// </summary>
    public string? ClientSecretName { get; set; }

    /// <summary>
    /// 選用的預先核發 bearer token 秘密名稱；Token 不得寫入 JSON、記錄或例外。
    /// </summary>
    public string? CredentialReferenceName { get; set; }

    /// <summary>
    /// 本機 local-dev-manifest 專用：允許 ADFS username/password grant。
    /// 正式環境必須 false，改走非密碼服務流程或預先核發的 bearer token。
    /// </summary>
    public bool AllowLocalDevPasswordGrant { get; set; }

    /// <summary>
    /// 選用的 refresh token 秘密名稱；Token 本體由秘密提供者或受控本機 token store 擁有。
    /// </summary>
    public string? RefreshTokenSecretName { get; set; }

    /// <summary>
    /// local-dev 專用 token store 路徑，保存 authorization_code 流程取得的 access/refresh token；正式環境不得使用。
    /// </summary>
    public string? LocalDevTokenStorePath { get; set; }

    /// <summary>
    /// local-dev authorization_code 流程使用的 OAuth redirect URI，必須與 ADFS client registration 完全一致。
    /// </summary>
    public string? RedirectUri { get; set; }
}
