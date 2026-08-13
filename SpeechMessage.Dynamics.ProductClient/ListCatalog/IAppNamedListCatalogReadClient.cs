// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/IAppNamedListCatalogReadClient.cs
// 用途：宣告 ORG-CALL-00014 的封閉 ProductClient 讀取能力。
//
// 呼叫端只能提供 deployment composition 已決定的 profileAlias/workloadSubjectId 與 request 取消權杖；介面不接受
// list selector、FetchXML、排序、owner、tenant、connector、endpoint 或 credential。這不是 ChurchReport consumer、
// feature gate 或任何 CE 流量啟用證據，且不提供 cache、retry、fallback 或 Entity rehydration 的擴充點。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 提供 app-named 名單目錄的固定、唯讀 ProductClient 邊界。
/// implementation 必須先驗證 deployment-owned profile/workload，才可呼叫 executor；成功時只能發佈 request-local、
/// defensive-copied DTO collection。transport、connector、lease、timeout/fault eviction 與其釋放順序仍由 executor
/// owner 負責，這個介面不保存 session、使用者、profile、回應或 cancellation registration。
/// </summary>
public interface IAppNamedListCatalogReadClient
{
    /// <summary>
    /// 執行固定 <c>list.catalog.retrieve.app.named</c> operation 並回傳目前 request 的 app-named 名單目錄快照。
    /// profile 與 workload 只能是 server/deployment 已決定的隔離邊界，不得從瀏覽器、路由、query、cookie 或另一個
    /// request 推導；無效 routing、錯誤 operation、錯誤 discriminator、缺失 branch 或無效資料列一律 fail closed，
    /// 不重試、不 fallback，也不回填 CRM Entity。
    /// </summary>
    /// <param name="profileAlias">由 deployment composition 擁有並驗證的 Dynamics profile alias。</param>
    /// <param name="workloadSubjectId">由 server 建立且限定此工作負載的 immutable subject ID。</param>
    /// <param name="cancellationToken">目前 request 的取消權杖，必須不經替換地傳遞給 executor。</param>
    /// <returns>不可轉型為陣列且不可由呼叫端改寫 backing collection 的 request-local DTO 快照。</returns>
    Task<IReadOnlyList<AppNamedListCatalogRecordDto>> RetrieveAppNamedListCatalogAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default);
}
