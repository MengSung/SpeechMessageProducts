/*
 * 檔案整體說明
 *
 * 目的與定位：provider-neutral gateway router。負責伺服器端 profile/provider 選擇、Money 能力驗證、綁卡／維護／定期 capability 路由、
 * 撥款查詢及錯誤正規化，
 * 不處理宿主產品 CRM、LINE 通知、畫面導向或 provider 封包細節。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentGateway、ProviderSelection、CapabilitySelection。
 * 主要操作入口包含一般付款、綁卡、訂單維護、定期扣款、付款查詢、回呼解析與撥款查詢。
 *
 * 使用邊界與維護：
 * Gateway 只持有 resolver 與 provider routing 表，不保存 request、profile、憑證或使用者狀態；各 capability 都來自本次解析的 provider instance，
 * 不接受呼叫端傳入 profile。未實作 capability 時回 UnsupportedOperation 且不觸發 provider I/O。Provider 實例生命週期由 DI Host 擁有。
 * 建立一般與定期付款先解析 profile，再共用驗證總額及每筆明細的有效 Money.Currency 與 SupportedCurrencies；無效或不支援即回 RequestInvalid，
 * 不能呼叫 provider 或傳送 HTTP。沒有明細時仍由既有 provider mapper 維持其相容 fallback 行為。
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
internal sealed class PaymentGateway : IPaymentGateway, IPaymentAllotGateway
{
    private readonly IPaymentProfileResolver _profileResolver;
    private readonly IReadOnlyDictionary<PaymentProviderKind, IPaymentProvider> _providers;
    private readonly IReadOnlyDictionary<PaymentProviderKind, IPaymentAllotProvider> _allotProviders;

    /// <summary>
    /// 建立只負責路由的 Gateway。Provider 實例由 DI 建立並由 DI 容器管理；Gateway 不自行 Dispose 或延長外部資源所有權。
    /// </summary>
    /// <param name="profileResolver">依產品指定名稱解析商店 profile 的 resolver。</param>
    /// <param name="providers">按固定 provider 種類索引的實作集合；撥款能力由相同 provider instance 提供。</param>
    public PaymentGateway(IPaymentProfileResolver profileResolver, IEnumerable<IPaymentProvider> providers)
    {
        _profileResolver = profileResolver;
        var providerArray = providers.ToArray();
        _providers = providerArray
            .GroupBy(provider => provider.ProviderKind)
            .ToDictionary(group => group.Key, group => group.First());
        _allotProviders = _providers.Values
            .OfType<IPaymentAllotProvider>()
            .ToDictionary(provider => provider.ProviderKind);
    }

    /// <summary>
    /// 建立付款前驗證 profile、總額及每筆明細的有效幣別與 provider 支援清單；
    /// 不合法或不支援的請求在傳輸前回傳 Failed／RequestInvalid。沒有明細時不改變既有 provider fallback 行為。
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

        var currencyError = ValidatePaymentCurrencies(selected.Provider!, request);
        if (currencyError is not null)
        {
            return Task.FromResult(CreateInvalidCurrencyResult(request.ProductOrderId, currencyError));
        }

        return selected.Provider!.CreatePaymentAsync(selected.Profile!, request, cancellationToken);
    }

    /// <summary>依 request profile 解析單一 provider，再查詢 profile-bound 綁卡結果。</summary>
    /// <param name="request">中立綁卡查詢資料，不包含解析後 profile 或銀行憑證。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 capability 的取消權杖。</param>
    /// <returns>不支援時回失敗結果且不發出 provider I/O。</returns>
    public Task<CardBindQueryResult> QueryCardBindAsync(
        CardBindQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentCardBinding>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new CardBindQueryResult
            {
                Status = PaymentStatus.Failed,
                Error = selected.Error
            })
            : selected.Capability!.QueryCardBindAsync(
                selected.Profile!,
                request,
                cancellationToken);
    }

    /// <summary>建立 provider 綁卡頁；profile 只由本次伺服器端解析結果決定。</summary>
    /// <param name="request">中立綁卡建立資料。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 capability 的取消權杖。</param>
    /// <returns>不支援時回 UnsupportedOperation 且不發出 provider I/O。</returns>
    public Task<CardBindResult> CreateCardBindAsync(
        CardBindRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentCardBinding>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new CardBindResult { Error = selected.Error })
            : selected.Capability!.CreateCardBindAsync(
                selected.Profile!,
                request,
                cancellationToken);
    }

    /// <summary>依解析後的 provider 訂單維護 capability 取消授權。</summary>
    public Task<PaymentStatusResult> CancelAuthorizationAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentOrderMaintenance>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = request.OrderNo,
                Error = selected.Error
            })
            : selected.Capability!.CancelAuthorizationAsync(
                selected.Profile!,
                request,
                cancellationToken);
    }

    /// <summary>依解析後的 provider 訂單維護 capability 立即請款。</summary>
    public Task<PaymentStatusResult> CaptureNowAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentOrderMaintenance>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = request.OrderNo,
                Error = selected.Error
            })
            : selected.Capability!.CaptureNowAsync(
                selected.Profile!,
                request,
                cancellationToken);
    }

    /// <summary>依解析後的 provider 建立定期付款方案。</summary>
    public Task<RecurringResult> CreateRecurringAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentRecurring>(
            request.ProfileName,
            request.ProviderHint);
        if (selected.Error is not null)
        {
            return Task.FromResult(new RecurringResult
            {
                Status = PaymentStatus.Failed,
                MasterOrderNo = request.ProductOrderId,
                Error = selected.Error
            });
        }

        var currencyError = ValidatePaymentCurrencies(selected.Provider!, request);
        return currencyError is not null
            ? Task.FromResult(new RecurringResult
            {
                Status = PaymentStatus.Failed,
                MasterOrderNo = request.ProductOrderId,
                Error = new PaymentError
                {
                    Kind = PaymentErrorKind.RequestInvalid,
                    Message = currencyError
                }
            })
            : selected.Capability!.CreateRecurringAsync(selected.Profile!, request, cancellationToken);
    }

    /// <summary>依解析後的 provider 查詢定期主單與子扣款集合。</summary>
    /// <param name="request">定期主單查詢識別。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 capability 的取消權杖。</param>
    /// <returns>完整唯讀查詢結果，或不含部分資料的失敗結果。</returns>
    public Task<RecurringResult> QueryRecurringAsync(
        RecurringQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentRecurring>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new RecurringResult
            {
                Status = PaymentStatus.Failed,
                MasterOrderNo = request.MasterOrderNo,
                Error = selected.Error
            })
            : selected.Capability!.QueryRecurringAsync(
                selected.Profile!,
                request,
                cancellationToken);
    }

    /// <summary>依解析後的 provider 停止定期扣款方案。</summary>
    /// <param name="request">含 profile 名稱與定期主單識別的中立維護資料。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 capability 的取消權杖。</param>
    /// <returns>銀行明確接受停止命令後才回報 Cancelled。</returns>
    public Task<PaymentStatusResult> StopRecurringAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveCapability<IPaymentRecurring>(
            request.ProfileName,
            request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = request.OrderNo,
                Error = selected.Error
            })
            : selected.Capability!.StopRecurringAsync(
                selected.Profile!,
                request,
                cancellationToken);
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

    /// <summary>
    /// 依 request.ProfileName 解析 server-side profile，並委派給同一 provider instance 提供的撥款查詢能力。
    /// 失敗只回標準分類，不將 profile resolver 例外本文或呼叫端資料寫入錯誤訊息。
    /// </summary>
    /// <param name="request">中立撥款查詢日期與付款類型；profile 名稱只用於設定查找。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，原樣傳遞至 provider。</param>
    /// <returns>provider-neutral 撥款結果；不支援時回傳空批次與穩定錯誤分類。</returns>
    public Task<PaymentAllotQueryResult> QueryAllotAsync(
        PaymentAllotQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        PaymentMerchantProfile profile;
        try
        {
            profile = _profileResolver.Resolve(request.ProfileName);
        }
        catch (PaymentConfigurationException)
        {
            return Task.FromResult(CreateAllotFailure(
                PaymentErrorKind.ConfigurationInvalid,
                "ConfigurationInvalid",
                "Payment profile configuration is invalid."));
        }

        if (!_allotProviders.TryGetValue(profile.Provider, out var provider))
        {
            return Task.FromResult(CreateAllotFailure(
                PaymentErrorKind.UnsupportedOperation,
                "UnsupportedOperation",
                "The configured payment provider does not support allot queries."));
        }

        return provider.QueryAllotAsync(profile, request, cancellationToken);
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
    /// 解析 request profile 對應的 provider instance，並檢查該 instance 是否實作所需 capability。
    /// 此選擇只屬本次呼叫，不保存 profile、request 或 capability-specific 狀態。
    /// </summary>
    private CapabilitySelection<TCapability> ResolveCapability<TCapability>(
        string? profileName,
        PaymentProviderKind? providerHint)
        where TCapability : class
    {
        var selected = ResolveProvider(profileName, providerHint);
        if (selected.Error is not null)
        {
            return CapabilitySelection<TCapability>.Failed(
                selected.Error,
                selected.Profile,
                selected.Provider);
        }

        if (selected.Provider is not TCapability capability)
        {
            return CapabilitySelection<TCapability>.Failed(
                new PaymentError
                {
                    Kind = PaymentErrorKind.UnsupportedOperation,
                    Code = "UnsupportedOperation",
                    Message = "The configured payment provider does not support this operation."
                },
                selected.Profile,
                selected.Provider);
        }

        return CapabilitySelection<TCapability>.Success(
            selected.Profile!,
            selected.Provider!,
            capability);
    }

    /// <summary>
    /// 驗證付款總額及每筆明細使用 provider 支援的有效幣別；此 helper 不修改請求、不做換匯或金額捨入。
    /// </summary>
    /// <param name="provider">本次伺服器端解析出的 provider。</param>
    /// <param name="request">一般或定期付款共用的 Money-first 請求。</param>
    /// <returns>第一個固定錯誤訊息；全部有效時為 null。</returns>
    private static string? ValidatePaymentCurrencies(
        IPaymentProvider provider,
        PaymentCreateRequest request)
    {
        string currency;
        try
        {
            currency = request.Total.Currency;
        }
        catch (InvalidOperationException)
        {
            return "Payment total must have a valid currency.";
        }

        if (!provider.SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
        {
            return $"Currency '{currency}' is not supported by provider '{provider.ProviderKind}'.";
        }

        foreach (var item in request.Items)
        {
            if (item is null)
            {
                return "Payment line items must have a valid currency matching the payment total.";
            }

            string itemCurrency;
            try
            {
                itemCurrency = item.Price.Currency;
            }
            catch (InvalidOperationException)
            {
                return "Payment line items must have a valid currency matching the payment total.";
            }

            if (!string.Equals(itemCurrency, currency, StringComparison.Ordinal))
            {
                return "Payment line items must have a valid currency matching the payment total.";
            }
        }

        return null;
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

    /// <summary>建立空批次撥款失敗結果，確保 gateway 路由錯誤不洩漏 resolver 或設定細節。</summary>
    /// <param name="kind">標準錯誤分類。</param>
    /// <param name="code">穩定且不含呼叫端資料的錯誤碼。</param>
    /// <param name="message">固定安全說明。</param>
    /// <returns>不含撥款明細的失敗結果。</returns>
    private static PaymentAllotQueryResult CreateAllotFailure(
        PaymentErrorKind kind,
        string code,
        string message)
    {
        return new PaymentAllotQueryResult
        {
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
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

    private sealed record CapabilitySelection<TCapability>(
        PaymentMerchantProfile? Profile,
        IPaymentProvider? Provider,
        TCapability? Capability,
        PaymentError? Error)
        where TCapability : class
    {
        public static CapabilitySelection<TCapability> Success(
            PaymentMerchantProfile profile,
            IPaymentProvider provider,
            TCapability capability)
        {
            return new CapabilitySelection<TCapability>(profile, provider, capability, null);
        }

        public static CapabilitySelection<TCapability> Failed(
            PaymentError error,
            PaymentMerchantProfile? profile = null,
            IPaymentProvider? provider = null)
        {
            return new CapabilitySelection<TCapability>(profile, provider, null, error);
        }
    }
}
