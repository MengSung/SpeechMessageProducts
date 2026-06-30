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

    public PaymentGateway(IPaymentProfileResolver profileResolver, IEnumerable<IPaymentProvider> providers)
    {
        _profileResolver = profileResolver;
        _providers = providers
            .GroupBy(provider => provider.ProviderKind)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveProvider(request.ProfileName, request.ProviderHint);
        return selected.Error is not null
            ? Task.FromResult(new PaymentCreateResult
            {
                ProductOrderId = request.ProductOrderId,
                Error = selected.Error
            })
            : selected.Provider!.CreatePaymentAsync(selected.Profile!, request, cancellationToken);
    }

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
