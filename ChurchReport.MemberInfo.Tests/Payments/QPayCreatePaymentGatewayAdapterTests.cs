using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class QPayCreatePaymentGatewayAdapterTests
{
    [Fact]
    public async Task CreateCardPaymentAsync_maps_legacy_qpay_fields_to_neutral_create_request()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "TS123",
            PaymentPageUrl = "https://pay.example.test/card"
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:DefaultProfile"] = "JesusTest"
            })
            .Build();
        var adapter = new QPayCreatePaymentGatewayAdapter(
            gateway,
            new PaymentCreateRequestFactory(),
            new ChurchReportPaymentProfileResolver(configuration));

        var result = await adapter.CreateCardPaymentAsync(new QPayCreatePaymentInput
        {
            Amount = 1200m,
            ProductName = "Fee payment",
            ProductOrderId = "C20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "fee",
            PaymentMethod = "C",
            PaymentMethodSubType = "ONE",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend",
            AutoBilling = "Y",
            Customer = new PaymentCustomer
            {
                Name = "Grace"
            },
            CreditCardToken = "cc-token"
        });

        gateway.CreatePaymentCallCount.Should().Be(1);
        gateway.LastCreateRequest.Should().NotBeNull();
        gateway.LastCreateRequest!.ProfileName.Should().Be("JesusTest");
        gateway.LastCreateRequest.ProviderHint.Should().BeNull();
        gateway.LastCreateRequest.ProductOrderId.Should().Be("C20260626112233");
        gateway.LastCreateRequest.Amount.Should().Be(1200m);
        gateway.LastCreateRequest.Description.Should().Be("Fee payment");
        gateway.LastCreateRequest.PaymentMethod.Should().Be("C");
        gateway.LastCreateRequest.PaymentMethodSubType.Should().Be("ONE");
        gateway.LastCreateRequest.Callbacks.ReturnUrl.Should().Be("https://church.example.test/qpay-return");
        gateway.LastCreateRequest.Callbacks.BackendUrl.Should().Be("https://church.example.test/qpay-backend");
        gateway.LastCreateRequest.Metadata["Param1"].Should().Be("fee-id");
        gateway.LastCreateRequest.Metadata["Param2"].Should().Be("Jesus");
        gateway.LastCreateRequest.Metadata["Param3"].Should().Be("fee");
        gateway.LastCreateRequest.Metadata["PayType"].Should().Be("C");
        gateway.LastCreateRequest.Metadata["AutoBilling"].Should().Be("Y");
        gateway.LastCreateRequest.Metadata["CCToken"].Should().Be("cc-token");
        gateway.LastCreateRequest.Metadata["UserId"].Should().Be("Grace");
        gateway.LastCreateRequest.Items.Should().ContainSingle();
        gateway.LastCreateRequest.Items[0].Name.Should().Be("Fee payment");
        gateway.LastCreateRequest.Items[0].Quantity.Should().Be(1);
        gateway.LastCreateRequest.Items[0].UnitPrice.Should().Be(1200m);
        result.PaymentPageUrl.Should().Be("https://pay.example.test/card");
    }

    [Fact]
    public async Task CreateCardPaymentAsync_uses_pay_provider_profile_before_payment_default_profile()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "MYPAY123",
            PaymentPageUrl = "https://mypay.example.test/card"
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PAY_PROVIDER"] = "高鉅金流",
                ["Payment:DefaultProfile"] = "JesusTest"
            })
            .Build();
        var adapter = new QPayCreatePaymentGatewayAdapter(
            gateway,
            new PaymentCreateRequestFactory(),
            new ChurchReportPaymentProfileResolver(configuration));

        await adapter.CreateCardPaymentAsync(new QPayCreatePaymentInput
        {
            Amount = 1200m,
            ProductName = "Fee payment",
            ProductOrderId = "C20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "fee",
            PaymentMethod = "C",
            PaymentMethodSubType = "ONE",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        gateway.LastCreateRequest.Should().NotBeNull();
        gateway.LastCreateRequest!.ProfileName.Should().Be("MyPayProduction");
        gateway.LastCreateRequest.ProviderHint.Should().BeNull();
    }

    [Fact]
    public async Task CreateCardPaymentAsync_defaults_recurring_schedule_when_ui_default_is_not_posted()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "TS123",
            PaymentPageUrl = "https://pay.example.test/card"
        });
        var adapter = CreateAdapter(gateway);

        await adapter.CreateCardPaymentAsync(new QPayCreatePaymentInput
        {
            Amount = 800m,
            ProductName = "Recurring donation",
            ProductOrderId = "C20260626112233",
            ProductEntityId = "booking-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "dedication-booking",
            PaymentMethod = "C",
            PaymentMethodSubType = "REGULAR",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        gateway.LastCreateRequest.Should().NotBeNull();
        gateway.LastCreateRequest!.Metadata["PayTypeSub"].Should().Be("REGULAR");
        gateway.LastCreateRequest.Metadata["DeductTotalNum"].Should().Be("12");
        gateway.LastCreateRequest.Metadata["PeriodType"].Should().Be("M");
        gateway.LastCreateRequest.Metadata["DeductFreq"].Should().Be("1");
    }

    [Fact]
    public async Task CreateLegacyOrderAsync_maps_neutral_create_result_to_existing_creorder_shape()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "TS123456789",
            PaymentPageUrl = "https://pay.example.test/card",
            ProviderData = new Dictionary<string, string>
            {
                ["shop_no"] = "NA0149_001"
            }
        });
        var adapter = CreateAdapter(gateway);

        var order = await adapter.CreateLegacyOrderAsync(new QPayCreatePaymentInput
        {
            Amount = 1200m,
            ProductName = "Fee payment",
            ProductOrderId = "C20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "fee",
            PaymentMethod = "C",
            PaymentMethodSubType = "ONE",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        order.Status.Should().Be("S");
        order.OrderNo.Should().Be("C20260626112233");
        order.TSNo.Should().Be("TS123456789");
        order.ShopNo.Should().Be("NA0149_001");
        order.PayType.Should().Be("C");
        order.Amount.Should().Be(120000);
        order.Description.Should().BeEmpty();
        order.Param1.Should().Be("fee-id");
        order.Param2.Should().Be("Jesus");
        order.Param3.Should().Be("fee");
        order.CardParam.Should().NotBeNull();
        order.CardParam.CardPayURL.Should().Be("https://pay.example.test/card");
        order.MobileParam.Should().BeNull();
        order.ATMParam.Should().BeNull();
    }

    [Fact]
    public async Task CreateLegacyOrderAsync_fails_closed_when_card_payment_page_url_is_missing()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "TS123456789",
            PaymentPageUrl = string.Empty
        });
        var adapter = CreateAdapter(gateway);

        var order = await adapter.CreateLegacyOrderAsync(new QPayCreatePaymentInput
        {
            Amount = 1200m,
            ProductName = "Fee payment",
            ProductOrderId = "C20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "fee",
            PaymentMethod = "C",
            PaymentMethodSubType = "ONE",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        order.Status.Should().Be("F");
        order.Description.Should().Contain("payment page URL");
        order.CardParam.Should().BeNull();
        order.MobileParam.Should().BeNull();
        order.ATMParam.Should().BeNull();
    }

    [Fact]
    public async Task CreateLegacyOrderAsync_maps_atm_virtual_account_from_provider_data()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "A20260626112233",
            ProviderOrderRef = "TSATM123456",
            PaymentPageUrl = "https://pay.example.test/atm",
            ProviderData = new Dictionary<string, string>
            {
                ["shop_no"] = "NA0149_001",
                ["atm_pay_no"] = "12345678901234",
                ["otp_url"] = "https://pay.example.test/otp"
            }
        });
        var adapter = CreateAdapter(gateway);

        var order = await adapter.CreateLegacyOrderAsync(new QPayCreatePaymentInput
        {
            Amount = 8m,
            ProductName = "Tithe donation",
            ProductOrderId = "A20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "tithe",
            PaymentMethod = "A",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        order.Status.Should().Be("S");
        order.ATMParam.Should().NotBeNull();
        order.ATMParam.AtmPayNo.Should().Be("12345678901234");
        order.ATMParam.WebAtmURL.Should().Be("https://pay.example.test/atm");
        order.ATMParam.OtpURL.Should().Be("https://pay.example.test/otp");
    }

    [Fact]
    public async Task CreateLegacyOrderAsync_fails_closed_when_atm_virtual_account_is_missing()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "A20260626112233",
            ProviderOrderRef = "TSATM123456",
            PaymentPageUrl = "https://pay.example.test/atm",
            ProviderData = new Dictionary<string, string>
            {
                ["shop_no"] = "NA0149_001"
            }
        });
        var adapter = CreateAdapter(gateway);

        var order = await adapter.CreateLegacyOrderAsync(new QPayCreatePaymentInput
        {
            Amount = 8m,
            ProductName = "Tithe donation",
            ProductOrderId = "A20260626112233",
            ProductEntityId = "fee-id",
            PaymentOrganization = "Jesus",
            PaymentCategory = "tithe",
            PaymentMethod = "A",
            ReturnUrl = "https://church.example.test/qpay-return",
            BackendUrl = "https://church.example.test/qpay-backend"
        });

        order.Status.Should().Be("F");
        order.Description.Should().Contain("ATM virtual account");
    }

    private static QPayCreatePaymentGatewayAdapter CreateAdapter(IPaymentGateway gateway)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:DefaultProfile"] = "JesusTest"
            })
            .Build();

        return new QPayCreatePaymentGatewayAdapter(
            gateway,
            new PaymentCreateRequestFactory(),
            new ChurchReportPaymentProfileResolver(configuration));
    }

    private sealed class RecordingPaymentGateway : IPaymentGateway
    {
        private readonly PaymentCreateResult _createResult;

        public RecordingPaymentGateway(PaymentCreateResult createResult)
        {
            _createResult = createResult;
        }

        public int CreatePaymentCallCount { get; private set; }
        public PaymentCreateRequest? LastCreateRequest { get; private set; }

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            CreatePaymentCallCount++;
            LastCreateRequest = request;
            return Task.FromResult(_createResult);
        }

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
