// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/ISmallGroupAppNamedListCatalogReadClient.cs
// 用途：宣告 ORG-CALL-00065 small-group app-named list catalog 的封閉 ProductClient 讀取邊界。
//
// 介面只接受 deployment/server 已建立的 profileAlias、workloadSubjectId 和目前 request 的 CancellationToken；不接受
// list ID、leader、FetchXML、排序、owner、tenant、connector、endpoint、credential 或 caller-controlled selector。此
// capability 沒有 ChurchReport consumer、feature gate、CE 流量、cache、retry、fallback 或 Entity rehydration 的職責。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 提供 small-group app-named 名單目錄的固定唯讀 ProductClient contract。
/// implementation 必須在任何 executor I/O 前驗證 server-owned routing，並在 exact operation/kind/branch 通過後才將
/// immutable wire rows 防禦性複製為 DTO。connector、lease、permit、timeout/cancellation/fault eviction 與釋放順序由
/// executor 擁有；介面與 implementation 均不保存 session、profile、response、cache 或 cancellation registration。
/// </summary>
public interface ISmallGroupAppNamedListCatalogReadClient
{
    /// <summary>
    /// 執行固定 <c>list.catalog.retrieve.appnamed.smallgroups</c> operation，取得目前 request 專屬的小組名單快照。
    /// profile/workload 只能來自 server/deployment composition，不能由路由、query、cookie、header、browser 或前一個
    /// request 取得；無效 routing、錯誤 operation、錯誤 discriminator、缺失 branch、null row 或空白 list ID 一律
    /// fail closed，不能重試、fallback、回填 CRM Entity 或發布 partial collection。
    /// </summary>
    /// <param name="profileAlias">由 deployment composition 選定並驗證的 Dynamics profile alias。</param>
    /// <param name="workloadSubjectId">由 server 建立且限定此工作負載的 immutable subject ID。</param>
    /// <param name="cancellationToken">目前 request 的取消權杖，必須原樣交給 executor。</param>
    /// <returns>不可轉型為陣列、不可由呼叫端寫入的 request-local DTO collection。</returns>
    Task<IReadOnlyList<SmallGroupAppNamedListCatalogRecordDto>> RetrieveSmallGroupAppNamedListCatalogAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default);
}
