using FluentAssertions;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Gateway;

public sealed class PaymentGatewayTests
{
    [Fact]
    public async Task Create_payment_uses_default_profile_when_request_profile_is_empty()
    {
        var provider = new FakePaymentProvider(PaymentProviderKind.MyPay);
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "DefaultMyPay",
                Provider = PaymentProviderKind.MyPay
            }),
            new[] { provider });

        await gateway.CreatePaymentAsync(new PaymentCreateRequest { ProductOrderId = "F1" });

        provider.ReceivedProfileName.Should().Be("DefaultMyPay");
    }

    [Fact]
    public async Task Create_payment_returns_configuration_error_when_provider_hint_mismatches_profile()
    {
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "DefaultMyPay",
                Provider = PaymentProviderKind.MyPay
            }),
            new[] { new FakePaymentProvider(PaymentProviderKind.MyPay) });

        var result = await gateway.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProviderHint = PaymentProviderKind.Sinopac,
            ProductOrderId = "F1"
        });

        result.Error.Kind.Should().Be(PaymentErrorKind.ConfigurationInvalid);
    }

    [Fact]
    public async Task Create_payment_returns_unsupported_operation_when_no_provider_matches_profile()
    {
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "SinopacProfile",
                Provider = PaymentProviderKind.Sinopac
            }),
            new[] { new FakePaymentProvider(PaymentProviderKind.MyPay) });

        var result = await gateway.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProductOrderId = "F1"
        });

        result.Error.Kind.Should().Be(PaymentErrorKind.UnsupportedOperation);
    }

    private sealed class StubProfileResolver : IPaymentProfileResolver
    {
        private readonly PaymentMerchantProfile _profile;

        public StubProfileResolver(PaymentMerchantProfile profile)
        {
            _profile = profile;
        }

        public PaymentMerchantProfile Resolve(string? profileName)
        {
            return _profile with { Name = string.IsNullOrWhiteSpace(profileName) ? _profile.Name : profileName };
        }
    }

    private sealed class FakePaymentProvider : IPaymentProvider
    {
        public FakePaymentProvider(PaymentProviderKind providerKind)
        {
            ProviderKind = providerKind;
        }

        public PaymentProviderKind ProviderKind { get; }
        public string ReceivedProfileName { get; private set; } = string.Empty;

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentMerchantProfile profile,
            PaymentCreateRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentCreateResult
            {
                Status = PaymentStatus.Pending,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = "fake-provider-order"
            });
        }

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentMerchantProfile profile,
            PaymentQueryRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Pending,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = request.ProviderOrderRef
            });
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentMerchantProfile profile,
            PaymentCallbackRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentCallbackResult
            {
                Status = PaymentStatus.Succeeded,
                ProductOrderId = request.Form.TryGetValue("order_id", out var orderId) ? orderId : string.Empty
            });
        }
    }
}
