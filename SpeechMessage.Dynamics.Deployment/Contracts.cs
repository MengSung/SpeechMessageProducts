using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 單次讀取的註冊清單。檔案可進版控，因此設定欄位只允許固定結構與秘密佔位；
/// 真實 token、收件者識別或帳密不得進入此模型。
/// </summary>
internal sealed record RegistrationManifest
{
    /// <summary>清單格式版本；不支援的值必須在任何組織讀寫前拒絕。</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>受此工具管理的唯一 Plugin 組件簡名，不接受路徑或任意組件。</summary>
    public string AssemblyName { get; init; } = "SpeechMessage.Dynamics.Plugins";
    /// <summary>步驟設定；每個穩定身份在驗證唯一後才可參與計畫。</summary>
    public IReadOnlyList<StepRegistration> Steps { get; init; } = Array.Empty<StepRegistration>();
}

/// <summary>
/// 一個受管理步驟的不可變範本。身份欄位決定唯一步驟，設定欄位只保留固定值或
/// `${secret:name}` 佔位；僅 apply 寫入瞬間才解析秘密。
/// </summary>
internal sealed record StepRegistration
{
    /// <summary>Plugin 完整型別名稱，必須在待部署 PE 中實作 IPlugin。</summary>
    public string Type { get; init; } = "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin";
    /// <summary>P0 骨架只允許 Create 訊息。</summary>
    public string Message { get; init; } = "Create";
    /// <summary>P0 骨架只允許 task 實體，避免觸碰會友或業務資料。</summary>
    public string Entity { get; init; } = "task";
    /// <summary>註冊階段；此骨架只允許 PostOperation。</summary>
    public string Stage { get; init; } = "PostOperation";
    /// <summary>執行模式；此骨架只允許 Asynchronous。</summary>
    public string Mode { get; init; } = "Asynchronous";
    /// <summary>穩定身份的一部分；不可依清單列位置推導。</summary>
    public int Rank { get; init; } = 1;
    /// <summary>Create 的篩選欄位；P0 必須為空，非空時拒絕而不靜默忽略。</summary>
    public IReadOnlyList<string> FilteringAttributes { get; init; } = Array.Empty<string>();
    /// <summary>前後映像；P0 不使用，非空時拒絕而不靜默遺漏。</summary>
    public IReadOnlyList<string> Images { get; init; } = Array.Empty<string>();
    /// <summary>只含固定模式、文化與秘密佔位的 JSON；不得含實際 userId。</summary>
    public string UnsecureConfig { get; init; } = "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}";
    /// <summary>測試 Channel Token 佔位；trace 模式為空，從不序列化實際值。</summary>
    public string SecureConfig { get; init; } = "";
    /// <summary>每組織明確啟用設定；缺少或 false 一律維持停用。</summary>
    public IReadOnlyDictionary<string, bool> EnabledByOrganization { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);
}

/// <summary>
/// 從有界 PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行待驗證組件的程式碼，
/// 也不將此物件放入 static、快取或跨命令狀態。
/// </summary>
internal sealed record AssemblyImage(string Name, string Version, string PublicKeyToken, string Content, string Sha256, IReadOnlyList<string> PluginTypes);

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
