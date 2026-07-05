// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacCallbackParser
// 主要成員：Parse、Validate、AddNormalized、CopyIfPresent、GetValue
// 引用命名空間：Newtonsoft.Json.Linq、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Diagnostics、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
