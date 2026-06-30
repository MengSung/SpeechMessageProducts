using ChurchReport.Controllers;
using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class QPayAdapterTests
{
    [Fact]
    public async Task ReturnUrl_calls_payment_gateway_parse_and_query_with_pay_token()
    {
        var payToken = "1234567890abcdef1234567890abcdef";
        var gateway = new RecordingPaymentGateway(
            new PaymentCallbackResult
            {
                Status = PaymentStatus.Pending,
                ProviderTransactionId = payToken,
                ProviderData = new Dictionary<string, string>
                {
                    ["shop_no"] = "NA0149_001",
                    ["pay_token"] = "1234...cdef"
                }
            },
            new PaymentStatusResult
            {
                Status = PaymentStatus.Succeeded,
                ProductOrderId = "C202606260001",
                ProviderOrderRef = payToken,
                ProviderTransactionId = "TS123",
                Amount = 1200m,
                ProviderData = new Dictionary<string, string>
                {
                    ["product_entity_id"] = "fee-id",
                    ["payment_category"] = "fee",
                    ["provider_message"] = "S00000"
                }
            });
        var workflow = new RecordingQPayReturnWorkflow();
        var controller = CreateController(gateway, workflow);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = new QueryString($"?ShopNo=NA0149_001&PayToken={payToken}");
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.QPayReturnUrl("NA0149_001", payToken);

        gateway.ParseCallbackCallCount.Should().Be(1);
        gateway.LastCallbackRequest!.ProviderHint.Should().Be(PaymentProviderKind.Sinopac);
        gateway.LastCallbackRequest.Query["ShopNo"].Should().Be("NA0149_001");
        gateway.LastCallbackRequest.Query["PayToken"].Should().Be(payToken);
        gateway.QueryPaymentCallCount.Should().Be(1);
        gateway.LastQueryRequest!.ProviderHint.Should().Be(PaymentProviderKind.Sinopac);
        gateway.LastQueryRequest.ProfileName.Should().Be("JesusTest");
        gateway.LastQueryRequest.ProviderOrderRef.Should().Be(payToken);
        gateway.LastQueryRequest.Metadata["ShopNo"].Should().Be("NA0149_001");
        workflow.CallCount.Should().Be(1);
        workflow.LastStatusResult.Should().BeEquivalentTo(gateway.StatusResult);
        workflow.LastShopNo.Should().Be("NA0149_001");
        workflow.LastPayToken.Should().Be(payToken);
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("~/Views/QPayCard/PaymentResult.cshtml");
    }

    [Fact]
    public async Task ReturnUrl_does_not_query_payment_when_core_rejects_callback()
    {
        var gateway = new RecordingPaymentGateway(
            new PaymentCallbackResult
            {
                Error = new PaymentError
                {
                    Kind = PaymentErrorKind.CallbackInvalid,
                    Message = "missing ShopNo or PayToken"
                }
            },
            new PaymentStatusResult());
        var workflow = new RecordingQPayReturnWorkflow();
        var controller = CreateController(gateway, workflow);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.QPayReturnUrl("", "PAYTOKEN-123");

        gateway.ParseCallbackCallCount.Should().Be(1);
        gateway.QueryPaymentCallCount.Should().Be(0);
        workflow.CallCount.Should().Be(0);
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("~/Views/QPayCard/PaymentResult.cshtml");
        ((bool)controller.ViewBag.IsSuccess).Should().Be(false);
        ((string)controller.ViewBag.ErrorDetails).Should().Contain("missing ShopNo or PayToken");
    }

    private static QPayCardController CreateController(
        IPaymentGateway gateway,
        IQPayReturnWorkflow workflow)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new QPayCardController(
            gateway,
            new PaymentHttpRequestMapper(),
            new ChurchReportPaymentProfileResolver(configuration),
            workflow);
    }

    private sealed class RecordingPaymentGateway : IPaymentGateway
    {
        private readonly PaymentCallbackResult _callbackResult;
        private readonly PaymentStatusResult _statusResult;

        public RecordingPaymentGateway(
            PaymentCallbackResult callbackResult,
            PaymentStatusResult statusResult)
        {
            _callbackResult = callbackResult;
            _statusResult = statusResult;
        }

        public int ParseCallbackCallCount { get; private set; }
        public int QueryPaymentCallCount { get; private set; }
        public PaymentCallbackRequest? LastCallbackRequest { get; private set; }
        public PaymentQueryRequest? LastQueryRequest { get; private set; }
        public PaymentStatusResult StatusResult => _statusResult;

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryPaymentCallCount++;
            LastQueryRequest = request;
            return Task.FromResult(_statusResult);
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            ParseCallbackCallCount++;
            LastCallbackRequest = request;
            return Task.FromResult(_callbackResult);
        }
    }

    private sealed class RecordingQPayReturnWorkflow : IQPayReturnWorkflow
    {
        public int CallCount { get; private set; }
        public string LastShopNo { get; private set; } = string.Empty;
        public string LastPayToken { get; private set; } = string.Empty;
        public PaymentStatusResult? LastStatusResult { get; private set; }

        public IActionResult HandleReturn(
            string shopNo,
            string payToken,
            PaymentStatusResult statusResult)
        {
            CallCount++;
            LastShopNo = shopNo;
            LastPayToken = payToken;
            LastStatusResult = statusResult;
            return new ViewResult { ViewName = "~/Views/QPayCard/PaymentResult.cshtml" };
        }
    }
}
