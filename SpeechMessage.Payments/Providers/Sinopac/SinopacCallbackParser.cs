using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 解析永豐 QPay frontend/backend callback。
/// Callback 只提供 ShopNo/PayToken 等查詢線索，真正付款狀態仍由 QueryPaymentAsync 查銀行。
/// </summary>
internal static class SinopacCallbackParser
{
    public static PaymentCallbackResult Parse(PaymentCallbackRequest request)
    {
        return Parse(request, expectedShopNo: null);
    }

    public static PaymentCallbackResult Parse(
        PaymentCallbackRequest request,
        PaymentMerchantProfile profile)
    {
        var expectedShopNo = profile.Credentials.TryGetValue("ShopNo", out var shopNo)
            ? shopNo
            : null;
        return Parse(request, expectedShopNo);
    }

    private static PaymentCallbackResult Parse(
        PaymentCallbackRequest request,
        string? expectedShopNo)
    {
        var fields = NormalizeFields(ReadFields(request));
        var shopNo = GetValue(fields, "shop_no");
        var payToken = GetValue(fields, "pay_token");
        var error = Validate(shopNo, payToken, expectedShopNo);

        return new PaymentCallbackResult
        {
            // QPay callback 代表「可查詢狀態」，不直接等同付款成功。
            Status = error.HasError ? PaymentStatus.Unknown : PaymentStatus.Pending,
            ProviderTransactionId = payToken,
            Acknowledgement = PaymentCallbackAcknowledgement.None,
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

    private static IReadOnlyDictionary<string, string> NormalizeFields(IReadOnlyDictionary<string, string> rawFields)
    {
        var fields = new Dictionary<string, string>(rawFields, StringComparer.OrdinalIgnoreCase);
        // 不同舊 endpoint/銀行文件可能使用大小寫或別名，先統一成核心欄位名稱。
        AddNormalized(fields, "shop_no", "shop_no", "ShopNo", "shopNo", "merchant_id");
        AddNormalized(fields, "pay_token", "pay_token", "PayToken", "payToken", "token");
        AddNormalized(fields, "hash", "hash", "HashCode", "signature", "sign");
        return fields;
    }

    private static PaymentError Validate(
        string shopNo,
        string payToken,
        string? expectedShopNo)
    {
        if (string.IsNullOrWhiteSpace(shopNo) || string.IsNullOrWhiteSpace(payToken))
        {
            return new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "Sinopac callback is missing ShopNo or PayToken."
            };
        }

        if (!string.IsNullOrWhiteSpace(expectedShopNo) &&
            !string.Equals(shopNo, expectedShopNo, StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "Sinopac callback ShopNo does not match the selected payment profile."
            };
        }

        return PaymentError.None;
    }

    private static IReadOnlyDictionary<string, string> BuildProviderData(IReadOnlyDictionary<string, string> fields)
    {
        var providerData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyIfPresent(fields, providerData, "shop_no");
        CopyIfPresent(fields, providerData, "pay_token");
        return providerData;
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

    private static void CopyIfPresent(
        IReadOnlyDictionary<string, string> source,
        IDictionary<string, string> target,
        string sourceKey)
    {
        if (source.TryGetValue(sourceKey, out var value))
        {
            target[sourceKey] = value;
        }
    }

    private static string GetValue(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
