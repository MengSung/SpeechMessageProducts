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
