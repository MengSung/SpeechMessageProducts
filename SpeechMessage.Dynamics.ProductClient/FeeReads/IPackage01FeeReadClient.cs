// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/FeeReads/IPackage01FeeReadClient.cs
// 目的：產品呼叫 Package 1 fee/lesson 讀取的唯一入口。
//
// 保母教學：
// - 產品只傳 contactId / 日期 / discipleLessonId 等 typed 參數。
// - 絕對不要傳 FetchXML、OData filter、連線字串。
// - 實作可能走 Gateway HTTP，也可能走 Embedded executor；對產品介面相同。
// - 命名雖然是 FeeRead，但 Package 1 也包含 fee 畫面需要的 stor-lesson 讀取。
// ============================================================================

using SpeechMessage.Dynamics.ProductClient.Models;

namespace SpeechMessage.Dynamics.ProductClient.FeeReads;

/// <summary>
/// Package 1 fee/lesson 讀取產品端介面。
/// </summary>
public interface IPackage01FeeReadClient
{
    /// <summary>
    /// 依聯絡人 + 日期區間查奉獻收費單。
    /// 對應受控 capability：fee.dedication.retrieve.by.contact.date.range；日期與 contact ID 由具型別參數編碼。
    /// </summary>
    Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        DateTime startDate,
        DateTime endDate,
        string? contactName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依聯絡人查奉獻收費單（不分日期）。
    /// 對應受控 capability：fee.dedication.retrieve.by.contact；不接受呼叫端提供的 FetchXML。
    /// </summary>
    Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 dedication booking + paid period 查 fee。
    /// 對應受控 capability：fees.retrieve.by.dedication.period；paidPeriod 會依伺服器範本的字串情境編碼。
    /// </summary>
    Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid dedicationBookingId,
        string paidPeriod,
        string? dedicationBookingName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// fee editor 依 disciple lesson 載入 stor lessons。
    /// 對應受控 capability：fees.editor.load.by.disciplelesson；僅回傳費用編輯器核准欄位。
    /// </summary>
    Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 contact 讀取 stor lessons。
    /// 對應受控 capability：lessons.stor.retrieve.by.contact；profile 與 workload 由產品啟動設定固定。
    /// </summary>
    Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 disciple lesson 讀取 stor lessons。
    /// 對應受控 capability：lessons.stor.retrieve.by.disciplelesson；呼叫端不能改寫 Dynamics 路由。
    /// </summary>
    Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        string? lessonName = null,
        CancellationToken cancellationToken = default);
}
