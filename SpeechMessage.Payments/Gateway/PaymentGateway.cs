/*
 * 檔案整體說明
 *
 * 目的與定位：provider-neutral gateway router。負責 profile/provider 選擇、建立付款的 Money 幣別能力驗證及錯誤正規化，
 * 不處理宿主產品 CRM、LINE 通知、畫面導向或 provider 封包細節。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentGateway、ProviderSelection。
 * 主要操作入口包含 CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveProvider、ProviderSelection、Success、
 * Failed。
 *
 * 使用邊界與維護：
 * Gateway 只持有 resolver 與 provider routing 表，不保存 request、profile、憑證或使用者狀態；provider 實例生命週期由 DI Host 擁有。
 * 建立付款先解析 profile，再驗證有效 Money.Currency 與 SupportedCurrencies；無效或不支援即回 RequestInvalid，不能呼叫 provider 或傳送 HTTP。
 * 查詢及 callback 將取消權杖交給 provider；修改 routing／Money 契約時須核對 Gateway 與 provider 測試。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Gateway;

/// <summary>
/// provider-neutral gateway router。
/// 這裡只負責 profile/provider 選擇與錯誤正規化，不處理任何宿主產品 CRM、
/// LINE 通知、畫面導向或 provider 封包細節。
/// </summary>
internal sealed class PaymentGateway : IPaymentGateway
{
    private readonly IPaymentProfileResolver _profileResolver;
    private readonly IReadOnlyDictionary<PaymentProviderKind, IPaymentProvider> _providers;

    /// <summary>
    /// 建立只負責路由的 Gateway。Provider 實例由 DI 建立並由 DI 容器管理；Gateway 不自行 Dispose 或延長外部資源所有權。
    /// </summary>
    /// <param name="profileResolver">依產品指定名稱解析商店 profile 的 resolver。</param>
    /// <param name="providers">按固定 provider 種類索引的實作集合；本物件只保存路由參照。</param>
    public PaymentGateway(IPaymentProfileResolver profileResolver, IEnumerable<IPaymentProvider> providers)
    {
        _profileResolver = profileResolver;
        _providers = providers
            .GroupBy(provider => provider.ProviderKind)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>
    /// 建立付款前驗證 profile、有效幣別及 provider 支援清單；不支援的請求在傳輸前回傳 Failed／RequestInvalid。
    /// </summary>
    /// <param name="request">含 Money 總額與產品訂單資訊的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，於通過輸入檢查後傳給 provider。</param>
    /// <returns>provider 建立結果，或不進行 provider I/O 的本地輸入錯誤。</returns>
    public Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveProvider(request.ProfileName, request.ProviderHint);
        if (selected.Error is not null)
        {
            return Task.FromResult(new PaymentCreateResult
            {
                ProductOrderId = request.ProductOrderId,
                Error = selected.Error
            });
        }

        string currency;
        try
        {
            currency = request.Total.Currency;
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(CreateInvalidCurrencyResult(
                request.ProductOrderId,
                "Payment total must have a valid currency."));
        }

        var provider = selected.Provider!;
        if (!provider.SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
        {
            return Task.FromResult(CreateInvalidCurrencyResult(
                request.ProductOrderId,
                $"Currency '{currency}' is not supported by provider '{selected.Profile!.Provider}'."));
        }

        return provider.CreatePaymentAsync(selected.Profile!, request, cancellationToken);
    }

    /// <summary>依指定 profile 查詢付款狀態，並把呼叫端取消權杖傳遞至選定 provider。</summary>
    /// <param name="request">包含產品訂單與 provider 交易參照的中立查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖。</param>
    /// <returns>provider 正規化的付款狀態結果。</returns>
    public Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveProvider(request.ProfileName, request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentStatusResult
            {
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = request.ProviderOrderRef,
                Error = selected.Error
            })
            : selected.Provider!.QueryPaymentAsync(selected.Profile!, request, cancellationToken);
    }

    /// <summary>將中立 callback request 路由到 profile 對應的 provider parser，不執行產品端回寫。</summary>
    /// <param name="request">宿主已轉成 provider-neutral 型別的 callback 資料。</param>
    /// <param name="cancellationToken">呼叫端取消權杖。</param>
    /// <returns>provider 正規化的 callback 結果與 acknowledgement。</returns>
    public Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveProvider(request.ProfileName, request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentCallbackResult
            {
                Error = selected.Error
            })
            : selected.Provider!.ParseCallbackAsync(selected.Profile!, request, cancellationToken);
    }

    private ProviderSelection ResolveProvider(string? profileName, PaymentProviderKind? providerHint)
    {
        PaymentMerchantProfile profile;
        try
        {
            profile = _profileResolver.Resolve(profileName);
        }
        catch (PaymentConfigurationException ex)
        {
            return ProviderSelection.Failed(PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }

        // ProviderHint 是 callback route 或產品流程對 provider 的防呆宣告。
        // 若指定 MyPay 卻解析到 Sinopac profile，直接 fail closed，避免用錯金鑰或錯誤 API。
        if (providerHint is not null &&
            providerHint.Value != PaymentProviderKind.Unknown &&
            providerHint.Value != profile.Provider)
        {
            return ProviderSelection.Failed(
                PaymentErrorKind.ConfigurationInvalid,
                $"Payment provider hint '{providerHint}' does not match profile '{profile.Name}' provider '{profile.Provider}'.");
        }

        if (!_providers.TryGetValue(profile.Provider, out var provider))
        {
            return ProviderSelection.Failed(
                PaymentErrorKind.UnsupportedOperation,
                $"Payment provider '{profile.Provider}' is not registered.");
        }

        return ProviderSelection.Success(profile, provider);
    }

    /// <summary>
    /// 建立請求不合法的失敗結果。此分支不呼叫 provider，確保不支援幣別不會進入任何 HTTP 傳輸。
    /// </summary>
    private static PaymentCreateResult CreateInvalidCurrencyResult(string productOrderId, string message)
    {
        return new PaymentCreateResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = productOrderId,
            Error = new PaymentError
            {
                Kind = PaymentErrorKind.RequestInvalid,
                Message = message
            }
        };
    }

    private sealed record ProviderSelection(
        PaymentMerchantProfile? Profile,
        IPaymentProvider? Provider,
        PaymentError? Error)
    {
        public static ProviderSelection Success(PaymentMerchantProfile profile, IPaymentProvider provider)
        {
            return new ProviderSelection(profile, provider, null);
        }

        public static ProviderSelection Failed(PaymentErrorKind kind, string message)
        {
            return new ProviderSelection(
                null,
                null,
                new PaymentError
                {
                    Kind = kind,
                    Message = message
                });
        }
    }
}
