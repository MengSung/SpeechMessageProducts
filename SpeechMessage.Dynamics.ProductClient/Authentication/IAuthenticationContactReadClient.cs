// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Authentication/IAuthenticationContactReadClient.cs
// 用途：提供 ORG-CALL-00055／00056 disabled-by-default、DTO-only 的認證聯絡人查詢產品契約。
//
// 安全與生命週期邊界：
// 1. 唯一查詢輸入為帳號或 LINE ID lookup value；固定 operation、profile、workload、connector、組織與
//    credential 均由 server-owned composition/authorization 決定，不能由終端使用者或 DTO 另行選取。
// 2. 介面沒有 password、hash、token、cookie、Entity、raw response 或 raw exception；它不是登入、Session、
//    claims 或 credential verification API，既有 consumer 在獨立遷移前不得使用它。
// 3. 每次回傳都是呼叫專屬 immutable result，沒有 static/cache/session/background owner；取消只向下游原樣傳遞。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.Authentication;

/// <summary>
/// 認證聯絡人唯讀的 stateless typed ProductClient 入口。此介面只提供 local-only read boundary；deployment
/// gate 保持 false 時 composition root 不得建立 client、host、pool、handler 或 outbound I/O。
/// </summary>
public interface IAuthenticationContactReadClient
{
    /// <summary>
    /// 以已由伺服器流程提供的帳號 lookup value 查詢固定 contact 投影。此方法不驗證帳號密碼，且不接受
    /// caller-selected query、profile 或 credential；查到 zero／duplicate／秘密分類時一律回傳固定 fail-closed 結果。
    /// </summary>
    /// <param name="profileAlias">由部署與授權層決定的 profile alias，不得由 browser/session 充當 authority。</param>
    /// <param name="workloadSubjectId">伺服器推導的 workload subject，不得使用 contact、LINE ID 或 session ID。</param>
    /// <param name="accountLookupValue">唯一允許的帳號定位值；空白、無效 Unicode 或超界值在 executor I/O 前拒絕。</param>
    /// <param name="cancellationToken">目前 request scope 的取消訊號；client 不註冊、不保存且原樣向 executor 傳遞。</param>
    /// <returns>不含秘密、Entity 或 transport 資料的 request-local immutable closed result。</returns>
    Task<AuthenticationContactReadResult> RetrieveByAccountAsync(
        string profileAlias,
        string workloadSubjectId,
        string accountLookupValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以已由伺服器流程提供的 LINE ID lookup value 查詢固定 active-contact 投影。active 條件與基數檢查由
    /// server-owned executor 固定；本方法不接受 raw CRM filter，亦不會在失敗時改走 legacy path 或重試。
    /// </summary>
    /// <param name="profileAlias">由部署與授權層決定的 profile alias，不得由 browser/session 充當 authority。</param>
    /// <param name="workloadSubjectId">伺服器推導的 workload subject，不得使用 contact、LINE ID 或 session ID。</param>
    /// <param name="lineIdLookupValue">唯一允許的 LINE ID 定位值；空白、無效 Unicode 或超界值在 executor I/O 前拒絕。</param>
    /// <param name="cancellationToken">目前 request scope 的取消訊號；client 不註冊、不保存且原樣向 executor 傳遞。</param>
    /// <returns>不含秘密、Entity 或 transport 資料的 request-local immutable closed result。</returns>
    Task<AuthenticationContactReadResult> RetrieveByLineIdAsync(
        string profileAlias,
        string workloadSubjectId,
        string lineIdLookupValue,
        CancellationToken cancellationToken = default);
}
