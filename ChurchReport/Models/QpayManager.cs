using ChurchReport.WebServiceConnector;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    public class QpayManager : Controller
    {
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        private QpayModel m_QpayModel = new QpayModel();

        private QPayProcessor m_QPayProcessor = new QPayProcessor();

        public QpayModel SetQpayModel()
        {
            try
            {
                m_QpayModel.Category = "十一";

                m_QpayModel.OtherCategoryArray = new List<String>();
                EntityCollection TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("宣道支持奉獻(請勿刪除)");
                String Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }

                String[] OtherCategoryArray = Description.Split(',');
                foreach (String OtherCategory in OtherCategoryArray)
                {
                    m_QpayModel.OtherCategoryArray.Add(OtherCategory);
                }

                m_QpayModel.CreditCardList = new List<CreditCard>{
                    new CreditCard { CCToken = "0000", CreditCardNumber = "AAAAAAAAAAAAAAAA", ExpireDate = "2020/5/25" },
                    new CreditCard { CCToken = "1111", CreditCardNumber = "BBBBBBBBBBBBBBBB", ExpireDate = "2020/5/26" },
                    new CreditCard { CCToken = "2222", CreditCardNumber = "CCCCCCCCCCCCCCCC", ExpireDate = "2020/5/27" },
                };

                return m_QpayModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        public async Task<IActionResult> SaveQPayDedication(QpayModel QpayModel, String LineUserId)
        {
            try
            {
                if (QpayModel.Amount != null && QpayModel.Amount > 0)
                {
                    String DedicationResult = await m_QPayProcessor.CreateFeeAsync( LineUserId, QpayModel);

                    String PayWay = "";
                    if (DedicationResult.Contains("*** 請依照訊息付款 ***") != true)
                    {
                        PayWay = "信用卡";
                    }
                    else
                    {
                        PayWay = "虛擬帳號";
                    }
                    return Json(new { status = "1", message = "感謝您的奉獻", DedicationResult = DedicationResult, PayWay = PayWay });
                }
                else
                {
                    return Json(new { status = "2", message = "未輸入奉獻金額" });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public QpayModel SetDedicationFeeList()
        {
            try
            {
                m_QpayModel.DedicationFeeList = new List<DedicationFee>{
                    new DedicationFee { DedicationDate = new DateTime( 2020, 5, 23), PayDate = new DateTime( 2020, 5, 23), Amount = 6000, PayWay = "信用卡", Category="十一", Others = "" },
                    new DedicationFee { DedicationDate = new DateTime( 2020, 5, 24), PayDate = new DateTime( 2020, 5, 25), Amount = 5000, PayWay = "ATM轉帳/匯款", Category="十一", Others = "" },
                    new DedicationFee { DedicationDate = new DateTime( 2020, 5, 25), PayDate = new DateTime( 2020, 5, 25), Amount = 8000, PayWay = "信用卡", Category="十一", Others = "" },
              };

                m_QpayModel.TotalAmount = 19000;

                return m_QpayModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }


    }
}
