// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/FeeReads/IPackage01DedicationBookingReadClient.cs
// 用途：提供 Package 1 認獻單的封閉、具型別、唯讀讀取邊界。
//
// 呼叫端只能提供 server-owned profile/workload、contact locator 與相容用名稱；不能提供
// FetchXML、CRM Entity、endpoint、credential、connector 或任意 operation。授權仍必須先由
// 上層伺服器驗證；contactId 只是定位值，絕不自行建立授權。
// ============================================================================

using SpeechMessage.Dynamics.ProductClient.Models;

namespace SpeechMessage.Dynamics.ProductClient.FeeReads;

/// <summary>
/// 定義認獻單的封閉讀取 capability。
/// 實作只會使用已登錄的 <c>payments.dedication.retrieve.by.contact</c> operation，並在當前 request
/// 建立 DTO 快照。介面不公開 CRM/OData、連線、token、session 或可重用的 mutable collection，因此不會
/// 將一位使用者或 profile 的資料、資源所有權延長至另一個呼叫。
/// </summary>
public interface IPackage01DedicationBookingReadClient
{
    /// <summary>
    /// 依已由伺服器驗證之 contact 定位值讀取認獻單。
    /// profile 與 workload 必須由部署端選定，<paramref name="contactId"/> 只能在上層完成授權後使用；
    /// <paramref name="contactName"/> 是非權威的相容投影參數，不能改變查詢範圍。取消權杖會原樣傳至
    /// executor，讓 connector/transport 的 owner 依既有 finally 或 lease 規則確定釋放資源。
    /// </summary>
    /// <param name="profileAlias">部署端擁有且已驗證的 Dynamics profile alias。</param>
    /// <param name="workloadSubjectId">部署端擁有且已驗證的 workload subject 識別值。</param>
    /// <param name="contactId">經上層授權後的 contact 定位值，不是授權主體。</param>
    /// <param name="contactName">選用的相容顯示文字；不參與授權或 profile 選擇。</param>
    /// <param name="cancellationToken">目前 request 的取消權杖，必須原樣向下傳遞。</param>
    /// <returns>目前 request 專屬、不可由呼叫端改寫 backing collection 的認獻單 DTO 快照。</returns>
    Task<IReadOnlyList<DedicationBookingRecordDto>> RetrieveDedicationBookingsByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default);
}
