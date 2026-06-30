namespace SpeechMessage.Payments.Models;

/// <summary>
/// 查詢付款狀態的通用請求。
/// ProviderOrderRef 使用中立命名，不讓 QPay PayToken、MyPay uid 或台新 transaction_id 外漏為公開 API。
/// </summary>
public sealed record PaymentQueryRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderOrderRef { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
