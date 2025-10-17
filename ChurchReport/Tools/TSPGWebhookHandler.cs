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
using Newtonsoft.Json.Linq;

namespace ChurchReport.Tools
{
    /// <summary>
    /// TSPG Webhook ?B?z?? - ?B?z?????d???y???^??q??
    /// </summary>
    public class TSPGWebhookHandler
    {
        private readonly string _storeKey;
        private readonly string _storeIV;

        #region ??c?æÌ
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

        #region ?D?n?B?z??k
        public async Task<TSPGWebhookResult> HandlePaymentNotificationAsync(HttpRequest request)
        {
            try
            {
                var notificationData = await ReadNotificationDataAsync(request);
                if (notificationData == null)
                {
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "?L?k?????D???", ResponseContent = "ERROR" };
                }
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookWarning("??????????", notificationData.OrderId);
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "???????", ResponseContent = "ERROR", Notification = notificationData };
                }
                var result = await ProcessPaymentNotificationAsync(notificationData);
                LogWebhookInfo($"?I??q???B?z???? - ?q??: {notificationData.OrderId}, ???A: {notificationData.State}", notificationData.OrderId);
                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"?B?z?I??q???o???~: {ex.Message}", "", ex);
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
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "?L?k????h??q?????", ResponseContent = "ERROR" };
                }
                if (!VerifyNotificationHash(notificationData))
                {
                    LogWebhookWarning("?h??q?????????????", notificationData.OrderId);
                    return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = "???????", ResponseContent = "ERROR", Notification = notificationData };
                }
                var result = await ProcessRefundNotificationAsync(notificationData);
                LogWebhookInfo($"?h??q???B?z???? - ?q??: {notificationData.OrderId}", notificationData.OrderId);
                return result;
            }
            catch (Exception ex)
            {
                LogWebhookError($"?B?z?h??q???o???~: {ex.Message}", "", ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR" };
            }
        }
        #endregion

        #region ????P?M?g
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
                        // Read body as text once
                        try { if (request.Body.CanSeek) request.Body.Position = 0; } catch { }
                        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
                        {
                            var body = await reader.ReadToEndAsync();
                            if (!string.IsNullOrWhiteSpace(request.ContentType) && request.ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // JSON payload
                                try
                                {
                                    var json = JObject.Parse(body);
                                    MapJsonToNotification(json, notification);
                                }
                                catch (Exception jex)
                                {
                                    LogWebhookWarning($"JSON ?y?k???~: {jex.Message}");
                                }
                            }
                            else
                            {
                                // Fallback to querystring-like body (key=value&...)
                                var queryParams = HttpUtility.ParseQueryString(body);
                                MapQueryToNotification(queryParams, notification);
                            }
                        }
                    }
                }
                else if (request.Method == "GET")
                {
                    MapQueryToNotification(request.Query, notification);
                }

                // IsPaymentSuccess is computed from State property in model
                return notification;
            }
            catch (Exception ex)
            {
                LogWebhookError($"????q?????o????~: {ex.Message}");
                return null;
            }
        }

        private void MapFormToNotification(IFormCollection form, TSPGPaymentNotification n)
        {
            n.StoreUid = form["store_uid"]; 
            n.S_Mid = form["s_mid"]; 
            n.OrderId = form["order_id"]; 
            n.OrderNo = form["order_no"]; 
            n.TransactionId = form["transaction_id"]; 
            n.TxType = form["tx_type"]; 
            n.State = form["state"]; 
            n.RetCode = form["ret_code"]; 
            n.RetMsg = form["ret_msg"]; 
            n.Cost = decimal.TryParse(form["cost"], out var cost) ? cost : 0; 
            n.ActualCost = decimal.TryParse(form["actual_cost"], out var actualCost) ? actualCost : 0; 
            n.Currency = form["currency"]; 
            n.PayType = form["pay_type"]; 
            n.UserName = form["user_name"]; 
            n.UserEmail = form["user_email"]; 
            n.UserPhone = form["user_phone"]; 
            n.ReturnMessage = form["retmsg"]; 
            n.Hash = form["hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = form["ret_hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = form["signature"]; 
            n.Echo0 = form["echo_0"]; 
            n.Echo1 = form["echo_1"]; 
            n.Echo2 = form["echo_2"]; 
            n.Echo3 = form["echo_3"]; 
            n.Echo4 = form["echo_4"]; 
            n.CardNo = form["cardno"]; 
            n.AuthCode = form["acode"]; 
            n.AuthIdResp = form["auth_id_resp"]; 
            n.CardType = form["card_type"]; 
            n.IssuingBank = form["issuing_bank"]; 
            if (DateTime.TryParse(form["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; 
        }
        private void MapQueryToNotification(NameValueCollection query, TSPGPaymentNotification n)
        {
            n.StoreUid = query["store_uid"]; 
            n.S_Mid = query["s_mid"]; 
            n.OrderId = query["order_id"]; 
            n.OrderNo = query["order_no"]; 
            n.TransactionId = query["transaction_id"]; 
            n.TxType = query["tx_type"]; 
            n.State = query["state"]; 
            n.RetCode = query["ret_code"]; 
            n.RetMsg = query["ret_msg"]; 
            n.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0; 
            n.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0; 
            n.Currency = query["currency"]; 
            n.PayType = query["pay_type"]; 
            n.UserName = query["user_name"]; 
            n.UserEmail = query["user_email"]; 
            n.UserPhone = query["user_phone"]; 
            n.ReturnMessage = query["retmsg"]; 
            n.Hash = query["hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = query["ret_hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = query["signature"]; 
            n.Echo0 = query["echo_0"]; 
            n.Echo1 = query["echo_1"]; 
            n.Echo2 = query["echo_2"]; 
            n.Echo3 = query["echo_3"]; 
            n.Echo4 = query["echo_4"]; 
            n.CardNo = query["cardno"]; 
            n.AuthCode = query["acode"]; 
            n.AuthIdResp = query["auth_id_resp"]; 
            n.CardType = query["card_type"]; 
            n.IssuingBank = query["issuing_bank"]; 
            if (DateTime.TryParse(query["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; 
        }
        private void MapQueryToNotification(IQueryCollection query, TSPGPaymentNotification n)
        {
            n.StoreUid = query["store_uid"]; 
            n.S_Mid = query["s_mid"]; 
            n.OrderId = query["order_id"]; 
            n.OrderNo = query["order_no"]; 
            n.TransactionId = query["transaction_id"]; 
            n.TxType = query["tx_type"]; 
            n.State = query["state"]; 
            n.RetCode = query["ret_code"]; 
            n.RetMsg = query["ret_msg"]; 
            n.Cost = decimal.TryParse(query["cost"], out var cost) ? cost : 0; 
            n.ActualCost = decimal.TryParse(query["actual_cost"], out var actualCost) ? actualCost : 0; 
            n.Currency = query["currency"]; 
            n.PayType = query["pay_type"]; 
            n.UserName = query["user_name"]; 
            n.UserEmail = query["user_email"]; 
            n.UserPhone = query["user_phone"]; 
            n.ReturnMessage = query["retmsg"]; 
            n.Hash = query["hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = query["ret_hash"]; 
            if (string.IsNullOrEmpty(n.Hash)) n.Hash = query["signature"]; 
            n.Echo0 = query["echo_0"]; 
            n.Echo1 = query["echo_1"]; 
            n.Echo2 = query["echo_2"]; 
            n.Echo3 = query["echo_3"]; 
            n.Echo4 = query["echo_4"]; 
            n.CardNo = query["cardno"]; 
            n.AuthCode = query["acode"]; 
            n.AuthIdResp = query["auth_id_resp"]; 
            n.CardType = query["card_type"]; 
            n.IssuingBank = query["issuing_bank"]; 
            if (DateTime.TryParse(query["pay_time"], out var payTime)) n.PayTime = payTime; else n.PayTime = DateTime.Now; 
        }

        private void MapJsonToNotification(JObject json, TSPGPaymentNotification n)
        {
            string V(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (json.TryGetValue(k, StringComparison.OrdinalIgnoreCase, out var token))
                    {
                        return token?.ToString();
                    }
                }
                return null;
            }

            decimal VD(params string[] keys)
            {
                var s = V(keys);
                return decimal.TryParse(s, out var d) ? d : 0m;
            }

            DateTime VDT(params string[] keys)
            {
                var s = V(keys);
                return DateTime.TryParse(s, out var dt) ? dt : DateTime.Now;
            }

            n.StoreUid = V("store_uid", "storeUid");
            n.S_Mid = V("s_mid", "sMid", "s_mid");
            n.OrderId = V("order_id", "orderId", "uid", "order");
            n.OrderNo = V("order_no", "orderNo", "order_id", "orderId");
            n.TransactionId = V("transaction_id", "transactionId", "tx_id", "txn_id");
            n.TxType = V("tx_type", "txType", "transaction_type");
            n.State = V("state", "result", "status");
            n.RetCode = V("ret_code", "retCode", "return_code", "code");
            n.RetMsg = V("ret_msg", "retMsg", "return_message", "message", "msg");
            n.Cost = VD("cost", "amount");
            n.ActualCost = VD("actual_cost", "actualCost", "paid_amount");
            n.Currency = V("currency");
            n.PayType = V("pay_type", "payType");
            n.UserName = V("user_name", "userName");
            n.UserEmail = V("user_email", "userEmail");
            n.UserPhone = V("user_phone", "userPhone");
            n.ReturnMessage = V("retmsg", "message", "msg");
            n.Hash = V("hash", "sign", "signature", "ret_hash", "hash_value");
            n.Echo0 = V("echo_0", "echo0");
            n.Echo1 = V("echo_1", "echo1");
            n.Echo2 = V("echo_2", "echo2");
            n.Echo3 = V("echo_3", "echo3");
            n.Echo4 = V("echo_4", "echo4");
            n.CardNo = V("cardno", "card_no", "cardNo");
            n.AuthCode = V("acode", "auth_code", "authCode");
            n.AuthIdResp = V("auth_id_resp", "authIdResp", "auth_id", "authId");
            n.CardType = V("card_type", "cardType");
            n.IssuingBank = V("issuing_bank", "issuingBank");
            n.PayTime = VDT("pay_time", "payTime");
        }
        #endregion

        #region ????P?B?z
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
                        LogWebhookWarning($"?????? - ?p??: {calculated}, ??J: {notification.Hash}", notification.OrderId);
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogWebhookError($"?????????~: {ex.Message}", notification?.OrderId ?? "", ex);
                return false;
            }
        }

        private async Task<TSPGWebhookResult> ProcessPaymentNotificationAsync(TSPGPaymentNotification n)
        {
            try
            {
                if (await IsDuplicateNotificationAsync(n))
                {
                    LogWebhookWarning($"??????I??q?? - ?q??: {n.OrderId}", n.OrderId);
                    return new TSPGWebhookResult { IsSuccess = true, Message = "????q???w????", ResponseContent = "OK", Notification = n };
                }
                await SaveNotificationAsync(n);
                if (n.IsPaymentSuccess) await ProcessSuccessfulPaymentAsync(n); else await ProcessFailedPaymentAsync(n);
                await TriggerBusinessLogicAsync(n);
                return new TSPGWebhookResult { IsSuccess = true, Message = "?I??q???B?z????", ResponseContent = "OK", Notification = n };
            }
            catch (Exception ex)
            {
                LogWebhookError($"?B?z?I??q????~ - ?q??: {n.OrderId}, ???~: {ex.Message}", n.OrderId, ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR", Notification = n };
            }
        }

        private async Task<TSPGWebhookResult> ProcessRefundNotificationAsync(TSPGPaymentNotification n)
        {
            try
            {
                await SaveRefundNotificationAsync(n);
                await ProcessRefundLogicAsync(n);
                return new TSPGWebhookResult { IsSuccess = true, Message = "?h??q???B?z????", ResponseContent = "OK", Notification = n };
            }
            catch (Exception ex)
            {
                LogWebhookError($"?B?z?h??q????~ - ?q??: {n.OrderId}, ???~: {ex.Message}", n.OrderId, ex);
                return new TSPGWebhookResult { IsSuccess = false, ErrorMessage = ex.Message, ResponseContent = "ERROR", Notification = n };
            }
        }
        #endregion

        #region ?~??y?{ (?w?d?X?R?I)
        private async Task<bool> IsDuplicateNotificationAsync(TSPGPaymentNotification n) { await Task.CompletedTask; return false; }
        private async Task SaveNotificationAsync(TSPGPaymentNotification n) { LogWebhookInfo($"?O?s?I??q?? - ?q??: {n.OrderId}, ???: {n.TransactionId}", n.OrderId); await Task.CompletedTask; }
        private async Task SaveRefundNotificationAsync(TSPGPaymentNotification n) { LogWebhookInfo($"?O?s?h??q?? - ?q??: {n.OrderId}, ???: {n.TransactionId}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessSuccessfulPaymentAsync(TSPGPaymentNotification n) { LogWebhookInfo($"Process successful payment - Order: {n.OrderId}, Amount: {n.ActualCost}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessFailedPaymentAsync(TSPGPaymentNotification n) { LogWebhookInfo($"Process failed payment - Order: {n.OrderId}, Message: {n.ReturnMessage}", n.OrderId); await Task.CompletedTask; }
        private async Task ProcessRefundLogicAsync(TSPGPaymentNotification n) { LogWebhookInfo($"Process refund - Order: {n.OrderId}, Amount: {n.ActualCost}", n.OrderId); await Task.CompletedTask; }
        private async Task TriggerBusinessLogicAsync(TSPGPaymentNotification n) { LogWebhookInfo($"Trigger business logic - Order: {n.OrderId}", n.OrderId); await Task.CompletedTask; }
        #endregion

        #region ?]?w?P??x
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

    #region Webhook ?^????G???
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