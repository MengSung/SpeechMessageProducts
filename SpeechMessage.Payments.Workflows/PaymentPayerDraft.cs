/*
 * 檔案整體說明
 *
 * 目的與定位：保存主機已確認的付款人識別及聯絡資料，供付款建立前映射；不持有CRM實體、HTTP或LINE物件，付款人資料只應在目前訂單處理範圍使用。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentPayerDraft。
 * 資料／選項欄位包含 PaymentPayerDraft、Name、Email、Phone、ExternalPayerId，名稱、型別與序列化規約須與消費端保持一致。
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
/// Host product supplied payer identity before a provider payment is created.
/// This stays product-neutral: no CRM contact, LINE user, MVC, or ChurchReport types.
/// </summary>
public sealed record PaymentPayerDraft
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string ExternalPayerId { get; init; } = string.Empty;
}
