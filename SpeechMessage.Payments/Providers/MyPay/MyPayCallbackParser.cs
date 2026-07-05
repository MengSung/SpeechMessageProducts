// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class MyPayCallbackParser
// 主要成員：Parse、CopyIfPresent、ParseAmount、GetValue、FirstNonEmpty
// 引用命名空間：Newtonsoft.Json.Linq、SpeechMessage.Payments.Diagnostics、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
