/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：RegistrationManifest：單次讀取的註冊清單。檔案可進版控，因此設定欄位只允許固定結構與秘密佔位； 真實 token、收件者識別或帳密不得進入此模型。；
 * StepRegistration：一個受管理步驟的不可變範本。身份欄位決定唯一步驟，設定欄位只保留固定值或 `${secret:name}` 佔位；僅 apply 寫入瞬間才解析秘密。；AssemblyImage：從有界
 * PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行待驗證組件的程式碼， 也不將此物件放入 static、快取或跨命令狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 RegistrationManifest、StepRegistration、AssemblyImage、DeploymentOperation、DeploymentPlan、
 * IOrganizationConnector、OrganizationLease。
 * - 受此工具管理的 Plugin／Workflow 組件簡名，不接受路徑或任意組件。
 * - Plugin 完整型別名稱，必須在待部署 PE 中實作 IPlugin。
 * - RegistrationDocument 保存多組件根；各 RegistrationManifest 保留獨立步驟、映像及活動。
 * - Update 必須明列篩選欄位；Create／Delete 必須為空，不靜默忽略輸入。
 * 主要操作入口包含 AssemblyImage、DeploymentOperation、DeploymentPlan、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 清單第二版中單一組件的註冊描述。檔案可進版控，因此設定欄位只允許固定結構與秘密佔位；
/// 真實 token、收件者識別或帳密不得進入此模型。
/// </summary>
internal sealed record RegistrationManifest
{
    /// <summary>清單格式版本；不支援的值必須在任何組織讀寫前拒絕。</summary>
    [JsonIgnore]
    public int SchemaVersion { get; init; } = 2;
    /// <summary>受此工具管理的 Plugin／Workflow 組件簡名，不接受路徑或任意組件。</summary>
    [JsonRequired]
    public string AssemblyName { get; init; } = "SpeechMessage.Dynamics.Plugins";
    /// <summary>步驟設定；每個穩定身份在驗證唯一後才可參與計畫。</summary>
    public IReadOnlyList<StepRegistration> Steps { get; init; } = Array.Empty<StepRegistration>();
    /// <summary>活動在註冊工具中的群組；只有含 CodeActivity 的組件必填。</summary>
    public string ActivityGroup { get; init; } = "";
    /// <summary>該組件中每個公開活動恰好有一筆，不屬於 Plugin 步驟。</summary>
    public IReadOnlyList<ActivityRegistration> Activities { get; init; } = Array.Empty<ActivityRegistration>();
}

/// <summary>
/// 清單第二版的文件根；各組件保留獨立身分與步驟，不把多組件攤平到第一個組件。
/// Loader 完成全份驗證後才交給命令；物件只持有結構及秘密佔位，不擁有 SDK 資源。
/// </summary>
internal sealed record RegistrationDocument
{
    /// <summary>唯一支援版本是二；不得把未提供的版本當成預設二。</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 2;
    /// <summary>受管理組件的候選集合，先完整驗證唯一身分才可建立計畫。</summary>
    [JsonRequired]
    public IReadOnlyList<RegistrationManifest> Assemblies { get; init; } = Array.Empty<RegistrationManifest>();
}

