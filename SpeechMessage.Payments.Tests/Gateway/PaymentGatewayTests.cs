using FluentAssertions;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Gateway;

/// <summary>
/// 驗證 <see cref="PaymentGateway"/> 的 provider routing 行為。
/// Gateway 只根據 profile/provider hint 選擇 provider，不知道永豐、高鉅或台新的內部協定細節。
/// </summary>
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
        // provider hint 與 profile 不一致時必須回設定錯誤；
        // 這可以防止例如 route 明明是台新，卻誤用高鉅 profile 建立付款。
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
        // 測試用 resolver：只模擬 profile name resolution，不讀 appsettings，也不帶真實 credential。
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
        // 測試用 provider：只記錄收到的 profile name，避免 gateway routing 測試碰到真實 provider HTTP 呼叫。
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
