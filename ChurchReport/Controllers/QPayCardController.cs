using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Controllers
{
    [Route("api/[controller]")]
    public class QPayCardController : Controller
    {
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly IQPayReturnWorkflow _qPayReturnWorkflow;

        public QPayCardController(
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            IQPayReturnWorkflow qPayReturnWorkflow)
        {
            _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
            _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
            _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
            _qPayReturnWorkflow = qPayReturnWorkflow ?? throw new ArgumentNullException(nameof(qPayReturnWorkflow));
        }

        [HttpPost]
        [HttpGet]
        [Route("QPayReturnUrl")]
        public async Task<IActionResult> QPayReturnUrl(string ShopNo, string PayToken)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[QPayCardController] QPayReturnUrl called at {DateTime.Now}");
                System.Diagnostics.Trace.WriteLine($"  - HTTP Method: {Request.Method}");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {ShopNo ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {MaskForTrace(PayToken)}");

                var profileName = _paymentProfileResolver.ResolveProfileName();
                var callbackRequest = await _paymentHttpRequestMapper.MapAsync(
                    Request,
                    profileName,
                    PaymentProviderKind.Sinopac,
                    HttpContext.RequestAborted);
                callbackRequest = EnsureReturnFields(callbackRequest, ShopNo, PayToken);

                var callbackResult = await _paymentGateway.ParseCallbackAsync(
                    callbackRequest,
                    HttpContext.RequestAborted);

                if (callbackResult.Error.HasError)
                {
                    return PaymentResultView(
                        false,
                        "Payment callback is invalid. Please contact the church office if the payment was already submitted.",
                        string.Empty,
                        callbackResult.Error.Message);
                }

                var providerOrderRef = FirstNonEmpty(
                    callbackResult.ProviderTransactionId,
                    ReadProviderData(callbackResult.ProviderData, "pay_token"),
                    PayToken);
                var shopNo = FirstNonEmpty(
                    ReadProviderData(callbackResult.ProviderData, "shop_no"),
                    ShopNo);

                var statusResult = await _paymentGateway.QueryPaymentAsync(
                    new PaymentQueryRequest
                    {
                        ProfileName = profileName,
                        ProviderHint = PaymentProviderKind.Sinopac,
                        ProductOrderId = callbackResult.ProductOrderId,
                        ProviderOrderRef = providerOrderRef,
                        Metadata = new Dictionary<string, string>
                        {
                            ["ShopNo"] = shopNo
                        }
                    },
                    HttpContext.RequestAborted);

                return _qPayReturnWorkflow.HandleReturn(shopNo, providerOrderRef, statusResult);
            }
            catch (Exception ex)
            {
                string errorDetail = $"ERROR: QPayCardController.QPayReturnUrl{Environment.NewLine}" +
                                   $"Time: {DateTime.Now}{Environment.NewLine}" +
                                   $"ShopNo: {ShopNo ?? "(null)"}{Environment.NewLine}" +
                                   $"PayToken: {MaskForTrace(PayToken)}{Environment.NewLine}" +
                                   $"Message: {ex.Message}{Environment.NewLine}" +
                                   $"StackTrace: {ex.StackTrace}{Environment.NewLine}" +
                                   $"InnerException: {ex.InnerException?.Message ?? "(none)"}";

                System.Diagnostics.Trace.WriteLine(errorDetail);
                Console.WriteLine(errorDetail);

                return PaymentResultView(
                    false,
                    "An error occurred while processing the payment result. Please try again later or contact the church office.",
                    string.Empty,
                    $"{ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private ViewResult PaymentResultView(
            bool isSuccess,
            string message,
            string orderId,
            string errorDetails)
        {
            ViewBag.IsSuccess = isSuccess;
            ViewBag.Message = message;
            ViewBag.OrderId = orderId;
            ViewBag.ErrorDetails = errorDetails;
            return View("~/Views/QPayCard/PaymentResult.cshtml");
        }

        private static PaymentCallbackRequest EnsureReturnFields(
            PaymentCallbackRequest request,
            string shopNo,
            string payToken)
        {
            var query = new Dictionary<string, string>(request.Query, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(shopNo) && !query.ContainsKey("ShopNo"))
            {
                query["ShopNo"] = shopNo;
            }

            if (!string.IsNullOrWhiteSpace(payToken) && !query.ContainsKey("PayToken"))
            {
                query["PayToken"] = payToken;
            }

            return request with { Query = query };
        }

        private static string ReadProviderData(
            IReadOnlyDictionary<string, string> providerData,
            string key)
        {
            return providerData.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string MaskForTrace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Length <= 8
                ? new string('*', value.Length)
                : value[..4] + "..." + value[^4..];
        }
    }
}
