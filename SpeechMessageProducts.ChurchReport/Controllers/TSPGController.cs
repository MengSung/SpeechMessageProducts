// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/TSPGController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class TSPGController
// 主要成員：PostBack、ResultUrl、CreatePayment、QueryOrderStatus、ParseTaishinCallbackAsync、ResolveTaishinProfileName、ExecutePostPaymentWorkflowAsync、HandleSuccessfulPaymentReturnAsync、HandleFailedPaymentReturn、BuildSuccessQueryString
// 引用命名空間：System、System.Threading.Tasks、ChurchReport.Payments、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、SpeechMessage.Payments.AspNetCore、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Threading.Tasks;
using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;
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
    /// 台新 TSPG 的 ChurchReport HTTP adapter。
    /// 台新 JSON/form callback parsing、hash 驗證與狀態轉換已移到 <c>SpeechMessage.Payments</c>；
    /// 此 controller 只保留 HTTP 入口、acknowledgement 與結果頁轉址；CRM/LINE 後處理委派給 ChurchReport workflow handlers。
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TSPGController : ControllerBase
    {
        private const string TaishinProfileName = "TaishinSandbox";

        private readonly IToolUtilityProvider _toolUtilityProvider;
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly PaymentAcknowledgementResultMapper _paymentAcknowledgementResultMapper;
        private readonly PaymentWorkflowResultMapper _paymentWorkflowResultMapper;
        private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;
        private readonly ChurchReportPaymentContextBuilder _paymentContextBuilder;

        private ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();
        private System.Threading.CancellationToken RequestAborted => HttpContext?.RequestAborted ?? default;

        public TSPGController(
            IToolUtilityProvider toolUtilityProvider,
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            PaymentAcknowledgementResultMapper paymentAcknowledgementResultMapper,
            PaymentWorkflowResultMapper paymentWorkflowResultMapper,
            PaymentPostPaymentWorkflow postPaymentWorkflow,
            ChurchReportPaymentContextBuilder paymentContextBuilder)
        {
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
        /// 台新前端返回入口，解析付款結果後轉到 ChurchReport 的成功或失敗頁。
        /// </summary>
        [HttpGet("post-back")]
        [HttpPost("post-back")]
        public async Task<IActionResult> PostBack()
        {
            PaymentCallbackResult callbackResult = null;

            try
            {
                // ASP.NET request 只在 controller 層轉成 neutral callback request；
                // hash 驗證與欄位正規化交由台新 provider parser。
                callbackResult = await ParseTaishinCallbackAsync();
                if (callbackResult.Error.HasError || string.IsNullOrWhiteSpace(callbackResult.ProductOrderId))
                {
                    LogWarning("PostBack", $"Core callback parse failed: {callbackResult.Error.Kind} {callbackResult.Error.Message}");
                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                var workflowResult = _paymentWorkflowResultMapper.Map(callbackResult);
                return workflowResult.Status == PaymentStatus.Succeeded
                    ? await HandleSuccessfulPaymentReturnAsync(workflowResult)
                    : HandleFailedPaymentReturn(workflowResult);
            }
            catch (Exception ex)
            {
                LogError("PostBack", "Payment return processing failed", ex);
                var acknowledgement = callbackResult?.Acknowledgement ??
                    PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}");
                return _paymentAcknowledgementResultMapper.ToActionResult(acknowledgement);
            }
        }

        /// <summary>
        /// 台新後端通知入口，付款成功時更新 ChurchReport CRM fee 並回覆台新需要的 acknowledgement。
        /// </summary>
        [HttpPost("result-url")]
        [HttpGet("result-url")]
        public async Task<IActionResult> ResultUrl()
        {
            PaymentCallbackResult callbackResult = null;

            try
            {
                callbackResult = await ParseTaishinCallbackAsync();
                if (callbackResult.Error.HasError || string.IsNullOrWhiteSpace(callbackResult.ProductOrderId))
                {
                    LogWarning("PaymentNotify", $"Core callback parse failed: {callbackResult.Error.Kind} {callbackResult.Error.Message}");
                    return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
                }

                // 產品層只消費 normalized workflow result，不直接解讀台新的 ret_code/state/hash。
                var workflowResult = _paymentWorkflowResultMapper.Map(callbackResult);
                await ExecutePostPaymentWorkflowAsync(workflowResult);
                LogInfo(
                    "PaymentNotify",
                    workflowResult.Status == PaymentStatus.Succeeded
                        ? $"Payment success processed - Order: {workflowResult.ProductOrderId}"
                        : $"Payment failed - Order: {workflowResult.ProductOrderId}, Message: {workflowResult.ProviderMessage}");

                return _paymentAcknowledgementResultMapper.ToActionResult(callbackResult.Acknowledgement);
            }
            catch (Exception ex)
            {
                LogError("PaymentNotify", "Payment notification processing failed", ex);
                var acknowledgement = callbackResult?.Acknowledgement ??
                    PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}");
                return _paymentAcknowledgementResultMapper.ToActionResult(acknowledgement);
            }
        }

        /// <summary>
        /// 台新建立付款的測試/整合入口。
        /// Request body 使用通用核心模型；Controller 只補上台新 profile 與 provider hint。
        /// </summary>
        [HttpPost("create-payment")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentCreateRequest request)
        {
            if (request is null)
            {
                return BadRequest(new { success = false, message = "Request body is required." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // 固定走台新 profile，避免外部呼叫誤用其他 provider profile 建立 TSPG 付款。
            var gatewayRequest = request with
            {
                ProfileName = ResolveTaishinProfileName(request.ProfileName),
                ProviderHint = PaymentProviderKind.Taishin
            };

            var result = await _paymentGateway.CreatePaymentAsync(gatewayRequest, RequestAborted);
            if (result.Error.HasError)
            {
                return BadRequest(new
                {
                    success = false,
                    order_id = result.ProductOrderId,
                    error_kind = result.Error.Kind.ToString(),
                    error_code = result.Error.Code,
                    message = result.Error.Message
                });
            }

            return Ok(new
            {
                success = true,
                order_id = result.ProductOrderId,
                payment_url = result.PaymentPageUrl,
                provider_order_ref = result.ProviderOrderRef
            });
        }

        /// <summary>
        /// 台新付款查詢入口，回傳 provider-neutral 狀態給呼叫端。
        /// </summary>
        [HttpGet("query-order/{orderId}")]
        public async Task<IActionResult> QueryOrderStatus(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return BadRequest(new { success = false, message = "Order id is required." });
            }

            var result = await _paymentGateway.QueryPaymentAsync(
                new PaymentQueryRequest
                {
                    ProfileName = ResolveTaishinProfileName(null),
                    ProviderHint = PaymentProviderKind.Taishin,
                    ProductOrderId = orderId,
                    ProviderOrderRef = orderId
                },
                RequestAborted);

            if (result.Error.HasError)
            {
                return BadRequest(new
                {
                    success = false,
                    order_id = result.ProductOrderId,
                    status = result.Status.ToString(),
                    error_kind = result.Error.Kind.ToString(),
                    error_code = result.Error.Code,
                    message = result.Error.Message
                });
            }

            return Ok(new
            {
                success = result.Status == PaymentStatus.Succeeded,
                order_id = string.IsNullOrWhiteSpace(result.ProductOrderId) ? orderId : result.ProductOrderId,
                status = result.Status.ToString(),
                provider_transaction_id = result.ProviderTransactionId
            });
        }

        private async Task<PaymentCallbackResult> ParseTaishinCallbackAsync()
        {
            // 保持 ASP.NET HttpRequest 只存在於 ChurchReport；核心只收到 PaymentCallbackRequest。
            var callbackRequest = await _paymentHttpRequestMapper.MapAsync(
                Request,
                ResolveTaishinProfileName(null),
                PaymentProviderKind.Taishin,
                RequestAborted);

            return await _paymentGateway.ParseCallbackAsync(callbackRequest, RequestAborted);
        }

        private string ResolveTaishinProfileName(string requestedProfileName)
        {
            return string.IsNullOrWhiteSpace(requestedProfileName)
                ? _paymentProfileResolver.ResolveProfileName(TaishinProfileName)
                : _paymentProfileResolver.ResolveProfileName(requestedProfileName);
        }

        private async Task ExecutePostPaymentWorkflowAsync(PaymentWorkflowResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(result.ProductOrderId))
                {
                    LogWarning("PostPaymentWorkflow", "Payment result has no order id.");
                    return;
                }

                // CRM entity 查詢留在 ChurchReport 產品層；通用金流核心只提供標準化付款結果。
                Entity feeEntity = ToolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", result.ProductOrderId);
                if (feeEntity == null)
                {
                    LogWarning("PostPaymentWorkflow", $"No fee entity found - OrderNo: {result.ProductOrderId}");
                    return;
                }

                // context builder 統一準備付款後 workflow 需要的 ChurchReport 資料。
                // 實際 CRM 更新與 LINE 通知由 PaymentPostPaymentWorkflow 的 handlers 執行。
                var context = _paymentContextBuilder.Build(
                    ToolUtility,
                    feeEntity,
                    result,
                    result.Status == PaymentStatus.Succeeded);

                await _postPaymentWorkflow.ExecuteAsync(context, RequestAborted);
                LogInfo("PostPaymentWorkflow", $"Workflow executed - OrderNo: {result.ProductOrderId}, FeeId: {feeEntity.Id}");
            }
            catch (Exception ex)
            {
                LogError("PostPaymentWorkflow", "Failed to execute post-payment workflow", ex);
            }
        }

        private async Task<IActionResult> HandleSuccessfulPaymentReturnAsync(PaymentWorkflowResult result)
        {
            try
            {
                LogInfo("PaymentReturn", $"Payment success - Order: {result.ProductOrderId}");
                await ExecutePostPaymentWorkflowAsync(result);

                Entity feeEntity = ToolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", result.ProductOrderId);
                var queryString = BuildSuccessQueryString(result, feeEntity);
                return Redirect($"/payment-success?{queryString}");
            }
            catch (Exception ex)
            {
                LogError("PaymentReturn", "Failed to process payment success return", ex);
                return Redirect("/payment-error");
            }
        }

        private IActionResult HandleFailedPaymentReturn(PaymentWorkflowResult result)
        {
            LogInfo("PaymentReturn", $"Payment failed - Order: {result.ProductOrderId}, Error: {result.ProviderMessage}");
            var errorMsg = string.IsNullOrWhiteSpace(result.ProviderMessage) ? "Payment failed" : result.ProviderMessage;
            var orderId = string.IsNullOrWhiteSpace(result.ProductOrderId) ? "UNKNOWN" : result.ProductOrderId;
            return Redirect($"/payment-failed?order_id={Uri.EscapeDataString(orderId)}" +
                $"&error={Uri.EscapeDataString(errorMsg)}");
        }

        private string BuildSuccessQueryString(PaymentWorkflowResult result, Entity feeEntity)
        {
            var amount = result.Amount?.ToString("0") ?? "0";
            if (feeEntity != null)
            {
                var money = ToolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (money != null)
                {
                    amount = Convert.ToInt32(money.Value).ToString();
                }
            }

            return $"order_id={Uri.EscapeDataString(result.ProductOrderId)}" +
                $"&transaction_id={Uri.EscapeDataString(result.ProviderTransactionId ?? string.Empty)}" +
                $"&amount={amount}";
        }

        private void LogInfo(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}");
        }

        private void LogWarning(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] Warning: {message}");
        }

        private void LogError(string method, string message, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}: {ex.Message}");
            if (ex.StackTrace != null)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG {method}] Stack: {ex.StackTrace}");
            }
        }
    }
}
