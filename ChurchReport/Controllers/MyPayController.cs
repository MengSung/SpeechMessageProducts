using System;
using System.Threading.Tasks;
using ChurchReport.Payments;
using ChurchReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using static ChurchReport.Services.MyPayFeeTypeHelper;

namespace ChurchReport.Controllers
{
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        private readonly ILogger<MyPayController> _logger;
        private readonly MyPayMessageBuilder _messageBuilder;
        private readonly MyPayCrmService _crmService;
        private readonly MyPayNotificationService _notificationService;
        private readonly MyPayFeeTypeHelper _feeTypeHelper;
        private readonly MyPayLogger _myPayLogger;
        private readonly IToolUtilityProvider _toolUtilityProvider;
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly PaymentAcknowledgementResultMapper _paymentAcknowledgementResultMapper;
        private readonly PaymentWorkflowResultMapper _paymentWorkflowResultMapper;

        private ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        public MyPayController(
            ILogger<MyPayController> logger,
            MyPayMessageBuilder messageBuilder,
            MyPayCrmService crmService,
            MyPayNotificationService notificationService,
            MyPayFeeTypeHelper feeTypeHelper,
            MyPayLogger myPayLogger,
            IToolUtilityProvider toolUtilityProvider,
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            PaymentAcknowledgementResultMapper paymentAcknowledgementResultMapper,
            PaymentWorkflowResultMapper paymentWorkflowResultMapper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageBuilder = messageBuilder ?? throw new ArgumentNullException(nameof(messageBuilder));
            _crmService = crmService ?? throw new ArgumentNullException(nameof(crmService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _feeTypeHelper = feeTypeHelper ?? throw new ArgumentNullException(nameof(feeTypeHelper));
            _myPayLogger = myPayLogger ?? throw new ArgumentNullException(nameof(myPayLogger));
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
            _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
            _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
            _paymentAcknowledgementResultMapper = paymentAcknowledgementResultMapper ?? throw new ArgumentNullException(nameof(paymentAcknowledgementResultMapper));
            _paymentWorkflowResultMapper = paymentWorkflowResultMapper ?? throw new ArgumentNullException(nameof(paymentWorkflowResultMapper));
        }

        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify()
        {
            PaymentCallbackResult callbackResult = null;

            try
            {
                var profileName = _paymentProfileResolver.ResolveProfileName("MyPayProduction");
                var callbackRequest = await _paymentHttpRequestMapper.MapAsync(
                    Request,
                    profileName,
                    PaymentProviderKind.MyPay,
                    HttpContext.RequestAborted);

                callbackResult = await _paymentGateway.ParseCallbackAsync(callbackRequest, HttpContext.RequestAborted);
                _myPayLogger.LogPaymentCallbackResult(callbackResult);

                if (callbackResult.Error.HasError || string.IsNullOrWhiteSpace(callbackResult.ProductOrderId))
                {
                    _logger.LogWarning(
                        "[MyPay回傳] Core callback parse failed: {ErrorKind} {ErrorMessage}",
                        callbackResult.Error.Kind,
                        callbackResult.Error.Message);

                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                var workflowResult = _paymentWorkflowResultMapper.Map(callbackResult);
                bool isSuccess = workflowResult.Status == PaymentStatus.Succeeded;

                _logger.LogInformation(
                    "[MyPay回傳] Core callback parsed. OrderId: {OrderId}, Status: {Status}, IsSuccess: {IsSuccess}",
                    workflowResult.ProductOrderId,
                    workflowResult.Status,
                    isSuccess);

                Entity feeEntity = ToolUtility.RetrieveEntityByField(
                    "new_fee",
                    "new_q_pay_order_number",
                    workflowResult.ProductOrderId);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] 找不到對應收費單 - OrderId: {workflowResult.ProductOrderId}");
                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                FeeType feeType = _feeTypeHelper.DetermineFeeType(ToolUtility, feeEntity);
                var contactId = ToolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                Entity contactEntity = null;
                string fullName = "會友";
                string lineId = null;

                if (contactId != Guid.Empty)
                {
                    contactEntity = ToolUtility.RetrieveEntity("contact", contactId);
                    if (contactEntity != null)
                    {
                        fullName = ToolUtility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                        lineId = ToolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                    }
                }

                _crmService.UpdateFeeEntityWithPaymentResult(ToolUtility, feeEntity, workflowResult, isSuccess);
                ToolUtility.UpdateEntity(ref feeEntity);

                if (!string.IsNullOrWhiteSpace(lineId))
                {
                    try
                    {
                        if (isSuccess)
                        {
                            _notificationService.SendLineNotificationByType(
                                ToolUtility,
                                feeEntity,
                                workflowResult,
                                fullName,
                                feeType,
                                contactEntity);
                        }
                        else
                        {
                            _notificationService.SendLineFailureNotificationByType(
                                ToolUtility,
                                feeEntity,
                                workflowResult,
                                fullName,
                                feeType,
                                contactEntity);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {workflowResult.ProductOrderId}");
                    }
                }

                return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyPay回傳] 處理異常");
                var acknowledgement = callbackResult?.Acknowledgement ?? PaymentCallbackAcknowledgement.PlainText("8888");
                return _paymentAcknowledgementResultMapper.ToActionResult(acknowledgement);
            }
        }

        [HttpGet("success")]
        public IActionResult PaymentSuccess([FromQuery] string order_id = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
            ViewBag.IsSuccess = true;
            return View("PaymentResult");
        }

        [HttpGet("failure")]
        public IActionResult PaymentFailure([FromQuery] string order_id = "", [FromQuery] string msg = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
            ViewBag.IsSuccess = false;
            return View("PaymentResult");
        }
    }
}
