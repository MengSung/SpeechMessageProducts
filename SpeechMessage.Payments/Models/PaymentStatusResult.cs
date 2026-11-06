/*
 * 檔案整體說明
 *
 * 目的與定位：查詢付款狀態後的通用結果。 ProviderData 用於產品 workflow；Diagnostics 用於除錯，兩者都必須由 sanitizer 清理敏感資料。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentStatusResult。
 * 資料／選項欄位包含 PaymentStatusResult、Status、ProductOrderId、ProviderOrderRef、ProviderTransactionId、Amount、Currency、
 * Error、ProviderData、Diagnostics，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentReturnControllerTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 查詢付款狀態後的通用結果。
/// ProviderData 用於產品 workflow；Diagnostics 用於除錯，兩者都必須由 sanitizer 清理敏感資料。
/// </summary>
public sealed record PaymentStatusResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderOrderRef { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public PaymentError Error { get; init; } = PaymentError.None;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
