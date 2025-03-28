using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace;


namespace ChurchReport.Tools
{
    public class QPayCardWebhook : Controller, IDisposable
    {
        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        ToolUtilityClass m_ToolUtilityClass;

        // 客製化
        // 聖谷行道會 2.0
        private const String SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN = @"O7kDZ6nG5nenmU7D2z5LzmKgRM9Pf/5/r08Z6zVlmduhTwbfV8HNObv0YceKtM5oiAvTeL3IaiSK8UEh7Y4fS+FSroM/PfHmEEIcvwmMSud3tZUASMEeLVKCy8bL38PfAws2toVIWsTf+qwcrXyHbgdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        public QPayCardWebhook()
        {
            this.m_LineMessagingClient = new LineMessagingClient(SPEECHMESSAGE_CHANNEL_ACCESS_TOKEN);

            //// 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_QPayProcessor = new QPayProcessor(m_LineMessagingClient, m_PushUtility, m_ReplyUtility);

            m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

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
                QryOrderPay aQryOrderPay = new QryOrderPay();

                aQryOrderPay = m_QPayProcessor.OrderPayQuery(ShopNo, PayToken);

                if (aQryOrderPay != null)
                {
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
                        QPayFeeProcessor aQPayFeeProcessor = new QPayFeeProcessor();
                        return aQPayFeeProcessor.QPayFeeProcessorReturnUrl(ShopNo, PayToken, aQryOrderPay);
                    }
                }
                else
                {
                    return new OkObjectResult("信用卡付款結果失敗!" + Environment.NewLine + aQryOrderPay.Description);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString);
                //Monitor.Exit(this);
                throw e;
            }
        }
    }
}
