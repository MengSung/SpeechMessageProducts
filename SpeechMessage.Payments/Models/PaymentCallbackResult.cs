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
