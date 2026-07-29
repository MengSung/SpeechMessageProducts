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
