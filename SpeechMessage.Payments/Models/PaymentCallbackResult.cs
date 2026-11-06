/*
 * 檔案整體說明
 *
 * 目的與定位：provider callback 解析後的通用結果。 Acknowledgement 描述 provider 需要的回應格式，宿主產品只負責轉成實際 web response。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbackResult。
 * 資料／選項欄位包含 PaymentCallbackResult、Status、ProductOrderId、ProviderTransactionId、Amount、Currency、Acknowledgement、
 * Error、ProviderData、Diagnostics，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Abstractions/IPaymentGateway.cs、SpeechMessage.Payments/Abstractions/IPaymentProvider.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// provider callback 解析後的通用結果。
/// Acknowledgement 描述 provider 需要的回應格式，宿主產品只負責轉成實際 web response。
/// </summary>
public sealed record PaymentCallbackResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    // 不同 provider 可能要回 plain text、JSON 或 redirect，規則由核心 parser 決定。
    public PaymentCallbackAcknowledgement Acknowledgement { get; init; } = PaymentCallbackAcknowledgement.None;
    public PaymentError Error { get; init; } = PaymentError.None;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
