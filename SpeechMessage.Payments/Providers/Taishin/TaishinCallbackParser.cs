/*
 * 檔案整體說明
 *
 * 目的與定位：解析台新 TSPG callback。 台新回呼可能由前端返回或後端通知送入，欄位大小寫與容器層級會不同， 因此 parser 會先正規化欄位，再做狀態與 hash 驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinCallbackParser。
 * 主要操作入口包含 Parse、ReadFields、ReadJsonFields、CopyObjectProperties、ReadFormEncodedFields、NormalizeFields、
 * AddNormalized、BuildProviderData、ParseMinorAmount；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 回呼欄位是外部輸入；parser 先正規化與驗證，再以 Money 保存可解析且幣別有效的金額。
 * 本 parser 不保留 request、付款人、profile 或可變共享狀態，也不執行 I/O；失效的金額／幣別不發布 PaidAmount。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 解析台新 TSPG callback。
/// 台新回呼可能由前端返回或後端通知送入，欄位大小寫與容器層級會不同，
/// 因此 parser 會先正規化欄位，再做狀態與 hash 驗證。
/// </summary>
internal static class TaishinCallbackParser
{
    private static readonly PaymentCallbackAcknowledgement Acknowledgement =
        PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}");

    /// <summary>正規化台新 callback 欄位、驗證 hash，並將有效的最小單位金額映射成 Money。</summary>
    /// <param name="profile">resolver 選定的商店 profile，僅用於本次 hash 驗證。</param>
    /// <param name="request">宿主投影的 HTTP-neutral callback 欄位，不會被 parser 保存。</param>
    /// <returns>已清理 callback 結果與 provider acknowledgement 描述。</returns>
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
            PaidAmount = ParseMinorAmount(
                GetValue(fields, "amount"),
                NormalizeCurrency(GetValue(fields, "currency"))),
            // TSPG 後端通知預期 JSON acknowledgement；宿主產品只負責把這個 descriptor 轉成實際 web response。
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

        // 台新部分回應會把交易資料包在 params 物件內；需攤平成同一層供後續驗證與映射使用。
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
        // 支援舊 controller、台新文件與不同大小寫欄位的別名，避免產品層自行判斷 provider 欄位。
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

    /// <summary>
    /// 將台新回呼的最小單位金額轉成 Money；保留十進位運算並拒絕白名單外幣別，
    /// 不讓不可信的回呼值透過舊 decimal／Currency 欄位繞過核心契約。
    /// </summary>
    private static Money? ParseMinorAmount(string amount, string currency)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        try
        {
            return new Money(parsed / 100m, currency);
        }
        catch (ArgumentException)
        {
            return null;
        }
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
