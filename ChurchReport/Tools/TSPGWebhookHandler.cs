using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using HttpRequest = Microsoft.AspNetCore.Http.HttpRequest;

namespace ChurchReport.Tools
{
    /// <summary>
    /// TSPG Webhook 處理器 - 處理來自高鉅金流的回調通知
    /// </summary>
    public class TSPGWebhookHandler
    {
        private readonly TSPGApiClient _apiClient;
        private readonly string _storeKey;
        private readonly string _storeIV;

        #region 建構函式

        public TSPGWebhookHandler()
        {
            _apiClient = new TSPGApiClient();
            _storeKey = GetConfigValue("TSPG_StoreKey", "your_store_key");
            _storeIV = GetConfigValue("TSPG_StoreIV", "your_store_iv");
        }

        public TSPGWebhookHandler(TSPGApiClient apiClient, string storeKey, string storeIV)
        {
            _apiClient = apiClient;
            _storeKey = storeKey;
            _storeIV = storeIV;
        }

        #endregion

        #region 主要處理方法

        /// <summary>
        /// 處理付款結果通知
        /// </summary>
        /// <param name="request">HTTP 請求</param>
        /// <returns>處理結果</returns>
        public async Task<TSPGWebhookResult> HandlePaymentNotificationAsync(HttpRequest request)
        {
            try
            {
                // 讀取請求內容
                var notificationData = await ReadNotificationDataAsync(request);
                
                if (notificationData == null)
                {
                    return new TSPGWebhookResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "無法讀取通知資料",
                        ResponseContent = "ERROR"
                    };
                }

                // 驗證檢查碼
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookError("檢查碼驗證失敗", notificationData.OrderId);
                    return new TSPGWebhookResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "檢查碼驗證失敗",
                        ResponseContent = "ERROR",
                        Notification = notificationData
                    };
                }

                // 處理付款通知
                var result = await ProcessPaymentNotificationAsync(notificationData);
                
