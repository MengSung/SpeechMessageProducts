/*
 * 檔案整體說明
 *
 * 目的與定位：解析高鉅 MyPay callback。 MyPay 回呼可能是 form、query、JSON 或 raw form-urlencoded； parser 只負責轉為通用
 * PaymentCallbackResult，不更新宿主產品資料。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPayCallbackParser。
 * 主要操作入口包含 Parse、ReadFields、ReadJsonFields、ReadFormEncodedFields、BuildProviderData、CopyIfPresent、ParseAmount、
 * GetValue；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// 解析高鉅 MyPay callback。
/// MyPay 回呼可能是 form、query、JSON 或 raw form-urlencoded；
/// parser 只負責轉為通用 PaymentCallbackResult，不更新宿主產品資料。
/// </summary>
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
            // MyPay 要求收到 callback 後回覆 8888；這是 provider protocol，不應散落在產品 web 入口。
            Acknowledgement = Acknowledgement,
            Error = error,
            ProviderData = PaymentDiagnosticsSanitizer.Sanitize(BuildProviderData(fields)),
            Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(fields)
        };
    }

    private static IReadOnlyDictionary<string, string> ReadFields(PaymentCallbackRequest request)
    {
        // 宿主產品先把 web request 轉成 neutral request；這裡依資料來源優先序還原欄位。
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
        // ProviderData 只放產品流程需要知道的 provider 欄位；完整欄位留在 sanitized Diagnostics。
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
