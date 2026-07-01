namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral recurring-payment schedule.
/// Providers can read the mapped metadata only when the selected profile needs it.
/// </summary>
public sealed record PaymentScheduleDraft
{
    public bool IsRecurring { get; init; }
    public int TotalPeriods { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public int Frequency { get; init; }
    public DateOnly? StartDate { get; init; }
}
