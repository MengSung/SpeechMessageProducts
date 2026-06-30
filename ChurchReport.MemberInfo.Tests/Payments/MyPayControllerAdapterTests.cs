using ChurchReport.Controllers;
using ChurchReport.Payments;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class MyPayControllerAdapterTests
{
    [Fact]
    public async Task PaymentNotify_calls_payment_gateway_and_returns_core_acknowledgement()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCallbackResult
        {
            Error = new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "invalid callback"
            },
            Acknowledgement = PaymentCallbackAcknowledgement.PlainText("8888")
        });
        var controller = CreateController(gateway);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["order_id"] = "F1",
            ["prc"] = "999"
        });
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.PaymentNotify();

        gateway.ParseCallbackCallCount.Should().Be(1);
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("8888");
        content.ContentType.Should().Be("text/plain");
    }

    private static MyPayController CreateController(IPaymentGateway gateway)
    {
        var feeTypeHelper = new MyPayFeeTypeHelper(NullLogger<MyPayFeeTypeHelper>.Instance);
        var messageBuilder = new MyPayMessageBuilder();

        return new MyPayController(
            NullLogger<MyPayController>.Instance,
            messageBuilder,
            new MyPayCrmService(NullLogger<MyPayCrmService>.Instance),
            new MyPayNotificationService(
                NullLogger<MyPayNotificationService>.Instance,
                messageBuilder,
                feeTypeHelper),
            feeTypeHelper,
            new MyPayLogger(NullLogger<MyPayLogger>.Instance),
            new ThrowingToolUtilityProvider(),
            gateway,
            new PaymentHttpRequestMapper(),
            new ChurchReportPaymentProfileResolver(new ConfigurationBuilder().Build()),
            new PaymentAcknowledgementResultMapper(),
            new PaymentWorkflowResultMapper(),
            new PaymentPostPaymentWorkflow(
                Array.Empty<IPaymentRecordUpdater>(),
                Array.Empty<IPaymentPayerNotifier>()));
    }

    private sealed class RecordingPaymentGateway : IPaymentGateway
    {
        private readonly PaymentCallbackResult _callbackResult;

        public RecordingPaymentGateway(PaymentCallbackResult callbackResult)
        {
            _callbackResult = callbackResult;
        }

        public int ParseCallbackCallCount { get; private set; }

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
            throw new NotSupportedException();
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            ParseCallbackCallCount++;
            return Task.FromResult(_callbackResult);
        }
    }

    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility()
        {
            throw new InvalidOperationException("ToolUtility should not be used for invalid callbacks.");
        }
    }
}
