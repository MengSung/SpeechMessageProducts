using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChurchReport.Payments;
using ChurchReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using static ChurchReport.Services.MyPayFeeTypeHelper;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 高鉅 MyPay callback 的 ChurchReport HTTP adapter。
    /// Provider callback parsing、acknowledgement 與狀態正規化交給共用金流核心；
    /// 本 controller 只負責 ChurchReport 的 CRM fee 查詢、付款後 workflow context 組裝與結果頁。
    /// </summary>
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
        private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;

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
            PaymentWorkflowResultMapper paymentWorkflowResultMapper,
            PaymentPostPaymentWorkflow postPaymentWorkflow)
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
            _postPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
        }

        /// <summary>
        /// MyPay 背景通知入口。
        /// ASP.NET request 先轉成中立 callback request，再由 MyPay provider parser 驗證與正規化；
        /// 成功解析後，交給共用付款後流程 pipeline 編排 CRM 更新與付款者通知。
        /// </summary>
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
                var isSuccess = workflowResult.Status == PaymentStatus.Succeeded;

                _logger.LogInformation(
                    "[MyPay回傳] Core callback parsed. OrderId: {OrderId}, Status: {Status}, IsSuccess: {IsSuccess}",
                    workflowResult.ProductOrderId,
                    workflowResult.Status,
                    isSuccess);

                var feeEntity = ToolUtility.RetrieveEntityByField(
                    "new_fee",
                    "new_q_pay_order_number",
                    workflowResult.ProductOrderId);

                if (feeEntity == null)
                {
                    _logger.LogWarning("[MyPay回傳] 找不到收費單 - OrderId: {OrderId}", workflowResult.ProductOrderId);
                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                var feeType = _feeTypeHelper.DetermineFeeType(ToolUtility, feeEntity);
                var contactEntity = ResolveContactEntity(feeEntity, out var fullName);
                var postPaymentContext = new PaymentPostPaymentContext(
                    workflowResult,
                    new Dictionary<string, object?>
                    {
                        [ChurchReportPaymentWorkflowContextKeys.ToolUtility] = ToolUtility,
                        [ChurchReportPaymentWorkflowContextKeys.FeeEntity] = feeEntity,
                        [ChurchReportPaymentWorkflowContextKeys.IsSuccess] = isSuccess,
                        [ChurchReportPaymentWorkflowContextKeys.FullName] = fullName,
                        [ChurchReportPaymentWorkflowContextKeys.FeeType] = feeType,
                        [ChurchReportPaymentWorkflowContextKeys.ContactEntity] = contactEntity
                    });

                await _postPaymentWorkflow.ExecuteAsync(postPaymentContext, HttpContext.RequestAborted);
                return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyPay回傳] 處理失敗");
                var acknowledgement = callbackResult?.Acknowledgement ?? PaymentCallbackAcknowledgement.PlainText("8888");
                return _paymentAcknowledgementResultMapper.ToActionResult(acknowledgement);
            }
        }

        [HttpGet("success")]
        public IActionResult PaymentSuccess([FromQuery] string order_id = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = "付款已完成，系統會更新收費紀錄並發送通知。";
            ViewBag.IsSuccess = true;
            return View("PaymentResult");
        }

        [HttpGet("failure")]
        public IActionResult PaymentFailure([FromQuery] string order_id = "", [FromQuery] string msg = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請確認付款資料或稍後再試。";
            ViewBag.IsSuccess = false;
            return View("PaymentResult");
        }

        private Entity ResolveContactEntity(Entity feeEntity, out string fullName)
        {
            fullName = "未知";
            var contactId = ToolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
            if (contactId == Guid.Empty)
            {
                return null;
            }

            var contactEntity = ToolUtility.RetrieveEntity("contact", contactId);
            if (contactEntity != null)
            {
                fullName = ToolUtility.GetEntityStringAttribute(contactEntity, "fullname") ?? "未知";
            }

            return contactEntity;
        }
    }
}
