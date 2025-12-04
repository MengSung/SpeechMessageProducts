using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;


namespace ChurchReport.Tools
{
    public class QPayCardWebhook : Controller, IDisposable
    {
        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        ToolUtilityClass m_ToolUtilityClass;

        // 客製化
        // 聖谷行道會 2.0
        private const String SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        public QPayCardWebhook()
        {
            this.m_LineMessagingClient = new LineMessagingClient(SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN);

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
                    
                    // 返回錯誤視圖而不是拋出例外
                    return new ContentResult
                    {
                        Content = $"<html><body><h1>付款查詢失敗</h1><p>無法查詢付款狀態，請稍後再試或聯繫客服</p><p>錯誤: {queryEx.Message}</p></body></html>",
                        ContentType = "text/html",
                        StatusCode = 200
                    };
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
                    
                    return new ContentResult
                    {
                        Content = $"<html><body><h1>信用卡付款結果失敗</h1><p>{errorMsg}</p><p>請稍後再試或聯繫客服</p></body></html>",
                        ContentType = "text/html",
                        StatusCode = 200
                    };
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                System.Diagnostics.Trace.WriteLine(ErrorString);
                System.Diagnostics.Trace.WriteLine($"StackTrace: {e.StackTrace}");
                
                // 發送錯誤通知但不中斷執行
                try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString); } catch { }
                
                // 返回錯誤頁面而不是拋出例外
                return new ContentResult
                {
                    Content = $"<html><body><h1>處理付款時發生錯誤</h1><p>系統處理時發生錯誤，請稍後再試或聯繫客服</p><p>錯誤詳情: {e.Message}</p></body></html>",
                    ContentType = "text/html",
                    StatusCode = 200
                };
            }
        }
    }
}
