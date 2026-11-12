/*
 * 檔案整體說明
 *
 * 目的與定位：將 Money-first 付款請求轉成台新 TSPG REST payload。台新使用 NTD／最小單位及 tx_type 區分建單、查詢，
 * 這些 provider 細節集中在此檔。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinRequestMapper。
 * 主要操作入口包含 MapCreatePayload、MapQueryPayload、CreateBaseRequest、GetCredential、ToMinorUnit、ToTaishinCurrency、
 * ResolveLayout、FirstNonEmpty。
 *
 * 使用邊界與維護：
 * 金額與 ISO 幣別只從 Money 讀取，再依台新協定轉換單位與 NTD 代碼；profile 憑證只在 mapping 呼叫期間讀取。
 * mapper 不執行 I/O、不保留 request 或 profile 狀態；協定改動須同步核對建單／查詢測試。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
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
    /// <summary>將 Money-first 付款請求映射為台新建單 payload；不讀取 obsolete Amount／Currency 成員。</summary>
    /// <param name="profile">resolver 選定且含台新必要設定的商店 profile。</param>
    /// <param name="request">經 Gateway 幣別驗證的中立付款請求。</param>
    /// <returns>台新建單 REST payload。</returns>
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
            Amt = ToMinorUnit(request.Total.Amount),
            Cur = ToTaishinCurrency(request.Total.Currency),
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

    /// <summary>將中立付款查詢請求映射成台新交易查詢 payload。</summary>
    /// <param name="profile">resolver 選定的台新 profile。</param>
    /// <param name="request">含訂單識別與交易參照的中立查詢請求。</param>
    /// <returns>台新查詢 REST payload。</returns>
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
