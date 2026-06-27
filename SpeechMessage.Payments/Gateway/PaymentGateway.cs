using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Gateway;

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
