using ChurchReport.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using HttpRequest = Microsoft.AspNetCore.Http.HttpRequest;

namespace ChurchReport.Tools
{
    /// <summary>
    /// TSPG Webhook 處理器 - 處理來自高鉅金流的回傳通知
    /// </summary>
    public class TSPGWebhookHandler
    {
        private readonly string _storeKey;
        private readonly string _storeIV;

        #region 建構函式
        public TSPGWebhookHandler()
        {
            _storeKey = GetConfigValue("TSPG_StoreKey", "your_store_key");
            _storeIV = GetConfigValue("TSPG_StoreIV", "your_store_iv");
        }

        public TSPGWebhookHandler(string storeKey, string storeIV)
        {
            _storeKey = storeKey;
            _storeIV = storeIV;
        }
        #endregion

        #region 主要處理方法
        public async Task<TSPGWebhookResult> HandlePaymentNotificationAsync(HttpRequest request)
        {
            try
            {
                var notificationData = await ReadNotificationDataAsync(request);
                if (notificationData == null)
                {
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "無法讀取請求資料", ResponseContent = "ERROR" };
                }
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookWarning("驗證雜湊失敗", notificationData.OrderId);
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "驗證失敗", ResponseContent = "ERROR", Notification = notificationData };
                }
                var result = await ProcessPaymentNotificationAsync(notificationData);
                LogWebhookInfo($"付款通知處理完成 - 訂單: {notificationData.OrderId}, 狀態: {notificationData.State}", notificationData.OrderId);
                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理付款通知發生例外: {ex.Message}", "", ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR" };
            }
        }

        public async Task<TSPGWebhookResult> HandleRefundNotificationAsync(HttpRequest request)
        {
            try
            {
                var notificationData = await ReadNotificationDataAsync(request);
                if (notificationData == null)
                {
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "無法讀取退款通知資料", ResponseContent = "ERROR" };
                }
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookWarning("退款通知雜湊驗證失敗", notificationData.OrderId);
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "驗證失敗", ResponseContent = "ERROR", Notification = notificationData };
                }
                var result = await ProcessRefundNotificationAsync(notificationData);
                LogWebhookInfo($"退款通知處理完成 - 訂單: {notificationData.OrderId}", notificationData.OrderId);
                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理退款通知發生例外: {ex.Message}", "", ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR" };
            }
        }
        #endregion

        #region 讀取與映射
        private async Task<TSPGPaymentNotification> ReadNotificationDataAsync(HttpRequest request)
        {
            try
            {
                var notification = new TSPGPaymentNotification();
                if (request.Method == "POST")
                {
                    if (request.HasFormContentType)
                    {
                        var form = await request.ReadFormAsync();
                        MapFormToNotification(form, notification);
                    }
                    else
                    {
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
                    MapQueryToNotification(request.Query, notification);
                }
                return notification;
            }
            catch (Exception ex)
            {
                LogWebhookError($"讀取通知資料發生錯誤: {ex.Message}", "", ex);
                return null;
            }
        }

        private void MapFormToNotification(IFormCollection form, TSPGPaymentNotification n)
        {
            n.StoreUid = form["store_uid"]; n.OrderId = form["order_id"]; n.TransactionId = form["transaction_id"]; n.State = form["state"]; n.Cost = decimal.TryParse(form["cost"], out var cost) ? cost : 0; n.ActualCost = decimal.TryParse(form["actual_cost"], out var actualCost) ? actualCost : 0; n.Currency = form["currency"]; n.PayType = form["pay_type"]; n.UserName = form["user_name"]; n.UserEmail = form["user_email"]; n.UserPhone = form["user_phone"]; n.ReturnMessage = form["retmsg"]; n.Hash = form["hash"]; n.Echo0 = form["echo_0"]; n.Echo1 = form["echo_1"]; n.Echo2 = form["echo_2"]; n.Echo3 = form["echo_3"]; n.Echo4 = form["echo_4"]; n.CardNo = form["cardno"]; n.AuthCode = form["acode"]; n.CardType = form["card_type"]; n.IssuingBank = form["issuing_bank"]; if (DateTime.TryParse(form["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; }
        private void MapQueryToNotification(NameValueCollection query, TSPGPaymentNotification n)
        {
            n.StoreUid = query["store_uid"]; n.OrderId = query["order_id"]; n.TransactionId = query["transaction_id"]; n.State = query["state"]; n.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0; n.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0; n.Currency = query["currency"]; n.PayType = query["pay_type"]; n.UserName = query["user_name"]; n.UserEmail = query["user_email"]; n.UserPhone = query["user_phone"]; n.ReturnMessage = query["retmsg"]; n.Hash = query["hash"]; n.Echo0 = query["echo_0"]; n.Echo1 = query["echo_1"]; n.Echo2 = query["echo_2"]; n.Echo3 = query["echo_3"]; n.Echo4 = query["echo_4"]; n.CardNo = query["cardno"]; n.AuthCode = query["acode"]; n.CardType = query["card_type"]; n.IssuingBank = query["issuing_bank"]; if (DateTime.TryParse(query["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; }
        private void MapQueryToNotification(IQueryCollection query, TSPGPaymentNotification n)
        {
            n.StoreUid = query["store_uid"]; n.OrderId = query["order_id"]; n.TransactionId = query["transaction_id"]; n.State = query["state"]; n.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0; n.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0; n.Currency = query["currency"]; n.PayType = query["pay_type"]; n.UserName = query["user_name"]; n.UserEmail = query["user_email"]; n.UserPhone = query["user_phone"]; n.ReturnMessage = query["retmsg"]; n.Hash = query["hash"]; n.Echo0 = query["echo_0"]; n.Echo1 = query["echo_1"]; n.Echo2 = query["echo_2"]; n.Echo3 = query["echo_3"]; n.Echo4 = query["echo_4"]; n.CardNo = query["cardno"]; n.AuthCode = query["acode"]; n.CardType = query["card_type"]; n.IssuingBank = query["issuing_bank"]; if (DateTime.TryParse(query["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; }
        #endregion

        #region 驗證與處理
        private bool VerifyNotificationHash(TSPGPaymentNotification notification)
        {
            try
            {
                string hashString = $"{_storeKey}{notification.TransactionId}{notification.OrderId}{notification.State}{_storeIV}";
                using (SHA256 sha256Hash = SHA256.Create())
                {
                    byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(hashString));
                    var sb = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                    string calculated = sb.ToString().ToUpper();
                    if (calculated != notification.Hash?.ToUpper())
                    {
                        LogWebhookWarning($"雜湊不符 - 計算: {calculated}, 傳入: {notification.Hash}", notification.OrderId);
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogWebhookError($"驗證雜湊例外: {ex.Message}", notification?.OrderId ?? "", ex);
                return false;
            }
        }

        private async Task<TSPGWebhookResult> ProcessPaymentNotificationAsync(TSPGPaymentNotification n)
        {
            try
            {
                if (await IsDuplicateNotificationAsync(n))
                {
                    LogWebhookWarning($"重複的付款通知 - 訂單: {n.OrderId}", n.OrderId);
                    return new TSPGWebhookResult { IsSuccess = true, Message = "重複通知已忽略", ResponseContent = "OK", Notification = n };
                }
                await SaveNotificationAsync(n);
                if (n.IsPaymentSuccess) await ProcessSuccessfulPaymentAsync(n); else await ProcessFailedPaymentAsync(n);
                await TriggerBusinessLogicAsync(n);
                return new TSPGWebhookResult { IsSuccess = true, Message = "付款通知處理完成", ResponseContent = "OK", Notification = n };
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理付款通知例外 - 訂單: {n.OrderId}, 錯誤: {ex.Message}", n.OrderId, ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR", Notification = n };
            }
        }

        private async Task<TSPGWebhookResult> ProcessRefundNotificationAsync(TSPGPaymentNotification n)
        {
            try
            {
                await SaveRefundNotificationAsync(n);
                await ProcessRefundLogicAsync(n);
                return new TSPGWebhookResult { IsSuccess = true, Message = "退款通知處理完成", ResponseContent = "OK", Notification = n };
            }
            catch (Exception ex)
            {
                LogWebhookError($"處理退款通知例外 - 訂單: {n.OrderId}, 錯誤: {ex.Message}", n.OrderId, ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR", Notification = n };
            }
        }
        #endregion

        #region 業務流程 (預留擴充點)
        private async Task<bool> IsDuplicateNotificationAsync(TSPGPaymentNotification n) { await Task.CompletedTask; return false; }
        private async Task SaveNotificationAsync(TSPGPaymentNotification n) { LogWebhookInfo($"保存付款通知 - 訂單: {n.OrderId}, 交易: {n.TransactionId}", n.OrderId); await Task.CompletedTask; }
        private async Task SaveRefundNotificationAsync(TSPGPaymentNotification n) { LogWebhookInfo($"保存退款通知 - 訂單: {n.OrderId}, 交易: {n.TransactionId}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessSuccessfulPaymentAsync(TSPGPaymentNotification n) { LogWebhookInfo($"處理成功付款 - 訂單: {n.OrderId}, 金額: {n.ActualCost}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessFailedPaymentAsync(TSPGPaymentNotification n) { LogWebhookInfo($"處理失敗付款 - 訂單: {n.OrderId}, 訊息: {n.ReturnMessage}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessRefundLogicAsync(TSPGPaymentNotification n) { LogWebhookInfo($"處理退款邏輯 - 訂單: {n.OrderId}, 退款金額: {n.ActualCost}", n.OrderId); await Task.CompletedTask; }
        private async Task TriggerBusinessLogicAsync(TSPGPaymentNotification n) { LogWebhookInfo($"觸發後續業務處理 - 訂單: {n.OrderId}", n.OrderId); await Task.CompletedTask; }
        #endregion

        #region 設定與日誌
        private string GetConfigValue(string key, string defaultValue)
        {
            try
            {
                var basePath = Directory.GetCurrentDirectory();
                var config = new ConfigurationBuilder().SetBasePath(basePath).AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).Build();
                var value = config[key] ?? config[$"TSPG:{key}"] ?? Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch { return defaultValue; }
        }
        private void LogWebhookInfo(string message, string orderId = "") => System.Diagnostics.Trace.WriteLine($"[TSPG Webhook Info] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})");
        private void LogWebhookWarning(string message, string orderId = "") => System.Diagnostics.Trace.WriteLine($"[TSPG Webhook Warning] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})");
        private void LogWebhookError(string message, string orderId = "", Exception ex = null)
        {
            string errorMessage = $"[TSPG Webhook Error] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} (OrderId: {orderId})";
            if (ex != null) errorMessage += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            System.Diagnostics.Trace.WriteLine(errorMessage);
        }
        #endregion
    }

    #region Webhook 回傳結果模型
    public class TSPGWebhookResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string ResponseContent { get; set; } = "OK";
        public TSPGPaymentNotification Notification { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.Now;
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
    }
    #endregion
}