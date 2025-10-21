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
                //return PostToTSPGWithBaseUrl(_testApiRoot, "auth.ashx", jsonData);
                return PostToTSPGWithHttpWebRequest(_testApiRoot, "auth.ashx", jsonData);
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
                return PostToTSPG("other.ashx", jsonData);
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

        /// <summary>
        /// 發送 POST 請求到 TSPG - 增強版，包含重試機制和完整錯誤處理
        /// </summary>
        /// <param name="endpoint">API 端點</param>
        /// <param name="postData">POST 資料</param>
        /// <param name="maxRetries">最大重試次數</param>
        /// <returns>TSPG 回應</returns>
        private static PayPageResponse PostToTSPG(string endpoint, object postData, int maxRetries = 3)
        {
            string apiBase = GetConfigValue("TSPG:ApiBaseUrl", "");
            if (string.IsNullOrEmpty(apiBase))
            {
                System.Diagnostics.Trace.WriteLine("[TSPG] 錯誤: ApiBaseUrl 設定為空");
                return CreateErrorResponse("", "ApiBaseUrl 設定不正確");
            }
            return PostToTSPGWithBaseUrl(apiBase, endpoint, postData, maxRetries);
        }

        /// <summary>
        /// 使用指定 BaseUrl 發送 POST 請求到 TSPG - 增強版
        /// 包含重試機制、完整錯誤處理、連線管理和詳細日志
        /// </summary>
        /// <param name="baseUrl">API 基礎 URL</param>
        /// <param name="endpoint">API 端點</param>
        /// <param name="postData">POST 資料 (JSON 字串或 NameValueCollection)</param>
        /// <param name="maxRetries">最大重試次數</param>
        /// <param name="timeoutSeconds">請求超時時間(秒)</param>
        /// <returns>TSPG 回應</returns>
        private static PayPageResponse PostToTSPGWithBaseUrl(string baseUrl, string endpoint, object postData, int maxRetries = 3, int timeoutSeconds = 30)
        {
            // 參數驗證
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return CreateErrorResponse("", "BaseUrl 不可為空");
            }
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return CreateErrorResponse("", "Endpoint 不可為空");
            }
            if (postData == null)
            {
                return CreateErrorResponse("", "PostData 不可為空");
            }

            // 設定 SSL/TLS 協定
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            
            // 建構完整 URL
            string root = baseUrl.TrimEnd('/');
            string cleanEndpoint = endpoint.TrimStart('/');
            string fullUrl = $"{root}/{cleanEndpoint}";

            // 記錄請求開始
            System.Diagnostics.Trace.WriteLine($"[TSPG] 開始 POST 請求: {fullUrl}");
            System.Diagnostics.Trace.WriteLine($"[TSPG] 最大重試次數: {maxRetries}, 超時時間: {timeoutSeconds}秒");

            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        // 重試前等待 (指數退避)
                        int delayMs = Math.Min(1000 * (int)Math.Pow(2, attempt - 2), 10000); // 最多等待 10 秒
                        System.Diagnostics.Trace.WriteLine($"[TSPG] 第 {attempt} 次嘗試，等待 {delayMs}ms...");
                        System.Threading.Thread.Sleep(delayMs);
                    }

                    System.Diagnostics.Trace.WriteLine($"[TSPG] 嘗試第 {attempt} 次請求...");

                    using (var client = new WebClient())
                    {
                        // 設定基本標頭和超時
                        client.Headers[HttpRequestHeader.UserAgent] = "ChurchReport-TSPG/2.14-Enhanced";
                        client.Headers[HttpRequestHeader.Accept] = "application/json, text/plain, */*";
                        client.Headers[HttpRequestHeader.AcceptEncoding] = "gzip, deflate";
                        client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                        client.Headers[HttpRequestHeader.Pragma] = "no-cache";
                        client.Headers["X-Request-ID"] = Guid.NewGuid().ToString("N"); // 請求追蹤 ID

                        // 設定超時 (WebClient 沒有直接的超時設定，需要使用 ServicePoint)
                        var servicePoint = ServicePointManager.FindServicePoint(new Uri(fullUrl));
                        servicePoint.ConnectionLeaseTimeout = timeoutSeconds * 1000;
                        servicePoint.MaxIdleTime = timeoutSeconds * 1000;

                        byte[] responseBytes = null;
                        string responseString = "";

                        if (postData is string jsonData)
                        {
                            // JSON 格式請求
                            client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                            
                            System.Diagnostics.Trace.WriteLine($"[TSPG] 發送 JSON 資料長度: {jsonData.Length} 字元");
                            if (jsonData.Length < 1000) // 只記錄較短的請求內容
                            {
                                System.Diagnostics.Trace.WriteLine($"[TSPG] JSON 內容: {jsonData}");
                            }

                            responseBytes = client.UploadData(fullUrl, "POST", Encoding.UTF8.GetBytes(jsonData));
                        }
                        else if (postData is NameValueCollection formData)
                        {
                            // Form 格式請求 (向下相容)
                            client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded; charset=utf-8";
                            
                            System.Diagnostics.Trace.WriteLine($"[TSPG] 發送 Form 資料欄位數: {formData.Count}");
                            
                            responseBytes = client.UploadValues(fullUrl, "POST", formData);
                        }
                        else
                        {
                            throw new ArgumentException($"不支援的 PostData 類型: {postData.GetType().Name}");
                        }

                        // 處理回應
                        responseString = Encoding.UTF8.GetString(responseBytes);
                        
                        System.Diagnostics.Trace.WriteLine($"[TSPG] 第 {attempt} 次請求成功");
                        System.Diagnostics.Trace.WriteLine($"[TSPG] 回應長度: {responseString.Length} 字元");
                        
                        if (responseString.Length < 2000) // 只記錄較短的回應內容
                        {
                            System.Diagnostics.Trace.WriteLine($"[TSPG] 回應內容: {responseString}");
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine($"[TSPG] 回應內容 (前500字元): {responseString.Substring(0, 500)}...");
                        }

                        // 解析回應
                        PayPageResponse result;
                        if (postData is string)
                        {
                            result = ParseTSPGResponse(responseString);
                        }
                        else
                        {
                            result = ParseResponse(responseString);
                        }

                        // 檢查回應是否有效
                        if (result != null && !string.IsNullOrEmpty(result.code))
                        {
                            System.Diagnostics.Trace.WriteLine($"[TSPG] 請求完成，回應碼: {result.code}, 訊息: {result.msg}");
                            return result;
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine("[TSPG] 警告: 回應格式不正確或為空");
                            if (attempt <= maxRetries)
                            {
                                continue; // 重試
                            }
                            return CreateErrorResponse("", "回應格式不正確");
                        }
                    }
                }
                catch (WebException webEx)
                {
                    lastException = webEx;
                    string errorDetail = ProcessWebException(webEx, attempt, maxRetries);
                    
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 第 {attempt} 次請求 WebException: {errorDetail}");
                    
                    // 判斷是否應該重試
                    if (ShouldRetryOnWebException(webEx) && attempt <= maxRetries)
                    {
                        continue; // 重試
                    }
                    else
                    {
                        return CreateErrorResponse("", $"網路請求失敗 (嘗試 {attempt} 次): {errorDetail}");
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    string errorMessage = $"第 {attempt} 次請求發生異常: {ex.GetType().Name}: {ex.Message}";
                    System.Diagnostics.Trace.WriteLine($"[TSPG] {errorMessage}");
                    
                    // 對於一般異常，只在網路相關錯誤時重試
                    if (IsNetworkRelatedError(ex) && attempt <= maxRetries)
                    {
                        continue; // 重試
                    }
                    else
                    {
                        return CreateErrorResponse("", $"請求異常 (嘗試 {attempt} 次): {ex.Message}");
                    }
                }
            }

            // 所有重試都失敗
            string finalError = lastException != null 
                ? $"所有重試失敗，最後錯誤: {lastException.Message}"
                : "所有重試失敗，未知錯誤";
                
            System.Diagnostics.Trace.WriteLine($"[TSPG] {finalError}");
            return CreateErrorResponse("", finalError);
        }

        /// <summary>
        /// 處理 WebException 並提取詳細錯誤資訊
        /// </summary>
        private static string ProcessWebException(WebException webEx, int currentAttempt, int maxRetries)
        {
            string errorMessage = $"WebException: {webEx.Message}";
            
            try
            {
                if (webEx.Response is HttpWebResponse httpResponse)
                {
                    errorMessage += $" [HTTP {(int)httpResponse.StatusCode} {httpResponse.StatusCode}]";
                    
                    // 嘗試讀取錯誤回應內容
                    try
                    {
                        using (var stream = httpResponse.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string errorContent = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(errorContent))
                            {
                                if (errorContent.Length > 500)
                                {
                                    errorContent = errorContent.Substring(0, 500) + "...";
                                }
                                errorMessage += $" 回應內容: {errorContent}";
                            }
                        }
                    }
                    catch (Exception readEx)
                    {
                        errorMessage += $" (無法讀取錯誤回應: {readEx.Message})";
                    }
                }
                else if (webEx.Response != null)
                {
                    errorMessage += $" 回應類型: {webEx.Response.GetType().Name}";
                }
            }
            catch (Exception ex)
            {
                errorMessage += $" (處理 WebException 時發生錯誤: {ex.Message})";
            }

            return errorMessage;
        }

        /// <summary>
        /// 判斷 WebException 是否應該重試
        /// </summary>
        private static bool ShouldRetryOnWebException(WebException webEx)
        {
            // 重試的條件
            switch (webEx.Status)
            {
                case WebExceptionStatus.Timeout:
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.PipelineFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.UnknownError:
                    return true;

                case WebExceptionStatus.ProtocolError:
                    // HTTP 錯誤碼判斷
                    if (webEx.Response is HttpWebResponse httpResponse)
                    {
                        int statusCode = (int)httpResponse.StatusCode;
                        // 5xx 伺服器錯誤和部分 4xx 錯誤可以重試
                        return statusCode >= 500 || statusCode == 408 || statusCode == 429;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 判斷異常是否為網路相關錯誤
        /// </summary>
        private static bool IsNetworkRelatedError(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException ||
                   ex is System.IO.IOException ||
                   ex is System.TimeoutException ||
                   (ex.Message != null && (
                       ex.Message.Contains("timeout") ||
                       ex.Message.Contains("connection") ||
                       ex.Message.Contains("network") ||
                       ex.Message.Contains("socket")
                   ));
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

        /// <summary>
        /// 使用 HttpWebRequest 方式發送 POST 請求到 TSPG
        /// 提供更精細的控制選項，如超時設定、代理設定等
        /// </summary>
        /// <param name="baseUrl">API 基礎 URL</param>
        /// <param name="endpoint">API 端點</param>
        /// <param name="postData">POST 資料 (JSON 字串或 NameValueCollection)</param>
        /// <param name="timeoutSeconds">請求超時時間(秒)，預設 30 秒</param>
        /// <returns>TSPG 回應</returns>
        private static PayPageResponse PostToTSPGWithHttpWebRequest(string baseUrl, string endpoint, object postData, int timeoutSeconds = 30)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            
            try
            {
                string root = baseUrl.TrimEnd('/');
                string url = root + "/" + endpoint.TrimStart('/');
                
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = timeoutSeconds * 1000; // 轉換為毫秒
                request.ReadWriteTimeout = timeoutSeconds * 1000;
                request.UserAgent = "ChurchReport-TSPG/2.14-HttpWebRequest";
                request.Accept = "application/json, text/plain, */*";
                request.KeepAlive = false; // 避免連線重用問題
                
                byte[] postBytes = null;
                string contentType = "";
                
                if (postData is string jsonData)
                {
                    // JSON 格式請求
                    contentType = "application/json; charset=utf-8";
                    postBytes = Encoding.UTF8.GetBytes(jsonData);
                }
                else if (postData is NameValueCollection formData)
                {
                    // Form 格式請求 (向下相容舊版)
                    contentType = "application/x-www-form-urlencoded; charset=utf-8";
                    
                    // 將 NameValueCollection 轉換為 URL 編碼字串
                    var formParams = new List<string>();
                    foreach (string key in formData.AllKeys)
                    {
                        string encodedKey = Uri.EscapeDataString(key);
                        string encodedValue = Uri.EscapeDataString(formData[key] ?? "");
                        formParams.Add($"{encodedKey}={encodedValue}");
                    }
                    string formString = string.Join("&", formParams);
                    postBytes = Encoding.UTF8.GetBytes(formString);
                }
                else
                {
                    throw new ArgumentException("Unsupported postData type. Expected string (JSON) or NameValueCollection.", nameof(postData));
                }
                
                request.ContentType = contentType;
                request.ContentLength = postBytes.Length;
                
                // 寫入 POST 資料
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(postBytes, 0, postBytes.Length);
                }
                
                // 取得回應
                string responseString = "";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                    {
                        responseString = reader.ReadToEnd();
                    }
                    
                    // 記錄 HTTP 狀態碼 (用於除錯)
                    System.Diagnostics.Trace.WriteLine($"[TSPG] HTTP Response Status: {response.StatusCode} ({(int)response.StatusCode})");
                }
                
                // 解析回應
                if (postData is string)
                {
                    // JSON 請求使用新版解析器
                    return ParseTSPGResponse(responseString);
                }
                else
                {
                    // Form 請求使用舊版解析器
                    return ParseResponse(responseString);
                }
            }
            catch (WebException webEx)
            {
                string errorMessage = $"WebException: {webEx.Message}";
                
                // 嘗試讀取錯誤回應內容
                if (webEx.Response != null)
                {
                    try
                    {
                        using (Stream errorStream = webEx.Response.GetResponseStream())
                        using (StreamReader reader = new StreamReader(errorStream, Encoding.UTF8))
                        {
                            string errorContent = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(errorContent))
                            {
                                errorMessage += $" Response: {errorContent}";
                            }
                        }
                        
                        // 記錄 HTTP 錯誤狀態碼
                        if (webEx.Response is HttpWebResponse httpResponse)
                        {
                            errorMessage += $" Status: {httpResponse.StatusCode} ({(int)httpResponse.StatusCode})";
                        }
                    }
                    catch (Exception readEx)
                    {
                        errorMessage += $" (無法讀取錯誤回應: {readEx.Message})";
                    }
                }
                
                System.Diagnostics.Trace.WriteLine($"[TSPG] PostToTSPGWithHttpWebRequest WebException: {errorMessage}");
                return new PayPageResponse { code = "9999", msg = errorMessage };
            }
            catch (Exception ex)
            {
                string errorMessage = $"Exception: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[TSPG] PostToTSPGWithHttpWebRequest Error: {errorMessage}");
                return new PayPageResponse { code = "9999", msg = errorMessage };
            }
        }

        /// <summary>
        /// 使用 HttpWebRequest 方式呼叫 TSPG (測試環境專用)
        /// </summary>
        /// <param name="request">TSPG 付款請求</param>
        /// <param name="enable3D">是否啟用 3D 驗證</param>
        /// <param name="timeoutSeconds">請求超時時間(秒)</param>
        /// <returns>付款回應</returns>
        public static PayPageResponse OrderCreateTestWithHttpWebRequest(TSPGPaymentRequest request, bool enable3D, int timeoutSeconds = 30)
        {
            try
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                if (request.Params == null) throw new ArgumentException("request.Params 不可為空", nameof(request));

                // 指定測試用特店與端末代號
                request.Mid = enable3D ? _testMerchant3D : _testMerchantNo3D;
                request.Tid = _testTerminalId;

                var jsonData = BuildPaymentPostData(request);
                
                // 使用 HttpWebRequest 方式呼叫
                return PostToTSPGWithHttpWebRequest(_testApiRoot, "auth.ashx", jsonData, timeoutSeconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderCreateTestWithHttpWebRequest Error: {ex.Message}");
                return CreateErrorResponse(request?.Params?.OrderNo, $"建立測試付款失敗 (HttpWebRequest): {ex.Message}");
            }
        }

        /// <summary>
        /// 使用 HttpWebRequest 方式查詢訂單狀態
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="timeoutSeconds">請求超時時間(秒)</param>
        /// <returns>查詢結果</returns>
        public static PayPageResponse OrderQueryWithHttpWebRequest(string orderId, int timeoutSeconds = 30)
        {
            try
            {
                var jsonData = BuildQueryJsonData(orderId);
                string apiBase = GetConfigValue("TSPG:ApiBaseUrl", "");
                return PostToTSPGWithHttpWebRequest(apiBase, "query.ashx", jsonData, timeoutSeconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] OrderQueryWithHttpWebRequest Error: {ex.Message}");
                return CreateErrorResponse(orderId, $"查詢訂單失敗 (HttpWebRequest): {ex.Message}");
            }
        }
        
        private static PayPageResponse ParseTSPGResponse(string responseString)
        {
            try
            {
                if (string.IsNullOrEmpty(responseString))
                {
                    System.Diagnostics.Trace.WriteLine("[TSPG] ParseTSPGResponse: 回應字串為空");
                    return new PayPageResponse { code = "9999", msg = "空白回應" };
                }

                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTSPGResponse: 開始解析回應，長度 {responseString.Length}");
                if (responseString.Length < 500)
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 回應內容: {responseString}");
                }

                // 嘗試解析 JSON 格式的 TSPG REST API v2.14 回應
                if (responseString.StartsWith("{"))
                {
                    var tspgResponse = JsonConvert.DeserializeObject<TSPGApiResponse>(responseString);
                    
                    if (tspgResponse != null)
                    {
                        // 提取 ret_code (位於 params 物件內或根層級)
                        string retCode = tspgResponse.ret_code ?? tspgResponse.Params?.ret_code ?? string.Empty;
                        string retMsg = tspgResponse.ret_msg ?? tspgResponse.Params?.ret_msg ?? "無相關資訊";
                        
                        // 提取 hpp_url (付款頁面網址)
                        string hppUrl = tspgResponse.Params?.hpp_url ?? string.Empty;
                        
                        // 提取其他欄位 - order_no 可能在根層級或 params 內
                        string transactionId = tspgResponse.Params?.transaction_id ?? string.Empty;
                        string orderNo = tspgResponse.order_no ?? tspgResponse.Params?.ORDERNO ?? string.Empty;
                        
                        // 如果 order_no 包含 "ORDERNO=" 格式，提取其後的字串
                        if ( string.IsNullOrEmpty(orderNo) && hppUrl.Contains("ORDERNO="))
                        {
                            int startIndex = hppUrl.IndexOf("ORDERNO=", StringComparison.Ordinal) + 8; // "ORDERNO=" 長度為 8
                            if (startIndex < hppUrl.Length)
                            {
                                // 提取 ORDERNO= 之後的字串，可能包含 & 分隔符
                                int endIndex = hppUrl.IndexOf('&', startIndex);
                                if (endIndex > startIndex)
                                {
                                    orderNo = hppUrl.Substring(startIndex, endIndex - startIndex);
                                }
                                else
                                {
                                    orderNo = hppUrl.Substring(startIndex);
                                }
                                orderNo = orderNo.Trim();
                            }
                        }
                        
                        // 記錄解析結果
                        System.Diagnostics.Trace.WriteLine($"[TSPG] 解析成功:");
                        System.Diagnostics.Trace.WriteLine($"  - ret_code: {retCode}");
                        System.Diagnostics.Trace.WriteLine($"  - ret_msg: {retMsg}");
                        System.Diagnostics.Trace.WriteLine($"  - mid: {tspgResponse.Mid}");
                        System.Diagnostics.Trace.WriteLine($"  - tid: {tspgResponse.Tid}");
                        
                        if (!string.IsNullOrEmpty(hppUrl))
                        {
                            System.Diagnostics.Trace.WriteLine($"  - hpp_url: {hppUrl}");
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine("  - 警告: 回應中沒有 hpp_url");
                        }
                        
                        if (!string.IsNullOrEmpty(transactionId))
                        {
                            System.Diagnostics.Trace.WriteLine($"  - transaction_id: {transactionId}");
                        }
                        
                        if (!string.IsNullOrEmpty(hppUrl))
                        {
                            System.Diagnostics.Trace.WriteLine($"  - order_no: {orderNo}");
                        }

                        // 判斷是否成功
                        // 台新 TSPG 成功代碼: "00" 或空值
                        // 其他代碼表示錯誤
                        bool isSuccess = string.IsNullOrEmpty(retCode) || 
                                       retCode == "00" || 
                                       retCode == "0" ||
                                       retCode == "0000";
                        
                        // 統一回應碼格式 (轉換為 4 位數)
                        string finalCode;
                        if (isSuccess)
                        {
                            finalCode = "0000"; // 成功
                        }
                        else if (retCode.Length > 0)
                        {
                            // 保留原始錯誤碼
                            finalCode = retCode;
                        }
                        else
                        {
                            finalCode = "9999"; // 未知錯誤
                        }
                        
                        System.Diagnostics.Trace.WriteLine($"[TSPG] 最終狀態: code={finalCode}, 成功={isSuccess}");

                        // 轉換為 PayPageResponse 格式
                        return new PayPageResponse
                        {
                            code = finalCode,
                            msg = retMsg,
                            uid = tspgResponse.Mid ?? string.Empty,
                            key = tspgResponse.Tid ?? string.Empty,
                            url = hppUrl, // 付款頁面網址 (最重要)
                            transaction_id = transactionId,
                            order_no = orderNo
                        };
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("[TSPG] 警告: JSON 反序列化結果為 null");
                        return new PayPageResponse { code = "9999", msg = "JSON 反序列化失敗" };
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine("[TSPG] 回應不是 JSON 格式，嘗試使用舊版 QueryString 解析器");
                }

                // 如果不是 JSON 或解析失敗，嘗試舊版 QueryString 格式
                return ParseResponse(responseString);
            }
            catch (JsonException jsonEx)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] JSON 解析錯誤: {jsonEx.Message}");
                System.Diagnostics.Trace.WriteLine($"[TSPG] 錯誤位置: {jsonEx.StackTrace}");
                if (responseString.Length < 1000)
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 原始回應: {responseString}");
                }
                return new PayPageResponse { code = "9999", msg = $"JSON 解析錯誤: {jsonEx.Message}" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] ParseTSPGResponse 發生異常: {ex.GetType().Name}");
                System.Diagnostics.Trace.WriteLine($"[TSPG] 錯誤訊息: {ex.Message}");
                System.Diagnostics.Trace.WriteLine($"[TSPG] 堆疊追蹤: {ex.StackTrace}");
                if (responseString.Length < 1000)
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 原始回應: {responseString}");
                }
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