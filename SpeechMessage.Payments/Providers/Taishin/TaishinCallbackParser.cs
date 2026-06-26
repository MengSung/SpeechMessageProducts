using System.Globalization;
using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

internal static class TaishinCallbackParser
{
    private static readonly PaymentCallbackAcknowledgement Acknowledgement =
        PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}");

    public static PaymentCallbackResult Parse(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request)
    {
        var fields = NormalizeFields(ReadFields(request));
        var status = TaishinStatusMapper.Map(GetValue(fields, "ret_code"), GetValue(fields, "state"));
        var error = TaishinHashVerifier.Validate(profile, fields);

        return new PaymentCallbackResult
        {
            Status = status,
            ProductOrderId = GetValue(fields, "order_id"),
            ProviderTransactionId = GetValue(fields, "transaction_id"),
            Amount = ParseMinorAmount(GetValue(fields, "amount")),
            Currency = NormalizeCurrency(GetValue(fields, "currency")),
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
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyObjectProperties(json, fields);

        if (json.TryGetValue("params", StringComparison.OrdinalIgnoreCase, out var parameters) &&
            parameters is JObject parameterObject)
        {
            CopyObjectProperties(parameterObject, fields);
        }

        return fields;
    }

    private static void CopyObjectProperties(JObject json, IDictionary<string, string> fields)
    {
        foreach (var property in json.Properties())
        {
            if (property.Value is JObject)
            {
                continue;
            }

            fields[property.Name] = property.Value.Type == JTokenType.Null
                ? string.Empty
                : property.Value.ToString();
        }
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

    private static IReadOnlyDictionary<string, string> NormalizeFields(IReadOnlyDictionary<string, string> rawFields)
    {
        var fields = new Dictionary<string, string>(rawFields, StringComparer.OrdinalIgnoreCase);
        AddNormalized(fields, "order_id", "order_id", "order_no", "ORDERNO", "orderId", "orderNo", "uid", "order");
        AddNormalized(fields, "transaction_id", "transaction_id", "transactionId", "tx_id", "txn_id");
        AddNormalized(fields, "ret_code", "ret_code", "retCode", "return_code", "code");
        AddNormalized(fields, "ret_msg", "ret_msg", "retMsg", "return_message", "message", "msg", "retmsg");
        AddNormalized(fields, "state", "state", "result", "status");
        AddNormalized(fields, "amount", "actual_cost", "actualCost", "cost", "amt", "amount", "paid_amount");
        AddNormalized(fields, "currency", "currency", "cur");
        AddNormalized(fields, "hash", "hash", "ret_hash", "signature", "sign", "hash_value");
        return fields;
    }

    private static void AddNormalized(
        IDictionary<string, string> fields,
        string targetKey,
        params string[] sourceKeys)
    {
        foreach (var sourceKey in sourceKeys)
        {
            if (fields.TryGetValue(sourceKey, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                fields[targetKey] = value;
                return;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BuildProviderData(IReadOnlyDictionary<string, string> fields)
    {
        var providerData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyIfPresent(fields, providerData, "order_id");
        CopyIfPresent(fields, providerData, "transaction_id");
        CopyIfPresent(fields, providerData, "ret_code");
        CopyIfPresent(fields, providerData, "ret_msg", "provider_message");
        CopyIfPresent(fields, providerData, "state");
        CopyIfPresent(fields, providerData, "amount");
        CopyIfPresent(fields, providerData, "currency");
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

    private static decimal? ParseMinorAmount(string amount)
    {
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed / 100m
            : null;
    }

    private static string NormalizeCurrency(string currency)
    {
        return string.Equals(currency, "NTD", StringComparison.OrdinalIgnoreCase)
            ? "TWD"
            : FirstNonEmpty(currency, "TWD");
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
