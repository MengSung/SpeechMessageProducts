// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/IMemberInfoPresentRecordReadClient.cs
// 目的：定義 ORG-CALL-00026 的獨立、唯讀、DTO-only ProductClient 契約。
//
// 安全與生命週期邊界：
// - 此能力刻意不併入 IPackage02ContactProfileClient，避免 present-read gate 取得 LINE 寫入或 aggregate surface。
// - 請求只含 deployment-owned profile、server-chosen workload 與已授權 contact GUID；沒有 HTTP、Session、
//   endpoint、owner、credential、query、CRM SDK 或可由瀏覽器選擇 routing 的型別。
// - 回傳列僅為 immutable scalar DTO；每次呼叫產生 request-local snapshot，取消只向 executor 原樣傳遞，
//   executor 仍是 connector、lease、transport 與其 deterministic cleanup 的唯一 owner。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// 提供已授權 contact 個人出席紀錄的獨立唯讀能力。
/// 這不是 generic Dynamics query，也不是既有 contact-profile 寫入 aggregate 的延伸；固定 operation、CE 版本、
/// response discriminator 與 contact-only parameter map 都由實作鎖定。介面不保存 session、profile、回應、
/// token、cache、timer、subscription、背景工作或外部資源，因此可由 DI 以無狀態 singleton 安全持有。
/// </summary>
public interface IMemberInfoPresentRecordReadClient
{
    /// <summary>
    /// 執行唯一 <c>memberinfo.present.retrieve.by.contact</c> operation，取得目前 request 的唯讀 DTO 快照。
    /// 呼叫端必須先完成 contact 的 server authorization；<paramref name="request"/> 中的 profile/workload 只能由
    /// deployment/service composition 產生，不能從 route、query、body、header、cookie 或 Session 透傳。實作會在
    /// outbound executor I/O 前拒絕空白 routing 或空 GUID，並對錯誤 CE、operation、branch、record 或上游失敗
    /// fail closed；不重試、不 fallback 至 legacy CRM，也不發布 partial collection。
    /// </summary>
    /// <param name="request">不含 CRM/HTTP/Session 型別的 deployment-owned 與已授權純量 request。</param>
    /// <param name="cancellationToken">目前 ASP.NET Core request 的取消權杖，必須原樣傳至 executor。</param>
    /// <returns>不暴露 backing array、不可由呼叫端寫入的新 DTO collection。</returns>
    Task<IReadOnlyList<MemberInfoPresentRecordReadDto>> RetrievePresentRecordsByContactAsync(
        MemberInfoPresentRecordReadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// ORG-CALL-00026 的受控純量 request。
/// ProfileAlias 與 WorkloadSubjectId 是部署/服務端已決定的 isolation routing 值，ContactId 僅是 controller 已授權
/// 的目標 locator；三者都不從 HTTP 或 Session 取得 authority。此 record 不含 credential、endpoint、owner、
/// query、Entity、cancellation registration 或資源 owner，因此僅隨單一呼叫堆疊存活，不能作為 shared cache key。
/// </summary>
public sealed record MemberInfoPresentRecordReadRequest
{
    /// <summary>
    /// 由 deployment composition 選定的 Dynamics profile alias。實作在 dispatch 前修剪、嚴格 UTF-8 驗證與限制
    /// 128 bytes；無效值不會觸發 executor、connector、host、lease 或 outbound I/O，也不能改選其他 profile。
    /// </summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 由服務端固定選定的 workload subject。實作在 dispatch 前修剪、嚴格 UTF-8 驗證與限制 256 bytes；它不是
    /// 登入使用者、Session、token 或瀏覽器提供的 subject，且不會被 singleton 保存至下一個 request。
    /// </summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>
    /// 已由 controller authorization 確認可讀取的 contact GUID。此值只定位固定 query 的 contact condition，
    /// 不能選擇 profile、organization、connector、credential、owner、endpoint 或資料範圍；空 GUID 一律在 I/O 前拒絕。
    /// </summary>
    public required Guid ContactId { get; init; }
}

/// <summary>
/// 個人出席紀錄的產品公開純量 DTO。
/// 每一個 instance 都由 ProductClient 從已驗證 wire record 明確複製；不含 CRM Entity、lookup graph、profile、
/// session、credential、cookie、endpoint、query、lease、stream、cancellation token 或可釋放資源。日期保持 nullable
/// <see cref="DateTime"/> 原值，避免此 boundary 未經驗證地改寫既有 Sunday-date 時區或 legacy display 語意。
/// </summary>
public sealed record MemberInfoPresentRecordReadDto
{
    /// <summary>
    /// 固定 present-record projection 的唯一非空 GUID。它只識別當前已授權 response 中的列，不能回送作 profile、
    /// authorization 或 connector selector；client 會拒絕空白或同一 response 內重複的值。
    /// </summary>
    public required Guid PresentRecordId { get; init; }

    /// <summary>
    /// 從固定 contact lookup 已投影的可選顯示名稱。文字在 wire/client 邊界維持 bounded UTF-8 scalar，沒有 Entity
    /// 或 Session reference；null 保留 legacy 缺值語意，不能觸發額外 CRM 補查、cache 或 fallback。
    /// </summary>
    public string? ContactFullName { get; init; }

    /// <summary>
    /// 從固定 <c>new_sunday_date</c> projection 複製的可選日期。值完全依上游封閉 contract 傳遞，不在 client
    /// 做 UTC、local、Unspecified 或 sentinel 推測；缺值保留 null，後續 legacy display 相容轉換由 consumer 擁有。
    /// </summary>
    public DateTime? SundayDate { get; init; }

    /// <summary>
    /// 固定 <c>new_sunday_present_this_week</c> 已由 connector closed-domain 驗證後的出席旗標。
    /// </summary>
    public required bool Sunday { get; init; }

    /// <summary>
    /// 固定 <c>new_group_present_this_week</c> 已由 connector closed-domain 驗證後的小組出席旗標。
    /// </summary>
    public required bool SmallGroup { get; init; }

    /// <summary>
    /// 從固定 <c>new_explanation</c> projection 複製的可選 bounded 說明文字。null 代表沒有說明；client 不截斷、
    /// 不記錄原文、不以外部 lookup 補值，避免隱藏資料遺失或延長另一個 request 的個人資料生命週期。
    /// </summary>
    public string? PrayItem { get; init; }
}
