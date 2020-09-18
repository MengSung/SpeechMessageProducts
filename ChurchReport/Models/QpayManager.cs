using ChurchReport.ViewModel;
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

        public String LoginType { get; set; } = "網頁登入";   //登入方式
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

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

                    String PhoneNumber = "";
                    if (Mobile != "")
                    {
                        PhoneNumber = Mobile;
                    }
                    else
                    {
                        PhoneNumber = HomePhone;
                    }

                    return Json(new { status = "1", clicktype = "查詢", DedicationNumber = DedicationNumber, NationId = NationId, FullName = FullName, Mobile = PhoneNumber, LastSixDigit = LastSixDigit, message = DedicationResult, DedicationResult = DedicationResult });
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
                        if ( ( PhoneNumber = m_ToolUtilityClass.GetEntityStringAttribute(aContact, "mobilephone") ) != "" )
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "永和禮拜堂 : 綁定錯誤 => " + ErrorString);

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
                // 身分證字號
                m_QpayModel.NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id");

                //奉獻類別
                m_QpayModel.Category = "十一奉獻";
                //付款方式
                m_QpayModel.PayWay = "信用卡";
                //奉獻日期
                m_QpayModel.DedicationDate = DateTime.Now;
                //奉獻分堂
                m_QpayModel.DedicateLocation = "永和禮拜堂";

                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString(), m_QpayModel.QueryStartDate, m_QpayModel.QueryEndDate);

                m_QpayModel.TotalAmount = 0;
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
                }

                // 收費單清單
                m_QpayModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_QpayModel.FullName, LineLoginContact.Id.ToString(), m_QpayModel.QueryStartDate, m_QpayModel.QueryEndDate);

                m_QpayModel.TotalAmount = 0;
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
        #region 電腦網頁登入
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

                m_QpayModel.Category = "十一奉獻";
                m_QpayModel.PayWay = "信用卡";
                //奉獻分堂
                m_QpayModel.DedicateLocation = "永和禮拜堂";

                m_QpayModel.OtherCategoryArray = new List<String>();
                EntityCollection TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("宣道支持奉獻(請勿刪除)");
                String Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }

                if (m_QpayModel.CreditCardList == null)
                {
                    m_QpayModel.CreditCardList = new List<CreditCard>();
                }
                else
                {
                    m_QpayModel.CreditCardList.Clear();
                }
                // 處理信用卡清單
                ProcessCreditCard();

                String[] OtherCategoryArray = Description.Split(',');
                foreach (String OtherCategory in OtherCategoryArray)
                {
                    m_QpayModel.OtherCategoryArray.Add(OtherCategory);
                }

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
                    else if (DedicationResult.Contains("*** 請依照訊息付款 ***") != true)
                    {
                        PayWay = "信用卡";
                        return Json(new { status = "1", message = "感謝您的奉獻", DedicationResult = DedicationResult, PayWay = PayWay });
                    }
                    else
                    {
                        PayWay = "虛擬帳號";
                        return Json(new { status = "1", message = "感謝您的奉獻", DedicationResult = DedicationResult, PayWay = PayWay });
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
        #endregion
        #region 與官網整合串連，決定登入者
        public Entity GetLoginContactQpay(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                // 透過帳號密碼登入畫面進入的
                EntityCollection aLoginContactCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(aGalleryViewModel.FullName);

                if (aLoginContactCollection.Entities.Count > 0)
                {
                    return aLoginContactCollection.Entities[0];
                }
                else
                {
                    // 沒找到奉獻者
                    return CreateQpayContact( aGalleryViewModel );
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
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "new_personal_id", aGalleryViewModel.NationId);
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
                                ExpireDate = ProcessExpireDate (VisaCCTokenSplit[3])
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
        public String ProcessExpireDate(String aExpiredDate )
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
                // 刪除約會
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
                case 100000002:
                    return "愛心奉獻";
                case 100000003:
                    return "五餅二魚";
                case 100000004:
                    return "代收代轉";
                case 100000005:
                    return "宣教奉獻";
                case 100000006:
                    return "其他奉獻";
                default:
                    return "十一奉獻";
            }
        }
        #endregion
    }
}
