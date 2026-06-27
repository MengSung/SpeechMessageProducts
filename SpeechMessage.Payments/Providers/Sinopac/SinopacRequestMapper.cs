using System.Globalization;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

internal static class SinopacRequestMapper
{
    public static SinopacOrderCreateRequest MapCreateRequest(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        var payType = ResolvePayType(request);
        var payload = new SinopacOrderCreateRequest
        {
            ShopNo = GetRequiredCredential(profile, "ShopNo"),
            OrderNo = request.ProductOrderId,
            Amount = ToMinorUnit(request.Amount),
            CurrencyID = NormalizeCurrency(request.Currency),
            PrdtName = request.Description,
            ReturnURL = FirstNonEmpty(request.Callbacks.ReturnUrl, request.Callbacks.SuccessUrl),
            BackendURL = request.Callbacks.BackendUrl,
            PayType = payType,
            Memo = GetMetadata(request, "Memo"),
            Param1 = GetMetadata(request, "Param1", "FeeId"),
            Param2 = GetMetadata(request, "Param2", "Organization"),
            Param3 = GetMetadata(request, "Param3", "Category", "CreditCategory")
        };

        if (string.Equals(payType, "A", StringComparison.OrdinalIgnoreCase))
        {
            payload.ATMParam = new SinopacOrderCreateAtmRequest
            {
                ExpireDate = FirstNonEmpty(
                    GetMetadata(request, "ExpireDate"),
                    DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            };
        }
        else
        {
            payload.CardParam = new SinopacOrderCreateCardRequest
            {
                AutoBilling = FirstNonEmpty(GetMetadata(request, "AutoBilling"), "Y"),
                ExpBillingDays = ParseNullableInt(GetMetadata(request, "ExpBillingDays")),
                ExpMinutes = ParseNullableInt(GetMetadata(request, "ExpMinutes")),
                PayTypeSub = FirstNonEmpty(
                    request.PaymentMethodSubType,
                    GetMetadata(request, "PayTypeSub"),
                    "ONE"),
                Staging = GetMetadata(request, "Staging"),
                DeductTotalNum = ParseNullableInt(GetMetadata(request, "DeductTotalNum")),
                PeriodType = GetMetadata(request, "PeriodType"),
                DeductFreq = ParseNullableInt(GetMetadata(request, "DeductFreq")),
                CCToken = GetMetadata(request, "CCToken")
            };
        }

        return payload;
    }

    public static SinopacOrderPayQueryRequest MapOrderPayQuery(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request)
    {
        return new SinopacOrderPayQueryRequest
        {
            ShopNo = GetRequiredCredential(profile, "ShopNo"),
            PayToken = request.ProviderOrderRef
        };
    }

    internal static string GetRequiredCredential(PaymentMerchantProfile profile, string key)
    {
        if (TryGetCredential(profile, key, out var value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"Sinopac profile '{profile.Name}' is missing credential '{key}'.");
    }

    internal static string GetXKeyId(PaymentMerchantProfile profile)
    {
        if (TryGetCredential(profile, "XKeyId", out var value) ||
            TryGetCredential(profile, "XKeyID", out value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"Sinopac profile '{profile.Name}' is missing credential 'XKeyId'.");
    }

    private static bool TryGetCredential(
        PaymentMerchantProfile profile,
        string key,
        out string value)
    {
        if (profile.Credentials.TryGetValue(key, out value!) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static int ToMinorUnit(decimal amount)
    {
        return decimal.ToInt32(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeCurrency(string currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "TWD" : currency;
    }

    private static string ResolvePayType(PaymentCreateRequest request)
    {
        var configuredPayType = GetMetadata(request, "PayType");
        if (!string.IsNullOrWhiteSpace(configuredPayType))
        {
            return configuredPayType;
        }

        if (string.Equals(request.PaymentMethod, "ATM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.PaymentMethod, "A", StringComparison.OrdinalIgnoreCase))
        {
            return "A";
        }

        return "C";
    }

    private static int? ParseNullableInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string GetMetadata(PaymentCreateRequest request, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (request.Metadata.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
