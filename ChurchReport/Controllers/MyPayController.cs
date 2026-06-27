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
    /// <summary>
    /// 高鉅 MyPay callback 的 ChurchReport HTTP adapter。
    /// Provider callback 解析、狀態碼轉換與 acknowledgement 規則已移到 <c>SpeechMessage.Payments</c>；
    /// 這個 controller 只保留 ChurchReport 產品責任：查 CRM fee、更新 CRM、判斷奉獻類型與發送 LINE 通知。
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

        /// <summary>
        /// MyPay 後端通知入口。
        /// 流程分成三段：先把 ASP.NET request 轉成 neutral callback request，再交給金流核心解析，
        /// 最後才執行 ChurchReport 的 CRM/LINE 後續處理。
        /// </summary>
        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify()
        {
            PaymentCallbackResult callbackResult = null;

            try
            {
                // MyPayNotify 是固定高鉅 callback route，因此明確指定 MyPayProduction profile 與 MyPay provider hint。
                // 若未來環境需要改 profile，應改設定或 resolver，不要在這裡組 MyPay 原始欄位。
                var profileName = _paymentProfileResolver.ResolveProfileName("MyPayProduction");
                var callbackRequest = await _paymentHttpRequestMapper.MapAsync(
                    Request,
                    profileName,
                    PaymentProviderKind.MyPay,
                    HttpContext.RequestAborted);

                // Provider-specific key/prc/order_id parsing 與 acknowledgement=8888 規則都在 MyPay provider 內處理。
                callbackResult = await _paymentGateway.ParseCallbackAsync(callbackRequest, HttpContext.RequestAborted);
                _myPayLogger.LogPaymentCallbackResult(callbackResult);

                if (callbackResult.Error.HasError || string.IsNullOrWhiteSpace(callbackResult.ProductOrderId))
                {
                    // 即使 callback payload 有誤，也依核心給的 acknowledgement 回覆 provider，
                    // 避免產品層自行猜測 MyPay 需要的固定回應文字。
                    _logger.LogWarning(
                        "[MyPay回傳] Core callback parse failed: {ErrorKind} {ErrorMessage}",
                        callbackResult.Error.Kind,
                        callbackResult.Error.Message);

                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                // 轉成 ChurchReport workflow result 後，後面只處理 CRM/LINE，不再碰 MyPay 原始 callback 欄位。
                var workflowResult = _paymentWorkflowResultMapper.Map(callbackResult);
                bool isSuccess = workflowResult.Status == PaymentStatus.Succeeded;

                _logger.LogInformation(
                    "[MyPay回傳] Core callback parsed. OrderId: {OrderId}, Status: {Status}, IsSuccess: {IsSuccess}",
                    workflowResult.ProductOrderId,
                    workflowResult.Status,
                    isSuccess);

                // CRM fee entity lookup 是 ChurchReport 產品流程，不能搬進通用金流核心。
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

                // CRM 更新與 LINE 通知都以 normalized PaymentStatus 為依據，不再解析 MyPay prc 狀態碼。
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
                // 發生未預期例外時仍回覆 MyPay acknowledgement，避免 provider 不斷重送造成重複處理壓力。
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
