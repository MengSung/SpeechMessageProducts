/*
 * 檔案整體說明
 *
 * 目的與定位：定義主機交付金流前的中性品項草稿，保存商品識別、數量、金額及附加metadata；核心不解讀產品實體ID，商品授權與價格核對仍由產品完成。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentLineItemDraft。
 * 資料／選項欄位包含 PaymentLineItemDraft、Name、Quantity、UnitPrice、Currency、ExternalItemId，名稱、型別與序列化規約須與消費端保持一致。
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
/// Host product supplied payable item before provider-specific mapping.
/// The external item id is carried as metadata because the payment core does not interpret it.
/// </summary>
public sealed record PaymentLineItemDraft
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ExternalItemId { get; init; } = string.Empty;
}
