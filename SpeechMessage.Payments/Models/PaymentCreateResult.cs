/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款後的通用結果。 PaymentPageUrl 是所有 hosted payment provider 的共同導向網址； ProviderData 是產品 workflow 需要的已清理欄位，
 * Diagnostics 則只供除錯與稽核。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCreateResult。
 * 資料／選項欄位包含 PaymentCreateResult、Status、ProductOrderId、ProviderOrderRef、PaymentPageUrl、Error、ProviderData、
 * Diagnostics，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentReturnControllerTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款後的通用結果。
/// PaymentPageUrl 是所有 hosted payment provider 的共同導向網址；
/// ProviderData 是產品 workflow 需要的已清理欄位，Diagnostics 則只供除錯與稽核。
/// </summary>
public sealed record PaymentCreateResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    // ProviderOrderRef 是 provider 交易識別的中立名稱，例如永豐 TSNo、台新 transaction_id、MyPay uid。
    public string ProviderOrderRef { get; init; } = string.Empty;
    public string PaymentPageUrl { get; init; } = string.Empty;
    public PaymentError Error { get; init; } = PaymentError.None;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
