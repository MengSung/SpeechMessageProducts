using ChurchReport.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ChurchReport.Tools
{
    /// <summary>
    /// 高鉅金流(TSPG)靜態工具類別 - 參考 MyPayToolkit 架構
    /// 提供標準化的金流服務接口實作
    /// </summary>
    public static class TspgToolkit
    {
        #region 私有成員與配置
        private static readonly ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        private static readonly string _currentVersion = "1.0.0";

        // 測試環境參數 (仍保留，可從組態覆蓋)
        private static readonly string _testApiRoot = GetConfigValue("TSPG:TestApiRoot", "https://tspg-t.taishinbank.com.tw/tspgapi/restapi");
        private static readonly string _testTerminalId = GetConfigValue("TSPG:TestTerminalId", "T0000000");
        private static readonly string _testMerchant3D = GetConfigValue("TSPG:TestMerchant3D", "999812777000198");
        private static readonly string _testMerchantNo3D = GetConfigValue("TSPG:TestMerchantNo3D", "999812777000199");
        #endregion

        #region 公開 API 方法 (對應永豐金流介面)

        /// <summary>
        /// 建立付款訂單 (對應 OrderCreate) - 支援 TSPG REST API v2.14
        /// </summary>
        /// <param name="request">付款請求參數</param>
        /// <returns>付款回應</returns>
        public static PayPageResponse OrderCreate(TSPGPaymentRequest request)
        {
            try
            {
                // 使用新的 REST API v2.14 格式
                var jsonData = BuildPaymentPostData(request);
                return PostToTSPG("auth.ashx", jsonData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderCreate Error: {ex.Message}");
                return CreateErrorResponse(request?.Params?.OrderNo, $"建立付款失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立付款訂單 (測試環境專用) — 依照申請單 API_ROOT / 特店代號 / 端末代號 呼叫。
        /// 兩組特店代號：啟用 3D / 不啟用 3D，透過 enable3D 參數切換。
        /// 相關設定改由 appsettings.json: TSPG:TestApiRoot / TestMerchant3D / TestMerchantNo3D / TestTerminalId
        /// </summary>
        /// <param name="request">付款請求</param>
        /// <param name="enable3D">true=使用啟用3D之特店代號，false=使用不啟用3D之特店代號</param>
        /// <returns>付款回應</returns>
        public static PayPageResponse OrderCreateTest(TSPGPaymentRequest request, bool enable3D)
        {
            try
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                if (request.Params == null) throw new ArgumentException("request.Params 不可為空", nameof(request));

                // 指定測試用特店與端末代號 (由組態取得)
                request.Mid = enable3D ? _testMerchant3D : _testMerchantNo3D;
                request.Tid = _testTerminalId;

                var jsonData = BuildPaymentPostData(request);
                return PostToTSPGWithBaseUrl(_testApiRoot, "auth.ashx", jsonData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderCreateTest Error: {ex.Message}");
                return CreateErrorResponse(request?.Params?.OrderNo, $"建立測試付款失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 查詢訂單狀態 (對應 OrderQuery) - 支援 TSPG REST API v2.14
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <returns>查詢結果</returns>
        public static PayPageResponse OrderQuery(string orderId)
        {
            try
            {
                var jsonData = BuildQueryJsonData(orderId);
                return PostToTSPG("query.ashx", jsonData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderQuery Error: {ex.Message}");
                return CreateErrorResponse(orderId, $"查詢訂單失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 訂單維護操作 (對應 OrderMaintain) - 支援 TSPG REST API v2.14
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="action">操作類型 (cancel/refund/capture)</param>
        /// <param name="amount">金額 (可選)</param>
        /// <param name="reason">原因 (可選)</param>
        /// <returns>操作結果</returns>
        public static PayPageResponse OrderMaintain(string orderId, string action, decimal? amount = null, string reason = null)
        {
            try
            {
                var jsonData = BuildMaintainJsonData(orderId, action, amount, reason);
                string endpoint = GetEndpointByAction(action);
                return PostToTSPG(endpoint, jsonData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderMaintain Error: {ex.Message}");
                return CreateErrorResponse(orderId, $"訂單維護失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消訂單
        /// </summary>
        public static PayPageResponse CancelOrder(string orderId) => OrderMaintain(orderId, "cancel");

        /// <summary>
        /// 申請退款
        /// </summary>
        public static PayPageResponse RefundOrder(TSPGRefundRequest request) => OrderMaintain(request.OrderId, "refund", request.RefundAmount, request.Reason);

        /// <summary>
        /// 信用卡請款
        /// </summary>
        public static PayPageResponse CaptureOrder(string orderId, decimal? amount = null) => OrderMaintain(orderId, "capture", amount);

        /// <summary>
        /// 查詢交易記錄
        /// </summary>
        public static TSPGTransactionHistoryResponse GetTransactionHistory(string startDate, string endDate)
        {
            try
            {
                var jsonData = BuildTransactionHistoryJsonData(startDate, endDate);
                var response = PostToTSPG("history.ashx", jsonData);
                
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
                System.Diagnostics.Trace.WriteLine($"[TSPG] GetTransactionHistory Error: {ex.Message}");
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

        /// <summary>
        /// 驗證回傳資料的檢查碼
        /// </summary>
        public static bool VerifyReturnHash(MyPayReturnModel returnData)
        {
            try
            {
                string storeKey = GetConfigValue("TSPG:StoreKey", "");
                string storeIV = GetConfigValue("TSPG:StoreIV", "");
                string hashString = $"{storeKey}{returnData.transaction_id}{returnData.order_id}{returnData.state}{storeIV}";
                string calculatedHash = CalculateSHA256Hash(hashString);
                return string.Equals(calculatedHash, returnData.hash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] VerifyReturnHash Error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 私有輔助方法

        private static string BuildPaymentPostData(TSPGPaymentRequest request)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            request.Mid = string.IsNullOrEmpty(request.Mid) ? storeId : request.Mid;
            request.Tid = string.IsNullOrEmpty(request.Tid) ? GetConfigValue("TSPG:TerminalId", "T0000000") : request.Tid;
            if (request.Params == null)
                request.Params = new TSPGPaymentParams();
            if (string.IsNullOrEmpty(request.Params.Layout))
                request.Params.Layout = "1";
            if (string.IsNullOrEmpty(request.Params.Cur))
                request.Params.Cur = "NTD";
            if (string.IsNullOrEmpty(request.Params.CaptFlag))
                request.Params.CaptFlag = "0";
            if (string.IsNullOrEmpty(request.Params.ResultFlag))
                request.Params.ResultFlag = "1";
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            return JsonConvert.SerializeObject(request, jsonSettings);
        }

        private static string BuildQueryJsonData(string orderId)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            return JsonConvert.SerializeObject(new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = 7,
                @params = new { order_no = orderId }
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static string BuildMaintainJsonData(string orderId, string action, decimal? amount, string reason)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            int txType = GetTransactionTypeByAction(action);
            return JsonConvert.SerializeObject(new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = txType,
                @params = new { order_no = orderId, amt = amount?.ToString("F2"), reason = reason }
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static int GetTransactionTypeByAction(string action)
        {
            switch (action.ToLower())
            {
                case "cancel": return 8;
                case "refund": return 5;
                case "capture": return 3;
                default: return 7;
            }
        }

        // 新增遺漏的方法: 根據操作取得端點
        private static string GetEndpointByAction(string action)
        {
            switch (action.ToLower())
            {
                case "cancel": return "cancel.ashx";
                case "refund": return "refund.ashx";
                case "capture": return "capture.ashx";
                case "query": return "query.ashx";
                default: return "maintain.ashx";
            }
        }
        
        private static NameValueCollection BuildQueryPostData(string orderId)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            var postData = new NameValueCollection { ["store_uid"] = storeId, ["order_id"] = orderId };
            postData["hash"] = GenerateQueryHash(orderId);
            return postData;
        }

        private static NameValueCollection BuildMaintainPostData(string orderId, string action, decimal? amount, string reason)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            var postData = new NameValueCollection { ["store_uid"] = storeId, ["order_id"] = orderId, ["action"] = action };
            if (amount.HasValue)
            {
                string amountKey = action == "refund" ? "refund_amount" : "capture_amount";
                postData[amountKey] = amount.Value.ToString();
            }
            if (!string.IsNullOrEmpty(reason)) postData["reason"] = reason;
            postData["hash"] = GenerateMaintainHash(orderId, action, amount);
            return postData;
        }

        private static string BuildTransactionHistoryJsonData(string startDate, string endDate)
        {
            string storeId = GetConfigValue("TSPG:StoreId", "");
            return JsonConvert.SerializeObject(new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = 7,
                @params = new { start_date = startDate, end_date = endDate }
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static PayPageResponse PostToTSPG(string endpoint, object postData)
        {
            string apiBase = GetConfigValue("TSPG:ApiBaseUrl", "");
            return PostToTSPGWithBaseUrl(apiBase, endpoint, postData);
        }

        private static PayPageResponse PostToTSPGWithBaseUrl(string baseUrl, string endpoint, object postData)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                using (var client = new WebClient())
                {
                    string root = baseUrl.TrimEnd('/');
                    string url = root + "/" + endpoint.TrimStart('/');
                    if (postData is string jsonData)
                    {
                        client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport-TSPG/2.14";
                        byte[] responseBytes = client.UploadData(url, "POST", Encoding.UTF8.GetBytes(jsonData));
                        string responseString = Encoding.UTF8.GetString(responseBytes);
                        return ParseTSPGResponse(responseString);
                    }
                    else if (postData is NameValueCollection formData)
                    {
                        client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                        client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport-TSPG/1.0";
                        byte[] responseBytes = client.UploadValues(url, "POST", formData);
                        string responseString = Encoding.UTF8.GetString(responseBytes);
                        return ParseResponse(responseString);
                    }
                    else
                    {
                        throw new ArgumentException("Unsupported postData type");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] PostToTSPG Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = ex.Message };
            }
        }

        private static PayPageResponse ParseTSPGResponse(string responseString)
        {
            try
            {
                if (string.IsNullOrEmpty(responseString)) return new PayPageResponse { code = "9999", msg = "空白回應" };
                if (responseString.StartsWith("{"))
                {
                    var tspgResponse = JsonConvert.DeserializeObject<TSPGApiResponse>(responseString);
                    return new PayPageResponse
                    {
                        code = tspgResponse?.ret_code ?? "9999",
                        msg = tspgResponse?.ret_msg ?? "未知錯誤",
                        uid = tspgResponse?.Mid ?? string.Empty,
                        key = tspgResponse?.Tid ?? string.Empty,
                        url = tspgResponse?.Params?.hpp_url ?? string.Empty
                    };
                }
                return ParseResponse(responseString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTSPGResponse Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"回應解析錯誤: {ex.Message}" };
            }
        }

        private static PayPageResponse ParseResponse(string responseString)
        {
            try
            {
                if (string.IsNullOrEmpty(responseString)) return new PayPageResponse { code = "9999", msg = "空白回應" };
                if (responseString.StartsWith("{")) return JsonConvert.DeserializeObject<PayPageResponse>(responseString);
                var queryParams = HttpUtility.ParseQueryString(responseString);
                return new PayPageResponse
                {
                    code = queryParams["code"] ?? "9999",
                    msg = queryParams["msg"] ?? "未知錯誤",
                    uid = queryParams["uid"] ?? string.Empty,
                    key = queryParams["key"] ?? string.Empty,
                    url = queryParams["url"] ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseResponse Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"回應解析錯誤: {ex.Message}" };
            }
        }

        private static string GeneratePaymentHash(NameValueCollection postData)
        {
            string storeKey = GetConfigValue("TSPG:StoreKey", "");
            string storeIV = GetConfigValue("TSPG:StoreIV", "");
            string hashString = $"{storeKey}{postData["store_uid"]}{postData["order_id"]}{postData["cost"]}{storeIV}";
            return CalculateSHA256Hash(hashString);
        }
        private static string GenerateQueryHash(string orderId)
        {
            string storeKey = GetConfigValue("TSPG:StoreKey", "");
            string storeId = GetConfigValue("TSPG:StoreId", "");
            string storeIV = GetConfigValue("TSPG:StoreIV", "");
            string hashString = $"{storeKey}{storeId}{orderId}{storeIV}";
            return CalculateSHA256Hash(hashString);
        }
        private static string GenerateMaintainHash(string orderId, string action, decimal? amount)
        {
            string storeKey = GetConfigValue("TSPG:StoreKey", "");
            string storeId = GetConfigValue("TSPG:StoreId", "");
            string storeIV = GetConfigValue("TSPG:StoreIV", "");
            string amountStr = amount?.ToString() ?? string.Empty;
            string hashString = $"{storeKey}{storeId}{orderId}{action}{amountStr}{storeIV}";
            return CalculateSHA256Hash(hashString);
        }
        private static string GenerateTransactionHistoryHash(string startDate, string endDate)
        {
            string storeKey = GetConfigValue("TSPG:StoreKey", "");
            string storeId = GetConfigValue("TSPG:StoreId", "");
            string storeIV = GetConfigValue("TSPG:StoreIV", "");
            string hashString = $"{storeKey}{storeId}{startDate}{endDate}{storeIV}";
            return CalculateSHA256Hash(hashString);
        }
        private static string CalculateSHA256Hash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                var builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString().ToUpper();
            }
        }

        private static List<TSPGTransaction> ParseTransactionHistory(string data)
        {
            var transactions = new List<TSPGTransaction>();
            if (string.IsNullOrWhiteSpace(data)) return transactions;
            try
            {
                var lines = data.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
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
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTransactionHistory Error: {ex.Message}");
            }
            return transactions;
        }

        private static PayPageResponse CreateErrorResponse(string orderId, string message) => new PayPageResponse { code = "9999", msg = message, uid = orderId ?? string.Empty, key = string.Empty, url = string.Empty };

        private static string GetConfigValue(string key, string defaultValue)
        {
            try
            {
                var value = m_Configuration[key] ?? Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch { return defaultValue; }
        }

        #endregion
    }
}