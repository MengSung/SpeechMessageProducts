namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Host product supplied payer identity before a provider payment is created.
/// This stays product-neutral: no CRM contact, LINE user, MVC, or ChurchReport types.
/// </summary>
public sealed record PaymentPayerDraft
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string ExternalPayerId { get; init; } = string.Empty;
}
