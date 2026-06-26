using System;
using System.Threading.Tasks;
using ChurchReport.Payments;
using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
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

        [HttpGet("post-back")]
        [HttpPost("post-back")]
        public async Task<IActionResult> PostBack()
        {
            PaymentCallbackResult callbackResult = null;

            try
            {
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
