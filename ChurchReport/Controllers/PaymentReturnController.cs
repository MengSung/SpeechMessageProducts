using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 金流回傳端點的主要 Controller。
    /// 這個 Controller 名稱刻意不包含 QPay，因為它負責的是 ChurchReport 的付款回傳流程，
    /// 不是永豐專屬 protocol；provider callback 解析與狀態查詢由 <see cref="IPaymentGateway"/> 負責。
    /// </summary>
    [Route("api/[controller]")]
    public class PaymentReturnController : Controller
    {
        private readonly IPaymentGateway _paymentGateway;
        private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
        private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
        private readonly IDonationPaymentReturnWorkflow _paymentReturnWorkflow;

        public PaymentReturnController(
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            IDonationPaymentReturnWorkflow paymentReturnWorkflow)
        {
            _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
            _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
            _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
            _paymentReturnWorkflow = paymentReturnWorkflow ?? throw new ArgumentNullException(nameof(paymentReturnWorkflow));
        }

        /// <summary>
        /// 新的中性付款回傳端點。
        /// 目前 provider callback 仍可能使用舊 QPay URL，因此此 action 是新的主要名稱，
        /// 舊 action 會透過 <see cref="ReturnCore"/> 重用同一段流程。
        /// </summary>
        [HttpPost]
        [HttpGet]
        [Route("Return")]
        [Route("/Payment/Return")]
        public Task<IActionResult> Return(string ShopNo, string PayToken)
        {
            return ReturnCore(ShopNo, PayToken, "PaymentReturnController.Return");
        }

        /// <summary>
        /// 實際付款回傳流程。
        /// 這裡是 HTTP 層與付款 workflow 的邊界：HTTP query/form 先被轉成 PaymentCallbackRequest，
        /// 再交給金流核心解析、查詢狀態，最後交給 ChurchReport 產品 workflow 處理 CRM/LINE 等後續流程。
        /// </summary>
        protected async Task<IActionResult> ReturnCore(
            string ShopNo,
            string PayToken,
            string traceSource)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"[{traceSource}] payment return called at {DateTime.Now}");
                System.Diagnostics.Trace.WriteLine($"  - HTTP Method: {Request.Method}");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {ShopNo ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {MaskForTrace(PayToken)}");

                var profileName = _paymentProfileResolver.ResolveProfileName();
                var callbackRequest = await _paymentHttpRequestMapper.MapAsync(
                    Request,
                    profileName,
                    PaymentProviderKind.Sinopac,
                    HttpContext.RequestAborted);

                // ASP.NET MVC action binding 可能已經先把 ShopNo/PayToken 綁到參數。
                // 這裡把它們補回 neutral callback request，讓 provider parser 有完整資料可判斷。
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

                // return callback 只代表金流通知抵達，不代表最終付款狀態。
                // 因此必須透過 provider-neutral gateway 查詢狀態，再交給 ChurchReport workflow。
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

                return _paymentReturnWorkflow.HandleReturn(shopNo, providerOrderRef, statusResult);
            }
            catch (Exception ex)
            {
                var errorDetail = $"ERROR: {traceSource}{Environment.NewLine}" +
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
            // PayToken 可能是可追蹤付款的敏感識別值，trace 只保留頭尾方便除錯。
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
