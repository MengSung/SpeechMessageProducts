using System;
using System.Threading.Tasks;
using ChurchReport.Payments;
using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 台新 TSPG 的 ChurchReport HTTP adapter。
    /// 台新 JSON/form callback parsing、hash 驗證與狀態轉換已移到 <c>SpeechMessage.Payments</c>；
    /// 此 controller 保留 ChurchReport 專屬的 CRM fee 更新、LINE 通知與結果頁轉址。
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TSPGController : ControllerBase
    {
        private const int PaymentStatusPaid = 100000001;
        private const int PaymentMethodCreditCard = 100000001;
        private const string TaishinProfileName = "TaishinSandbox";

        private readonly IToolUtilityProvider _toolUtilityProvider;
        private readonly IConfiguration _configuration;
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly PaymentAcknowledgementResultMapper _paymentAcknowledgementResultMapper;
        private readonly PaymentWorkflowResultMapper _paymentWorkflowResultMapper;

        private ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();
        private string LineChannelAccessToken => GetLineChannelAccessToken();
        private System.Threading.CancellationToken RequestAborted => HttpContext?.RequestAborted ?? default;

        public TSPGController(
            IToolUtilityProvider toolUtilityProvider,
            IConfiguration configuration,
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            PaymentAcknowledgementResultMapper paymentAcknowledgementResultMapper,
            PaymentWorkflowResultMapper paymentWorkflowResultMapper)
        {
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
            _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
            _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
            _paymentAcknowledgementResultMapper = paymentAcknowledgementResultMapper ?? throw new ArgumentNullException(nameof(paymentAcknowledgementResultMapper));
            _paymentWorkflowResultMapper = paymentWorkflowResultMapper ?? throw new ArgumentNullException(nameof(paymentWorkflowResultMapper));
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
                    ? HandleSuccessfulPaymentReturn(workflowResult)
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
                if (workflowResult.Status == PaymentStatus.Succeeded)
                {
                    UpdateFeeEntityByOrderNo(workflowResult);
                    LogInfo("PaymentNotify", $"Payment success processed - Order: {workflowResult.ProductOrderId}");
                }
                else
                {
                    LogInfo("PaymentNotify", $"Payment failed - Order: {workflowResult.ProductOrderId}, Message: {workflowResult.ProviderMessage}");
                }

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

        private void UpdateFeeEntityByOrderNo(PaymentWorkflowResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(result.ProductOrderId))
                {
                    LogWarning("UpdateFeeEntity", "Payment result has no order id.");
                    return;
                }

                // CRM entity 查詢與欄位更新是 ChurchReport product workflow，不能下沉到通用金流核心。
                Entity feeEntity = ToolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", result.ProductOrderId);
                if (feeEntity == null)
                {
                    LogWarning("UpdateFeeEntity", $"No fee entity found - OrderNo: {result.ProductOrderId}");
                    return;
                }

                UpdateFeeEntityFields(feeEntity, result);
                ToolUtility.UpdateEntity(ref feeEntity);
                LogInfo("UpdateFeeEntity", $"Fee entity updated - OrderNo: {result.ProductOrderId}, FeeId: {feeEntity.Id}");

                var amount = ToolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (amount != null)
                {
                    SendPaymentNotificationToContact(feeEntity, result, amount.Value);
                }
            }
            catch (Exception ex)
            {
                LogError("UpdateFeeEntity", "Failed to update fee entity", ex);
            }
        }

        private void UpdateFeeEntityFields(Entity feeEntity, PaymentWorkflowResult result)
        {
            var shouldPayMoney = ToolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
            ToolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PaymentStatusPaid);
            ToolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
            ToolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
            ToolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", DateTime.Now);
            ToolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PaymentMethodCreditCard);

            var originalDescription = ToolUtility.GetEntityStringAttribute(feeEntity, "new_description");
            var newDescription = $"{originalDescription}{Environment.NewLine}" +
                $"[Taishin payment success] Order:{result.ProductOrderId}, Transaction:{result.ProviderTransactionId}, " +
                $"Amount:{shouldPayMoney}, Time:{DateTime.Now}";
            ToolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
        }

        private void SendPaymentNotificationToContact(Entity feeEntity, PaymentWorkflowResult result, decimal amount)
        {
            try
            {
                // LINE 通知是產品體驗，不是台新 provider contract；核心只回傳付款狀態與交易識別。
                var contactId = ToolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty)
                {
                    LogWarning("SendNotification", "Fee entity has no contact.");
                    return;
                }

                Entity contactEntity = ToolUtility.RetrieveEntity("contact", contactId);
                if (contactEntity == null)
                {
                    LogWarning("SendNotification", $"No contact found - ContactId: {contactId}");
                    return;
                }

                string lineId = ToolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrEmpty(lineId))
                {
                    LogWarning("SendNotification", $"Contact has no LINE ID - ContactId: {contactId}");
                    return;
                }

                string fullName = ToolUtility.GetEntityStringAttribute(contactEntity, "fullname");
                var message = BuildPaymentSuccessMessage(fullName, result.ProductOrderId, amount, result);
                SendLineMessage(lineId, message);
                LogInfo("SendNotification", $"Payment LINE message sent - ContactId: {contactId}, LineId: {lineId}");
            }
            catch (Exception ex)
            {
                LogError("SendNotification", "Failed to send LINE message", ex);
            }
        }

        private string BuildPaymentSuccessMessage(string fullName, string orderNo, decimal amount, PaymentWorkflowResult result)
        {
            var message = $"[Taishin payment success]{Environment.NewLine}{Environment.NewLine}";
            message += $"Dear {fullName},{Environment.NewLine}{Environment.NewLine}";
            message += $"Payment information:{Environment.NewLine}";
            message += $"Order: {orderNo}{Environment.NewLine}";
            message += $"Amount: NT$ {amount:N0}{Environment.NewLine}";
            message += $"Payment time: {DateTime.Now:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            message += $"Payment method: Credit card{Environment.NewLine}";
            if (!string.IsNullOrEmpty(result.ProviderTransactionId))
            {
                message += $"Transaction: {result.ProviderTransactionId}{Environment.NewLine}";
            }

            return message;
        }

        private void SendLineMessage(string lineId, string message)
        {
            var token = LineChannelAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                LogWarning("SendLineMessage", "LINE Channel Access Token is empty.");
                return;
            }

            var lineMessagingClient = new LineMessagingClient(token);
            var pushUtility = new PushUtility(lineMessagingClient);
            pushUtility.SendMessage(lineId, message).Wait();
            LogInfo("SendLineMessage", $"LINE message sent - LineId: {lineId}");
        }

        private IActionResult HandleSuccessfulPaymentReturn(PaymentWorkflowResult result)
        {
            try
            {
                LogInfo("PaymentReturn", $"Payment success - Order: {result.ProductOrderId}");
                UpdateFeeEntityByOrderNo(result);

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

        private string GetLineChannelAccessToken()
        {
            try
            {
                string organization = _configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    string configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    string token = _configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }

                string defaultOrg = _configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                return _configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogError("GetLineChannelAccessToken", "Failed to read LINE token configuration", ex);
                return string.Empty;
            }
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
