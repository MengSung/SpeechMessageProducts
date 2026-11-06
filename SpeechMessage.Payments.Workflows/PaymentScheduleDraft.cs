/*
 * 檔案整體說明
 *
 * 目的與定位：描述定期付款的中性週期草稿，將期別與排程資訊交給需要它的provider profile映射；它不建立timer或自動扣款背景工作，也不自行決定銀行排程規則。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentScheduleDraft。
 * 資料／選項欄位包含 PaymentScheduleDraft、IsRecurring、TotalPeriods、PeriodType、Frequency、StartDate，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs、
 * SpeechMessage.Payments.Workflows/PaymentOrderDraft.cs、
 * SpeechMessageProducts.ChurchReport/Payments/DonationPaymentFormModelMapper.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral recurring-payment schedule.
/// Providers can read the mapped metadata only when the selected profile needs it.
/// </summary>
public sealed record PaymentScheduleDraft
{
    public bool IsRecurring { get; init; }
    public int TotalPeriods { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public int Frequency { get; init; }
    public DateOnly? StartDate { get; init; }
}
