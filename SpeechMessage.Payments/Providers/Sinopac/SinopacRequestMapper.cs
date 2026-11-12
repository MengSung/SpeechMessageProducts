/*
 * 檔案整體說明
 *
 * 目的與定位：將 Money-first 通用請求轉成永豐 QPay 建單／查詢 payload。QPay 欄位與最小單位換算僅留在 provider core，
 * 宿主產品不直接建立 CreOrderReq。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacRequestMapper。
 * 主要操作入口包含 MapCreateRequest、MapOrderPayQuery、GetRequiredCredential、GetXKeyId、TryGetCredential、ToMinorUnit、
 * NormalizeCurrency、ResolvePayType；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 建單金額由 PaymentCreateRequest.Total.Amount 轉為整數分，幣別取 Money.Currency；profile 憑證只在單次 mapping 取用，不由 mapper 保存。
 * 本檔不執行網路或跨請求共享狀態；DateTime.Now 到期日既有行為不在 P4-02 修改範圍。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
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
    /// <summary>將已由 Gateway 驗證為 provider 支援幣別的 Money 請求映射成永豐建單 payload。</summary>
    /// <param name="profile">resolver 選定且含必要商店欄位的 profile；只在此映射期間讀取。</param>
    /// <param name="request">付款請求；金額來源固定為 Total，不讀取 obsolete Amount／Currency。</param>
    /// <returns>永豐協定建單資料，金額以最小單位整數表示。</returns>
    public static SinopacOrderCreateRequest MapCreateRequest(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        var payType = ResolvePayType(request);
        var payload = new SinopacOrderCreateRequest
        {
            ShopNo = GetRequiredCredential(profile, "ShopNo"),
            OrderNo = request.ProductOrderId,
            Amount = ToMinorUnit(request.Total.Amount),
            CurrencyID = NormalizeCurrency(request.Total.Currency),
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

    /// <summary>將中立付款查詢參照映射成永豐 PayToken 查詢 payload。</summary>
    /// <param name="profile">resolver 選定的永豐商店 profile。</param>
    /// <param name="request">含產品訂單與 provider 交易參照的查詢請求。</param>
    /// <returns>不含宿主型別的永豐查詢 payload。</returns>
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

    /// <summary>取得非空的指定永豐憑證；訊息只回報 profile 名稱及缺少的鍵名，不回報憑證值。</summary>
    /// <param name="profile">本次操作選定的永豐 profile。</param>
    /// <param name="key">必要憑證鍵名。</param>
    /// <returns>該憑證值，供呼叫端即時使用。</returns>
    internal static string GetRequiredCredential(PaymentMerchantProfile profile, string key)
    {
        if (TryGetCredential(profile, key, out var value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"Sinopac profile '{profile.Name}' is missing credential '{key}'.");
    }

    /// <summary>取得永豐 X-KeyID 標頭值，接受舊設定鍵大小寫別名但不輸出憑證。</summary>
    /// <param name="profile">本次操作選定的永豐 profile。</param>
    /// <returns>本次 HTTP 請求所需的 X-KeyID。</returns>
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
