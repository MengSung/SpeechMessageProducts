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
                    
                    if (aQryOrderPay.TSResultContent.Param3 == "收費單")
                    {
                        QPayFeeProcessor aQPayFeeProcessor = new QPayFeeProcessor();
                        return aQPayFeeProcessor.QPayFeeProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay);
                    }
                    else if (aQryOrderPay.TSResultContent.Param3 == "認獻單")
                    {
                        QPayDedicationBookingProcessor aQPayDedicationBookingProcessor = new QPayDedicationBookingProcessor();
                        return aQPayDedicationBookingProcessor.QPayDedicationBookingProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay);
                    }
                    else
                    {
                        // 預設處理為收費單
                        System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Unknown Param3, defaulting to fee processor");
                        QPayFeeProcessor aQPayFeeProcessor = new QPayFeeProcessor();
                        return aQPayFeeProcessor.QPayFeeProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay);
                    }
                }
                else
                {
                    string errorMsg = aQryOrderPay?.Description ?? "查詢結果為空";
                    System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Query result is null or invalid: {errorMsg}");
                    
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
    }
}
