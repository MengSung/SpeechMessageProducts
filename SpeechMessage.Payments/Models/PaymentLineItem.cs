namespace SpeechMessage.Payments.Models;

public sealed record PaymentLineItem
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
}
