using System.ComponentModel.DataAnnotations;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 產品端 Dynamics 存取設定，只描述產品到 Gateway 的非秘密路由形狀。
/// 產品不得在此設定 Dynamics endpoint、Worker、套件、Credential、Token 或使用者 Session。
/// </summary>
public sealed class ProductDynamicsOptions
{
    /// <summary>組態區段名稱。</summary>
    public const string SectionName = "DynamicsAccess";

    /// <summary>
    /// 部署時固定的執行模式。正式與開發的目前支援路徑都是 <see cref="DynamicsExecutionMode.Gateway"/>；
    /// <see cref="DynamicsExecutionMode.Embedded"/> 保留為延後能力且必須 fail closed。
    /// </summary>
    [Required]
    public DynamicsExecutionMode ExecutionMode { get; set; } = DynamicsExecutionMode.Gateway;

    /// <summary>
    /// 產品可見的邏輯 Profile alias；Gateway 會依已驗證的工作負載身分在伺服器端解析實際 Profile。
    /// </summary>
    [Required]
    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>Gateway 模式的內部服務設定。</summary>
    public GatewayModeOptions? Gateway { get; set; }

    /// <summary>
    /// Embedded 延後能力的非秘密信任綁定；不得包含任何 Dynamics 傳輸或驗證材料。
    /// </summary>
    public EmbeddedModeOptions? Embedded { get; set; }
}

/// <summary>
/// Central Gateway 與 Local Gateway 共用的產品端 HTTP 邊界設定。
/// Endpoint 必須指向受控 Gateway，而不是 Dynamics 服務。
/// </summary>
public sealed class GatewayModeOptions
{
    /// <summary>受控 Gateway 的 HTTPS base URI。</summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>版本化 Gateway API 前綴。</summary>
    public string ApiPrefix { get; set; } = "/v1";

    /// <summary>
    /// 單一 Gateway 回應可讀取的最大位元組數；ProductClient 會在完整配置進入記憶體前同時檢查
    /// Content-Length 與串流累計值，避免不受控回應造成記憶體保留或跨要求資源壓力。
    /// </summary>
    public int MaxResponseBytes { get; set; } = 2_097_152;
}

/// <summary>
/// Embedded 延後能力只保留部署端信任與容量協調綁定。
/// 此型別刻意不提供 Dynamics URL、版本、Secret、Credential、OAuth、Worker 或套件欄位，
/// 以免產品設定重新形成繞過 Gateway 的傳輸路線。
/// </summary>
public sealed class EmbeddedModeOptions
{
    /// <summary>
    /// 部署端核准的產品／Profile 綁定名稱。
    /// 這只是非秘密識別，不得包含 endpoint、帳號、Token、Cookie、LINE ID 或 Session ID。
    /// </summary>
    [Required]
    public string ProductProfileBinding { get; set; } = string.Empty;

    /// <summary>
    /// 部署端核准的 Organization admission coordinator 參照。
    /// 即使已填入，Embedded 仍不得自行建立 Worker、Credential、連線池、Timer 或背景工作。
    /// </summary>
    [Required]
    public string OrganizationAdmissionCoordinatorRef { get; set; } = string.Empty;
}
