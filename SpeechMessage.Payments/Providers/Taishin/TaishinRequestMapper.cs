using System.Globalization;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

internal static class TaishinRequestMapper
{
    public static TaishinPaymentRequest MapCreatePayload(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        var payload = CreateBaseRequest(profile);
        payload.TxType = 1;
        payload.Params = new TaishinPaymentParams
        {
            Layout = ResolveLayout(request.PaymentMethod),
            OrderNo = request.ProductOrderId,
            Amt = ToMinorUnit(request.Amount),
            Cur = ToTaishinCurrency(request.Currency),
            OrderDesc = request.Description,
            CaptFlag = request.Metadata.TryGetValue("CaptFlag", out var captFlag) ? captFlag : "0",
            ResultFlag = "1",
            PostBackUrl = FirstNonEmpty(request.Callbacks.ReturnUrl, request.Callbacks.SuccessUrl),
            ResultUrl = request.Callbacks.BackendUrl,
            CardholderName = request.Customer.Name,
            CardholderEmail = request.Customer.Email,
            CardholderMobilePhone = string.IsNullOrWhiteSpace(request.Customer.Phone)
                ? null
                : new TaishinCardholderMobilePhone
                {
                    PhoneNumber = request.Customer.Phone
                }
        };

        return payload;
    }

    public static TaishinPaymentRequest MapQueryPayload(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request)
    {
        var payload = CreateBaseRequest(profile);
        payload.TxType = 7;
        payload.Params = new TaishinPaymentParams
        {
            OrderNo = FirstNonEmpty(request.ProductOrderId, request.ProviderOrderRef)
        };

        return payload;
    }

    private static TaishinPaymentRequest CreateBaseRequest(PaymentMerchantProfile profile)
    {
        return new TaishinPaymentRequest
        {
            Mid = GetCredential(profile, "StoreId"),
            Tid = GetCredential(profile, "TerminalId")
        };
    }

    private static string GetCredential(PaymentMerchantProfile profile, string key)
    {
        if (profile.Credentials.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"Taishin profile '{profile.Name}' is missing credential '{key}'.");
    }

    private static string ToMinorUnit(decimal amount)
    {
        return decimal
            .Round(amount * 100m, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
    }

    private static string ToTaishinCurrency(string currency)
    {
        return string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase)
            ? "NTD"
            : currency;
    }

    private static string ResolveLayout(string paymentMethod)
    {
        return string.Equals(paymentMethod, "Mobile", StringComparison.OrdinalIgnoreCase)
            ? "2"
            : "1";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
