// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class TaishinRequestMapper
// 主要成員：MapCreatePayload、MapQueryPayload、CreateBaseRequest、GetCredential、ToMinorUnit、ToTaishinCurrency、ResolveLayout、FirstNonEmpty
// 引用命名空間：System.Globalization、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Globalization;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 將通用付款請求轉成台新 TSPG REST payload。
/// 台新使用 NTD/小單位金額與 tx_type 區分建單、查詢，這些 provider 細節集中在此檔。
/// </summary>
internal static class TaishinRequestMapper
{
    public static TaishinPaymentRequest MapCreatePayload(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        var payload = CreateBaseRequest(profile);
        // tx_type=1 為建立付款頁授權交易。
        payload.TxType = 1;
        payload.Params = new TaishinPaymentParams
        {
            Layout = ResolveLayout(request.PaymentMethod),
            OrderNo = request.ProductOrderId,
            Amt = ToMinorUnit(request.Amount),
            Cur = ToTaishinCurrency(request.Currency),
            OrderDesc = request.Description,
            CaptFlag = request.Metadata.TryGetValue("CaptFlag", out var captFlag) ? captFlag : "0",
            ResultFlag = "1",
            PostBackUrl = FirstNonEmpty(request.Callbacks.ReturnUrl, request.Callbacks.SuccessUrl),
            ResultUrl = request.Callbacks.BackendUrl,
            CardholderName = request.Customer.Name,
            CardholderEmail = request.Customer.Email,
            CardholderMobilePhone = string.IsNullOrWhiteSpace(request.Customer.Phone)
                ? null
                : new TaishinCardholderMobilePhone
                {
                    PhoneNumber = request.Customer.Phone
                }
        };

        return payload;
    }

    public static TaishinPaymentRequest MapQueryPayload(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request)
    {
        var payload = CreateBaseRequest(profile);
        // tx_type=7 為交易查詢，OrderNo 可用產品訂單號或 provider reference。
        payload.TxType = 7;
        payload.Params = new TaishinPaymentParams
        {
            OrderNo = FirstNonEmpty(request.ProductOrderId, request.ProviderOrderRef)
        };

        return payload;
    }

    private static TaishinPaymentRequest CreateBaseRequest(PaymentMerchantProfile profile)
    {
        return new TaishinPaymentRequest
        {
            Mid = GetCredential(profile, "StoreId"),
            Tid = GetCredential(profile, "TerminalId")
        };
    }

    private static string GetCredential(PaymentMerchantProfile profile, string key)
    {
        if (profile.Credentials.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"Taishin profile '{profile.Name}' is missing credential '{key}'.");
    }

    private static string ToMinorUnit(decimal amount)
    {
        return decimal
            .Round(amount * 100m, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
    }

    private static string ToTaishinCurrency(string currency)
    {
        return string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase)
            ? "NTD"
            : currency;
    }

    private static string ResolveLayout(string paymentMethod)
    {
        // 台新 layout=2 表示行動版頁面；未指定時保守使用一般網頁版 layout=1。
        return string.Equals(paymentMethod, "Mobile", StringComparison.OrdinalIgnoreCase)
            ? "2"
            : "1";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
