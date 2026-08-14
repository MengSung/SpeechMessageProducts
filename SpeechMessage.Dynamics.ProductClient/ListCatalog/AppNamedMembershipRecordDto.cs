// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/AppNamedMembershipRecordDto.cs
// 用途：定義 ORG-CALL-00057 可安全從 ProductClient 發佈的 App-named membership 純量快照。
//
// DTO 僅含 list GUID 與 nullable name；沒有 CRM Entity、listmember、lookup graph、query、profile、credential、
// session、cache、stream、token 或可釋放資源。client 在每次讀取時建立新 instance 並持有 collection defensive copy。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 表示一筆已授權 contact 的 App-named membership 安全發佈資料列。
/// 此型別只攜帶 fixed response branch 已驗證的 allowlisted scalar，不是 CRM list editable model 或任何 routing authority；
/// consumer 不得使用它的值選擇 profile、workload、connector、organization、endpoint、credential 或另一筆 contact。
/// instance 沒有 static/shared state、subscription、timer、stream 或 Dispose 責任，其最大生命週期即持有它的 request result。
/// </summary>
public sealed record AppNamedMembershipRecordDto
{
    /// <summary>
    /// 固定 membership projection 的非空 list GUID。
    /// 它只識別目前已授權 response 的一筆列，不能回送成 connector/profile/authorization selector；client 會拒絕空 GUID
    /// 與同一 response 的 duplicate GUID，避免不明確資料列在後續 consumer 或序列化時被誤用。
    /// </summary>
    public required Guid ListId { get; init; }

    /// <summary>
    /// 固定 projection 所提供的 nullable 名單顯示名稱。
    /// null 保留上游缺值語意；client 不會透過 Entity、metadata、cache、legacy read 或前一 request 補查，也不記錄名稱，
    /// 因而不會額外延長 contact membership 資料或讓另一個 profile／使用者觀察到它。
    /// </summary>
    public string? ListName { get; init; }
}
