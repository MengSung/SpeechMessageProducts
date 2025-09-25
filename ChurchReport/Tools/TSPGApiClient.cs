using ChurchReport.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ChurchReport.Tools
{
    /// <summary>
    /// 高鉅金流 (TSPG) API 客戶端 - 完整 API 實作
    /// </summary>
    public class TSPGApiClient
    {
        #region 配置參數

        private readonly string _storeId;
        private readonly string _storeKey;
        private readonly string _storeIV;
        private readonly string _apiBaseUrl;
        private readonly bool _isTestMode;

        #endregion

        #region 建構函式

        public TSPGApiClient()
        {
            _storeId = GetConfigValue("TSPG_StoreId", "your_store_id");
            _storeKey = GetConfigValue("TSPG_StoreKey", "your_store_key");
            _storeIV = GetConfigValue("TSPG_StoreIV", "your_store_iv");
            _apiBaseUrl = GetConfigValue("TSPG_ApiBaseUrl", "https://www.paymypay.com/api/");
            _isTestMode = bool.TryParse(GetConfigValue("TSPG_TestMode", "true"), out var b) ? b : true;
        }

        public TSPGApiClient(string storeId, string storeKey, string storeIV, string apiBaseUrl, bool isTestMode = true)
        {
            _storeId = storeId;
            _storeKey = storeKey;
            _storeIV = storeIV;
            _apiBaseUrl = apiBaseUrl;
            _isTestMode = isTestMode;
        }

        #endregion

        #region 主要 API 方法

        /// <summary>
        /// 建立付款訂單
        /// </summary>
        /// <param name="request">付款請求</param>
        /// <returns>付款回應</returns>
        public async Task<PayPageResponse> CreatePaymentAsync(TSPGPaymentRequest request)
        {
            try
            {
                var postData = BuildPaymentPostData(request);
                return await PostToTSPGAsync("doPay.php", postData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CreatePaymentAsync Error: {ex.Message}");
                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"建立付款失敗: {ex.Message}",
                    uid = request.OrderId,
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 查詢訂單狀態
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <returns>查詢結果</returns>
        public async Task<PayPageResponse> QueryOrderAsync(string orderId)
        {
            try
            {
                var postData = BuildQueryPostData(orderId);
                return await PostToTSPGAsync("queryOrder.php", postData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"QueryOrderAsync Error: {ex.Message}");
                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"查詢訂單失敗: {ex.Message}",
                    uid = orderId,
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 取消訂單
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <returns>取消結果</returns>
        public async Task<PayPageResponse> CancelOrderAsync(string orderId)
        {
            try
            {
                var postData = BuildCancelPostData(orderId);
                return await PostToTSPGAsync("cancelOrder.php", postData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CancelOrderAsync Error: {ex.Message}");
                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"取消訂單失敗: {ex.Message}",
                    uid = orderId,
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 申請退款
        /// </summary>
        /// <param name="request">退款請求</param>
        /// <returns>退款結果</returns>
        public async Task<PayPageResponse> RefundAsync(TSPGRefundRequest request)
        {
            try
            {
                var postData = BuildRefundPostData(request);
                return await PostToTSPGAsync("refund.php", postData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"RefundAsync Error: {ex.Message}");
                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"申請退款失敗: {ex.Message}",
                    uid = request.OrderId,
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 信用卡請款
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="amount">請款金額 (可選，不提供則請款全額)</param>
        /// <returns>請款結果</returns>
        public async Task<PayPageResponse> CaptureAsync(string orderId, decimal? amount = null)
        {
            try
            {
                var postData = BuildCapturePostData(orderId, amount);
                return await PostToTSPGAsync("capture.php", postData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CaptureAsync Error: {ex.Message}");
                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"信用卡請款失敗: {ex.Message}",
                    uid = orderId,
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 取得交易記錄
        /// </summary>
        /// <param name="startDate">開始日期 (YYYY-MM-DD)</param>
        /// <param name="endDate">結束日期 (YYYY-MM-DD)</param>
        /// <returns>交易記錄</returns>
        public async Task<TSPGTransactionHistoryResponse> GetTransactionHistoryAsync(string startDate, string endDate)
        {
            try
            {
                var postData = BuildTransactionHistoryPostData(startDate, endDate);
                var response = await PostToTSPGAsync("transactionHistory.php", postData);
                
                return new TSPGTransactionHistoryResponse
                {
                    Code = response.code,
                    Message = response.msg,
                    Transactions = ParseTransactionHistory(response.key),
                    StartDate = startDate,
                    EndDate = endDate
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetTransactionHistoryAsync Error: {ex.Message}");
                return new TSPGTransactionHistoryResponse
                {
                    Code = "9999",
                    Message = $"取得交易記錄失敗: {ex.Message}",
                    Transactions = new List<TSPGTransaction>(),
                    StartDate = startDate,
                    EndDate = endDate
                };
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 建構付款 POST 資料
        /// </summary>
        private NameValueCollection BuildPaymentPostData(TSPGPaymentRequest request)
        {
            var postData = new NameValueCollection();

            // 基本參數
            postData["store_uid"] = _storeId;
            postData["order_id"] = request.OrderId;
            postData["cost"] = request.Amount.ToString();
            postData["product_name"] = request.ProductName;
            postData["return_url"] = request.ReturnUrl;
            postData["notify_url"] = request.NotifyUrl;
            postData["currency"] = request.Currency ?? "TWD";
            postData["pay_type"] = request.PaymentType ?? "credit";

            // 可選參數
            if (!string.IsNullOrEmpty(request.UserName))
                postData["user_name"] = request.UserName;
            if (!string.IsNullOrEmpty(request.UserEmail))
                postData["user_email"] = request.UserEmail;
            if (!string.IsNullOrEmpty(request.UserPhone))
                postData["user_phone"] = request.UserPhone;

            // 自訂參數
            if (!string.IsNullOrEmpty(request.Echo0))
                postData["echo_0"] = request.Echo0;
            if (!string.IsNullOrEmpty(request.Echo1))
                postData["echo_1"] = request.Echo1;
            if (!string.IsNullOrEmpty(request.Echo2))
                postData["echo_2"] = request.Echo2;
            if (!string.IsNullOrEmpty(request.Echo3))
                postData["echo_3"] = request.Echo3;
            if (!string.IsNullOrEmpty(request.Echo4))
                postData["echo_4"] = request.Echo4;

            // 產生檢查碼
            string hash = GenerateHash(postData);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 建構查詢 POST 資料
        /// </summary>
        private NameValueCollection BuildQueryPostData(string orderId)
        {
            var postData = new NameValueCollection();
            postData["store_uid"] = _storeId;
            postData["order_id"] = orderId;

            string hash = GenerateQueryHash(orderId);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 建構取消 POST 資料
        /// </summary>
        private NameValueCollection BuildCancelPostData(string orderId)
        {
            var postData = new NameValueCollection();
            postData["store_uid"] = _storeId;
            postData["order_id"] = orderId;
            postData["action"] = "cancel";

            string hash = GenerateCancelHash(orderId);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 建構退款 POST 資料
        /// </summary>
        private NameValueCollection BuildRefundPostData(TSPGRefundRequest request)
        {
            var postData = new NameValueCollection();
            postData["store_uid"] = _storeId;
            postData["order_id"] = request.OrderId;
            postData["action"] = "refund";
            
            if (request.RefundAmount.HasValue)
                postData["refund_amount"] = request.RefundAmount.Value.ToString();
            
            if (!string.IsNullOrEmpty(request.Reason))
                postData["reason"] = request.Reason;

            string hash = GenerateRefundHash(request);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 建構請款 POST 資料
        /// </summary>
        private NameValueCollection BuildCapturePostData(string orderId, decimal? amount)
        {
            var postData = new NameValueCollection();
            postData["store_uid"] = _storeId;
            postData["order_id"] = orderId;
            postData["action"] = "capture";
            
            if (amount.HasValue)
                postData["capture_amount"] = amount.Value.ToString();

            string hash = GenerateCaptureHash(orderId, amount);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 建構交易記錄查詢 POST 資料
        /// </summary>
        private NameValueCollection BuildTransactionHistoryPostData(string startDate, string endDate)
        {
            var postData = new NameValueCollection();
            postData["store_uid"] = _storeId;
            postData["start_date"] = startDate;
            postData["end_date"] = endDate;

            string hash = GenerateTransactionHistoryHash(startDate, endDate);
            postData["hash"] = hash;

            return postData;
        }

        /// <summary>
        /// 發送 POST 請求到 TSPG API
        /// </summary>
        private async Task<PayPageResponse> PostToTSPGAsync(string endpoint, NameValueCollection postData)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport-TSPG/1.0";

                    string url = _apiBaseUrl.TrimEnd('/') + "/" + endpoint;
                    byte[] responseBytes = await client.UploadValuesTaskAsync(url, "POST", postData);
                    string responseString = Encoding.UTF8.GetString(responseBytes);

                    return ParseResponse(responseString);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"PostToTSPGAsync Error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 解析 API 回應
        /// </summary>
        private PayPageResponse ParseResponse(string responseString)
        {
            try
            {
                // 嘗試解析 JSON 回應
                if (responseString.StartsWith("{"))
                {
                    return JsonConvert.DeserializeObject<PayPageResponse>(responseString);
                }

                // 解析查詢字串格式的回應
                var response = new PayPageResponse();
                var queryParams = System.Web.HttpUtility.ParseQueryString(responseString);

                response.code = queryParams["code"] ?? "9999";
                response.msg = queryParams["msg"] ?? "未知錯誤";
                response.uid = queryParams["uid"] ?? "";
                response.key = queryParams["key"] ?? "";
                response.url = queryParams["url"] ?? "";

                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ParseResponse Error: {ex.Message}");

                return new PayPageResponse
                {
                    code = "9999",
                    msg = $"回應解析錯誤: {ex.Message}",
                    uid = "",
                    key = "",
                    url = ""
                };
            }
        }

        /// <summary>
        /// 產生付款檢查碼
        /// </summary>
        private string GenerateHash(NameValueCollection postData)
        {
            string storeUid = postData["store_uid"] ?? "";
            string orderId = postData["order_id"] ?? "";
            string cost = postData["cost"] ?? "";

            string hashString = $"{_storeKey}{storeUid}{orderId}{cost}{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 產生查詢檢查碼
        /// </summary>
        private string GenerateQueryHash(string orderId)
        {
            string hashString = $"{_storeKey}{_storeId}{orderId}{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 產生取消檢查碼
        /// </summary>
        private string GenerateCancelHash(string orderId)
        {
            string hashString = $"{_storeKey}{_storeId}{orderId}cancel{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 產生退款檢查碼
        /// </summary>
        private string GenerateRefundHash(TSPGRefundRequest request)
        {
            string refundAmount = request.RefundAmount?.ToString() ?? "";
            string hashString = $"{_storeKey}{_storeId}{request.OrderId}refund{refundAmount}{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 產生請款檢查碼
        /// </summary>
        private string GenerateCaptureHash(string orderId, decimal? amount)
        {
            string captureAmount = amount?.ToString() ?? "";
            string hashString = $"{_storeKey}{_storeId}{orderId}capture{captureAmount}{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 產生交易記錄查詢檢查碼
        /// </summary>
        private string GenerateTransactionHistoryHash(string startDate, string endDate)
        {
            string hashString = $"{_storeKey}{_storeId}{startDate}{endDate}{_storeIV}";

            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 解析交易記錄
        /// </summary>
        private List<TSPGTransaction> ParseTransactionHistory(string data)
        {
            var transactions = new List<TSPGTransaction>();
            
            if (string.IsNullOrEmpty(data))
                return transactions;

            try
            {
                // 實作交易記錄解析邏輯
                var lines = data.Split('\n');
                
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var fields = line.Split(',');
                    if (fields.Length >= 6)
                    {
                        transactions.Add(new TSPGTransaction
                        {
                            OrderId = fields[0],
                            TransactionId = fields[1],
                            Amount = decimal.TryParse(fields[2], out var amount) ? amount : 0,
                            Status = fields[3],
                            TransactionDate = DateTime.TryParse(fields[4], out var date) ? date : DateTime.MinValue,
                            PayType = fields[5]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ParseTransactionHistory Error: {ex.Message}");
            }

            return transactions;
        }

        /// <summary>
        /// 取得設定值
        /// </summary>
        private string GetConfigValue(string key, string defaultValue)
        {
            try
            {
                var basePath = Directory.GetCurrentDirectory();
                var config = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .Build();

                // 優先讀取精確鍵，或支援 TSPG:Key 的區段格式
                var value = config[key] ?? config[$"TSPG:{key}"] ?? Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion

        #region 驗證方法

        /// <summary>
        /// 驗證回傳資料的檢查碼
        /// </summary>
        /// <param name="returnData">回傳資料</param>
        /// <returns>驗證結果</returns>
        public bool VerifyReturnHash(MyPayReturnModel returnData)
        {
            try
            {
                string hashString = $"{_storeKey}{returnData.transaction_id}{returnData.order_id}{returnData.state}{_storeIV}";

                using (SHA256 sha256Hash = SHA256.Create())
                {
                    byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        builder.Append(bytes[i].ToString("x2"));
                    }
                    string calculatedHash = builder.ToString().ToUpper();

                    return calculatedHash == returnData.hash?.ToUpper();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"VerifyReturnHash Error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}