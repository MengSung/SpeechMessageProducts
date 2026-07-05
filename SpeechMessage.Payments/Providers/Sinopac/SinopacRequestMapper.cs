// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacRequestMapper
// 主要成員：MapCreateRequest、MapOrderPayQuery、GetRequiredCredential、GetXKeyId、TryGetCredential、ToMinorUnit、NormalizeCurrency、ResolvePayType、ParseNullableInt、GetMetadata
// 引用命名空間：System.Globalization、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Globalization;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 將通用付款請求轉成永豐 QPay 建單/查詢 payload。
/// QPay 欄位名稱仍保留在 provider core 內，宿主產品不再直接建立 CreOrderReq。
/// </summary>
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
            // ATM/匯款走 ATMParam；到期日未提供時沿用舊版預設十天。
            payload.ATMParam = new SinopacOrderCreateAtmRequest
            {
                ExpireDate = FirstNonEmpty(
                    GetMetadata(request, "ExpireDate"),
                    DateTime.Now.AddDays(10).ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            };
        }
        else
        {
            // 信用卡/行動支付/LinePay 仍使用 CardParam。定期定額預設由宿主產品 adapter 補齊。
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
        // 宿主產品 adapter 可能明確帶 PayType；若沒有，neutral PaymentMethod 再轉成 QPay PayType。
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
