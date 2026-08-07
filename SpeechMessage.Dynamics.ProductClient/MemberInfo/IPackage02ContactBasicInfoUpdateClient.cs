// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/IPackage02ContactBasicInfoUpdateClient.cs
// 目的：提供 ChurchReport 呼叫 P7.2 contact basic-info capability 的唯一產品端介面。
//
// 邊界：
// - 介面只接受封閉 request DTO，不接受 Entity、欄位 dictionary、FetchXML、CRM URL、credential 或 token。
// - ProfileAlias 與 WorkloadSubjectId 由產品組合根／已驗證 workload 提供；ConnectorKind 與 CE version
//   仍由 deployment-owned profile 固定，不能由 request 選擇。
// - implementation 不擁有 HTTP、Data8 client、permit 或 session；那些資源由 executor 的 request scope 管理。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// P7.2 會友基本資料更新的 typed ProductClient 入口。
/// </summary>
public interface IPackage02ContactBasicInfoUpdateClient
{
    /// <summary>
    /// 以固定 capability 更新 contact 的手機與地址文字欄位，並要求 executor 回傳已確認的 read-back branch。
    /// 空白 phone/address 沿用「不覆寫」語意；缺少兩者時仍由相同 operation 產生封閉 NoChange 結果，
    /// 不在產品端建立 legacy CRM 或第二條 transport 路徑。
    /// </summary>
    /// <param name="request">只含產品可見 routing scalar、contact GUID、兩個 allowlisted 值與冪等鍵的 request。</param>
    /// <param name="cancellationToken">由目前 request scope 擁有、且不被 client 保存的取消訊號。</param>
    /// <returns>只含 Changed/NoChange 與固定 correlation category 的產品結果。</returns>
    Task<ContactBasicInfoUpdateResult> UpdateAsync(
        ContactBasicInfoUpdateRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// P7.2 contact basic-info 的最小產品端輸入。它不包含 CRM logical name、OrganizationId、endpoint、
/// ConnectorKind、CE version、credential、token 或 raw SDK payload；這些都由部署端 profile 與 connector 內部擁有。
/// </summary>
public sealed record ContactBasicInfoUpdateRequest
{
    /// <summary>部署端固定的 Dynamics profile alias；不能由 HTTP body 或終端使用者任意改寫。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證服務身分推導的 workload subject；不可用 LINE user id 充當 CRM session key。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>唯一允許更新的 contact identity。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>要寫入 mobilephone 的文字；null 或空白表示不覆寫。</summary>
    public string? Phone { get; init; }

    /// <summary>要寫入 address2_line1 的文字；null 或空白表示不覆寫。</summary>
    public string? Address { get; init; }

    /// <summary>由 caller 提供的 bounded URL-safe 冪等鍵；此 capability 不允許缺少或重複使用不合法格式。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>
/// P7.2 contact basic-info 的產品安全結果。只重用 Abstractions 已驗證的 enum，不保存 contact、欄位值、
/// baseline、CRM response、URL、credential、token、exception 或任何 connector resource。
/// </summary>
public sealed record ContactBasicInfoUpdateResult
{
    /// <summary>表示沒有變更或已完成並 read-back 確認的封閉結果。</summary>
    public required ContactBasicInfoUpdateDisposition Disposition { get; init; }

    /// <summary>只描述 no-dispatch 或 read-back-confirmed 的 bounded lifecycle 分類。</summary>
    public required ContactBasicInfoUpdateCorrelationCategory CorrelationCategory { get; init; }
}
