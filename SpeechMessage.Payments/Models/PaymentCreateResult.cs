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
