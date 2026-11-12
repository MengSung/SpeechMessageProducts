/*
 * 檔案整體說明
 *
 * 目的與定位：解析高鉅 MyPay callback。 MyPay 回呼可能是 form、query、JSON 或 raw form-urlencoded； parser 只負責轉為通用
 * PaymentCallbackResult，不更新宿主產品資料。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPayCallbackParser。
 * 主要操作入口包含 Parse、ReadFields、ReadJsonFields、ReadFormEncodedFields、BuildProviderData、CopyIfPresent、
 * ParsePaidAmount、GetValue；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 回呼欄位是外部輸入；解析結果僅保存受 Money 白名單驗證的付款金額，無效金額不產生 PaidAmount。
 * 本 parser 不保留 request、付款人、profile 或可變共享狀態，也不執行 I/O；修改正規化規則時須同步核對 callback 測試與 sanitizer。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
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

    /// <summary>解析外部 MyPay callback 欄位並驗證簽章；只發布 Money 能接受的金額與已清理診斷。</summary>
    /// <param name="request">包含 callback 原始投影欄位的中立請求，不會被 parser 保存。</param>
    /// <returns>狀態、Money 金額、acknowledgement 與 sanitizer 清理後的結果。</returns>
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
            PaidAmount = ParsePaidAmount(
                FirstNonEmpty(GetValue(fields, "actual_currency"), GetValue(fields, "currency"), "TWD"),
                GetValue(fields, "actual_cost"),
                GetValue(fields, "cost")),
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

    /// <summary>
    /// 從 provider 回呼的實收金額欄位建立 Money；使用不受主機文化影響的十進位解析，
    /// 遇到不合法數字或 Money 不接受的幣別時不發布金額。
    /// </summary>
    private static Money? ParsePaidAmount(string currency, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                try
                {
                    return new Money(amount, currency);
                }
                catch (ArgumentException)
                {
                    return null;
                }
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
