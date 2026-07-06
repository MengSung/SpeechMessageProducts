// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/MyPayController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class MyPayController
// 主要成員：PaymentNotify、PaymentSuccess、PaymentFailure、ResolveContactEntity
// 引用命名空間：System、System.Threading.Tasks、ChurchReport.Payments、ChurchReport.Services、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Logging、Microsoft.Xrm.Sdk、SpeechMessage.Payments.AspNetCore
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
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
        private readonly PaymentMessageBuilder _messageBuilder;
        private readonly PaymentCrmService _crmService;
        private readonly PaymentNotificationService _notificationService;
        private readonly PaymentFeeTypeHelper _feeTypeHelper;
        private readonly PaymentCallbackLogger _paymentCallbackLogger;
        private readonly IToolUtilityProvider _toolUtilityProvider;
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly PaymentAcknowledgementResultMapper _paymentAcknowledgementResultMapper;
        private readonly PaymentWorkflowResultMapper _paymentWorkflowResultMapper;
        private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;
        private readonly ChurchReportPaymentContextBuilder _paymentContextBuilder;

        private ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        public MyPayController(
            ILogger<MyPayController> logger,
            PaymentMessageBuilder messageBuilder,
            PaymentCrmService crmService,
            PaymentNotificationService notificationService,
            PaymentFeeTypeHelper feeTypeHelper,
            PaymentCallbackLogger paymentCallbackLogger,
            IToolUtilityProvider toolUtilityProvider,
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            PaymentAcknowledgementResultMapper paymentAcknowledgementResultMapper,
            PaymentWorkflowResultMapper paymentWorkflowResultMapper,
            PaymentPostPaymentWorkflow postPaymentWorkflow,
            ChurchReportPaymentContextBuilder paymentContextBuilder)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageBuilder = messageBuilder ?? throw new ArgumentNullException(nameof(messageBuilder));
            _crmService = crmService ?? throw new ArgumentNullException(nameof(crmService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _feeTypeHelper = feeTypeHelper ?? throw new ArgumentNullException(nameof(feeTypeHelper));
            _paymentCallbackLogger = paymentCallbackLogger ?? throw new ArgumentNullException(nameof(paymentCallbackLogger));
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
            _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
            _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
            _paymentAcknowledgementResultMapper = paymentAcknowledgementResultMapper ?? throw new ArgumentNullException(nameof(paymentAcknowledgementResultMapper));
            _paymentWorkflowResultMapper = paymentWorkflowResultMapper ?? throw new ArgumentNullException(nameof(paymentWorkflowResultMapper));
            _postPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
            _paymentContextBuilder = paymentContextBuilder ?? throw new ArgumentNullException(nameof(paymentContextBuilder));
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
                _paymentCallbackLogger.LogPaymentCallbackResult(callbackResult);

                if (callbackResult.Error.HasError || string.IsNullOrWhiteSpace(callbackResult.ProductOrderId))
                {
                    _logger.LogWarning(
                        "[付款回傳] Core callback parse failed: {ErrorKind} {ErrorMessage}",
                        callbackResult.Error.Kind,
                        callbackResult.Error.Message);

                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                var workflowResult = _paymentWorkflowResultMapper.Map(callbackResult);
                var isSuccess = workflowResult.Status == PaymentStatus.Succeeded;

                _logger.LogInformation(
                    "[付款回傳] Core callback parsed. OrderId: {OrderId}, Status: {Status}, IsSuccess: {IsSuccess}",
                    workflowResult.ProductOrderId,
                    workflowResult.Status,
                    isSuccess);

                var feeEntity = ToolUtility.RetrieveEntityByField(
                    "new_fee",
                    "new_q_pay_order_number",
                    workflowResult.ProductOrderId);

                if (feeEntity == null)
                {
                    _logger.LogWarning("[付款回傳] 找不到收費單 - OrderId: {OrderId}", workflowResult.ProductOrderId);
                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                var postPaymentContext = _paymentContextBuilder.Build(
                    ToolUtility,
                    feeEntity,
                    workflowResult,
                    isSuccess);

                await _postPaymentWorkflow.ExecuteAsync(postPaymentContext, HttpContext.RequestAborted);
                return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[付款回傳] 處理失敗");
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
