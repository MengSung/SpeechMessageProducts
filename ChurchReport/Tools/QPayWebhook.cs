using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;


namespace ChurchReport.Tools
{
    public class QPayCardWebhook : Controller, IDisposable
    {
        #region 設定與配置
        // ✅ 透過 appsettings.json 讀取設定，避免硬編碼
        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });
        private static IConfiguration m_Configuration => s_lazyConfiguration.Value;
        #endregion

        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        ToolUtilityClass m_ToolUtilityClass;

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        public QPayCardWebhook()
        {
            // ✅ 從 appsettings.json 讀取 LINE Channel Access Token
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            //// 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_QPayProcessor = new QPayProcessor(m_LineMessagingClient, m_PushUtility, m_ReplyUtility);

            // 透過 Factory 取得 ToolUtilityClass 單一實例
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        }

        #region 釋放記憶體
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                m_ToolUtilityClass.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~QPayCardWebhook()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        /// <summary>
        /// 從 appsettings.json 取得 LINE Channel Access Token
        /// ✅ 根據 CRM 組織名稱動態選擇對應的 Token
        /// </summary>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                // 嘗試從組織設定讀取
                var organization = m_Configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    var configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    var token = m_Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];

                    if (!string.IsNullOrEmpty(token))
                    {
                        System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[QPayCardWebhook] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] 錯誤: 讀取 LINE Token 設定失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        //[HttpGet]
        //[Route("QPayReturnUrl")]
        //public async Task<IActionResult> QPayReturnUrl(int? id = 0)
        //{
        //    return new OkObjectResult("付款結果可能成功");
        //}

        public ActionResult QPayReturnUrl(string ShopNo, string PayToken)
        {
            bool isPaymentDebugLogEnabled = QPayPaymentDebugLogger.IsEnabled();
            string correlationId = isPaymentDebugLogEnabled ? BuildPaymentDebugCorrelationId() : string.Empty;
            string requestContext = isPaymentDebugLogEnabled ? BuildPaymentDebugRequestContext(correlationId) : string.Empty;

            try
            {
                // 記錄開始處理
                System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] QPayReturnUrl started");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {ShopNo}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {PayToken}");

                QryOrderPay aQryOrderPay = null;

                try
                {
                    aQryOrderPay = m_QPayProcessor.OrderPayQuery(ShopNo, PayToken);
                    System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] OrderPayQuery completed");
                }
                catch (Exception queryEx)
                {
                    String queryError = $"查詢訂單失敗: {queryEx.Message}";
                    System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Error: {queryError}");
                    System.Diagnostics.Trace.WriteLine($"  - StackTrace: {queryEx.StackTrace}");

                    if (isPaymentDebugLogEnabled)
                    {
                        QPayPaymentDebugLogger.WritePaymentResult(
                            nameof(QPayCardWebhook),
                            "OrderPayQueryException",
                            ShopNo,
                            PayToken,
                            aQryOrderPay,
                            false,
                            queryError,
                            queryEx.ToString(),
                            correlationId,
                            requestContext);
                    }
                    
                    // 發送錯誤通知
                    try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, queryError); } catch { }
                    
                    // 返回美觀的錯誤視圖
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "付款查詢失敗，無法查詢付款狀態，請稍後再試或聯繫教會辦公室。";
                    ViewBag.OrderId = PayToken;
                    ViewBag.ErrorDetails = $"錯誤訊息: {queryEx.Message}";
                    return View("~/Views/QPayCard/PaymentResult.cshtml");
                }

                if (aQryOrderPay != null && aQryOrderPay.TSResultContent != null)
                {
                    System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Processing payment type: {aQryOrderPay.TSResultContent.Param3}");
                    QryOrder orderQueryDebugInfo = null;
                    string orderQueryDebugError = string.Empty;

                    if (isPaymentDebugLogEnabled && !QPayPaymentResultHelper.IsPaymentSuccess(aQryOrderPay))
                    {
                        TryQueryOrderDetailsForPaymentDebug(
                            ShopNo,
                            aQryOrderPay,
                            out orderQueryDebugInfo,
                            out orderQueryDebugError);
                    }
                    
                    if (aQryOrderPay.TSResultContent.Param3 == "收費單")
                    {
                        QPayFeeProcessor aQPayFeeProcessor = new QPayFeeProcessor();
                        return aQPayFeeProcessor.QPayFeeProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay, correlationId, requestContext, orderQueryDebugInfo, orderQueryDebugError);
                    }
                    else if (aQryOrderPay.TSResultContent.Param3 == "認獻單")
                    {
                        QPayDedicationBookingProcessor aQPayDedicationBookingProcessor = new QPayDedicationBookingProcessor();
                        return aQPayDedicationBookingProcessor.QPayDedicationBookingProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay, correlationId, requestContext, orderQueryDebugInfo, orderQueryDebugError);
                    }
                    else
                    {
                        // 預設處理為收費單
                        System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Unknown Param3, defaulting to fee processor");
                        QPayFeeProcessor aQPayFeeProcessor = new QPayFeeProcessor();
                        return aQPayFeeProcessor.QPayFeeProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay, correlationId, requestContext, orderQueryDebugInfo, orderQueryDebugError);
                    }
                }
                else
                {
                    string errorMsg = aQryOrderPay?.Description ?? "查詢結果為空";
                    System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Query result is null or invalid: {errorMsg}");

                    if (isPaymentDebugLogEnabled)
                    {
                        QPayPaymentDebugLogger.WritePaymentResult(
                            nameof(QPayCardWebhook),
                            "InvalidOrderPayQueryResult",
                            ShopNo,
                            PayToken,
                            aQryOrderPay,
                            false,
                            errorMsg,
                            "OrderPayQuery result is null or TSResultContent is null.",
                            correlationId,
                            requestContext);
                    }
                    
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "信用卡付款結果失敗，請稍後再試或聯繫教會辦公室。";
                    ViewBag.OrderId = PayToken;
                    ViewBag.ErrorDetails = errorMsg;
                    return View("~/Views/QPayCard/PaymentResult.cshtml");
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                System.Diagnostics.Trace.WriteLine(ErrorString);
                System.Diagnostics.Trace.WriteLine($"StackTrace: {e.StackTrace}");

                if (isPaymentDebugLogEnabled)
                {
                    QPayPaymentDebugLogger.WritePaymentResult(
                        nameof(QPayCardWebhook),
                        "UnhandledException",
                        ShopNo,
                        PayToken,
                        null,
                        false,
                        e.Message,
                        e.ToString(),
                        correlationId,
                        requestContext);
                }
                
                // 發送錯誤通知但不中斷執行
                try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString); } catch { }
                
                // 返回美觀的錯誤頁面
                ViewBag.IsSuccess = false;
                ViewBag.Message = "處理付款時發生錯誤，系統處理時發生錯誤，請稍後再試或聯繫教會辦公室。";
                ViewBag.OrderId = PayToken ?? "";
                ViewBag.ErrorDetails = $"錯誤詳情: {e.Message}\n時間: {DateTime.Now}";
                return View("~/Views/QPayCard/PaymentResult.cshtml");
            }
        }

        private string BuildPaymentDebugCorrelationId()
        {
            string traceIdentifier = HttpContext?.TraceIdentifier;
            return string.IsNullOrWhiteSpace(traceIdentifier)
                ? Guid.NewGuid().ToString("N")
                : traceIdentifier;
        }

        private void TryQueryOrderDetailsForPaymentDebug(string shopNo, QryOrderPay orderPayResult, out QryOrder orderQueryResult, out string orderQueryError)
        {
            orderQueryResult = null;
            orderQueryError = string.Empty;

            try
            {
                string orderNo = orderPayResult?.TSResultContent?.OrderNo;
                if (string.IsNullOrWhiteSpace(orderNo))
                {
                    orderQueryError = "Skipped OrderQuery: OrderNo is empty.";
                    return;
                }

                string payType = orderPayResult?.TSResultContent?.PayType;
                orderQueryResult = m_QPayProcessor.OrderQuery(shopNo, orderNo, payType);
            }
            catch (Exception ex)
            {
                orderQueryError = ex.Message;
                System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Payment debug OrderQuery failed: {ex.Message}");
            }
        }

        private string BuildPaymentDebugRequestContext(string correlationId)
        {
            try
            {
                var request = HttpContext?.Request;
                if (request == null)
                {
                    return "CorrelationId=" + correlationId + ";Request=null";
                }

                string maskedQueryString = MaskSensitiveQueryString(request.QueryString.ToString());
                string fullUrl = request.Scheme + "://" + request.Host + request.Path + maskedQueryString;

                return "CorrelationId=" + correlationId +
                    ";Method=" + request.Method +
                    ";Url=" + fullUrl +
                    ";Path=" + request.Path +
                    ";QueryString=" + maskedQueryString +
                    ";RemoteIp=" + (HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty) +
                    ";ForwardedFor=" + GetRequestHeader("X-Forwarded-For") +
                    ";ForwardedProto=" + GetRequestHeader("X-Forwarded-Proto") +
                    ";UserAgent=" + GetRequestHeader("User-Agent") +
                    ";Referer=" + MaskSensitiveText(GetRequestHeader("Referer"));
            }
            catch (Exception ex)
            {
                return "CorrelationId=" + correlationId + ";RequestContextError=" + ex.Message;
            }
        }

        private string GetRequestHeader(string name)
        {
            if (Request?.Headers == null || !Request.Headers.TryGetValue(name, out var values))
            {
                return string.Empty;
            }

            return values.ToString();
        }

        private string MaskSensitiveQueryString(string queryString)
        {
            if (string.IsNullOrWhiteSpace(queryString))
            {
                return string.Empty;
            }

            string trimmedQueryString = queryString.TrimStart('?');
            string[] parts = trimmedQueryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return queryString;
            }

            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index];
                int separatorIndex = part.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                string key = part.Substring(0, separatorIndex);
                string value = part.Substring(separatorIndex + 1);

                if (IsSensitiveQueryKey(key))
                {
                    parts[index] = key + "=" + MaskForPaymentDebug(value);
                }
            }

            return "?" + string.Join("&", parts);
        }

        private string MaskSensitiveText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string masked = value;
            foreach (string key in new[] { "PayToken", "token", "access_token", "id_token", "secret", "password", "lineid" })
            {
                masked = MaskSensitiveQueryValue(masked, key + "=");
            }

            return masked;
        }

        private string MaskSensitiveQueryValue(string value, string marker)
        {
            int markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            while (markerIndex >= 0)
            {
                int valueStart = markerIndex + marker.Length;
                int valueEnd = value.IndexOf('&', valueStart);
                if (valueEnd < 0)
                {
                    valueEnd = value.Length;
                }

                string originalValue = value.Substring(valueStart, valueEnd - valueStart);
                value = value.Substring(0, valueStart) + MaskForPaymentDebug(originalValue) + value.Substring(valueEnd);
                markerIndex = value.IndexOf(marker, valueStart + 1, StringComparison.OrdinalIgnoreCase);
            }

            return value;
        }

        private bool IsSensitiveQueryKey(string key)
        {
            return key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("lineid", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string MaskForPaymentDebug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.Length <= 8)
            {
                return new string('*', value.Length);
            }

            return value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
        }
    }
}
