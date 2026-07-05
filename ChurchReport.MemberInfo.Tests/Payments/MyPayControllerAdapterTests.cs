// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class MyPayControllerAdapterTests、class RecordingPaymentGateway、class ThrowingToolUtilityProvider
// 主要成員：Constructor_accepts_context_builder_dependency、PaymentNotify_calls_payment_gateway_and_returns_core_acknowledgement、CreateController、CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、GetToolUtility、ParseCallbackCallCount
// 引用命名空間：ChurchReport.Controllers、ChurchReport.Payments、ChurchReport.Services、FluentAssertions、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Configuration、Microsoft.Extensions.Logging.Abstractions
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    public void Constructor_accepts_context_builder_dependency()
    {
        typeof(MyPayController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Should()
            .Contain(parameter => parameter.ParameterType == typeof(ChurchReportPaymentContextBuilder));
    }

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
        var feeTypeHelper = new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance);
        var messageBuilder = new PaymentMessageBuilder();

        return new MyPayController(
            NullLogger<MyPayController>.Instance,
            messageBuilder,
            new PaymentCrmService(NullLogger<PaymentCrmService>.Instance),
            new PaymentNotificationService(
                NullLogger<PaymentNotificationService>.Instance,
                messageBuilder,
                feeTypeHelper),
            feeTypeHelper,
            new PaymentCallbackLogger(NullLogger<PaymentCallbackLogger>.Instance),
            new ThrowingToolUtilityProvider(),
            gateway,
            new PaymentHttpRequestMapper(),
            new ChurchReportPaymentProfileResolver(new ConfigurationBuilder().Build()),
            new PaymentAcknowledgementResultMapper(),
            new PaymentWorkflowResultMapper(),
            new PaymentPostPaymentWorkflow(
                Array.Empty<IPaymentRecordUpdater>(),
                Array.Empty<IPaymentPayerNotifier>()),
            new ChurchReportPaymentContextBuilder(feeTypeHelper));
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
