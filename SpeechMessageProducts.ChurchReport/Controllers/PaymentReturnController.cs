// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/PaymentReturnController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class PaymentReturnController
// 主要成員：Return、ReturnCore、PaymentResultView、EnsureReturnFields、ReadProviderData、FirstNonEmpty、MaskForTrace
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ChurchReport.Payments、Microsoft.AspNetCore.Mvc、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.AspNetCore
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    /// ChurchReport 的付款回傳 Controller。
    ///
    /// 這個 Controller 是「產品層 callback 入口」，不是任何一家銀行的 provider 實作。
    /// 它的工作是把 ASP.NET Core 收到的 HTTP request 轉成通用金流核心看得懂的
    /// <see cref="PaymentCallbackRequest"/>，再把金流核心查到的付款狀態交給
    /// <see cref="IDonationPaymentReturnWorkflow"/> 做 ChurchReport 專屬後續處理。
    ///
    /// 分工邊界如下：
    /// - 這裡可以知道 ASP.NET Core、Controller、ViewBag、舊網址。
    /// - 這裡可以指定目前這條舊 callback 屬於 Sinopac profile，因為舊銀行設定就是打這個網址。
    /// - 這裡不直接更新 CRM、不直接發 LINE、不直接解析銀行加密欄位。
    /// - provider protocol 的 callback 解析與查詢付款狀態由 <see cref="IPaymentGateway"/> 負責。
    ///
    /// 舊外部網址仍透過 Route attribute 保留，避免銀行後台設定或既有連結立刻失效；
    /// 但是 C# 類別與方法使用 PaymentReturn 這種中性名稱，避免把整條產品流程誤命名成單一 provider。
    /// </summary>
    [Route("api/[controller]")]
    public class PaymentReturnController : Controller
    {
        private const string PaymentResultViewName = "~/Views/PaymentReturn/PaymentResult.cshtml";

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
        /// 接收付款完成後的瀏覽器導回或 provider callback。
        ///
        /// 這個 action 同時掛上新的中性 URL 與舊 URL：
        /// - /api/PaymentReturn/Return：新的中性入口。
        /// - /Payment/Return：比較短的中性入口。
        /// - 舊 route template：保留給銀行後台或既有連結使用。
        ///
        /// 實作時不要新增舊 provider 形狀的方法名稱；如果未來還有舊網址要保留，
        /// 請只加 Route attribute，讓 C# 呼叫端永遠面對 Return 這個中性 action。
        /// </summary>
        [HttpPost]
        [HttpGet]
        [Route("Return")]
        [Route("/Payment/Return")]
        [Route("/api/QPayCard/QPayReturnUrl")]
        public Task<IActionResult> Return(string ShopNo, string PayToken)
        {
            return ReturnCore(ShopNo, PayToken, "PaymentReturnController.Return");
        }

        /// <summary>
        /// 實際處理付款回傳的共用流程。
        ///
        /// 步驟拆解：
        /// 1. 先把 ASP.NET Core 的 Request 轉成通用 callback request。
        /// 2. 補上 MVC model binding 已經抓到的 ShopNo / PayToken，避免 query/form mapper 沒吃到時資料遺失。
        /// 3. 呼叫 <see cref="IPaymentGateway.ParseCallbackAsync"/> 讓金流核心驗證 callback。
        /// 4. 如果 callback 無效，就回到付款結果頁並顯示可診斷的錯誤。
        /// 5. 如果 callback 有效，再查詢 provider 的最新付款狀態。
        /// 6. 把狀態交給 ChurchReport 專屬 workflow，後續 CRM 更新、LINE 通知、頁面呈現都在 workflow 處理。
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

                callbackRequest = EnsureReturnFields(callbackRequest, ShopNo, PayToken);

                var callbackResult = await _paymentGateway.ParseCallbackAsync(
                    callbackRequest,
                    HttpContext.RequestAborted);

                if (callbackResult.Error.HasError)
                {
                    return PaymentResultView(
                        false,
                        "付款回傳資料無效。如果您已經完成付款，請聯絡教會辦公室協助確認。",
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
                    "處理付款結果時發生錯誤，請稍後再試或聯絡教會辦公室。",
                    string.Empty,
                    $"{ex.Message}{Environment.NewLine}{Environment.NewLine}{ex.StackTrace}");
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
            return View(PaymentResultViewName);
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
