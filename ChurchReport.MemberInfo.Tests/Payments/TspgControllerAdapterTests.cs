using System.Text;
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

public sealed class TspgControllerAdapterTests
{
    [Fact]
    public void Constructor_accepts_common_post_payment_workflow_dependencies()
    {
        var constructorParameters = typeof(TSPGController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().Contain(typeof(PaymentPostPaymentWorkflow));
        constructorParameters.Should().Contain(typeof(ChurchReportPaymentContextBuilder));
    }

    [Fact]
    public async Task ResultUrl_calls_payment_gateway_and_returns_core_json_acknowledgement()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCallbackResult
        {
            Error = new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "invalid callback"
            },
            Acknowledgement = PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}")
        });
        var controller = CreateController(gateway);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"params\":{\"order_no\":\"F1\"}}"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.ResultUrl();

        gateway.ParseCallbackCallCount.Should().Be(1);
        gateway.LastCallbackRequest!.ProviderHint.Should().Be(PaymentProviderKind.Taishin);
        gateway.LastCallbackRequest.RawBody.Should().Contain("order_no");
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/json");
        content.Content.Should().Contain("success");
    }

    [Fact]
    public async Task PostBack_calls_payment_gateway_before_product_workflow()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCallbackResult
        {
            Error = new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "invalid callback"
            },
            Acknowledgement = PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}")
        });
        var controller = CreateController(gateway);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["order_no"] = "F1",
            ["ret_code"] = "00"
        });
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.PostBack();

        gateway.ParseCallbackCallCount.Should().Be(1);
        gateway.LastCallbackRequest!.ProviderHint.Should().Be(PaymentProviderKind.Taishin);
        gateway.LastCallbackRequest.Form["order_no"].Should().Be("F1");
        result.Should().BeOfType<ContentResult>();
    }

    [Fact]
    public async Task CreatePayment_calls_payment_gateway_create_with_taishin_profile()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "F202606260001",
            ProviderOrderRef = "TXCREATE",
            PaymentPageUrl = "https://tspg.example/pay"
        });
        var controller = CreateController(gateway);

        var result = await controller.CreatePayment(new PaymentCreateRequest
        {
            ProductOrderId = "F202606260001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.test/post-back",
                BackendUrl = "https://example.test/result-url"
            }
        });

        gateway.CreatePaymentCallCount.Should().Be(1);
        gateway.LastCreateRequest!.ProviderHint.Should().Be(PaymentProviderKind.Taishin);
        gateway.LastCreateRequest.ProfileName.Should().Be("TaishinSandbox");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            success = true,
            order_id = "F202606260001",
            payment_url = "https://tspg.example/pay",
            provider_order_ref = "TXCREATE"
        });
    }

    [Fact]
    public async Task QueryOrderStatus_calls_payment_gateway_query()
    {
        var gateway = new RecordingPaymentGateway(new PaymentStatusResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "F202606260002",
            ProviderOrderRef = "F202606260002",
            ProviderTransactionId = "TXQUERY"
        });
        var controller = CreateController(gateway);

        var result = await controller.QueryOrderStatus("F202606260002");

        gateway.QueryPaymentCallCount.Should().Be(1);
        gateway.LastQueryRequest!.ProviderHint.Should().Be(PaymentProviderKind.Taishin);
        gateway.LastQueryRequest.ProfileName.Should().Be("TaishinSandbox");
        gateway.LastQueryRequest.ProviderOrderRef.Should().Be("F202606260002");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            success = true,
            order_id = "F202606260002",
            status = PaymentStatus.Succeeded.ToString(),
            provider_transaction_id = "TXQUERY"
        });
    }

    private static TSPGController CreateController(IPaymentGateway gateway)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new TSPGController(
            new ThrowingToolUtilityProvider(),
            gateway,
            new PaymentHttpRequestMapper(),
            new ChurchReportPaymentProfileResolver(configuration),
            new PaymentAcknowledgementResultMapper(),
            new PaymentWorkflowResultMapper(),
            new PaymentPostPaymentWorkflow(
                Array.Empty<IPaymentRecordUpdater>(),
                Array.Empty<IPaymentPayerNotifier>()),
            new ChurchReportPaymentContextBuilder(
                new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance)));
    }

    private sealed class RecordingPaymentGateway : IPaymentGateway
    {
        private readonly PaymentCallbackResult _callbackResult;
        private readonly PaymentCreateResult _createResult;
        private readonly PaymentStatusResult _statusResult;

        public RecordingPaymentGateway(PaymentCallbackResult callbackResult)
        {
            _callbackResult = callbackResult;
            _createResult = new PaymentCreateResult();
            _statusResult = new PaymentStatusResult();
        }

        public RecordingPaymentGateway(PaymentCreateResult createResult)
        {
            _callbackResult = new PaymentCallbackResult();
            _createResult = createResult;
            _statusResult = new PaymentStatusResult();
        }

        public RecordingPaymentGateway(PaymentStatusResult statusResult)
        {
            _callbackResult = new PaymentCallbackResult();
            _createResult = new PaymentCreateResult();
            _statusResult = statusResult;
        }

        public int ParseCallbackCallCount { get; private set; }
        public int CreatePaymentCallCount { get; private set; }
        public int QueryPaymentCallCount { get; private set; }
        public PaymentCallbackRequest? LastCallbackRequest { get; private set; }
        public PaymentCreateRequest? LastCreateRequest { get; private set; }
        public PaymentQueryRequest? LastQueryRequest { get; private set; }

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

    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility()
        {
            throw new InvalidOperationException("ToolUtility should not be used for invalid callbacks.");
        }
    }
}
