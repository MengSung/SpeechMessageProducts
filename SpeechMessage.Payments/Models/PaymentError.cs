namespace SpeechMessage.Payments.Models;

/// <summary>
/// 金流核心統一錯誤模型。
/// Provider 拒絕、設定錯誤、網路失敗與簽章失敗都轉成此模型，讓產品層不用 catch provider 例外。
/// </summary>
public sealed record PaymentError
{
    public static PaymentError None { get; } = new();

    public PaymentErrorKind Kind { get; init; } = PaymentErrorKind.None;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool HasError => Kind != PaymentErrorKind.None;
}
