namespace SpeechMessage.Payments.Models;

public enum PaymentErrorKind
{
    None = 0,
    ConfigurationInvalid = 1,
    RequestInvalid = 2,
    ProviderRejected = 3,
    ProviderUnavailable = 4,
    SignatureInvalid = 5,
    CallbackInvalid = 6,
    NetworkFailure = 7,
    SerializationFailure = 8,
    UnsupportedOperation = 9,
    Unexpected = 10
}