                // 記錄處理結果
                LogWebhookInfo($"付款通知處理完成 - 訂單: {notificationData.OrderId}, 狀態: {notificationData.State}", notificationData.OrderId);

                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理付款通知時發生錯誤: {ex.Message}", "", ex);
                return new TSPGWebhookResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ResponseContent = "ERROR"
                };
            }
        }

        /// <summary>
        /// 處理退款結果通知
        /// </summary>
        /// <param name="request">HTTP 請求</param>
        /// <returns>處理結果</returns>
        public async Task<TSPGWebhookResult> HandleRefundNotificationAsync(HttpRequest request)
        {
            try
            {
                var notificationData = await ReadNotificationDataAsync(request);
                
                if (notificationData == null)
                {
                    return new TSPGWebhookResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "無法讀取退款通知資料",
                        ResponseContent = "ERROR"
                    };
                }

                // 驗證檢查碼
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookError("退款通知檢查碼驗證失敗", notificationData.OrderId);
                    return new TSPGWebhookResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "檢查碼驗證失敗",
                        ResponseContent = "ERROR",
                        Notification = notificationData
                    };
                }

                // 處理退款通知
                var result = await ProcessRefundNotificationAsync(notificationData);
                
                LogWebhookInfo($"退款通知處理完成 - 訂單: {notificationData.OrderId}", notificationData.OrderId);

                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理退款通知時發生錯誤: {ex.Message}", "", ex);
                return new TSPGWebhookResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ResponseContent = "ERROR"
                };
            }
        }

        #endregion

        #region 私有處理方法

        /// <summary>
        /// 讀取通知資料
        /// </summary>
        private async Task<TSPGPaymentNotification> ReadNotificationDataAsync(HttpRequest request)
        {
            try
            {
                var notification = new TSPGPaymentNotification();

                if (request.Method == "POST")
                {
                    // 讀取 POST 資料
                    if (request.HasFormContentType)
                    {
                        // 表單資料
                        var form = await request.ReadFormAsync();
                        MapFormToNotification(form, notification);
                    }
                    else
                    {
                        // JSON 或其他格式
                        request.Body.Position = 0;
                        using (var reader = new StreamReader(request.Body))
                        {
                            var body = await reader.ReadToEndAsync();
                            var queryParams = HttpUtility.ParseQueryString(body);
                            MapQueryToNotification(queryParams, notification);
                        }
                    }
                }
                else if (request.Method == "GET")
                {
                    // 讀取 GET 參數
                    MapQueryToNotification(request.Query, notification);
                }

                return notification;
            }
            catch (Exception ex)
            {
                LogWebhookError($"讀取通知資料時發生錯誤: {ex.Message}", "", ex);
                return null;
            }
        }

        /// <summary>
        /// 將表單資料對應到通知物件
        /// </summary>
        private void MapFormToNotification(IFormCollection form, TSPGPaymentNotification notification)
        {
            notification.StoreUid = form["store_uid"];
            notification.OrderId = form["order_id"];
            notification.TransactionId = form["transaction_id"];
            notification.State = form["state"];
            notification.Cost = decimal.TryParse(form["cost"], out var cost) ? cost : 0;
            notification.ActualCost = decimal.TryParse(form["actual_cost"], out var actualCost) ? actualCost : 0;
            notification.Currency = form["currency"];
            notification.PayType = form["pay_type"];
            notification.UserName = form["user_name"];
            notification.UserEmail = form["user_email"];
            notification.UserPhone = form["user_phone"];
            notification.ReturnMessage = form["retmsg"];
            notification.Hash = form["hash"];
            notification.Echo0 = form["echo_0"];
            notification.Echo1 = form["echo_1"];
            notification.Echo2 = form["echo_2"];
            notification.Echo3 = form["echo_3"];
            notification.Echo4 = form["echo_4"];
            notification.CardNo = form["cardno"];
            notification.AuthCode = form["acode"];
            notification.CardType = form["card_type"];
            notification.IssuingBank = form["issuing_bank"];

            // 解析付款時間
            if (DateTime.TryParse(form["pay_time"], out var payTime))
            {
                notification.PayTime = payTime;
            }
            else
            {
                notification.PayTime = DateTime.Now; // 預設為現在時間
            }
        }

        /// <summary>
        /// 將查詢參數對應到通知物件
        /// </summary>
        private void MapQueryToNotification(NameValueCollection query, TSPGPaymentNotification notification)
        {
            notification.StoreUid = query["store_uid"];
            notification.OrderId = query["order_id"];
            notification.TransactionId = query["transaction_id"];
            notification.State = query["state"];
            notification.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0;
            notification.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0;
            notification.Currency = query["currency"];
            notification.PayType = query["pay_type"];
            notification.UserName = query["user_name"];
            notification.UserEmail = query["user_email"];
            notification.UserPhone = query["user_phone"];
            notification.ReturnMessage = query["retmsg"];
            notification.Hash = query["hash"];
            notification.Echo0 = query["echo_0"];
            notification.Echo1 = query["echo_1"];
            notification.Echo2 = query["echo_2"];
            notification.Echo3 = query["echo_3"];
            notification.Echo4 = query["echo_4"];
            notification.CardNo = query["cardno"];
            notification.AuthCode = query["acode"];
            notification.CardType = query["card_type"];
            notification.IssuingBank = query["issuing_bank"];

            // 解析付款時間
            if (DateTime.TryParse(query["pay_time"], out var payTime))
            {
                notification.PayTime = payTime;
            }
            else
            {
                notification.PayTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 將查詢參數對應到通知物件 (Microsoft.AspNetCore.Http.IQueryCollection 版本)
        /// </summary>
        private void MapQueryToNotification(IQueryCollection query, TSPGPaymentNotification notification)
        {
            notification.StoreUid = query["store_uid"];
            notification.OrderId = query["order_id"];
            notification.TransactionId = query["transaction_id"];
            notification.State = query["state"];
            notification.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0;
            notification.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0;
            notification.Currency = query["currency"];
            notification.PayType = query["pay_type"];
            notification.UserName = query["user_name"];
            notification.UserEmail = query["user_email"];
            notification.UserPhone = query["user_phone"];
            notification.ReturnMessage = query["retmsg"];
            notification.Hash = query["hash"];
            notification.Echo0 = query["echo_0"];
            notification.Echo1 = query["echo_1"];
            notification.Echo2 = query["echo_2"];
            notification.Echo3 = query["echo_3"];
            notification.Echo4 = query["echo_4"];
            notification.CardNo = query["cardno"];
            notification.AuthCode = query["acode"];
            notification.CardType = query["card_type"];
            notification.IssuingBank = query["issuing_bank"];

            // 解析付款時間
            if (DateTime.TryParse(query["pay_time"], out var payTime))
            {
                notification.PayTime = payTime;
            }
            else
            {
                notification.PayTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 驗證通知的檢查碼
        /// </summary>
        private bool VerifyNotificationHash(TSPGPaymentNotification notification)
        {
            try
            {
                // 根據 TSPG 文件，檢查碼計算方式：
                // hash = SHA256(KEY + transaction_id + order_id + state + IV)
                string hashString = $"{_storeKey}{notification.TransactionId}{notification.OrderId}{notification.State}{_storeIV}";

                using (SHA256 sha256Hash = SHA256.Create())
                {
                    byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));

                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        builder.Append(bytes[i].ToString("x2"));
                    }
                    string calculatedHash = builder.ToString().ToUpper();

                    bool isValid = calculatedHash == notification.Hash?.ToUpper();
                    
                    if (!isValid)
                    {
                        LogWebhookWarning($"檢查碼不符 - 計算值: {calculatedHash}, 接收值: {notification.Hash}", notification.OrderId);
                    }

                    return isValid;
                }
            }
            catch (Exception ex)
            {
                LogWebhookError($"驗證檢查碼時發生錯誤: {ex.Message}", notification?.OrderId ?? "", ex);
                return false;
            }
        }

        /// <summary>
        /// 處理付款通知
        /// </summary>
        private async Task<TSPGWebhookResult> ProcessPaymentNotificationAsync(TSPGPaymentNotification notification)
        {
            try
            {
                // 檢查是否為重複通知
                if (await IsDuplicateNotificationAsync(notification))
                {
                    LogWebhookWarning($"收到重複的付款通知 - 訂單: {notification.OrderId}", notification.OrderId);
                    return new TSPGWebhookResult
                    {
                        IsSuccess = true,
                        Message = "重複通知已忽略",
                        ResponseContent = "OK",
                        Notification = notification
                    };
                }

                // 記錄通知
                await SaveNotificationAsync(notification);

                // 根據付款狀態進行處理
                if (notification.IsPaymentSuccess)
                {
                    await ProcessSuccessfulPaymentAsync(notification);
                }
                else
                {
                    await ProcessFailedPaymentAsync(notification);
                }

                // 觸發業務邏輯處理
                await TriggerBusinessLogicAsync(notification);

                return new TSPGWebhookResult
                {
                    IsSuccess = true,
                    Message = "付款通知處理成功",
                    ResponseContent = "OK",
                    Notification = notification
                };
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理付款通知時發生錯誤 - 訂單: {notification.OrderId}, 錯誤: {ex.Message}", notification.OrderId, ex);
                return new TSPGWebhookResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ResponseContent = "ERROR",
                    Notification = notification
                };
            }
        }

        /// <summary>
        /// 處理退款通知
        /// </summary>
        private async Task<TSPGWebhookResult> ProcessRefundNotificationAsync(TSPGPaymentNotification notification)
        {
            try
            {
                // 記錄退款通知
                await SaveRefundNotificationAsync(notification);

                // 處理退款邏輯
                await ProcessRefundLogicAsync(notification);

                return new TSPGWebhookResult
                {
                    IsSuccess = true,
                    Message = "退款通知處理成功",
                    ResponseContent = "OK",
                    Notification = notification
                };
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理退款通知時發生錯誤 - 訂單: {notification.OrderId}, 錯誤: {ex.Message}", notification.OrderId, ex);
                return new TSPGWebhookResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ResponseContent = "ERROR",
                    Notification = notification
                };
            }
        }

        #endregion

        #region 業務邏輯處理方法

        /// <summary>
        /// 檢查是否為重複通知
        /// </summary>
        private async Task<bool> IsDuplicateNotificationAsync(TSPGPaymentNotification notification)
        {
            try
            {
                // 這裡應該查詢資料庫檢查是否已處理過相同的通知
                // 可以根據 transaction_id + order_id + state 來判斷
                // 目前先返回 false，實際實作時需要連接資料庫
                return false;
            }
            catch (Exception ex)
            {
                LogWebhookError($"檢查重複通知時發生錯誤: {ex.Message}", notification.OrderId, ex);
                return false;
            }
        }

        /// <summary>
        /// 儲存通知記錄
        /// </summary>
        private async Task SaveNotificationAsync(TSPGPaymentNotification notification)
        {
            try
            {
                // 這裡應該將通知資料儲存到資料庫
                // 包含完整的通知內容，以供後續查詢和對帳使用
                LogWebhookInfo($"儲存付款通知 - 訂單: {notification.OrderId}, 交易號: {notification.TransactionId}", notification.OrderId);
                
                // TODO: 實作資料庫儲存邏輯
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"儲存通知記錄時發生錯誤: {ex.Message}", notification.OrderId, ex);
                throw;
            }
        }

        /// <summary>
        /// 儲存退款通知記錄
        /// </summary>
        private async Task SaveRefundNotificationAsync(TSPGPaymentNotification notification)
        {
            try
            {
                LogWebhookInfo($"儲存退款通知 - 訂單: {notification.OrderId}, 交易號: {notification.TransactionId}", notification.OrderId);
                
                // TODO: 實作退款通知資料庫儲存邏輯
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"儲存退款通知記錄時發生錯誤: {ex.Message}", notification.OrderId, ex);
                throw;
            }
        }

        /// <summary>
        /// 處理成功付款
        /// </summary>
        private async Task ProcessSuccessfulPaymentAsync(TSPGPaymentNotification notification)
        {
            try
            {
                LogWebhookInfo($"處理成功付款 - 訂單: {notification.OrderId}, 金額: {notification.ActualCost}", notification.OrderId);
                
                // TODO: 實作成功付款的業務邏輯
                // 例如：更新訂單狀態、發送確認電子郵件、更新庫存等
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理成功付款時發生錯誤: {ex.Message}", notification.OrderId, ex);
                throw;
            }
        }

        /// <summary>
        /// 處理失敗付款
        /// </summary>
        private async Task ProcessFailedPaymentAsync(TSPGPaymentNotification notification)
        {
            try
            {
                LogWebhookInfo($"處理失敗付款 - 訂單: {notification.OrderId}, 原因: {notification.ReturnMessage}", notification.OrderId);
                
                // TODO: 實作失敗付款的業務邏輯
                // 例如：更新訂單狀態、記錄失敗原因、通知客戶等
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理失敗付款時發生錯誤: {ex.Message}", notification.OrderId, ex);
                throw;
            }
        }

        /// <summary>
        /// 處理退款邏輯
        /// </summary>
        private async Task ProcessRefundLogicAsync(TSPGPaymentNotification notification)
        {
            try
            {
                LogWebhookInfo($"處理退款邏輯 - 訂單: {notification.OrderId}, 退款金額: {notification.ActualCost}", notification.OrderId);
                
                // TODO: 實作退款的業務邏輯
                // 例如：更新訂單狀態、恢復庫存、發送退款通知等
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理退款邏輯時發生錯誤: {ex.Message}", notification.OrderId, ex);
                throw;
            }
        }

        /// <summary>
        /// 觸發業務邏輯處理
        /// </summary>
        private async Task TriggerBusinessLogicAsync(TSPGPaymentNotification notification)
        {
            try
            {
                // TODO: 這裡可以觸發額外的業務邏輯
                // 例如：發送 webhook 到其他系統、更新 CRM、發送推播通知等
                LogWebhookInfo($"觸發業務邏輯處理 - 訂單: {notification.OrderId}", notification.OrderId);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWebhookError($"觸發業務邏輯時發生錯誤: {ex.Message}", notification.OrderId, ex);
                // 注意：這裡不要拋出例外，避免影響主要的通知處理流程
            }
        }

        #endregion

        #region 輔助方法

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

                var value = config[key] ?? config[$"TSPG:{key}"] ?? Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 記錄 Webhook 資訊
        /// </summary>
        private void LogWebhookInfo(string message, string orderId = "")
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG Webhook Info] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})");
        }

        /// <summary>
        /// 記錄 Webhook 警告
        /// </summary>
        private void LogWebhookWarning(string message, string orderId = "")
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG Webhook Warning] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})");
        }

        /// <summary>
        /// 記錄 Webhook 錯誤
        /// </summary>
        private void LogWebhookError(string message, string orderId = "", Exception ex = null)
        {
            string errorMessage = $"[TSPG Webhook Error] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})";
            
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            
            System.Diagnostics.Trace.WriteLine(errorMessage);
        }

        #endregion
    }

    #region Webhook 結果模型

    /// <summary>
    /// TSPG Webhook 處理結果
    /// </summary>
    public class TSPGWebhookResult
    {
        /// <summary>
        /// 是否處理成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 處理訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 錯誤訊息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 回應內容 (回傳給 TSPG 的內容，通常是 "OK" 或 "ERROR")
        /// </summary>
        public string ResponseContent { get; set; } = "OK";

        /// <summary>
        /// 通知資料
        /// </summary>
        public TSPGPaymentNotification Notification { get; set; }

        /// <summary>
        /// 處理時間
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 額外資料
        /// </summary>
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
    }

    #endregion
}