using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

internal static class MyPayCallbackParser
{
    private static readonly PaymentCallbackAcknowledgement Acknowledgement =
        PaymentCallbackAcknowledgement.PlainText("8888");

    public static PaymentCallbackResult Parse(PaymentCallbackRequest request)
    {
        var fields = ReadFields(request);
        var error = MyPaySignatureVerifier.Validate(fields);
        var status = fields.TryGetValue("prc", out var prc)
            ? MyPayStatusMapper.Map(prc)
            : PaymentStatus.Unknown;

        return new PaymentCallbackResult
        {
            Status = status,
            ProductOrderId = GetValue(fields, "order_id"),
            ProviderTransactionId = GetValue(fields, "uid"),
            Amount = ParseAmount(GetValue(fields, "actual_cost"), GetValue(fields, "cost")),
            Currency = FirstNonEmpty(GetValue(fields, "actual_currency"), GetValue(fields, "currency"), "TWD"),
            Acknowledgement = Acknowledgement,
            Error = error,
            ProviderData = PaymentDiagnosticsSanitizer.Sanitize(BuildProviderData(fields)),
            Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(fields)
        };
    }

    private static IReadOnlyDictionary<string, string> ReadFields(PaymentCallbackRequest request)
    {
        if (request.Form.Count > 0)
        {
            return request.Form;
        }

        if (request.Query.Count > 0)
        {
            return request.Query;
        }

        if (string.IsNullOrWhiteSpace(request.RawBody))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            ? ReadJsonFields(request.RawBody)
            : ReadFormEncodedFields(request.RawBody);
    }

    private static IReadOnlyDictionary<string, string> ReadJsonFields(string rawBody)
    {
        var json = JObject.Parse(rawBody);
        return json.Properties().ToDictionary(
            property => property.Name,
            property => property.Value.Type == JTokenType.Null ? string.Empty : property.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ReadFormEncodedFields(string rawBody)
    {
        return rawBody
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace("+", " ")),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildProviderData(IReadOnlyDictionary<string, string> fields)
    {
        var providerData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyIfPresent(fields, providerData, "uid");
        CopyIfPresent(fields, providerData, "prc");
        CopyIfPresent(fields, providerData, "order_id");
        CopyIfPresent(fields, providerData, "retmsg", "provider_message");
        CopyIfPresent(fields, providerData, "pfn");
        CopyIfPresent(fields, providerData, "finishtime");
        return providerData;
    }

    private static void CopyIfPresent(
        IReadOnlyDictionary<string, string> source,
        IDictionary<string, string> target,
        string sourceKey,
        string? targetKey = null)
    {
        if (source.TryGetValue(sourceKey, out var value))
        {
            target[targetKey ?? sourceKey] = value;
        }
    }

    private static decimal? ParseAmount(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (decimal.TryParse(candidate, out var amount))
            {
                return amount;
            }
        }

        return null;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
