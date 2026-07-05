// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LinePayCSharp/LinePayClient.cs
// 所屬區塊：LINE Pay C# 整合封裝，處理付款 API 模型與呼叫。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LinePayClient
// 主要成員：GetPaymentAsync、ReserveAsync、ConfirmAsync、RefundAsync、GetAuthorizationAsync、CaptureAsync、VoidAuthorizationAsync、PreApprovedPayAsync、RegKeyCheckAsync、RegKeyExpireAsync
// 引用命名空間：Line.Pay.Models、Newtonsoft.Json、System、System.Net.Http、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Pay.Models;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Line.Pay
{
    /// <summary>
    /// LINE Pay API client, which handles request/response to LINE Pay server.
    /// </summary>
    public class LinePayClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly bool _disposeClient;
        static private Uri realUri = new Uri("https://api-pay.line.me");
        static private Uri sandboxUri = new Uri("https://sandbox-api-pay.line.me");
        private string version;

        static private JsonSerializerSettings serializerSettings = new JsonSerializerSettings() {
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        /// <summary>
        /// Create LINE Pay client - 使用外部提供的 HttpClient（建議用於 DI 情境）
        /// </summary>
        /// <param name="httpClient">外部提供的 HttpClient 實例（不會被 Dispose）</param>
        /// <param name="channelId">Payment Integration Information - Channel ID</param>
        /// <param name="channelSecret">Payment Integration Information - ChannelSecret Key</param>
        /// <param name="isSandbox">Specify true if sandbox</param>
        /// <param name="version">Specify version</param>
        public LinePayClient(HttpClient httpClient, string channelId, string channelSecret, bool isSandbox = false, string version = "v2")
        {
            if (httpClient == null) { throw new ArgumentNullException(nameof(httpClient)); }
            if (string.IsNullOrEmpty(channelId)) { throw new ArgumentNullException(nameof(channelId)); }
            if (string.IsNullOrEmpty(channelSecret)) { throw new ArgumentNullException(nameof(channelSecret)); }

            this.version = version;
            this.client = httpClient;
            this._disposeClient = false;
            client.BaseAddress = isSandbox ? sandboxUri : realUri;
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-LINE-ChannelId", channelId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-LINE-ChannelSecret", channelSecret);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
        }

        /// <summary>
        /// Create LINE Pay client - 建立內部 HttpClient（向後相容，但不建議用於生產環境）
        /// </summary>
        /// <param name="channelId">Payment Integration Information - Channel ID</param>
        /// <param name="channelSecret">Payment Integration Information - ChannelSecret Key</param>
        /// <param name="isSandbox">Specify true if sandbox</param>
        /// <param name="version">Specify version</param>
        [Obsolete("建議使用接受 HttpClient 參數的建構函式，以避免 Socket 耗盡問題")]
        public LinePayClient(string channelId, string channelSecret, bool isSandbox = false, string version = "v2")
        {
            if (string.IsNullOrEmpty(channelId)) { throw new ArgumentNullException(nameof(channelId)); }
            if (string.IsNullOrEmpty(channelSecret)) { throw new ArgumentNullException(nameof(channelSecret)); }

            this.version = version;
            client = new HttpClient();
            _disposeClient = true;
            client.BaseAddress = isSandbox ? sandboxUri : realUri;
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-LINE-ChannelId", channelId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-LINE-ChannelSecret", channelSecret);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
        }


        #region Message

        /// <summary>
        /// Get Payment Details AP
        /// Gets the details of payments made with LINE Pay. This API only gets the payments that have been captured.
        /// </summary>
        /// <param name="transactionId">A transaction ID issued by LINE Pay, for payment or refund.</param>
        /// <param name="orderId">Merchant Transaction Order ID</param>
        /// <returns>PaymentResponse</returns>
        public async Task<PaymentResponse> GetPaymentAsync(Int64? transactionId, string orderId = null)
        {
            if (transactionId == null && string.IsNullOrEmpty(orderId))
                throw new ArgumentNullException("Either transactionId or orderId has to be specified.");

            var uri = transactionId != null ? $"/{version}/payments?transactionId={transactionId}" : $"/{version}/payments?orderId={orderId}";
            var response = await client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<PaymentResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Reserve Payment API
        /// Prior to processing payments with LINE Pay, the Merchant is evaluated if it is a normal Merchant store then the
        /// information is reserved for payment.When a payment is successfully reserved, the Merchant gets a "transactionId"
        /// that is a key value used until the payment is completed or refunded.
        /// </summary>
        /// <param name="reserve">Reserve Information</param>
        /// <returns>ReserveResponse</returns>
        public async Task<ReserveResponse> ReserveAsync(Reserve reserve)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/{version}/payments/request");
            request.Content = new StringContent(JsonConvert.SerializeObject(reserve, serializerSettings), Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<ReserveResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Payment Confirm API
        /// This API is used for a Merchant to complete its payment. The Merchant must call Confirm Payment API to
        /// actually complete the payment.However, when "capture" parameter is "false" on payment reservation, the
        /// payment status becomes AUTHORIZATION, and the payment is completed only after "Capture API" is called.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="amount">Payment amount</param>
        /// <param name="currency">Payment currency (ISO 4217)</param>
        /// <returns>ConfirmResponse</returns>
        public async Task<ConfirmResponse> ConfirmAsync(Int64 transactionId, int amount, Currency currency)
        {
            var confirm = new Confirm() { Amount = amount, Currency = currency };
            return await ConfirmAsync(transactionId, confirm);
        }

        /// <summary>
        /// Payment Confirm API
        /// This API is used for a Merchant to complete its payment. The Merchant must call Confirm Payment API to
        /// actually complete the payment.However, when "capture" parameter is "false" on payment reservation, the
        /// payment status becomes AUTHORIZATION, and the payment is completed only after "Capture API" is called.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="confirm">Confirm</param>
        /// <returns>ConfirmResponse</returns>
        public async Task<ConfirmResponse> ConfirmAsync(Int64 transactionId, Confirm confirm)
        {
            var response = await client.PostAsync($"/{version}/payments/{transactionId}/confirm", new StringContent(JsonConvert.SerializeObject(confirm, serializerSettings), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<ConfirmResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Refund Payment API
        /// Requests refund of payments made with LINE Pay. To refund a payment, the LINE Pay user's payment transaction
        /// Id must be forwarded.A partial refund is also possible depending on the refund amount.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="refundAmount">Refund Amount</param>
        /// <returns>RefundResponse</returns>
        public async Task<RefundResponse> RefundAsync(Int64 transactionId, int refundAmount)
        {
            var refund = new Refund() { RefundAmount = refundAmount };
            return await RefundAsync(transactionId, refund);
        }
        /// <summary>
        /// Refund Payment API
        /// Requests refund of payments made with LINE Pay. To refund a payment, the LINE Pay user's payment transaction
        /// Id must be forwarded.A partial refund is also possible depending on the refund amount.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="refund">Refund</param>
        /// <returns>RefundResponse</returns>
        public async Task<RefundResponse> RefundAsync(Int64 transactionId, Refund refund)
        {
            var response = await client.PostAsync($"/{version}/payments/{transactionId}/refund", new StringContent(JsonConvert.SerializeObject(refund, serializerSettings), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<RefundResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Get Authorization Details API
        /// Gets the details authorized with LINE Pay. This API only gets data that is authorized or whose authorization is voided;
        /// the one that is already captured can be viewed by using "Get Payment Details API”.
        /// </summary>
        /// <param name="transactionId">Transaction number issued by LINE Pay</param>
        /// <param name="orderId">Order number of Merchant</param>
        /// <returns>AuthorizationResponse</returns>
        public async Task<AuthorizationResponse> GetAuthorizationAsync(Int64? transactionId, string orderId)
        {
            if (transactionId == null && string.IsNullOrEmpty(orderId))
                throw new ArgumentNullException("Either transactionId or orderId has to be specified.");

            var uri = transactionId != null ? $"/{version}/payments/authorizations?transactionId={transactionId}" : $"/{version}/payments/authorizations?orderId={orderId}";

            var response = await client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<AuthorizationResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Capture API
        /// If "capture" is "false" when the Merchant calls the “Reserve Payment API” , the payment is completed only after the Capture API is called.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="amount">Payment amount</param>
        /// <param name="currency">Payment currency </param>
        /// <returns>CaptureResponse</returns>
        public async Task<CaptureResponse> CaptureAsync(Int64 transactionId, int amount, Currency currency)
        {
            var capture = new Capture() { Amount = amount, Currency = currency };
            return await CaptureAsync(transactionId, capture);
        }
        /// <summary>
        /// Capture API
        /// If "capture" is "false" when the Merchant calls the “Reserve Payment API” , the payment is completed only after the Capture API is called.
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <param name="capture">Capture</param>
        /// <returns>CaptureResponse</returns>
        public async Task<CaptureResponse> CaptureAsync(Int64 transactionId, Capture capture)
        {
            var response = await client.PostAsync($"/{version}/payments/authorizations/{transactionId}/capture", new StringContent(JsonConvert.SerializeObject(capture, serializerSettings), Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<CaptureResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Void Authorization API
        /// Voids a previously authorized payment. A payment that has been already captured can be refunded by using the "Refund Payment API”
        /// </summary>
        /// <param name="transactionId">Transaction Id</param>
        /// <returns></returns>
        public async Task<ResponseBase> VoidAuthorizationAsync(Int64 transactionId)
        {
            var response = await client.PostAsync($"/{version}/payments/authorizations/{transactionId}/void", null);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<ResponseBase>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Preapproved Payment API
        /// When the payment type of the Reserve Payment API was set as PREAPPROVED, a regKey is returned with the
        /// payment result.Preapproved Payment API uses this regKey to directly complete a payment without using the LINE app.
        /// </summary>
        /// <param name="regKey">Registration Key</param>
        /// <param name="preApprovedPay">PreApprovedPay</param>
        /// <returns></returns>
        public async Task<PreApprovedPayResponse> PreApprovedPayAsync(string regKey, PreApprovedPay preApprovedPay)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/{version}/payments/preapprovedPay/{regKey}/payment");
            request.Content = new StringContent(JsonConvert.SerializeObject(preApprovedPay, serializerSettings), Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<PreApprovedPayResponse>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Check regKey Status API
        /// Checks if regKey is available before using the preapproved payment API
        /// </summary>
        /// <param name="regKey">Registration Key</param>
        /// <param name="creditCardAuth">Check Authorization for Credit Card minimum amount saved in regKey</param>
        /// <returns></returns>
        public async Task<ResponseBase> RegKeyCheckAsync(string regKey, bool creditCardAuth = false)
        {
            var response = await client.GetAsync($"/{version}/payments/preapprovedPay/{regKey}/check?creditCardAuth={creditCardAuth}");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<ResponseBase>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Expire regKey API
        /// Expires the regKey information registered for preapproved payment.Once the API is called, the regKey is no longer used for preapproved payments.
        /// </summary>
        /// <param name="regKey">Registration Key</param>
        /// <returns></returns>
        public async Task<ResponseBase> RegKeyExpireAsync(string regKey)
        {
            var response = await client.PostAsync($"/{version}/payments/preapprovedPay/{regKey}/expire", null);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<ResponseBase>(await response.Content.ReadAsStringAsync());
            else
                throw new Exception(await response.Content.ReadAsStringAsync());
        }
        #endregion

        /// <summary>
        /// Dispose client.
        /// </summary>
        public void Dispose()
        {
            if (_disposeClient)
            {
                client?.Dispose();
            }
        }
    }
}