using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.WebServiceConnector;
using Line.Messaging;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    public class QpayManager : Controller
    {
        #region 資料區
        // 商店編號
        // SANDBOX 測試用
        //string m_ShopNo = "NA0149_001";
        // 永豐金流正式環境
        string m_ShopNo = "DA3009_001";

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public QpayModel m_QpayModel { get; set; } = new QpayModel();

        private QPayProcessor m_QPayProcessor = new QPayProcessor();

        public Entity m_Contact;

        public String LoginType { get; set; } = "網頁登入";   //登入方式

        // 客製化
        // 神住611靈糧堂
        private const String CHANNEL_ACCESS_TOKEN = @"e4DmmyIWDuKndlRjHR3BscuVYoqlk9SVxhFXhoZJyhCmBKzIKk9j89bMKLPBoX/Foxvpm/H5XKqA5yu8xjDyxRtdc04LPNvcBRDwzdu1ovcX1L3ErJZkL06pHSRfjvOKBQTMZdiA6j7TnisCPUqwXwdB04t89/1O/w1cDnyilFU=";
        private LineMessagingClient m_LineMessagingClient { get; set; }
        private PushUtility m_PushUtility { get; set; }

        #endregion
        #region 初始化
        public QpayManager()
        {
            // 客製化，請選擇
            // 神住611靈糧堂(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region Line 單獨登入
        public async Task<IActionResult> SaveKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                if (QpayModel.ClickType == "查詢")
                {
                    return await QueryKeyInDedication(QpayModel);
                }
                else
                {
                    return await UpdateKeyInDedication(QpayModel);
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public async Task<IActionResult> QueryKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                String DedicationResult = "";

                String DedicationNumber = QpayModel.DedicationNumber != null ? QpayModel.DedicationNumber : "未填奉獻編號";
                String NationId = QpayModel.NationId != null ? QpayModel.NationId : "未填身分證字號";
                String FullName = QpayModel.FullName != null ? QpayModel.FullName : "未填姓名";
                String HomePhone = QpayModel.Mobile != null ? QpayModel.Mobile : "未填手機號碼";
                String Mobile = QpayModel.Mobile != null ? QpayModel.Mobile : "未填手機號碼";
                String LastSixDigit = QpayModel.LastSixDigit != null ? QpayModel.LastSixDigit : "未填帳戶後六碼";

                EntityCollection DedicationContacts = this.m_ToolUtilityClass.QueryDediccationContatsByFetchXml(DedicationNumber, FullName, HomePhone, Mobile, NationId, LastSixDigit);

                if (DedicationContacts.Entities.Count == 1)
                {
                    DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "pager");
                    NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "new_personal_id");
                    FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "fullname");
                    HomePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "telephone2");
                    Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "mobilephone");
                    LastSixDigit = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "new_last_six_digit");

                    Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", DedicationContacts.Entities[0].Id);

                    String ChurchName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aRetrievedContact, "parentcustomerid");

                    String PhoneNumber = "";
                    if (Mobile != "")
                    {
                        PhoneNumber = Mobile;
                    }
                    else
                    {
                        PhoneNumber = HomePhone;
                    }

                    return Json(new { status = "1", clicktype = "查詢", DedicationNumber = DedicationNumber, NationId = NationId, FullName = FullName, Mobile = PhoneNumber, LastSixDigit = LastSixDigit, message = DedicationResult, DedicationResult = DedicationResult, ChurchName = ChurchName });
                }
                else if (DedicationContacts.Entities.Count > 1)
                {
                    //m_QpayModel.SameNameList = new List<SameNameElement>();

                    m_QpayModel.SameNameList.Clear();
                    foreach (Entity aContact in DedicationContacts.Entities)
                    {
                        SameNameElement aSameNameElement = new SameNameElement();
                        aSameNameElement.SameNameElementId = aContact.Id.ToString();
                        aSameNameElement.DedicationNumber = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "pager");
                        aSameNameElement.NationId = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_personal_id");
                        aSameNameElement.FullName = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                        String PhoneNumber;
                        if ((PhoneNumber = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "mobilephone")) != "")
                        {
                            aSameNameElement.Mobile = PhoneNumber;
                        }
                        else
                        {
                            aSameNameElement.Mobile = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "telephone2");
                        }

                        aSameNameElement.SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContact, "new_cell_list_contact");

                        aSameNameElement.ChurchName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContact, "parentcustomerid");

                        m_QpayModel.SameNameList.Add(aSameNameElement);
                    };

                    return Json(new { status = "2", clicktype = "查詢", DedicationNumber = "", NationId = NationId, FullName = "", Mobile = "", message = "", DedicationResult = "" });

                }
                else
                {
                    return Json(new { status = "3", clicktype = "查詢", message = "沒找到這個連絡人", fullname = FullName, DedicationResult = "沒找到這個連絡人" });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public async Task<IActionResult> AuditQueryDedication(QpayModel QpayModel)
        {
            try
            {
                String DedicationResult = "";

                String DedicationNumber = QpayModel.DedicationNumber != null ? QpayModel.DedicationNumber : "未填奉獻編號";
                String NationId = QpayModel.NationId != null ? QpayModel.NationId : "未填身分證字號";
                String FullName = QpayModel.FullName != null ? QpayModel.FullName : "未填姓名";
                String HomePhone = QpayModel.Mobile != null ? QpayModel.Mobile : "未填手機號碼";
                String Mobile = QpayModel.Mobile != null ? QpayModel.Mobile : "未填手機號碼";
                String LastSixDigit = QpayModel.LastSixDigit != null ? QpayModel.LastSixDigit : "未填帳戶後六碼";

                m_QpayModel.QueryStartDate = QpayModel.QueryStartDate != null ? QpayModel.QueryStartDate : new DateTime(DateTime.Now.Year, 1, 1);
                m_QpayModel.QueryEndDate = QpayModel.QueryEndDate != null ? QpayModel.QueryEndDate : DateTime.Now;

                EntityCollection DedicationContacts = this.m_ToolUtilityClass.QueryDediccationContatsByFetchXml(DedicationNumber, FullName, HomePhone, Mobile, NationId, LastSixDigit);

                if (DedicationContacts.Entities.Count == 1)
                {
                    DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "pager");
                    NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "new_personal_id");
                    FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "fullname");
                    HomePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "telephone2");
                    Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "mobilephone");
                    LastSixDigit = this.m_ToolUtilityClass.GetEntityStringAttribute(DedicationContacts.Entities[0], "new_last_six_digit");

                    String PhoneNumber = "";
                    if (Mobile != "")
                    {
                        PhoneNumber = Mobile;
                    }
                    else
                    {
                        PhoneNumber = HomePhone;
                    }

                    SetDedicationFeeList(DedicationContacts.Entities[0]);

                    String TotalAmount = "總金額 = " + m_QpayModel.TotalAmount + "元";

                    return Json(new { status = "1", clicktype = "查詢", DedicationNumber = DedicationNumber, NationId = NationId, FullName = FullName, Mobile = PhoneNumber, LastSixDigit = LastSixDigit, TotalAmount = TotalAmount, message = DedicationResult, DedicationResult = DedicationResult });
                }
                else if (DedicationContacts.Entities.Count > 1)
                {
                    //m_QpayModel.SameNameList = new List<SameNameElement>();

                    m_QpayModel.SameNameList.Clear();
                    foreach (Entity aContact in DedicationContacts.Entities)
                    {
                        SameNameElement aSameNameElement = new SameNameElement();
                        aSameNameElement.SameNameElementId = aContact.Id.ToString();
                        aSameNameElement.DedicationNumber = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "pager");
                        aSameNameElement.NationId = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_personal_id");
                        aSameNameElement.FullName = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                        String PhoneNumber;
                        if ((PhoneNumber = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "mobilephone")) != "")
                        {
                            aSameNameElement.Mobile = PhoneNumber;
                        }
                        else
                        {
                            aSameNameElement.Mobile = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "telephone2");
                        }

                        aSameNameElement.SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContact, "new_cell_list_contact");

                        m_QpayModel.SameNameList.Add(aSameNameElement);
                    };

                    return Json(new { status = "2", clicktype = "查詢", DedicationNumber = "", NationId = NationId, FullName = "", Mobile = "", message = "", DedicationResult = "" });

                }
                else
                {
                    return Json(new { status = "3", clicktype = "查詢", message = "沒找到這個連絡人", fullname = FullName, DedicationResult = "沒找到這個連絡人" });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public async Task<IActionResult> UpdateKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                if (QpayModel.Amount != null && QpayModel.Amount > 0)
                {
                    String DedicationResult = await m_QPayProcessor.SaveKeyInDedication(QpayModel);

                    if (DedicationResult.Contains("錯誤") != true)
                    {
                        return Json(new { status = "1", clicktype = "上傳", message = DedicationResult, DedicationResult = DedicationResult });
                    }
                    else
                    {
                        return Json(new { status = "3", clicktype = "上傳", message = DedicationResult, DedicationResult = DedicationResult });
                    }
                }
                else
                {
                    return Json(new { status = "3", clicktype = "上傳", message = "未輸入奉獻金額" });
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public QpayModel SetDedicationFeeList(String UserLineId)
        {
            try
            {
                Entity LineLoginContact = new Entity("contact");
                if (UserLineId != null)
                {
                    LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);
                }
                else
                {
                    if (this.m_Contact != null)
                    {
                        // 從官網串接金流那邊進來的，所以在官網登入時就建立了連絡人
                        LineLoginContact = this.m_Contact;
                    }
                }

                // 全名
                m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                // 行動電話
                m_QpayModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                // 奉獻單編號
                m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");
                // 身分證字號
                m_QpayModel.NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id");

                //是否上傳國稅局
                if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref LineLoginContact, "new_ntbt_ornot") == true)
                {
                    m_QpayModel.Ntbt = "願意上傳國稅局";
                }
                else
                {
                    m_QpayModel.Ntbt = "不願意上傳國稅局";
                }

                //奉獻類別
                m_QpayModel.Category = "十一奉獻";
                //付款方式
                m_QpayModel.PayWay = "信用卡";
                //奉獻日期
                m_QpayModel.DedicationDate = DateTime.Now;
                //奉獻分堂
                m_QpayModel.DedicateLocation = "神住611靈糧堂";

                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString(), m_QpayModel.QueryStartDate, m_QpayModel.QueryEndDate);

                m_QpayModel.TotalAmount = 0;
                m_QpayModel.DedicationFeeList.Clear();
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon").ToLocalTime();
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date").ToLocalTime();
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_QpayModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    aDedicationFee.PaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_paid_period");
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
                if (m_QpayModel.ClickType == null)
                {
                    // 全名
                    m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                    // 全名
                    m_QpayModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                    // 奉獻單編號
                    m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");

                    //是否上傳國稅局
                    if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref LineLoginContact, "new_ntbt_ornot") == true)
                    {
                        m_QpayModel.Ntbt = "願意上傳國稅局";
                    }
                    else
                    {
                        m_QpayModel.Ntbt = "不願意上傳國稅局";
                    }

                }

                // 收費單清單
                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString(), m_QpayModel.QueryStartDate, m_QpayModel.QueryEndDate);

                m_QpayModel.TotalAmount = 0;
                m_QpayModel.DedicationFeeList.Clear();
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon").ToLocalTime();
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date").ToLocalTime();
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_QpayModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    aDedicationFee.PaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_paid_period");
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
        #endregion
        #region 電腦網頁或是LINE登入
        public QpayModel SetQpayModel(Entity aContact)
        {
            try
            {
                m_Contact = aContact;

                // 全名
                m_QpayModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 奉獻單編號
                m_QpayModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "pager");

                // 身分證字號
                m_QpayModel.NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_personal_id");

                //是否上傳國稅局
                if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aContact, "new_ntbt_ornot") == true)
                {
                    m_QpayModel.Ntbt = "願意上傳國稅局";
                }
                else
                {
                    m_QpayModel.Ntbt = "不願意上傳國稅局";
                }

                m_QpayModel.Category = "十一奉獻";
                m_QpayModel.PayWay = "信用卡";
                //奉獻分堂
                m_QpayModel.DedicateLocation = "神住611靈糧堂";


                #region 宣道支持奉獻
                m_QpayModel.OtherCategoryArray = new List<String>();
                EntityCollection TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("宣道支持奉獻(請勿刪除)");
                String Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }
                //String[] OtherCategoryArray = Description.Split(',');
                String[] OtherCategoryArray = Description.Split(Environment.NewLine.ToCharArray());
                m_QpayModel.OtherCategoryArray.Clear();
                foreach (String OtherCategory in OtherCategoryArray)
                {
                    m_QpayModel.OtherCategoryArray.Add(OtherCategory);
                }
                #endregion

                #region 特別奉獻清單項目
                m_QpayModel.SpecialCategoryArray = new List<String>();
                TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("特別奉獻清單(不可刪除)");
                Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }
                //String[] OtherCategoryArray = Description.Split(',');
                String[] SpecialCategoryArray = Description.Split(Environment.NewLine.ToCharArray());
                m_QpayModel.SpecialCategoryArray.Clear();
                foreach (String SpecialCategory in SpecialCategoryArray)
                {
                    String SpecialCategoryString = ProcessSpecialCategoryString(SpecialCategory);

                    if (SpecialCategoryString != "")
                    {
                        m_QpayModel.SpecialCategoryArray.Add(SpecialCategoryString);
                    }
                }
                #endregion

                #region// 處理信用卡清單
                if (m_QpayModel.CreditCardList == null)
                {
                    m_QpayModel.CreditCardList = new List<CreditCard>();
                }
                else
                {
                    m_QpayModel.CreditCardList.Clear();
                }

                ProcessCreditCard();
                #endregion

                if (m_QpayModel.DedicationBookingList == null)
                {
                    m_QpayModel.DedicationBookingList = new List<DedicationBooking>();
                }
                else
                {
                    m_QpayModel.DedicationBookingList.Clear();
                }
                // 處理認獻清單
                ProcessDedicationBooking();

                m_QpayModel.QueryStartDate = new DateTime(DateTime.Now.Year, 1, 1);
                m_QpayModel.QueryEndDate = DateTime.Now;

                // 教會職稱是否是會計
                m_QpayModel.IsAOfficeWorker = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_church_jobtitle") == "會計" ? true : false;

                return m_QpayModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public async Task<IActionResult> SaveQPayDedication(QpayModel QpayModel)
        {
            try
            {
                if (QpayModel.Amount != null && QpayModel.Amount > 0)
                {
                    String DedicationResult = await m_QPayProcessor.CreateFeeAsync(m_Contact, QpayModel);

                    String PayWay = "";
                    if (DedicationResult == "信用卡繳費失敗!")
                    {
                        return Json(new { status = "2", message = "信用卡繳費失敗!" });
                    }
                    else if (DedicationResult == "信用卡定期定額建立失敗!")
                    {
                        return Json(new { status = "2", message = "信用卡定期定額建立失敗!" });
                    }
                    else if (DedicationResult.Contains("*** 請依照訊息付款 ***") != true)
                    {
                        PayWay = "信用卡";
                        return Json(new { status = "1", message = "正在處理您的奉獻中.....", DedicationResult = DedicationResult, PayWay = PayWay });
                    }
                    else
                    {
                        PayWay = "虛擬帳號";
                        return Json(new { status = "1", message = "正在處理您的奉獻中.....", DedicationResult = DedicationResult, PayWay = PayWay });
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        #endregion
        #region 與官網整合串連，決定登入者
        public Entity GetLoginContactQpay(GalleryViewModel aGalleryViewModel, ref String QueryResult)
        {
            try
            {
                // 透過身分證字號尋找連絡人
                EntityCollection aLoginContactCollection = m_ToolUtilityClass.RetrieveContactCollectionByNationId(aGalleryViewModel.NationId);

                if (aLoginContactCollection.Entities.Count > 0)
                {
                    // 有找到奉獻者

                    // 透過姓名全名再找一遍
                    Entity aLoginContact = FilterQpayContactByFullName(aGalleryViewModel, aLoginContactCollection);

                    if (aLoginContact != null)
                    {
                        // 姓名跟身分證字號一樣的有找到
                        // 奉獻者的欄位 行動電話、身分證字號、奉獻編號 沒有值才會加上去，所以不會覆蓋原有的
                        //UpdateQpayContact(aGalleryViewModel, ref aLoginContact);

                        QueryResult = aGalleryViewModel.FullName + "成功登入";

                        return aLoginContact;
                    }
                    else
                    {
                        // 顯示錯誤
                        // 有找到身分證字號一樣，但是姓名不一樣
                        QueryResult = aGalleryViewModel.FullName + "登入錯誤:" + "有找到身分證字號，但是姓名卻不一樣";

                        return null;
                    }
                }
                else
                {
                    // 透過姓名全名再找一遍
                    EntityCollection aFullNameContactCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(aGalleryViewModel.FullName);

                    if (aFullNameContactCollection.Entities.Count > 0)
                    {
                        // 有找到身分證字號不一樣，但是姓名卻有一樣
                        // 仍然新增一個連絡人
                        //QueryResult = aGalleryViewModel.FullName + "登入錯誤:" + "有找到姓名，但是身分證字號卻不一樣";

                        QueryResult = aGalleryViewModel.FullName + "成功登入" + "為您在系統中建立了資料";
                        return CreateQpayContact(aGalleryViewModel);
                    }
                    else
                    {
                        // 沒找到姓名跟身分證字號一樣的
                        // 沒找到奉獻者，所以新增一個連絡人
                        QueryResult = aGalleryViewModel.FullName + "成功登入" + "為您在系統中建立了資料";
                        return CreateQpayContact(aGalleryViewModel);

                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Entity CreateQpayContact(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                Entity aContactToCreate = new Entity("contact");

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "lastname", aGalleryViewModel.FullName);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "mobilephone", aGalleryViewModel.Mobile);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "address2_line1", aGalleryViewModel.Address);//設定住家地址
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "new_personal_id", aGalleryViewModel.NationId);

                // 一般小組新增的新人，委身類型設為"新朋友"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactToCreate, "customertypecode", 100000000);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "description", "透過官網建立的奉獻新朋友");

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "pager", aGalleryViewModel.NationId);

                return this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.CreateEntity(aContactToCreate));
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Entity FilterQpayContactByFullName(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            try
            {
                foreach (Entity aContact in aContactEntityCollection.Entities)
                {
                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname") == aGalleryViewModel.FullName)
                    {
                        // 奉獻者姓名與身分證字號相同
                        return aContact;
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Entity FilterQpayContactByNationId(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            try
            {
                foreach (Entity aContact in aContactEntityCollection.Entities)
                {
                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_personal_id") == aGalleryViewModel.NationId || this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "pager") == aGalleryViewModel.NationId)
                    {
                        // 奉獻者姓名與身分證字號相同或是奉獻者姓名與奉獻編號相同
                        return aContact;
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public Entity FilterQpayContactByMobile(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            try
            {
                foreach (Entity aContact in aContactEntityCollection.Entities)
                {
                    if (this.m_ToolUtilityClass.FilterDigit(this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "mobilephone")) == this.m_ToolUtilityClass.FilterDigit(aGalleryViewModel.Mobile))
                    {
                        // 奉獻者姓名與行動電話相同
                        return aContact;
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void UpdateQpayContact(GalleryViewModel aGalleryViewModel, ref Entity aContact)
        {
            try
            {
                // 奉獻旗標預設為 FALSE
                bool Updateflag = false;

                // 奉獻者的欄位 行動電話、身分證字號、奉獻編號 沒有值才會加上去，所以不會覆蓋原有的
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "mobilephone") == "")
                {
                    // 奉獻者沒有行動電話
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "mobilephone", aGalleryViewModel.Mobile);
                    Updateflag = true;
                }
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_personal_id") == "")
                {
                    // 奉獻者沒有身分證字號
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "new_personal_id", aGalleryViewModel.NationId);
                    Updateflag = true;
                }
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "pager") == "")
                {
                    // 奉獻者沒有奉獻編號
                    m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "pager", aGalleryViewModel.NationId);
                    Updateflag = true;
                }

                if (Updateflag == true)
                {
                    // 旗標為TRUE所以要更新奉獻者
                    this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        #endregion
        #region 信用卡管理
        public void ProcessCreditCard()
        {
            try
            {
                GetCreditCardList(this.m_Contact);
                //預設信用卡
                if (m_QpayModel.CreditCardList.Count > 0)
                {
                    // 選第1個信用卡
                    m_QpayModel.SelectedCreditCard = m_QpayModel.CreditCardList[0].CCToken;
                    // 選最後個信用卡
                    //m_QpayModel.SelectedCreditCard = m_QpayModel.CreditCardList[m_QpayModel.CreditCardList.Count - 1].CCToken;
                }
                else
                {
                    // 沒有預存信用卡
                    m_QpayModel.SelectedCreditCard = null;
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void GetCreditCardList(Entity aContact)
        {
            #region// 取得連絡人信用卡資訊

            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

            if (VisaInfo != "")
            {
                String[] VisaInfoSplit = VisaInfo.Split('|');

                if (VisaInfoSplit.Length > 0)
                {
                    foreach (String CreditCard in VisaInfoSplit)
                    {
                        String[] VisaCCTokenSplit = CreditCard.Split('，');

                        if (VisaCCTokenSplit.Length == 4)
                        {
                            m_QpayModel.CreditCardList.Add(new CreditCard
                            {
                                CCToken = VisaCCTokenSplit[0],
                                LeftCardNumber = VisaCCTokenSplit[1],
                                RightCardNumber = VisaCCTokenSplit[2],
                                CreditCardNumber = VisaCCTokenSplit[1] + "-XXXX-" + VisaCCTokenSplit[2],
                                ExpireDate = ProcessExpireDate(VisaCCTokenSplit[3])
                            });
                        }
                        else
                        {
                            //return null;
                        }

                    }
                }
                else
                {
                    return;
                }

            }
            else
            {
                return;
            }
            #endregion
        }
        public String ProcessExpireDate(String aExpiredDate)
        {
            #region//轉換過期日

            //char[] CharArr = aExpiredDate.ToCharArray();
            //int Year = Convert.ToInt32(  "20" + new string(new char[] { CharArr[0], CharArr[1] }) );
            //int Month = Convert.ToInt32( new string(new char[] { CharArr[2], CharArr[3] }) );

            //return new DateTime(Year, Month, 1).ToLocalTime().ToShortDateString();

            char[] CharArr = aExpiredDate.ToCharArray();
            return new string(new char[] { CharArr[0], CharArr[1] }) + "/" + new string(new char[] { CharArr[2], CharArr[3] });
            #endregion
        }
        public void DeleteCreditCard(CreditCard aCreditCardToDelete)
        {
            try
            {
                // 刪除信用卡
                m_QpayModel.CreditCardList.Remove(aCreditCardToDelete);

                String VisaInfo = "";

                foreach (CreditCard aCreditCard in m_QpayModel.CreditCardList)
                {
                    VisaInfo += aCreditCard.CCToken + "，" + aCreditCard.LeftCardNumber + "，" + aCreditCard.RightCardNumber + "，" + aCreditCard.ExpireDate + "|";
                }

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_visa_info", VisaInfo);

                this.m_ToolUtilityClass.UpdateEntity(ref m_Contact);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void DeleteSameNameContact(SameNameElement aSameNameElement)
        {
            try
            {
                // 刪除約會
                m_QpayModel.SameNameList.RemoveAt(1);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 認獻管理
        public void ProcessDedicationBooking()
        {
            try
            {
                GetDedicationBookingList(this.m_Contact);

                //預設認獻
                if (m_QpayModel.DedicationBookingList.Count > 0)
                {
                    // 選第1個認獻
                    m_QpayModel.SelectedDedicationBooking = m_QpayModel.DedicationBookingList[0].EntityId;
                    // 選最後個認獻
                    //m_QpayModel.SelectedDedicationBooking = m_QpayModel.CreditCardList[m_QpayModel.DedicationBookingList.Count - 1].EntityId;
                }

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void GetDedicationBookingList(Entity aContact)
        {
            #region// 取得連絡人認獻資訊
            EntityCollection aDedicationBookingEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationBookingByFetchXml(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname"), aContact.Id.ToString());

            m_QpayModel.DedicationBookingList.Clear();

            foreach (Entity aDedicationBookingEntity in aDedicationBookingEntityCollection.Entities)
            {
                Entity aRetrievedDedicationBookingEntity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", aDedicationBookingEntity.Id);

                DedicationBooking aDedicationBooking = new DedicationBooking();

                // 實體 ID
                aDedicationBooking.EntityId = aRetrievedDedicationBookingEntity.Id.ToString();
                // 類別
                aDedicationBooking.DedicationCategory = ConvertToCategory(this.m_ToolUtilityClass.GetOptionSetAttribute(aRetrievedDedicationBookingEntity, "new_dedication_category"));
                // 認獻單狀態
                aDedicationBooking.DedicationBookingStatus = ConvertToDedicationBookingStatus(this.m_ToolUtilityClass.GetOptionSetAttribute(aRetrievedDedicationBookingEntity, "new_dedication_booking_status"));
                // 每期金額
                aDedicationBooking.AmountPerStage = Decimal.Truncate(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aRetrievedDedicationBookingEntity, "new_amount_per_stage").Value).ToString();
                // 總期數
                aDedicationBooking.TotalStages = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedDedicationBookingEntity, "new_total_stages");
                // 應付總金額
                aDedicationBooking.DedicationAmount = Decimal.Truncate(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aRetrievedDedicationBookingEntity, "new_dedication_amount").Value).ToString();
                // 目前期數
                aDedicationBooking.PaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedDedicationBookingEntity, "new_paid_period");
                // 已付金額
                aDedicationBooking.RollupPaidFee = Decimal.Truncate(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aRetrievedDedicationBookingEntity, "new_rollup_paid_fee").Value).ToString();
                // 認獻開始日期
                aDedicationBooking.StartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedDedicationBookingEntity, "new_dedication_start_date").ToLocalTime().ToShortDateString();
                // 認獻結束日期
                aDedicationBooking.EndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedDedicationBookingEntity, "new_dedication_end_date").ToLocalTime().ToShortDateString();

                m_QpayModel.DedicationBookingList.Add(aDedicationBooking);
            }
            #endregion
            #region// 取得連絡人認獻資訊 (開發用的虛擬資料)
            //m_QpayModel.DedicationBookingList.Clear();

            //m_QpayModel.DedicationBookingList.Add(new DedicationBooking
            //{
            //    EntityId = "001",
            //    DedicationCategory = "十一奉獻",
            //    DedicationBookingStatus = "進行中",
            //    AmountPerStage = "5000",                    // 每期金額
            //    TotalStages = "12期",
            //    DedicationAmount = "60000",                 //應付總金額
            //    PaidPeriod = "第2期",
            //    RollupPaidFee = "10000",
            //    StartDate = "2021/4/13",
            //    EndDate = "2022/4/13"
            //});
            //m_QpayModel.DedicationBookingList.Add(new DedicationBooking
            //{
            //    EntityId = "002",
            //    DedicationCategory = "感恩奉獻",
            //    DedicationBookingStatus = "進行中",
            //    AmountPerStage = "6000",                    // 每期金額
            //    TotalStages = "12期",
            //    DedicationAmount = "72000",                 //應付總金額
            //    PaidPeriod = "第2期",
            //    RollupPaidFee = "12000",
            //    StartDate = "2021/5/13",
            //    EndDate = "2022/5/13"
            //});
            #endregion
        }
        public async Task<string> DeleteDedicationBooking(DedicationBooking aDedicationBookingToDelete)
        {
            try
            {
                Entity aDedicationBookingToDeleteEntity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", new Guid(aDedicationBookingToDelete.EntityId));

                if (aDedicationBookingToDeleteEntity != null)
                {
                    //"P"請款要求
                    //"C"取消授權
                    //"R"退款要求
                    //"E" 中止定期定額 要求
                    OrderMaintain aOrderMaintain = await OrderMaintain(this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationBookingToDeleteEntity, "new_q_pay_card_order_no"), "E");

                    if (aOrderMaintain != null && aOrderMaintain.Status == "S")
                    {
                        // 取消認獻
                        m_QpayModel.DedicationBookingList.Remove(aDedicationBookingToDelete);
                        // 設定認獻狀態: 已取消
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingToDeleteEntity, "new_dedication_booking_status", 100000004);

                        // 認獻單備註 = 寫入中止定期定額成功的原因
                        String Result =
                                this.m_ToolUtilityClass.GetEntityStringAttribute(m_Contact, "fullname") + ":中止定期定額成功!" + Environment.NewLine +
                                "類別:" + aDedicationBookingToDelete.DedicationCategory + Environment.NewLine +
                                "每期金額:" + aDedicationBookingToDelete.AmountPerStage + Environment.NewLine +
                                "總期數:" + aDedicationBookingToDelete.TotalStages + Environment.NewLine +
                                "應付總金額:" + aDedicationBookingToDelete.DedicationAmount + Environment.NewLine +
                                "目前期數:" + aDedicationBookingToDelete.PaidPeriod + Environment.NewLine +
                                "已付金額:" + aDedicationBookingToDelete.RollupPaidFee + Environment.NewLine +
                                "開始日期:" + aDedicationBookingToDelete.StartDate + Environment.NewLine +
                                "結束日期:" + aDedicationBookingToDelete.EndDate + Environment.NewLine +
                                aOrderMaintain.Description + Environment.NewLine +
                                "--------------------------------------" + Environment.NewLine;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToDeleteEntity, "new_explain", this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingToDeleteEntity, "new_explain") + Environment.NewLine + Result);
                        this.m_ToolUtilityClass.UpdateEntity(ref aDedicationBookingToDeleteEntity);

                        // 送出 LINE 訊息
                        String aContactLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Contact, "new_lineid");
                        if (aContactLineId != "")
                        {
                            m_PushUtility.SendMessage(aContactLineId, Result);
                        }

                    }
                    else
                    {
                        // 認獻單備註 = 寫入中止定期定額成功的原因
                        String Result =
                                this.m_ToolUtilityClass.GetEntityStringAttribute(m_Contact, "fullname") + ":中止定期定額失敗!" + Environment.NewLine +
                                "類別:" + aDedicationBookingToDelete.DedicationCategory + Environment.NewLine +
                                "每期金額:" + aDedicationBookingToDelete.AmountPerStage + Environment.NewLine +
                                "總期數:" + aDedicationBookingToDelete.TotalStages + Environment.NewLine +
                                "應付總金額:" + aDedicationBookingToDelete.DedicationAmount + Environment.NewLine +
                                "目前期數:" + aDedicationBookingToDelete.PaidPeriod + Environment.NewLine +
                                "已付金額:" + aDedicationBookingToDelete.RollupPaidFee + Environment.NewLine +
                                "開始日期:" + aDedicationBookingToDelete.StartDate + Environment.NewLine +
                                "結束日期:" + aDedicationBookingToDelete.EndDate + Environment.NewLine +
                                aOrderMaintain.Description + Environment.NewLine +
                                "--------------------------------------" + Environment.NewLine;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingToDeleteEntity, "new_explain", this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingToDeleteEntity, "new_explain") + Environment.NewLine + Result);
                        this.m_ToolUtilityClass.UpdateEntity(ref aDedicationBookingToDeleteEntity);

                        // 送出 LINE 訊息
                        String aContactLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Contact, "new_lineid");
                        if (aContactLineId != "")
                        {
                            m_PushUtility.SendMessage(aContactLineId, Result);
                        }
                    }

                    return aOrderMaintain.Description;
                }
                else
                {
                    return "";
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 查詢找不到時新增新人
        public async Task<IActionResult> CreateContact(string FullName)
        {
            try
            {
                // 建立新人
                EntityCollection aQueriedContacts = this.m_ToolUtilityClass.RetrieveContactEntityByFullNameCollection(FullName);

                if (aQueriedContacts.Entities.Count == 0)
                {
                    Entity aContactToCreate = new Entity("contact");

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "lastname", FullName);
                    Guid aContactToCreateId = this.m_ToolUtilityClass.CreateEntity(aContactToCreate);


                    // 自動奉獻編號
                    // Entity aContactCreated = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactToCreateId);
                    // AutoDedicationNumbering(FullName, aContactCreated);

                    return Json(new { status = "1", message = "成功建立了" + FullName });
                }
                else
                {
                    return Json(new { status = "2", message = "錯誤: 有同名同姓的" + FullName });

                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "神住611靈糧堂 : 註冊錯誤 => " + ErrorString);

                return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        /// <summary>
        /// 自動奉獻編號
        /// </summary>
        /// <param name="aContactCreated"></param>
        private void AutoDedicationNumbering(String FullName, Entity aContactCreated)
        {
            //Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactCreated.Id);

            if (FullName.Length <= 5)
            {
                //新增的是聯絡人
                SetDedicationNumber("0", aContactCreated);
            }
            else
            {
                //新增的是公司組織
                SetDedicationNumber("9", aContactCreated);
            }
        }
        private void SetDedicationNumber(String StartNumber, Entity aContactCreated)
        {
            EntityCollection aContactEntityCollection = this.m_ToolUtilityClass.QueryContatsByStartedDedicationNumber(StartNumber);
            if (aContactEntityCollection.Entities.Count > 0)
            {
                int NewNumber = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntityCollection.Entities[0], "pager")) + 1;

                this.m_ToolUtilityClass.SetEntityStringAttribute(aContactCreated, "pager", StartNumber + NewNumber.ToString());

                this.m_ToolUtilityClass.UpdateEntity(aContactCreated);
            }
        }

        #endregion
        #region 工具區
        private String ConvertToPayway(Entity aFeeEntity)
        {
            switch (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way"))
            {
                case 100000004:
                    return "未知";
                case 100000000:
                    return "現金";
                case 100000001:
                    return "信用卡";
                case 100000002:
                    return "ATM轉帳";
                case 100000003:
                    return "超商付款";
                case 100000006:
                    return "銀行轉帳";
                default:
                    return "未知";
            }
        }
        public String ConvertToCategory(Entity aFeeEntity)
        {
            switch (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_category"))
            {
                case 100000000:
                    return "十一奉獻";
                case 100000001:
                    return "感恩奉獻";
                case 100000003:
                    return "建殿奉獻";
                case 100000002:
                    return "慈惠奉獻";
                case 100000004:
                    return "苗圃奉獻";
                case 100000005:
                    return "宣教奉獻";
                case 100000006:
                    return "其他奉獻";
                case 100000007:
                    return "豐盛120奉獻";
                case 100000008:
                    return "特別奉獻";
                default:
                    return "十一奉獻";
            }
        }
        public String ConvertToCategory(int OptionSetValue)
        {
            switch (OptionSetValue)
            {
                case 100000000:
                    return "十一奉獻";
                case 100000001:
                    return "感恩奉獻";
                case 100000003:
                    return "建殿奉獻";
                case 100000002:
                    return "慈惠奉獻";
                case 100000004:
                    return "苗圃奉獻";
                case 100000005:
                    return "宣教奉獻";
                case 100000006:
                    return "其他奉獻";
                case 100000007:
                    return "豐盛120奉獻";
                case 100000008:
                    return "特別奉獻";
                default:
                    return "十一奉獻";
            }
        }
        public String ConvertToDedicationBookingStatus(int OptionSetValue)
        {
            switch (OptionSetValue)
            {
                case 100000000:
                    return "尚未啟動";
                case 100000001:
                    return "進行中";
                case 100000002:
                    return "已結案";
                case 100000003:
                    return "啟動失敗";
                case 100000004:
                    return "已取消";
                default:
                    return "十一奉獻";
            }
        }
        public String ProcessSpecialCategoryString(String SpecialCategory)
        {
            String[] OtherCategoryArray = SpecialCategory.Split(',');
            if (OtherCategoryArray.Length == 2)
            {
                String[] StartAndEndDateArray = OtherCategoryArray[0].Split('~');
                if (StartAndEndDateArray.Length == 2)
                {
                    DateTime aStartDate = ParseDateTime(StartAndEndDateArray[0]).ToLocalTime();
                    DateTime aEndDate = ParseDateTime(StartAndEndDateArray[1]).ToLocalTime().AddDays(1);

                    if (aStartDate.Date < DateTime.Now && DateTime.Now < aEndDate.Date)
                    {
                        return OtherCategoryArray[1];
                    }
                    else
                    {
                        return "";
                    }
                }
                else
                {
                    return "";
                }

            }
            else
            {
                return "";
            }
        }
        #endregion
        #region 副程式
        private DateTime ParseDateTime(String strDateOrTime)
        {
            try
            {
                CultureInfo provider = CultureInfo.InvariantCulture;
                DateTimeStyles style = DateTimeStyles.None;  //default is None

                //return DateTime.ParseExact(strDateOrTime, "yyyy/MM/dd", provider, style);
                return DateTime.Parse(strDateOrTime);
            }
            catch (Exception e)  //ParseException
            {
                Console.WriteLine("*** ERROR in _GetDateTime(" + strDateOrTime + ") => 改為現在時間 [" + e.Message + "]");

                return DateTime.Now;
            }
        }
        #endregion
        #region 永豐金流工具區
        public async Task<OrderMaintain> OrderMaintain(String aOrderNo, String aCommand)
        {
            OrderMaintainReq orderMaintainReq = new OrderMaintainReq()
            {
                ShopNo = m_ShopNo,
                OrderNo = aOrderNo,
                Command = aCommand
            };

            OrderMaintain retObj = QPayToolkit.OrderMaintain(orderMaintainReq);

            //ltResponse.Text = QPayCommon.SerializeToJson(retObj);
            return retObj;
        }
        #endregion
    }
}
