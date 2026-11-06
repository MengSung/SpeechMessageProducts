/*
 * 檔案整體說明
 *
 * 目的與定位：組合付款人、品項、付款方式及週期設定成產品中性的訂單草稿，產品附加資料放metadata；草稿尚未執行付款，銀行協定與送出由gateway處理。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentOrderDraft。
 * 資料／選項欄位包含 PaymentOrderDraft、ProfileName、ProductOrderId、Amount、Currency、Description、Payer、Method、Schedule、Items、
 * Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payment order draft used by host products before provider execution.
/// Keep product-specific data in Metadata and keep provider-specific protocol out of this type.
/// </summary>
public sealed record PaymentOrderDraft
{
    public string ProfileName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public PaymentPayerDraft Payer { get; init; } = new();
    public PaymentMethodSelection Method { get; init; } = new();
    public PaymentScheduleDraft Schedule { get; init; } = new();
    public IReadOnlyList<PaymentLineItemDraft> Items { get; init; } = Array.Empty<PaymentLineItemDraft>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