/// <summary>
/// 一個受管理步驟的不可變範本。身份欄位決定唯一步驟，設定欄位只保留固定值或
/// `${secret:name}` 佔位；僅 apply 寫入瞬間才解析秘密。
/// </summary>
internal sealed record StepRegistration
{
    /// <summary>情境、switch 與影子報告共用的核定小寫模組代號。</summary>
    [JsonRequired]
    public string Module { get; init; } = "diagnostics";
    /// <summary>Plugin 完整型別名稱，必須在待部署 PE 中實作 IPlugin。</summary>
    [JsonRequired]
    public string Type { get; init; } = "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin";
    /// <summary>核定的 Create、Update 或 Delete 訊息；由 loader 驗證組合。</summary>
    [JsonRequired]
    public string Message { get; init; } = "Create";
    /// <summary>實體 logical name；只宣告註冊範圍，不作為組織或資料存取授權。</summary>
    [JsonRequired]
    public string Entity { get; init; } = "task";
    /// <summary>註冊階段只接受三種公開管線階段，並驗證映像與模式限制。</summary>
    [JsonRequired]
    public string Stage { get; init; } = "PostOperation";
    /// <summary>同步或非同步模式；非同步只可用於 PostOperation。</summary>
    [JsonRequired]
    public string Mode { get; init; } = "Asynchronous";
    /// <summary>穩定身份的一部分；不可依清單列位置推導。</summary>
    [JsonRequired]
    public int Rank { get; init; } = 1;
    /// <summary>Update 必須明列篩選欄位；Create／Delete 必須為空，不靜默忽略輸入。</summary>
    [JsonRequired]
    public IReadOnlyList<string> FilteringAttributes { get; init; } = Array.Empty<string>();
    /// <summary>具名的前後映像與必要欄位；不允許空欄位隱含擷取全部資料。</summary>
    [JsonRequired]
    public IReadOnlyList<RegistrationImage> Images { get; init; } = Array.Empty<RegistrationImage>();
    /// <summary>只含固定模式、文化與秘密佔位的 JSON；不得含實際 userId。</summary>
    [JsonRequired]
    public string UnsecureConfig { get; init; } = "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}";
    /// <summary>測試 Channel Token 佔位；trace 模式為空，從不序列化實際值。</summary>
    [JsonRequired]
    public string SecureConfig { get; init; } = "";
    /// <summary>每組織明確啟用設定；缺少或 false 一律維持停用。</summary>
    [JsonRequired]
    public IReadOnlyDictionary<string, bool> EnabledByOrganization { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    /// <summary>舊步驟只能以實際匯出的資料庫 ID 對應，不以名稱猜測或跨模組共用。</summary>
    public IReadOnlyList<LegacyStepReference> Legacy { get; init; } = Array.Empty<LegacyStepReference>();
    /// <summary>驗收摘要只有相對檔名、差異數與證據基準，不包含 trace 或業務資料。</summary>
    public ShadowReport ShadowReport { get; init; } = new();
}

/// <summary>映像只宣告具名欄位；由 planner 驗證與註冊，不允許整筆擷取。</summary>
internal sealed record RegistrationImage
{
    /// <summary>傳入 PluginInput 的映像別名，在同一步驟內必須唯一。</summary>
    [JsonRequired]
    public string Name { get; init; } = "";
    /// <summary>只接受 PreImage 或 PostImage，並受訊息階段限制。</summary>
    [JsonRequired]
    public string Type { get; init; } = "";
    /// <summary>明列實體 schema 欄位，空清單不得轉成全部欄位。</summary>
    [JsonRequired]
    public IReadOnlyList<string> Attributes { get; init; } = Array.Empty<string>();
}

/// <summary>舊步驟的非機密身分，來源必須是 export-legacy 的實際查詢結果。</summary>
internal sealed record LegacyStepReference
{
    /// <summary>舊組件簡名，用來核對 ID 所屬組件。</summary>
    [JsonRequired]
    public string Assembly { get; init; } = "";
    /// <summary>舊型別完整名稱，用來核對 owning type，不能當成步驟 ID。</summary>
    [JsonRequired]
    public string Type { get; init; } = "";
    /// <summary>舊步驟的非空資料庫 ID，格式錯誤或重複必須拒絕。</summary>
    [JsonRequired]
    public string StepId { get; init; } = "";
}

/// <summary>影子報告的版控摘要，空值表示尚未驗收，不代表零差異。</summary>
internal sealed record ShadowReport
{
    /// <summary>repo 外報告的檔名或相對位置，不保存原始內容。</summary>
    public string File { get; init; } = "";
    /// <summary>null 表示未驗收；非負數才是實際比較差異數。</summary>
    public int? Differences { get; init; }
    /// <summary>只允許 05A 核定的基準代號；不得把閱讀對照標成實機證據。</summary>
    public string Basis { get; init; } = "";
}

/// <summary>公開 CodeActivity 的註冊描述；群組由所屬組件提供。</summary>
internal sealed record ActivityRegistration
{
    /// <summary>活動所屬核定模組，與情境及影子測試共用。</summary>
    [JsonRequired]
    public string Module { get; init; } = "";
    /// <summary>經 PE 檢查的公開活動完整型別名稱。</summary>
    [JsonRequired]
    public string Type { get; init; } = "";
    /// <summary>工作流程編輯器顯示名稱，與舊活動的核定名稱一致。</summary>
    [JsonRequired]
    public string DisplayName { get; init; } = "";
    /// <summary>舊活動型別參照，沒有 Plugin step ID。</summary>
    public IReadOnlyList<LegacyActivityReference> Legacy { get; init; } = Array.Empty<LegacyActivityReference>();
    /// <summary>實機或離線驗收的安全摘要，預設尚未驗收。</summary>
    public ShadowReport ShadowReport { get; init; } = new();
}

/// <summary>舊活動的非秘密身分；由 exporter 查到使用該活動的流程。</summary>
internal sealed record LegacyActivityReference
{
    /// <summary>舊活動所屬組件簡名。</summary>
    [JsonRequired]
    public string Assembly { get; init; } = "";
    /// <summary>舊 CodeActivity 完整型別名稱。</summary>
    [JsonRequired]
    public string Type { get; init; } = "";
}

/// <summary>
/// 從有界 PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行待驗證組件的程式碼，
/// 也不將此物件放入 static、快取或跨命令狀態。
/// </summary>
internal sealed record AssemblyImage(string Name, string Version, string PublicKeyToken, string Content, string Sha256, IReadOnlyList<string> PluginTypes)
{
    /// <summary>公開且非 abstract 的 CodeActivity 型別；由 PE 靜態檢查取得，不能以執行 DLL 反射補齊。</summary>
    public IReadOnlyList<string> ActivityTypes { get; init; } = Array.Empty<string>();
}

/// <summary>計畫操作只存固定動詞與非敏感結構鍵，不保存設定值或憑證。</summary>
internal sealed record DeploymentOperation(string Kind, string Key);

/// <summary>
/// 可人工審核的不可變計畫。SHA-256 涵蓋所有有效欄位，apply 前另驗雜湊、來源清單、
/// DLL 與組織指紋；秘密只以名稱佔位呈現。
/// </summary>
internal sealed record DeploymentPlan(
    string Organization,
    DateTimeOffset CreatedUtc,
    string Fingerprint,
    string ManifestHash,
    string AssemblyHash,
    IReadOnlyList<DeploymentOperation> Operations,
    string Sha256)
{
    /// <summary>拒絕不支援的 plan 格式，避免舊格式被當成完整核准內容。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>供人工確認哪些 User Secrets 名稱會在寫入時解析，不含秘密值。</summary>
    public IReadOnlyList<string> SecretPlaceholders { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 單一命令的連線建立邊界。測試以記憶體服務實作，正式實作每次建立新 client，
/// 不得回傳靜態 client 或共用使用者／組織狀態的連線池。
/// </summary>
internal interface IOrganizationConnector
{
    /// <summary>只為已通過白名單驗證的組織建立本命令獨占服務。</summary>
    /// <param name="organization">由命令解析器核准的固定組織名稱。</param>
    /// <returns>命令結束時由唯一 owner Dispose 的連線租約。</returns>
    OrganizationLease Connect(string organization);
}

/// <summary>
/// 將服務與唯一釋放委派綁在一次命令生命週期內。Dispose 後立即解除服務與 delegate 參考，
/// 不允許後續命令重用此 lease，避免連線、credential 或故障狀態跨命令殘留。
/// </summary>
internal sealed class OrganizationLease : IDisposable
{
    private IOrganizationService? service;
    private Action? dispose;

    /// <summary>本指令獨占的 Dataverse 服務；租約釋放後不再保留可用服務參考。</summary>
    public IOrganizationService Service =>
        Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(OrganizationLease));

    /// <summary>建立租約並明確指定唯一釋放動作。</summary>
    /// <param name="service">只屬於本次命令的服務實例。</param>
    /// <param name="dispose">負責釋放服務及其連線資源的委派。</param>
    /// <exception cref="ArgumentNullException">服務或釋放委派為 null。</exception>
    public OrganizationLease(IOrganizationService service, Action dispose)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    /// <summary>冪等釋放連線並解除服務與 delegate 參考；釋放後不能再次取得此連線。</summary>
    public void Dispose()
    {
        var release = Interlocked.Exchange(ref dispose, null);
        if (release is null)
        {
            return;
        }

        try
        {
            release();
        }
        finally
        {
            Interlocked.Exchange(ref service, null);
        }
    }
}
