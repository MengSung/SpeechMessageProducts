/*
 * 檔案整體說明
 *
 * 目的與定位：描述產品選定的付款方式與provider profile路由資訊，將畫面選項轉為中性決策；此選擇不包含銀行協定，也不能讓未驗證的caller任意切換部署憑證。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentMethodSelection。
 * 資料／選項欄位包含 PaymentMethodSelection、Method、SubType、ProviderProfileName，名稱、型別與序列化規約須與消費端保持一致。
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
/// Product-neutral payment method chosen by the host product.
/// Provider profile is metadata for routing; provider protocol remains in SpeechMessage.Payments.
/// </summary>
public sealed record PaymentMethodSelection
{
    public string Method { get; init; } = string.Empty;
    public string SubType { get; init; } = string.Empty;
    public string ProviderProfileName { get; init; } = string.Empty;
}
