// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/IAppNamedMembershipReadClient.cs
// 用途：宣告 ORG-CALL-00057 app-named membership 的封閉、唯讀 ProductClient 邊界。
//
// 此 contract 只接受 deployment-owned profile、server-owned workload 與已授權 contact GUID；它不暴露 HTTP、
// Entity、QueryExpression、list selector、endpoint、credential、Session、cache、retry 或 fallback。executor 仍是
// connector、lease、transport、timeout/cancellation/fault cleanup 的唯一 owner，client 不保存跨 request 的可變資料。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 提供已授權 contact 的固定 app-named membership 唯讀能力。
/// 實作必須在任何 executor I/O 前驗證所有 routing scalar 與 contact GUID，並且只接受 exact operation、response
/// kind 與 non-null membership branch。每次成功呼叫都要建立新的 DTO 與不能向下轉型成陣列的唯讀 collection，避免
/// singleton 透過 profile、workload、contact、wire row 或回應集合洩漏另一使用者、profile 或 request 的資料。
/// </summary>
public interface IAppNamedMembershipReadClient
{
    /// <summary>
    /// 執行唯一 <c>list.membership.retrieve.appnamed.by.contact</c> operation，取得目前 request 的 membership snapshot。
    /// 呼叫端必須先以服務端權限流程確認 contact 可讀取；request 內的 profile/workload 只能由 deployment/service
    /// composition 決定，contact 只作固定 query 的 locator，三者都不可由 browser、route、query、cookie、Session 或
    /// 前一 request 選擇。無效 input、上游 fault 或封閉 response contract 違約均 fail closed，不重試、不 fallback。
    /// </summary>
    /// <param name="request">只含 deployment/server-owned routing 與已授權 contact GUID 的 immutable scalar request。</param>
    /// <param name="cancellationToken">目前 request 取消權杖，必須未經替換、linked registration 或捕捉地傳遞給 executor。</param>
    /// <returns>由 defensive-copied DTO 組成、沒有可轉型 backing array 的 request-local 唯讀結果。</returns>
    Task<IReadOnlyList<AppNamedMembershipRecordDto>> RetrieveAppNamedMembershipsByContactAsync(
        AppNamedMembershipReadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// ORG-CALL-00057 的受控純量讀取 request。
/// ProfileAlias 與 WorkloadSubjectId 是 deployment／服務端已決定的完整 routing isolation boundary，ContactId 則只能是
/// 已由上層 authorization 確認的目標 locator；此型別不含 caller-selected profile、credential、endpoint、query、
/// Entity、Session、cache key 或外部資源，僅在一個 request 呼叫堆疊內使用，不能作為共享狀態或快取權威。
/// </summary>
public sealed record AppNamedMembershipReadRequest
{
    /// <summary>
    /// 由 deployment composition 選定的 Dynamics profile alias。
    /// client 會在 dispatch 前拒絕空白值；它不得由瀏覽器、登入 Session、contact、list row 或前一 request 推導，
    /// 因為 profile 選擇會決定下游 connector pool 的隔離邊界。
    /// </summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 由 server 建立並限定此 workload 的 immutable subject。
    /// client 會在 dispatch 前拒絕空白值，且不將其寫入 logger、static field、cache、background work 或下一個 request；
    /// 任何 tenant/product/authorization 細節仍由上層服務端流程與 executor profile scope 負責。
    /// </summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>
    /// 已在 ProductClient 前完成 server authorization 的 contact 定位 GUID。
    /// 此值只會寫入固定 operation 的 <c>contactId</c> parameter，不能選擇 list、排序、filter、profile、connector、
    /// organization、endpoint、credential 或資料範圍；空 GUID 一律在 executor、lease 或任何 outbound I/O 前拒絕。
    /// </summary>
    public required Guid ContactId { get; init; }
}
