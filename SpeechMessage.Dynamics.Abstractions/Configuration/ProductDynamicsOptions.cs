using System.ComponentModel.DataAnnotations;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 產品唯一可擁有的 Dynamics 部署設定。它刻意只含主機模式、Profile Alias 與 Gateway
/// 端點；Organization GUID、CRM Service URI、Connector、Credential、Token、Pool 與
/// Worker 皆屬部署端 ControlPlane，不能寫入產品 JSON 或跨產品/Session 傳遞。
/// </summary>
public sealed class ProductDynamicsOptions
{
    /// <summary>產品組態區段名稱。</summary>
    public const string SectionName = "DynamicsAccess";

    /// <summary>
    /// 啟動時固定的連線模式。此值只能由部署設定變更；請求處理期間不得切換，讓 DI owner
    /// 可以在停止或設定 replace-and-drain 時確定釋放對應 HTTP handler、Pool 與 Worker。
    /// </summary>
    [Required]
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Embedded;

    /// <summary>
    /// 部署端已登錄 Profile 的別名。它不是 CRM endpoint 或 Organization ID；Resolver 以此
    /// 選取不可變 Profile snapshot，並將容量、Credential 與連線狀態隔離在該 snapshot 內。
    /// </summary>
    [Required]
    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>
    /// DedicatedGateway 或 CentralGateway 的 HTTPS 設定。Embedded 模式不可需要或讀取此值，
    /// 以免產品在 F5 時意外建立網路 handler、保留 Gateway Session，或改走未核准端點。
    /// </summary>
    public GatewayEndpointOptions? Gateway { get; set; }
}

/// <summary>
/// 產品呼叫 Gateway 所需且受限的 HTTP 設定。此類別不承載 CRM URL、Connector 或任何憑證；
/// HTTP client、socket 與 response stream 的唯一所有者是 ProductClient DI 註冊，會隨
/// ServiceProvider 釋放，而不是被靜態欄位或使用者 Session 保留。
/// </summary>
public sealed class GatewayEndpointOptions
{
    /// <summary>Gateway 的絕對 HTTPS 根 URI，允許 Dedicated 的 localhost 或 Central 內部主機。</summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>受版本控制且無 query/fragment 的 Gateway API 前置路徑。</summary>
    public string ApiPrefix { get; set; } = "/v1";

    /// <summary>
    /// 單一 Gateway 回應可讀取的最大位元組數。此上限避免回應 Buffer 無界成長；超限時
    /// ProductClient 必須拒絕並在 finally 歸還暫租陣列與釋放 stream。
    /// </summary>
    public int MaxResponseBytes { get; set; } = 2_097_152;

    /// <summary>單次 Gateway 請求的部署端 timeout 秒數；實際使用時必須建立可釋放的 linked CTS。</summary>
    public int RequestTimeoutSeconds { get; set; } = 35;
}
