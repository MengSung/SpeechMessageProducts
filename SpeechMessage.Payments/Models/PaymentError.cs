namespace SpeechMessage.Payments.Models;

public sealed record PaymentError
{
    public static PaymentError None { get; } = new();

    public PaymentErrorKind Kind { get; init; } = PaymentErrorKind.None;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool HasError => Kind != PaymentErrorKind.None;
}
