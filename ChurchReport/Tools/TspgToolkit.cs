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

        // TSPG 金流配置參數
        private static readonly string _storeId = GetConfigValue("TSPG:StoreId", "your_store_id");
        private static readonly string _storeKey = GetConfigValue("TSPG:StoreKey", "your_store_key");  
        private static readonly string _storeIV = GetConfigValue("TSPG:StoreIV", "your_store_iv");
        private static readonly string _apiBaseUrl = GetConfigValue("TSPG:ApiBaseUrl", "https://www.paymypay.com/api/");
        private static readonly bool _isTestMode = bool.TryParse(GetConfigValue("TSPG:TestMode", "true"), out var testMode) ? testMode : true;
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
        /// <param name="orderId">訂單編號</param>
        /// <returns>取消結果</returns>
        public static PayPageResponse CancelOrder(string orderId)
        {
            return OrderMaintain(orderId, "cancel");
        }

        /// <summary>
        /// 申請退款
        /// </summary>
        /// <param name="request">退款請求</param>
        /// <returns>退款結果</returns>
        public static PayPageResponse RefundOrder(TSPGRefundRequest request)
        {
            return OrderMaintain(request.OrderId, "refund", request.RefundAmount, request.Reason);
        }

        /// <summary>
        /// 信用卡請款
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="amount">請款金額 (可選)</param>
        /// <returns>請款結果</returns>
        public static PayPageResponse CaptureOrder(string orderId, decimal? amount = null)
        {
            return OrderMaintain(orderId, "capture", amount);
        }

        /// <summary>
        /// 查詢交易記錄
        /// </summary>
        /// <param name="startDate">開始日期</param>
        /// <param name="endDate">結束日期</param>
        /// <returns>交易記錄回應</returns>
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
        /// <param name="returnData">回傳資料</param>
        /// <returns>驗證結果</returns>
        public static bool VerifyReturnHash(MyPayReturnModel returnData)
        {
            try
            {
                string hashString = $"{_storeKey}{returnData.transaction_id}{returnData.order_id}{returnData.state}{_storeIV}";
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

        /// <summary>
        /// 建構付款 POST 資料 - 使用 TSPG REST API v2.14 格式
        /// </summary>
        private static string BuildPaymentPostData(TSPGPaymentRequest request)
        {
            // 確保基本配置
            request.Mid = request.Mid ?? _storeId;
            request.Tid = request.Tid ?? GetConfigValue("TSPG:TerminalId", "T0000000");
            
            // 確保 Params 不為 null
            if (request.Params == null)
                request.Params = new TSPGPaymentParams();

            // 設定預設值
            if (string.IsNullOrEmpty(request.Params.Layout))
                request.Params.Layout = "1";
            if (string.IsNullOrEmpty(request.Params.Cur))
                request.Params.Cur = "NTD";
            if (string.IsNullOrEmpty(request.Params.CaptFlag))
                request.Params.CaptFlag = "0";
            if (string.IsNullOrEmpty(request.Params.ResultFlag))
                request.Params.ResultFlag = "1";

            // 序列化為 JSON
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            return JsonConvert.SerializeObject(request, jsonSettings);
        }

        /// <summary>
        /// 建構查詢 POST 資料 - 支援 REST API v2.14
        /// </summary>
        private static string BuildQueryJsonData(string orderId)
        {
            var queryRequest = new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = _storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = 7, // 查詢
                @params = new
                {
                    order_no = orderId
                }
            };

            return JsonConvert.SerializeObject(queryRequest, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// 建構維護操作 POST 資料 - 支援 REST API v2.14
        /// </summary>
        private static string BuildMaintainJsonData(string orderId, string action, decimal? amount, string reason)
        {
            int txType = GetTransactionTypeByAction(action);
            
            var maintainRequest = new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = _storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = txType,
                @params = new
                {
                    order_no = orderId,
                    amt = amount?.ToString("F2"),
                    reason = reason
                }
            };

            return JsonConvert.SerializeObject(maintainRequest, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// 根據操作類型取得交易類型代碼
        /// </summary>
        private static int GetTransactionTypeByAction(string action)
        {
            switch (action.ToLower())
            {
                case "cancel":
                    return 8; // 取消授權
                case "refund":
                    return 5; // 退貨
                case "capture":
                    return 3; // 請款
                default:
                    return 7; // 查詢
            }
        }

        /// <summary>
        /// 建構查詢 POST 資料
        /// </summary>
        private static NameValueCollection BuildQueryPostData(string orderId)
        {
            var postData = new NameValueCollection
            {
                ["store_uid"] = _storeId,
                ["order_id"] = orderId
            };
            postData["hash"] = GenerateQueryHash(orderId);
            return postData;
        }

        /// <summary>
        /// 建構維護操作 POST 資料
        /// </summary>
        private static NameValueCollection BuildMaintainPostData(string orderId, string action, decimal? amount, string reason)
        {
            var postData = new NameValueCollection
            {
                ["store_uid"] = _storeId,
                ["order_id"] = orderId,
                ["action"] = action
            };

            if (amount.HasValue)
            {
                string amountKey = action == "refund" ? "refund_amount" : "capture_amount";
                postData[amountKey] = amount.Value.ToString();
            }

            if (!string.IsNullOrEmpty(reason))
                postData["reason"] = reason;

            postData["hash"] = GenerateMaintainHash(orderId, action, amount);
            return postData;
        }

        /// <summary>
        /// 建構交易記錄查詢 JSON 資料 - REST API v2.14
        /// </summary>
        private static string BuildTransactionHistoryJsonData(string startDate, string endDate)
        {
            var historyRequest = new
            {
                sender = "rest",
                ver = "1.0.0",
                mid = _storeId,
                tid = GetConfigValue("TSPG:TerminalId", "T0000000"),
                pay_type = 1,
                tx_type = 7, // 查詢
                @params = new
                {
                    start_date = startDate,
                    end_date = endDate
                }
            };

            return JsonConvert.SerializeObject(historyRequest, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// 發送 POST 請求到 TSPG - 支援 REST API v2.14 JSON 格式
        /// </summary>
        private static PayPageResponse PostToTSPG(string endpoint, object postData)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            
            try
            {
                using (var client = new WebClient())
                {
                    string url = _apiBaseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
                    
                    if (postData is string jsonData)
                    {
                        // JSON 格式請求 (REST API v2.14)
                        client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport-TSPG/2.14";
                        
                        byte[] responseBytes = client.UploadData(url, "POST", Encoding.UTF8.GetBytes(jsonData));
                        string responseString = Encoding.UTF8.GetString(responseBytes);
                        
                        return ParseTSPGResponse(responseString);
                    }
                    else if (postData is NameValueCollection formData)
                    {
                        // 舊格式 form 請求 (向下相容)
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

        /// <summary>
        /// 解析 TSPG REST API v2.14 JSON 回應
        /// </summary>
        private static PayPageResponse ParseTSPGResponse(string responseString)
        {
            try
            {
                if (string.IsNullOrEmpty(responseString))
                    return new PayPageResponse { code = "9999", msg = "空白回應" };

                // 嘗試解析 REST API v2.14 JSON 回應
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

                // 回退到舊格式解析
                return ParseResponse(responseString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTSPGResponse Error: {ex.Message}");
                return new PayPageResponse { code = "9999", msg = $"回應解析錯誤: {ex.Message}" };
            }
        }

        /// <summary>
        /// 解析 API 回應
        /// </summary>
        private static PayPageResponse ParseResponse(string responseString)
        {
            try
            {
                if (string.IsNullOrEmpty(responseString))
                    return new PayPageResponse { code = "9999", msg = "空白回應" };

                // 嘗試解析 JSON 回應
                if (responseString.StartsWith("{"))
                {
                    return JsonConvert.DeserializeObject<PayPageResponse>(responseString);
                }

                // 解析查詢字串格式的回應
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

        /// <summary>
        /// 根據操作類型取得對應的端點 - REST API v2.14
        /// </summary>
        private static string GetEndpointByAction(string action)
        {
            switch (action.ToLower())
            {
                case "cancel":
                    return "cancel.ashx"; // 取消授權
                case "refund":
                    return "refund.ashx"; // 退貨
                case "capture":
                    return "capture.ashx"; // 請款
                case "query":
                    return "query.ashx"; // 查詢
                default:
                    return "maintain.ashx"; // 通用維護
            }
        }

        /// <summary>
        /// 產生付款檢查碼
        /// </summary>
        private static string GeneratePaymentHash(NameValueCollection postData)
        {
            string hashString = $"{_storeKey}{postData["store_uid"]}{postData["order_id"]}{postData["cost"]}{_storeIV}";
            return CalculateSHA256Hash(hashString);
        }

        /// <summary>
        /// 產生查詢檢查碼
        /// </summary>
        private static string GenerateQueryHash(string orderId)
        {
            string hashString = $"{_storeKey}{_storeId}{orderId}{_storeIV}";
            return CalculateSHA256Hash(hashString);
        }

        /// <summary>
        /// 產生維護操作檢查碼
        /// </summary>
        private static string GenerateMaintainHash(string orderId, string action, decimal? amount)
        {
            string amountStr = amount?.ToString() ?? string.Empty;
            string hashString = $"{_storeKey}{_storeId}{orderId}{action}{amountStr}{_storeIV}";
            return CalculateSHA256Hash(hashString);
        }

        /// <summary>
        /// 產生交易記錄查詢檢查碼
        /// </summary>
        private static string GenerateTransactionHistoryHash(string startDate, string endDate)
        {
            string hashString = $"{_storeKey}{_storeId}{startDate}{endDate}{_storeIV}";
            return CalculateSHA256Hash(hashString);
        }

        /// <summary>
        /// 計算 SHA256 檢查碼
        /// </summary>
        private static string CalculateSHA256Hash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                var builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString().ToUpper();
            }
        }

        /// <summary>
        /// 解析交易記錄
        /// </summary>
        private static List<TSPGTransaction> ParseTransactionHistory(string data)
        {
            var transactions = new List<TSPGTransaction>();
            
            if (string.IsNullOrWhiteSpace(data))
                return transactions;

            try
            {
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
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTransactionHistory Error: {ex.Message}");
            }

            return transactions;
        }

        /// <summary>
        /// 建立錯誤回應
        /// </summary>
        private static PayPageResponse CreateErrorResponse(string orderId, string message)
        {
            return new PayPageResponse
            {
                code = "9999",
                msg = message,
                uid = orderId ?? string.Empty,
                key = string.Empty,
                url = string.Empty
            };
        }

        /// <summary>
        /// 取得設定值
        /// </summary>
        private static string GetConfigValue(string key, string defaultValue)
        {
            try
            {
                var value = m_Configuration[key] ?? Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion
    }
}