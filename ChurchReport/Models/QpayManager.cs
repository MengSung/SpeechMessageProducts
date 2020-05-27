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
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public QpayModel m_QpayModel { get; set; } = new QpayModel();            

        private QPayProcessor m_QPayProcessor = new QPayProcessor();

        public Entity m_Contact;
        #endregion
        #region Line 單獨登入
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
                    String DedicationResult = await m_QPayProcessor.CreateFeeAsync(LineUserId, QpayModel);

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
        public async Task<IActionResult> SaveKeyInDedication( QpayModel QpayModel )
        {
            try
            {
                if (QpayModel.Amount != null && QpayModel.Amount > 0)
                {
                    String DedicationResult = await m_QPayProcessor.SaveKeyInDedication( QpayModel );

                    if (DedicationResult.Contains("錯誤") != true)
                    {
                        return Json(new { status = "1", message = DedicationResult, DedicationResult = DedicationResult });
                    }
                    else
                    {
                        return Json(new { status = "2", message = DedicationResult, DedicationResult = DedicationResult });
                    }
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
        public QpayModel SetDedicationFeeList(String UserLineId)
        {
            try
            {
                Entity LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);

                // 全名
                m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                // 全名
                m_QpayModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                // 奉獻單編號
                m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");

                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString());

                m_QpayModel.TotalAmount = 0;
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon");
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date");
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_QpayModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    m_QpayModel.DedicationFeeList.Add(aDedicationFee);
                }

                return m_QpayModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public QpayModel SetDedicationFeeList(Entity LineLoginContact)
        {
            try
            {
                // 全名
                m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                // 全名
                m_QpayModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                // 奉獻單編號
                m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");

                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString());

                m_QpayModel.TotalAmount = 0;
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon");
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date");
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_QpayModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    m_QpayModel.DedicationFeeList.Add(aDedicationFee);
                }

                return m_QpayModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        private String ConvertToPayway(Entity aFeeEntity)
        {
            switch (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way"))
            {
                case 100000004:
                    return  "未知";
                case 100000000:
                    return  "現金";
                case 100000001:
                    return  "信用卡";
                case 100000002:
                    return  "ATM轉帳";
                case 100000003:
                    return  "超商付款";
                default:
                    return  "未知";
            }
        }
        public String ConvertToCategory( Entity aFeeEntity )
        {
            switch (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_category"))
            {
                case 100000000:
                    return "十一";
                case 100000001:
                    return "感恩";
                case 100000002:
                    return "建堂";
                case 100000003:
                    return "宣教";
                case 100000004:
                    return "急難救助";
                case 100000005:
                    return "青年事工";
                case 100000006:
                    return "萬軍";
                case 100000007:
                    return "其他";
                default:
                    return "十一";
            }
        }
        #endregion
        #region 電腦網頁登入
        public QpayModel SetQpayModel( Entity aContact )
        {
            try
            {
                m_Contact = aContact;

                // 全名
                m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 奉獻單編號
                m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "pager");


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
        public async Task<IActionResult> SaveQPayDedication( QpayModel QpayModel )
        {
            try
            {
                if (QpayModel.Amount != null && QpayModel.Amount > 0)
                {
                    String DedicationResult = await m_QPayProcessor.CreateFeeAsync( m_Contact, QpayModel);

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

        #endregion
    }
}
