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
    /// ADFS authority URI，例如 https://sts.example.com/adfs；必須是部署固定的 HTTPS 端點。
    /// </summary>
    public string? AuthorityUri { get; set; }

    /// <summary>
    /// CRM 的 OAuth resource/audience，例如 https://jesus.example.com/；不得由要求內容覆寫。
    /// </summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// ADFS application/client ID；不是秘密，但仍屬設定檔世代的一部分，變更時必須 replace-and-drain。
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// confidential client 使用的選用 client-secret 秘密名稱；只存參考，不存秘密值。
    /// </summary>
    public string? ClientSecretName { get; set; }

    /// <summary>
    /// 僅 local-dev 允許對 ADFS 使用 username/password grant。
    /// 正式環境必須使用經核准的非密碼服務流程或預先核發 bearer，避免保存人類帳號密碼。
    /// </summary>
    public bool AllowLocalDevPasswordGrant { get; set; }

    /// <summary>
    /// 選用的 refresh-token 秘密名稱，可由環境變數或秘密庫解析；不得記錄 Token 本體。
    /// </summary>
    public string? RefreshTokenSecretName { get; set; }

    /// <summary>
    /// 僅 local-dev 使用的 JSON token store 路徑，保存 authorization_code 流程取得的 access/refresh token。
    /// </summary>
    public string? LocalDevTokenStorePath { get; set; }

    /// <summary>
    /// authorization_code 交換使用的 OAuth redirect URI，必須與 ADFS client registration 完全一致。
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
    /// 單一未壓縮 CRM JSON 回應的硬上限。回應以串流讀入共用緩衝區，超過上限時在 JSON 解析前拒絕，
    /// 避免惡意或異常回應造成大型配置與記憶體壓力；租用緩衝區歸還前會清除已使用範圍。
    /// </summary>
    [Range(1024, 67_108_864)]
    public int MaxResponseBytes { get; set; } = 2_097_152;

    /// <summary>
    /// 冪等唯讀作業遇到 HTTP 429 或 503 時的有界重試次數，不包含第一次嘗試。
    /// 寫入與其他狀態碼不會自動重送，避免重複副作用。
    /// </summary>
    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// 單次 Retry-After 或指數退避的最長等待秒數；所有重試仍共用 <see cref="TimeoutSeconds"/> 的總逾時，
    /// 因此不會因連續等待而形成無界背景工作。
    /// </summary>
    [Range(0, 30)]
    public int MaxRetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Organization 級 admission / capacity 設定。
    /// </summary>
    public OrganizationAdmissionOptions Admission { get; set; } = new();
}
