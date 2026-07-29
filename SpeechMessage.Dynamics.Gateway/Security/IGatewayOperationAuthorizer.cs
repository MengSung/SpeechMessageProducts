using System.Collections.Immutable;
using System.Security.Claims;

namespace SpeechMessage.Dynamics.Gateway.Security;

/// <summary>
/// 定義 Gateway 在建立 <c>OperationExecutionRequest</c> 前唯一可用的 operation authorization 邊界。
/// 實作必須只接受 authentication middleware 建立的 <see cref="ClaimsPrincipal"/> 與 route 中的 alias／operation，
/// 並以 server-owned immutable binding 同時決定 workload、canonical alias 與 canonical operation；不得讀取
/// X-Principal、X-Workload、request body identity、credential、token 或 CRM endpoint。介面不授予任何資源 ownership，
/// 呼叫是同步、無 I/O、無鎖且不得建立背景工作，確保拒絕一定發生在 executor、admission queue 與 outbound transport 前。
/// </summary>
public interface IGatewayOperationAuthorizer
{
    /// <summary>
    /// 依序驗證 authentication、principal binding、profile alias 與 capability operation。
    /// 成功結果包含 server canonical 值；失敗結果不揭露可用 alias／operation，也不保留 principal、claims 或 request reference。
    /// </summary>
    /// <param name="principal">ASP.NET Core authentication middleware 產生的 request-scoped principal。</param>
    /// <param name="profileAlias">route 提供但尚未信任的 Profile Alias。</param>
    /// <param name="capabilityOperationId">route 提供但尚未信任的 Capability Operation ID。</param>
    /// <returns>不可變、短生命週期且不含 credential/token 的授權結果。</returns>
    GatewayOperationAuthorization Authorize(
        ClaimsPrincipal principal,
        string profileAlias,
        string capabilityOperationId);

    /// <summary>
    /// 驗證目前 authenticated principal 是否具有 server-owned workload binding，並回傳該 binding 的 immutable operation subset。
    /// Catalog 授權不接受 route、query、header 或 body 參數，因此 caller 不能選擇 workload 或擴張白名單；
    /// 成功結果只含非秘密 canonical ID，不含 principal、credential、token 或可變 collection，呼叫為同步、無 I/O 且不產生 cleanup ownership。
    /// </summary>
    /// <param name="principal">ASP.NET Core authentication middleware 產生的 request-scoped principal。</param>
    /// <returns>成功時含 workload 的 authorized operation IDs；失敗時為空且供 endpoint 統一回傳 403。</returns>
    GatewayOperationCatalogAuthorization AuthorizeOperationCatalog(ClaimsPrincipal principal);
}

/// <summary>
/// 表示一次 Gateway operation authorization 的不可變結果。
/// 成功時三個 routing 欄位全由 server binding／registry canonicalization 產生；失敗時它們皆為空字串，
/// 避免 caller-controlled route 值被後續程式誤當成已授權資料。結果不含 <see cref="ClaimsPrincipal"/>、
/// <c>HttpContext</c>、credential、token、stream 或 cancellation state，可在 request 完成後立即被回收，沒有 cleanup owner。
/// </summary>
/// <param name="Succeeded">是否完整通過 principal→workload→alias→operation 驗證。</param>
/// <param name="WorkloadSubjectId">成功時的 server-owned workload subject；失敗時為空。</param>
/// <param name="ProfileAlias">成功時的 catalog canonical alias；失敗時為空。</param>
/// <param name="CapabilityOperationId">成功時的 registry canonical operation ID；失敗時為空。</param>
/// <param name="FailureCode">內部穩定失敗分類；HTTP 回應仍統一 403，避免形成授權探測 oracle。</param>
public sealed record GatewayOperationAuthorization(
    bool Succeeded,
    string WorkloadSubjectId,
    string ProfileAlias,
    string CapabilityOperationId,
    string FailureCode)
{
    /// <summary>
    /// 建立成功結果。呼叫端只能傳入已由 immutable binding 與 registry 解析的 canonical 值，
    /// 此 helper 不執行額外 I/O 或配置查詢，也不保留輸入之外的物件參考。
    /// </summary>
    public static GatewayOperationAuthorization Success(
        string workloadSubjectId,
        string profileAlias,
        string capabilityOperationId)
        => new(
            true,
            workloadSubjectId,
            profileAlias,
            capabilityOperationId,
            string.Empty);

    /// <summary>
    /// 建立不攜帶 route／principal 資料的失敗結果，使 executor request 無法從失敗物件被誤建；
    /// failure code 只供 server-side diagnostics 分類，不應直接回傳給未授權 caller。
    /// </summary>
    public static GatewayOperationAuthorization Denied(string failureCode)
        => new(false, string.Empty, string.Empty, string.Empty, failureCode);
}

/// <summary>
/// 表示一次 operation catalog 授權的不可變結果。成功時 operation IDs 直接引用啟動期已防禦性複製的 immutable binding snapshot，
/// 可安全供並行 request 唯讀列舉；失敗時集合為空，避免 endpoint 因誤用結果而洩漏 registry。
/// 結果不保留 <see cref="ClaimsPrincipal"/>、<c>HttpContext</c>、token、credential、stream、cancellation registration 或背景工作，
/// request 完成後可直接回收且沒有額外 cleanup owner。
/// </summary>
/// <param name="Succeeded">是否通過 authentication 與 exact principal binding。</param>
/// <param name="WorkloadSubjectId">成功時的 server-owned workload subject；失敗時為空。</param>
/// <param name="CapabilityOperationIds">成功時 binding 允許的 canonical operation IDs；失敗時為空。</param>
/// <param name="FailureCode">內部穩定失敗分類；HTTP endpoint 統一以 403 呈現，不形成 principal mapping oracle。</param>
public sealed record GatewayOperationCatalogAuthorization(
    bool Succeeded,
    string WorkloadSubjectId,
    ImmutableArray<string> CapabilityOperationIds,
    string FailureCode)
{
    /// <summary>
    /// 建立成功 catalog 結果；呼叫端只能傳入 authorizer 已解析的 binding snapshot，不重新配置 List 或 mutable HashSet，
    /// 因此每次 request 的額外成本固定且不會建立跨 request cache。
    /// </summary>
    public static GatewayOperationCatalogAuthorization Success(GatewayWorkloadBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(
            true,
            binding.WorkloadSubjectId,
            binding.CapabilityOperationIds,
            string.Empty);
    }

    /// <summary>
    /// 建立不含 workload 或 operation metadata 的失敗結果；failure code 只供 server-side 分類，不應直接回傳給 caller。
    /// </summary>
    public static GatewayOperationCatalogAuthorization Denied(string failureCode)
        => new(false, string.Empty, ImmutableArray<string>.Empty, failureCode);
}
