namespace SpeechMessage.Payments.Models;

public enum PaymentAckKind
{
    None = 0,
    PlainText = 1,
    Json = 2,
    Redirect = 3
}
