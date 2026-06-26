using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

internal static class MyPaySignatureVerifier
{
    private static readonly HashSet<string> KnownPrcCodes = new(StringComparer.Ordinal)
    {
        "250",
        "290",
        "600",
        "300",
        "400",
        "260",
        "270",
        "280"
    };

    public static PaymentError Validate(IReadOnlyDictionary<string, string> fields)
    {
        var errors = new List<string>();

        if (!fields.TryGetValue("uid", out var uid) || string.IsNullOrWhiteSpace(uid) || uid.Length != 32)
        {
            errors.Add("uid is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("key", out var key) || string.IsNullOrWhiteSpace(key) || key.Length != 32)
        {
            errors.Add("key is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("prc", out var prc) || !KnownPrcCodes.Contains(prc))
        {
            errors.Add("prc is missing or unsupported.");
        }

        if (!fields.TryGetValue("order_id", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            errors.Add("order_id is required.");
        }

        return errors.Count == 0
            ? PaymentError.None
            : new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = string.Join(" ", errors)
            };
    }
}
