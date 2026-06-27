using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.MyPay;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.MyPay;

public sealed class MyPayProviderTests
{
    [Theory]
    [InlineData("250", PaymentStatus.Succeeded)]
    [InlineData("290", PaymentStatus.Succeeded)]
    [InlineData("600", PaymentStatus.Succeeded)]
    [InlineData("260", PaymentStatus.Pending)]
    [InlineData("270", PaymentStatus.Pending)]
    [InlineData("280", PaymentStatus.Pending)]
    [InlineData("300", PaymentStatus.Failed)]
    [InlineData("400", PaymentStatus.Failed)]
    public void Status_mapper_maps_prc_to_normalized_status(string prc, PaymentStatus expected)
    {
        MyPayStatusMapper.Map(prc).Should().Be(expected);
    }

    [Fact]
    public void Callback_parser_maps_valid_callback_to_neutral_result_with_plain_text_ack()
    {
        var request = new PaymentCallbackRequest
        {
            ProfileName = "MyPayProduction",
            ProviderHint = PaymentProviderKind.MyPay,
            Form = new Dictionary<string, string>
            {
                ["uid"] = "1234567890abcdef1234567890abcdef",
                ["key"] = "abcdef1234567890abcdef1234567890",
                ["prc"] = "250",
                ["order_id"] = "F202606250001",
                ["cost"] = "1200",
                ["currency"] = "TWD",
                ["retmsg"] = "approved",
                ["cardno"] = "4111111111111111"
            }
        };

        var result = MyPayCallbackParser.Parse(request);

        result.Status.Should().Be(PaymentStatus.Succeeded);
        result.ProductOrderId.Should().Be("F202606250001");
        result.ProviderTransactionId.Should().Be("1234567890abcdef1234567890abcdef");
        result.Amount.Should().Be(1200m);
        result.Acknowledgement.Should().Be(PaymentCallbackAcknowledgement.PlainText("8888"));
        result.ProviderData.Should().ContainKey("prc").WhoseValue.Should().Be("250");
        result.Diagnostics["key"].Should().Be("***");
        result.Diagnostics["cardno"].Should().Be("411111******1111");
    }

    [Fact]
    public void Callback_parser_returns_callback_invalid_and_still_acknowledges_invalid_payload()
    {
        var request = new PaymentCallbackRequest
        {
            ProfileName = "MyPayProduction",
            ProviderHint = PaymentProviderKind.MyPay,
            Form = new Dictionary<string, string>
            {
                ["uid"] = "",
                ["key"] = "bad",
                ["prc"] = "999",
                ["order_id"] = ""
            }
        };

        var result = MyPayCallbackParser.Parse(request);

        result.Error.Kind.Should().Be(PaymentErrorKind.CallbackInvalid);
        result.Acknowledgement.Should().Be(PaymentCallbackAcknowledgement.PlainText("8888"));
    }

    [Fact]
    public void Request_mapper_maps_create_request_to_mypay_payload()
    {
        var profile = new PaymentMerchantProfile
        {
            Name = "MyPayProduction",
            Provider = PaymentProviderKind.MyPay,
            Credentials = new Dictionary<string, string>
            {
                ["StoreId"] = "130544850001"
            }
        };
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "F202606250001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            Customer = new PaymentCustomer
            {
                Name = "Grace",
                Email = "grace@example.test",
                Phone = "0912345678"
            },
            Callbacks = new PaymentCallbacks
            {
                SuccessUrl = "https://example.test/success",
                FailureUrl = "https://example.test/failure",
                BackendUrl = "https://example.test/backend"
            }
        };

        var payload = MyPayRequestMapper.MapCreatePayload(profile, request);

        payload.StoreUid.Should().Be("130544850001");
        payload.OrderId.Should().Be("F202606250001");
        payload.Cost.Should().Be("1200");
        payload.Currency.Should().Be("TWD");
        payload.UserName.Should().Be("Grace");
        payload.SuccessReturnUrl.Should().Be("https://example.test/success");
        payload.FailureReturnUrl.Should().Be("https://example.test/failure");
    }

    [Fact]
    public void Request_mapper_uses_store_uid_for_direct_merchant_create_form()
    {
        var profile = new PaymentMerchantProfile
        {
            Name = "MyPayProduction",
            Provider = PaymentProviderKind.MyPay,
            Credentials = new Dictionary<string, string>
            {
                ["StoreId"] = "130544850001",
                ["Key"] = "m4KNdB8NtuIc6mJa1XAYX3W1jWoHQCgy"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://ka.usecase.cc/api/init"
            }
        };

        var form = MyPayRequestMapper.MapCreateForm(profile, CreateMyPayPaymentRequest());

        form.Should().ContainKey("store_uid").WhoseValue.Should().Be("130544850001");
        form.Should().NotContainKey("agent_uid");
        form.Should().ContainKey("service");
        form.Should().ContainKey("encry_data");
    }

    [Fact]
    public void Request_mapper_uses_agent_uid_only_when_agent_profile_is_configured()
    {
        var profile = new PaymentMerchantProfile
        {
            Name = "MyPayAgent",
            Provider = PaymentProviderKind.MyPay,
            Credentials = new Dictionary<string, string>
            {
                ["StoreId"] = "289151880002",
                ["Key"] = "merchant-key",
                ["AgentId"] = "518169081001",
                ["AgentKey"] = "0DZP5XgV1dLXXNQqNUFZ7UXvSP6DBalS"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://ka.usecase.cc/api/agent"
            }
        };

        var form = MyPayRequestMapper.MapCreateForm(profile, CreateMyPayPaymentRequest());

        form.Should().ContainKey("agent_uid").WhoseValue.Should().Be("518169081001");
        form.Should().NotContainKey("store_uid");
        form.Should().ContainKey("service");
        form.Should().ContainKey("encry_data");
    }

    [Fact]
    public void Service_registration_adds_mypay_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultProfile"] = "MyPayProduction",
                ["Profiles:MyPayProduction:Provider"] = "MyPay"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSpeechMessagePayments(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IPaymentProvider>()
            .Should()
            .Contain(paymentProvider => paymentProvider.ProviderKind == PaymentProviderKind.MyPay);
    }

    private static PaymentCreateRequest CreateMyPayPaymentRequest()
    {
        return new PaymentCreateRequest
        {
            ProductOrderId = "F202606250001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            Customer = new PaymentCustomer
            {
                Name = "Grace",
                Email = "grace@example.test",
                Phone = "0912345678"
            },
            Callbacks = new PaymentCallbacks
            {
                SuccessUrl = "https://example.test/success",
                FailureUrl = "https://example.test/failure",
                BackendUrl = "https://example.test/backend"
            }
        };
    }
}
