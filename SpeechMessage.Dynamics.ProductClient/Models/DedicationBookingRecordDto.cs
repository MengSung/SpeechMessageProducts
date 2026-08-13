// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Models/DedicationBookingRecordDto.cs
// 用途：定義 Package 1 認獻單讀取能力發佈給產品層的不可變值物件。
//
// 此 DTO 僅承載已由 deployment-owned operation 投影的允許欄位；它不保留 CRM Entity、
// OData 文件、連線、profile、credential、session、取消註冊或 transport resource。每一次
// 讀取都建立新的 DTO，避免任何 request、使用者或 profile 之間共用可變 wire record。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.Models;

/// <summary>
/// 表示認獻單讀取能力可安全發佈的不可變資料列。
/// 此型別只含後續畫面所需的純量投影，沒有 CRM SDK、連線或身分資訊；呼叫端不得將任一欄位
/// 作為 profile、租戶、帳號或授權範圍。資料列由 client 在目前 request 中防禦性複製，並以
/// 唯讀集合交付，使後續來源集合的變動不會影響已發佈結果。
/// </summary>
public sealed record DedicationBookingRecordDto
{
    /// <summary>
    /// 認獻單的識別碼。這是已投影的資料值，而非可用來選擇 connector、profile 或授權範圍的路由權限。
    /// </summary>
    public Guid? DedicationBookingId { get; init; }

    /// <summary>
    /// 認獻類別的 OptionSet 數值；若上游欄位不存在則為 <see langword="null"/>，不以預設值猜測類別。
    /// </summary>
    public int? DedicationCategoryOption { get; init; }

    /// <summary>
    /// 認獻類別的已投影顯示文字。文字僅在目前結果生命週期內使用，不會寫入共享快取。
    /// </summary>
    public string? DedicationCategoryLabel { get; init; }

    /// <summary>
    /// 認獻單狀態的 OptionSet 數值；空值保留上游的未知狀態，避免將資料誤判為有效狀態。
    /// </summary>
    public int? DedicationBookingStatusOption { get; init; }

    /// <summary>
    /// 認獻單狀態的已投影顯示文字；不包含 CRM 原始 Entity 或其他未允許的 metadata。
    /// </summary>
    public string? DedicationBookingStatusLabel { get; init; }

    /// <summary>
    /// 每一期的認獻金額。decimal 值已脫離 connector/wire record，沒有 money wrapper 的存留關係。
    /// </summary>
    public decimal? AmountPerStage { get; init; }

    /// <summary>
    /// 認獻期數的已投影文字；不將它解析或重寫為授權、日期或 profile 資訊。
    /// </summary>
    public string? TotalStages { get; init; }

    /// <summary>
    /// 認獻總金額。空值忠實表示上游未投影值，避免以零覆寫資料缺失。
    /// </summary>
    public decimal? DedicationAmount { get; init; }

    /// <summary>
    /// 已繳費期間的顯示值；此值僅供讀取結果呈現，不能當成後續查詢的授權依據。
    /// </summary>
    public string? PaidPeriod { get; init; }

    /// <summary>
    /// 已繳費彙總金額。該純量不會保留 CRM aggregate、Entity 或連線資源。
    /// </summary>
    public decimal? RollupPaidFee { get; init; }

    /// <summary>
    /// 認獻有效期間的開始時間；保留 <see cref="DateTimeOffset"/>，避免在跨 request 的共用狀態中隱含時區。
    /// </summary>
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// 認獻有效期間的結束時間；空值表示上游未提供日期，並不表示無限期保留或重試讀取。
    /// </summary>
    public DateTimeOffset? EndDate { get; init; }
}
