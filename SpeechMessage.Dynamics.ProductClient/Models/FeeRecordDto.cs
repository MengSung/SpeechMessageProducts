// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Models/FeeRecordDto.cs
// 目的：Package 1 fee-read 回傳給產品的中立 DTO。
//
// 保母教學：
// - 產品不要再直接吃 CRM Entity / SDK 型別。
// - 這個 DTO 只放畫面需要的欄位，不放 token / session / 連線字串。
// - CategoryLabel / PayWayLabel 優先用上游 formatted value；沒有再由產品端 fallback。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.Models;

/// <summary>
/// 單筆奉獻收費單讀取結果。
/// </summary>
public sealed class FeeRecordDto
{
    public Guid? FeeId { get; init; }
    public DateTimeOffset? CreatedOn { get; init; }
    public DateTimeOffset? PayDate { get; init; }
    public decimal Amount { get; init; }
    public int? PayWayOption { get; init; }
    public string? PayWayLabel { get; init; }
    public string? CategoryLabel { get; init; }
    public string? Others { get; init; }
    public string? PaidPeriod { get; init; }
    public string? Name { get; init; }
}
