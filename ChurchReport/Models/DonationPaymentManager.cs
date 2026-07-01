using ChurchReport.Payments;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.WebServiceConnector;
using Line.Messaging;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    /// <summary>
    /// ChurchReport 奉獻付款 UI 狀態與產品流程管理器。
    /// 這個類別留在 ChurchReport 專案，負責 UI 表單狀態、CRM 更新、LINE 通知與付款建單前後的產品流程。
    /// 可重用金流核心只處理 provider 協定，因此這裡透過 DonationPaymentProcessor 與 IDonationPaymentCreateGatewayAdapter 接到抽離後的金流模組。
    /// </summary>
    public class DonationPaymentManager : Controller
    {
        #region 資料區
        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
        static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        // 商店編號
        // SANDBOX 測試用
        //string m_ShopNo = "NA0149_001";
        //string m_ShopNo = m_Configuration["Sandbox:ShopNo"];
        // 永豐金流正式環境
        //string m_ShopNo = "DA4195_001";
        //string m_ShopNo = m_Configuration["Sinopac:ShopNo"];
        string m_ShopNo = "";

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        public DonationPaymentFormModel m_DonationPaymentFormModel { get; set; } = new DonationPaymentFormModel();

        /// <summary>
        /// ChurchReport 奉獻付款主流程。
        /// DonationPaymentManager 是產品層的主要名稱；舊 DonationPaymentManager 只保留在薄相容 wrapper。
        /// 實際金流流程由 DonationPaymentProcessor 承擔，避免產品流程繼續綁在永豐 QPay 名稱上。
        /// </summary>
        private DonationPaymentProcessor m_DonationPaymentProcessor;

        /// <summary>
        /// 建立付款用的中性 adapter。
        /// 這個 adapter 是 ChurchReport 與抽離後金流核心的邊界，未來其他 ASP.NET Core 產品可用同一種模式接入。
        /// </summary>
        private readonly IDonationPaymentCreateGatewayAdapter m_DonationPaymentCreateGatewayAdapter;

        // 登入的連絡人
        public Entity m_LoginContact;

        // 收費單的連絡人
        public Entity m_Contact;

        public String LoginType { get; set; } = "網頁登入";   //登入方式

        private LineMessagingClient m_LineMessagingClient { get; set; }
        private PushUtility m_PushUtility { get; set; }

        #endregion
        #region 初始化
        public DonationPaymentManager()
            : this((IDonationPaymentCreateGatewayAdapter)null)
        {
        }

        public DonationPaymentManager(
            DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this((IDonationPaymentCreateGatewayAdapter)donationPaymentCreateGatewayAdapter)
        {
        }

        public DonationPaymentManager(
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        {
            // 商店編號
            if( m_Configuration["Cash_Environment"] == "正式環境" )
            {
                // 永豐金流正式環境
                m_ShopNo = m_Configuration["Sinopac:ShopNo"];
            }
            else
            {
                // SANDBOX 測試用
                m_ShopNo = m_Configuration["Sandbox:ShopNo"];
            }

            // 初始化 LINE Messaging Client (從 appsettings.json 取得 Token)
            string channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);
            m_PushUtility = new PushUtility(m_LineMessagingClient);

            m_DonationPaymentCreateGatewayAdapter = donationPaymentCreateGatewayAdapter;
            m_DonationPaymentProcessor = new DonationPaymentProcessor(
                m_LineMessagingClient,
                m_PushUtility,
                new ReplyUtility(m_LineMessagingClient),
                m_DonationPaymentCreateGatewayAdapter);

        }
        #endregion

        #region 配置讀取方法
        /// <summary>
        /// 從 appsettings.json 讀取 LINE Channel Access Token
        /// 優先依 CrmConnection:Organization 選擇 LineMessaging:{Org}:ChannelAccessToken
        /// 若無則使用 LineMessaging:DefaultOrganization
        /// </summary>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                string organization = m_Configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    string configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    string token = m_Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }

                string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                string defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];
                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[DonationPaymentManager] 警告: LINE Channel Access Token 未設定");
                }
                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentManager] 錯誤: 讀取 LINE Token 配置失敗 - {ex.Message}");
                return string.Empty;
            }
        }
        #endregion
        #region Line 單獨登入
        public async Task<IActionResult> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                if (DonationPaymentFormModel.ClickType == "查詢")
                {
                    return await QueryKeyInDedication(DonationPaymentFormModel);
                }
                else
                {
                    return await UpdateKeyInDedication(DonationPaymentFormModel);
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public async Task<IActionResult> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel, Entity aLoginContact)
        {
            try
            {
                this.m_LoginContact = aLoginContact;
                m_DonationPaymentProcessor.m_LoginContact = aLoginContact;

                if (DonationPaymentFormModel.ClickType == "查詢")
                {
                    return await QueryKeyInDedication(DonationPaymentFormModel);
                }
                else
                {
                    return await UpdateKeyInDedication(DonationPaymentFormModel);
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public async Task<IActionResult> QueryKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                String DedicationResult = "";

                String DedicationNumber = DonationPaymentFormModel.DedicationNumber != null ? DonationPaymentFormModel.DedicationNumber : "未填奉獻編號";
                String NationId = DonationPaymentFormModel.NationId != null ? DonationPaymentFormModel.NationId : "未填身分證字號";
                String FullName = DonationPaymentFormModel.FullName != null ? DonationPaymentFormModel.FullName : "未填姓名";
                String HomePhone = DonationPaymentFormModel.Mobile != null ? DonationPaymentFormModel.Mobile : "未填手機號碼";
                String Mobile = DonationPaymentFormModel.Mobile != null ? DonationPaymentFormModel.Mobile : "未填手機號碼";
                String LastSixDigit = DonationPaymentFormModel.LastSixDigit != null ? DonationPaymentFormModel.LastSixDigit : "未填帳戶後六碼";

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
                    //m_DonationPaymentFormModel.SameNameList = new List<SameNameElement>();

                    m_DonationPaymentFormModel.SameNameList.Clear();
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

                        m_DonationPaymentFormModel.SameNameList.Add(aSameNameElement);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public async Task<IActionResult> AuditQueryDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                String DedicationResult = "";

                String DedicationNumber = DonationPaymentFormModel.DedicationNumber != null ? DonationPaymentFormModel.DedicationNumber : "未填奉獻編號";
                String NationId = DonationPaymentFormModel.NationId != null ? DonationPaymentFormModel.NationId : "未填身分證字號";
                String FullName = DonationPaymentFormModel.FullName != null ? DonationPaymentFormModel.FullName : "未填姓名";
                String HomePhone = DonationPaymentFormModel.Mobile != null ? DonationPaymentFormModel.Mobile : "未填手機號碼";
                String Mobile = DonationPaymentFormModel.Mobile != null ? DonationPaymentFormModel.Mobile : "未填手機號碼";
                String LastSixDigit = DonationPaymentFormModel.LastSixDigit != null ? DonationPaymentFormModel.LastSixDigit : "未填帳戶後六碼";

                m_DonationPaymentFormModel.QueryStartDate = DonationPaymentFormModel.QueryStartDate != null ? DonationPaymentFormModel.QueryStartDate : new DateTime(DateTime.Now.Year, 1, 1);
                m_DonationPaymentFormModel.QueryEndDate = DonationPaymentFormModel.QueryEndDate != null ? DonationPaymentFormModel.QueryEndDate : DateTime.Now;

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

                    String TotalAmount = "總金額 = " + m_DonationPaymentFormModel.TotalAmount + "元";

                    // 回傳同時包含奉獻清單資料，讓前端可直接繫結
                    var feeList = m_DonationPaymentFormModel.DedicationFeeList.Select(f => new
                    {
                        Category = f.Category,
                        DedicationDate = f.DedicationDate,
                        PayDate = f.PayDate,
                        PayWay = f.PayWay,
                        Amount = f.Amount,
                        PaidPeriod = f.PaidPeriod,
                        Others = f.Others
                    }).ToList();

                    return Json(new
                    {
                        status = "1",
                        clicktype = "查詢",
                        DedicationNumber = DedicationNumber,
                        NationId = NationId,
                        FullName = FullName,
                        Mobile = PhoneNumber,
                        LastSixDigit = LastSixDigit,
                        TotalAmount = m_DonationPaymentFormModel.TotalAmount,
                        DedicationFeeList = feeList,
                        message = DedicationResult,
                        DedicationResult = DedicationResult
                    });
                }
                else if (DedicationContacts.Entities.Count > 1)
                {
                    //m_DonationPaymentFormModel.SameNameList = new List<SameNameElement>();

                    m_DonationPaymentFormModel.SameNameList.Clear();
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

                        m_DonationPaymentFormModel.SameNameList.Add(aSameNameElement);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public async Task<IActionResult> UpdateKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                if (DonationPaymentFormModel.Amount != null && DonationPaymentFormModel.Amount > 0)
                {
                    String DedicationResult = await m_DonationPaymentProcessor.SaveKeyInDedication(DonationPaymentFormModel);

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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public DonationPaymentFormModel SetDedicationFeeList(String UserLineId)
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
                m_DonationPaymentFormModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                // 行動電話
                m_DonationPaymentFormModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                // 奉獻單編號
                m_DonationPaymentFormModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");
                // 身分證字號
                m_DonationPaymentFormModel.NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "new_personal_id");

                //是否上傳國稅局
                if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref LineLoginContact, "new_ntbt_ornot") == true)
                {
                    m_DonationPaymentFormModel.Ntbt = "願意上傳國稅局";
                }
                else
                {
                    m_DonationPaymentFormModel.Ntbt = "不願意上傳國稅局";
                }

                //奉獻類別
                m_DonationPaymentFormModel.Category = "十一奉獻";
                //付款方式
                m_DonationPaymentFormModel.PayWay = "信用卡";
                //奉獻日期
                m_DonationPaymentFormModel.DedicationDate = DateTime.Now;
                //奉獻分堂
                m_DonationPaymentFormModel.DedicateLocation = "好牧人";

                m_DonationPaymentFormModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_DonationPaymentFormModel.FullName, LineLoginContact.Id.ToString(), m_DonationPaymentFormModel.QueryStartDate, m_DonationPaymentFormModel.QueryEndDate);

                // [DEDQUERY] temporary diagnostic: show the actual date range used and how many fee rows CRM returned. Remove after diagnosis.
                System.Diagnostics.Trace.WriteLine($"[DEDQUERY] FullName={m_DonationPaymentFormModel.FullName} Start={m_DonationPaymentFormModel.QueryStartDate:yyyy-MM-dd} End={m_DonationPaymentFormModel.QueryEndDate:yyyy-MM-dd} Returned={aDedicationFeeEntityCollection.Entities.Count}");

                m_DonationPaymentFormModel.TotalAmount = 0;
                m_DonationPaymentFormModel.DedicationFeeList.Clear();
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon").ToLocalTime();
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date").ToLocalTime();
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_DonationPaymentFormModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    aDedicationFee.PaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_paid_period");
                    m_DonationPaymentFormModel.DedicationFeeList.Add(aDedicationFee);
                }

                return m_DonationPaymentFormModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public DonationPaymentFormModel SetDedicationFeeList(Entity LineLoginContact)
        {
            try
            {
                if (m_DonationPaymentFormModel.ClickType == null)
                {
                    // 全名
                    m_DonationPaymentFormModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                    // 全名
                    m_DonationPaymentFormModel.Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "mobilephone");
                    // 奉獻單編號
                    m_DonationPaymentFormModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LineLoginContact, "pager");

                    //是否上傳國稅局
                    if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref LineLoginContact, "new_ntbt_ornot") == true)
                    {
                        m_DonationPaymentFormModel.Ntbt = "願意上傳國稅局";
                    }
                    else
                    {
                        m_DonationPaymentFormModel.Ntbt = "不願意上傳國稅局";
                    }

                }

                // 收費單清單
                m_DonationPaymentFormModel.DedicationFeeList = new List<DedicationFee>();
                EntityCollection aDedicationFeeEntityCollection = this.m_ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml(m_DonationPaymentFormModel.FullName, LineLoginContact.Id.ToString(), m_DonationPaymentFormModel.QueryStartDate, m_DonationPaymentFormModel.QueryEndDate);

                // [DEDQUERY] temporary diagnostic: show the actual date range used and how many fee rows CRM returned. Remove after diagnosis.
                System.Diagnostics.Trace.WriteLine($"[DEDQUERY] FullName={m_DonationPaymentFormModel.FullName} Start={m_DonationPaymentFormModel.QueryStartDate:yyyy-MM-dd} End={m_DonationPaymentFormModel.QueryEndDate:yyyy-MM-dd} Returned={aDedicationFeeEntityCollection.Entities.Count}");

                m_DonationPaymentFormModel.TotalAmount = 0;
                m_DonationPaymentFormModel.DedicationFeeList.Clear();
                foreach (Entity aDedicationFeeEntity in aDedicationFeeEntityCollection.Entities)
                {
                    DedicationFee aDedicationFee = new DedicationFee();

                    aDedicationFee.DedicationDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "createdon").ToLocalTime();
                    aDedicationFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDedicationFeeEntity, "new_pay_date").ToLocalTime();
                    aDedicationFee.Amount = Convert.ToInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationFeeEntity, "new_fee_really_paid").Value);
                    m_DonationPaymentFormModel.TotalAmount += aDedicationFee.Amount;
                    aDedicationFee.PayWay = ConvertToPayway(aDedicationFeeEntity);
                    aDedicationFee.Category = ConvertToCategory(aDedicationFeeEntity);
                    aDedicationFee.Others = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_others");
                    aDedicationFee.PaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationFeeEntity, "new_paid_period");
                    m_DonationPaymentFormModel.DedicationFeeList.Add(aDedicationFee);
                }

                return m_DonationPaymentFormModel;
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
        public DonationPaymentFormModel SetDonationPaymentModel(Entity aContact)
        {
            try
            {
                m_Contact = aContact;

                // 全名
                m_DonationPaymentFormModel.FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 奉獻單編號
                m_DonationPaymentFormModel.DedicationNumber = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "pager");

                // 身分證字號
                m_DonationPaymentFormModel.NationId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_personal_id");

                //是否上傳國稅局
                if (this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aContact, "new_ntbt_ornot") == true)
                {
                    m_DonationPaymentFormModel.Ntbt = "願意上傳國稅局";
                }
                else
                {
                    m_DonationPaymentFormModel.Ntbt = "不願意上傳國稅局";
                }

                m_DonationPaymentFormModel.Category = "十一奉獻";
                m_DonationPaymentFormModel.PayWay = "信用卡";
                //奉獻分堂
                m_DonationPaymentFormModel.DedicateLocation = "好牧人";


                #region 宣道支持奉獻
                m_DonationPaymentFormModel.OtherCategoryArray = new List<String>();
                EntityCollection TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("宣道支持奉獻(請勿刪除)");
                String Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }
                //String[] OtherCategoryArray = Description.Split(',');
                String[] OtherCategoryArray = Description.Split(Environment.NewLine.ToCharArray());
                m_DonationPaymentFormModel.OtherCategoryArray.Clear();
                foreach (String OtherCategory in OtherCategoryArray)
                {
                    m_DonationPaymentFormModel.OtherCategoryArray.Add(OtherCategory);
                }
                #endregion

                #region 特別奉獻清單項目
                m_DonationPaymentFormModel.SpecialCategoryArray = new List<String>();
                TaskCollection = m_ToolUtilityClass.RetrieveTaskByFetchXml("特別奉獻清單(不可刪除)");
                Description = "";
                if (TaskCollection.Entities.Count > 0)
                {
                    Description = this.m_ToolUtilityClass.GetEntityStringAttribute(TaskCollection.Entities[0], "description");
                }
                //String[] OtherCategoryArray = Description.Split(',');
                String[] SpecialCategoryArray = Description.Split(Environment.NewLine.ToCharArray());
                m_DonationPaymentFormModel.SpecialCategoryArray.Clear();
                foreach (String SpecialCategory in SpecialCategoryArray)
                {
                    String SpecialCategoryString = ProcessSpecialCategoryString(SpecialCategory);

                    if (SpecialCategoryString != "")
                    {
                        m_DonationPaymentFormModel.SpecialCategoryArray.Add(SpecialCategoryString);
                    }
                }
                #endregion

                #region// 處理信用卡清單
                if (m_DonationPaymentFormModel.CreditCardList == null)
                {
                    m_DonationPaymentFormModel.CreditCardList = new List<CreditCard>();
                }
                else
                {
                    m_DonationPaymentFormModel.CreditCardList.Clear();
                }

                ProcessCreditCard();
                #endregion

                if (m_DonationPaymentFormModel.DedicationBookingList == null)
                {
                    m_DonationPaymentFormModel.DedicationBookingList = new List<DedicationBooking>();
                }
                else
                {
                    m_DonationPaymentFormModel.DedicationBookingList.Clear();
                }
                // 處理認獻清單
                ProcessDedicationBooking();

                m_DonationPaymentFormModel.QueryStartDate = new DateTime(DateTime.Now.Year, 1, 1);
                m_DonationPaymentFormModel.QueryEndDate = DateTime.Now;

                // 教會職稱是否是會計
                //m_DonationPaymentFormModel.IsAOfficeWorker = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_church_jobtitle") == "會計" ? true : false;

                string jobTitle = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_church_jobtitle");
                m_DonationPaymentFormModel.IsAOfficeWorker = !string.IsNullOrEmpty(jobTitle) && jobTitle.Contains("會計");

                #region ✅ 動態取得奉獻類別清單
                // 從 Dynamics 365 OptionSet 動態取得奉獻類別清單
                try
                {
                    var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                        this.m_ToolUtilityClass.m_Crm2011OrganizationService,
                        null, // Logger (可選)
                        new Microsoft.Extensions.Caching.Memory.MemoryCache(
                            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                    );

                    // 取得 new_fee 實體的 new_category OptionSet 對應表
                    var categoryMapping = optionSetService.GetOptionSetMapping("new_fee", "new_category");

                    // 將 Dictionary 的 Key (顯示文字) 轉換為 List<string>
                    m_DonationPaymentFormModel.DedicationCategoryList = categoryMapping.Keys.ToList();

                    System.Diagnostics.Debug.WriteLine($"[SetDonationPaymentModel] 成功取得 {m_DonationPaymentFormModel.DedicationCategoryList.Count} 個奉獻類別");
                }
                catch (Exception ex)
                {
                    // 如果動態取得失敗，使用備用的硬編碼清單
                    System.Diagnostics.Debug.WriteLine($"[SetDonationPaymentModel] 動態取得奉獻類別失敗，使用備用清單: {ex.Message}");
                    m_DonationPaymentFormModel.DedicationCategoryList = new List<String> {
                        "主日奉獻", "十一奉獻", "感恩奉獻", "建堂奉獻",
                        "宣教奉獻", "愛心奉獻", "特別奉獻"
                    };
                }
                #endregion

                // 動態 OptionSet 若查詢成功但回傳空集合，仍不能讓奉獻頁下拉選單變成空白。
                // 這裡統一回補必要表單預設值，保留 CRM 成功載入的清單，也保護失敗或空回傳的路徑。
                m_DonationPaymentFormModel.EnsureFormDefaults();

                return m_DonationPaymentFormModel;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="SetDonationPaymentModel"/>。
        /// 保留 wrapper 可避免舊 Controller、View 或測試在分階段改名時立刻中斷。
        /// </summary>
        [Obsolete("Use SetDonationPaymentModel. SetDonationPaymentFormModel is retained only for compatibility during migration.")]
        public DonationPaymentFormModel SetDonationPaymentFormModel(Entity aContact)
        {
            return SetDonationPaymentModel(aContact);
        }

        public async Task<IActionResult> SaveDonationPaymentDedicationAsync(DonationPaymentFormModel donationModel)
        {
            try
            {
                //控管節期獻金
                if (donationModel.Category != null && donationModel.Category == "節期獻金")
                {
                    if (donationModel.Others == null || donationModel.Others =="")
                    {
                        return Json(new { status = "2", message = "錯誤:沒有選擇節期!" });
                    }
                }

                //控管特別奉獻
                if (donationModel.Category != null && donationModel.Category == "特別奉獻")
                {
                    if (donationModel.Others == null || donationModel.Others == "")
                    {
                        return Json(new { status = "2", message = "錯誤:沒有選擇特別奉獻的項目!" });
                    }
                }

                //控管奉獻金額
                if (donationModel.Amount != null && donationModel.Amount > 0)
                {
                    String DedicationResult = await m_DonationPaymentProcessor.CreateFeeAsync(m_Contact, donationModel);

                    String PayWay = "";
                    if (DedicationResult.StartsWith("信用卡繳費失敗!"))
                    {
                        return Json(new { status = "2", message = DedicationResult });
                    }
                    else if (DedicationResult.StartsWith("信用卡定期定額建立失敗!"))
                    {
                        return Json(new { status = "2", message = DedicationResult });
                    }
                    else if (DedicationResult.StartsWith("行動支付付款失敗!"))
                    {
                        return Json(new { status = "2", message = DedicationResult });
                    }
                    else if (DedicationResult.StartsWith("LinePay付款失敗!"))
                    {
                        return Json(new { status = "2", message = DedicationResult });
                    }
                    else if (DedicationResult.Contains("*** 請依照訊息付款 ***") != true)
                    {
                        PayWay = "信用卡";
                        // TODO:因為尚未上線，目前暫時無法使用信用卡支付，所以還原為null
                        //return Json(new { status = "2", message = "因為尚未上線，目前暫時無法使用信用卡支付!", DedicationResult = DedicationResult, PayWay = PayWay });
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 與官網整合串連，決定登入者
        public Entity GetDonationPaymentLoginContact(GalleryViewModel aGalleryViewModel, ref String QueryResult)
        {
            try
            {
                // 透過身分證字號尋找連絡人
                EntityCollection aLoginContactCollection = m_ToolUtilityClass.RetrieveContactCollectionByNationId(aGalleryViewModel.NationId);

                if (aLoginContactCollection.Entities.Count > 0)
                {
                    // 有找到奉獻者

                    // 透過姓名全名再找一遍
                    Entity aLoginContact = FilterDonationContactByFullName(aGalleryViewModel, aLoginContactCollection);

                    if (aLoginContact != null)
                    {
                        // 姓名跟身分證字號一樣的有找到
                        // 奉獻者的欄位 行動電話、身分證字號、奉獻編號 沒有值才會加上去，所以不會覆蓋原有的
                        //UpdateDonationContact(aGalleryViewModel, ref aLoginContact);

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
                        return CreateDonationContact(aGalleryViewModel);
                    }
                    else
                    {
                        // 沒找到姓名跟身分證字號一樣的
                        // 沒找到奉獻者，所以新增一個連絡人
                        QueryResult = aGalleryViewModel.FullName + "成功登入" + "為您在系統中建立了資料";
                        return CreateDonationContact(aGalleryViewModel);

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

        /// <summary>
        /// 舊官網奉獻登入流程的相容入口；新程式應呼叫
        /// <see cref="GetDonationPaymentLoginContact"/>。
        /// </summary>
        [Obsolete("Use GetDonationPaymentLoginContact. GetLoginContactDonationPayment is retained only for compatibility during migration.")]
        public Entity GetLoginContactDonationPayment(GalleryViewModel aGalleryViewModel, ref String QueryResult)
        {
            return GetDonationPaymentLoginContact(aGalleryViewModel, ref QueryResult);
        }

        public Entity CreateDonationContact(GalleryViewModel aGalleryViewModel)
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

                // 設定奉獻帶編號
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactToCreate, "pager", aGalleryViewModel.NationId);

                return this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.CreateEntity(aContactToCreate));
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="CreateDonationContact"/>。
        /// </summary>
        [Obsolete("Use CreateDonationContact. CreateDonationPaymentContact is retained only for compatibility during migration.")]
        public Entity CreateDonationPaymentContact(GalleryViewModel aGalleryViewModel)
        {
            return CreateDonationContact(aGalleryViewModel);
        }

        public Entity FilterDonationContactByFullName(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
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

        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="FilterDonationContactByFullName"/>。
        /// </summary>
        [Obsolete("Use FilterDonationContactByFullName. FilterDonationPaymentContactByFullName is retained only for compatibility during migration.")]
        public Entity FilterDonationPaymentContactByFullName(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            return FilterDonationContactByFullName(aGalleryViewModel, aContactEntityCollection);
        }

        public Entity FilterDonationContactByNationId(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
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

        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="FilterDonationContactByNationId"/>。
        /// </summary>
        [Obsolete("Use FilterDonationContactByNationId. FilterDonationPaymentContactByNationId is retained only for compatibility during migration.")]
        public Entity FilterDonationPaymentContactByNationId(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            return FilterDonationContactByNationId(aGalleryViewModel, aContactEntityCollection);
        }

        public Entity FilterDonationContactByMobile(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
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

        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="FilterDonationContactByMobile"/>。
        /// </summary>
        [Obsolete("Use FilterDonationContactByMobile. FilterDonationPaymentContactByMobile is retained only for compatibility during migration.")]
        public Entity FilterDonationPaymentContactByMobile(GalleryViewModel aGalleryViewModel, EntityCollection aContactEntityCollection)
        {
            return FilterDonationContactByMobile(aGalleryViewModel, aContactEntityCollection);
        }

        public void UpdateDonationContact(GalleryViewModel aGalleryViewModel, ref Entity aContact)
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

        /// <summary>
        /// 舊方法名稱的相容入口；新程式應呼叫 <see cref="UpdateDonationContact"/>。
        /// </summary>
        [Obsolete("Use UpdateDonationContact. UpdateDonationPaymentContact is retained only for compatibility during migration.")]
        public void UpdateDonationPaymentContact(GalleryViewModel aGalleryViewModel, ref Entity aContact)
        {
            UpdateDonationContact(aGalleryViewModel, ref aContact);
        }
        #endregion
        #region 信用卡管理
        public void ProcessCreditCard()
        {
            try
            {
                GetCreditCardList(this.m_Contact);
                //預設信用卡
                if (m_DonationPaymentFormModel.CreditCardList.Count > 0)
                {
                    // 選第1個信用卡
                    m_DonationPaymentFormModel.SelectedCreditCard = m_DonationPaymentFormModel.CreditCardList[0].CCToken;
                    // 選最後個信用卡
                    //m_DonationPaymentFormModel.SelectedCreditCard = m_DonationPaymentFormModel.CreditCardList[m_DonationPaymentFormModel.CreditCardList.Count - 1].CCToken;
                }
                else
                {
                    // 沒有預存信用卡
                    m_DonationPaymentFormModel.SelectedCreditCard = null;
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
                            m_DonationPaymentFormModel.CreditCardList.Add(new CreditCard
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
                m_DonationPaymentFormModel.CreditCardList.Remove(aCreditCardToDelete);

                String VisaInfo = "";

                foreach (CreditCard aCreditCard in m_DonationPaymentFormModel.CreditCardList)
                {
                    VisaInfo += aCreditCard.CCToken + "，" + aCreditCard.LeftCardNumber + "，" + aCreditCard.RightCardNumber + "，" + aCreditCard.ExpireDate.Replace("/","") + "|";
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
                m_DonationPaymentFormModel.SameNameList.RemoveAt(1);

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
                if (m_DonationPaymentFormModel.DedicationBookingList.Count > 0)
                {
                    // 選第1個認獻
                    m_DonationPaymentFormModel.SelectedDedicationBooking = m_DonationPaymentFormModel.DedicationBookingList[0].EntityId;
                    // 選最後個認獻
                    //m_DonationPaymentFormModel.SelectedDedicationBooking = m_DonationPaymentFormModel.CreditCardList[m_DonationPaymentFormModel.DedicationBookingList.Count - 1].EntityId;
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

            m_DonationPaymentFormModel.DedicationBookingList.Clear();

            foreach (Entity aDedicationBookingEntity in aDedicationBookingEntityCollection.Entities)
            {
                Entity aRetrievedDedicationBookingEntity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", aDedicationBookingEntity.Id);

                DedicationBooking aDedicationBooking = new DedicationBooking();

                // 實體 ID
                aDedicationBooking.EntityId = aRetrievedDedicationBookingEntity.Id.ToString();
                // 類別
                aDedicationBooking.DedicationCategory = ConvertToCategory(aRetrievedDedicationBookingEntity);
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

                m_DonationPaymentFormModel.DedicationBookingList.Add(aDedicationBooking);
            }
            #endregion
            #region// 取得連絡人認獻資訊 (開發用的虛擬資料)
            //m_DonationPaymentFormModel.DedicationBookingList.Clear();

            //m_DonationPaymentFormModel.DedicationBookingList.Add(new DedicationBooking
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
            //m_DonationPaymentFormModel.DedicationBookingList.Add(new DedicationBooking
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
                        m_DonationPaymentFormModel.DedicationBookingList.Remove(aDedicationBookingToDelete);
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

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "好牧人 : 註冊錯誤 => " + ErrorString);

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
                case 100000005:
                    return "LinePay";
                case 100000006:
                    return "銀行轉帳";
                case 100000007:
                    return "行動支付";
                case 100000008:
                    return "銀聯卡";
                default:
                    return "未知";
            }
        }
        public String ConvertToCategory(Entity aFeeEntity)
        {
            try
            {
                // 方法 1: 優先使用 FormattedValues（最快速且最可靠）
                if (aFeeEntity.FormattedValues.Contains("new_category"))
                {
                    string displayText = aFeeEntity.FormattedValues["new_category"];
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConvertToCategory] 使用 FormattedValues: {displayText}");
                        return displayText;
                    }
                }
                                // 預設值（當無法取得顯示文字時）
                System.Diagnostics.Debug.WriteLine($"[ConvertToCategory] 使用預設值");
                return "十一奉獻";
            }
            catch (Exception ex)
            {
                // 記錄錯誤並返回預設值
                System.Diagnostics.Debug.WriteLine($"[ConvertToCategory] 錯誤: {ex.Message}");
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
                    return "尚未啟動";
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
        #region 永豐金流工具區
        public async Task<OrderMaintain> OrderMaintain(String aOrderNo, String aCommand)
        {
            return await Task.FromResult(new OrderMaintain
            {
                OrderNo = aOrderNo ?? string.Empty,
                ShopNo = m_ShopNo ?? string.Empty,
                Command = aCommand ?? string.Empty,
                Status = "F",
                Description = "QPay order maintenance is not part of the reusable payment core first release."
            });
        }
        #endregion

        /// <summary>
        /// 取得指定 contact 的奉獻清單（供 AJAX 使用）
        /// </summary>
        /// <param name="contactId">contact 實體 Id (string GUID)</param>
        /// <returns>List of anonymous objects serializable to JSON</returns>
        public List<object> GetDedicationFeesByContactId(string contactId)
        {
            try
            {
                if (String.IsNullOrEmpty(contactId)) return new List<object>();

                Guid id;
                if (!Guid.TryParse(contactId, out id)) return new List<object>();

                Entity contactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", id);
                if (contactEntity == null) return new List<object>();

                // Fill model's dedication list for this contact
                SetDedicationFeeList(contactEntity);

                var feeList = m_DonationPaymentFormModel.DedicationFeeList.Select(f => new
                {
                    Category = f.Category,
                    DedicationDate = f.DedicationDate,
                    PayDate = f.PayDate,
                    PayWay = f.PayWay,
                    Amount = f.Amount,
                    PaidPeriod = f.PaidPeriod,
                    Others = f.Others
                }).ToList<object>();

                return feeList;
            }
            catch (Exception)
            {
                return new List<object>();
            }
        }
        private DateTime ParseDateTime(string dateString)
        {
            // 嘗試以多種格式解析日期字串
            DateTime result;
            string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "yyyyMMdd" };
            if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }
            // 若無法解析則嘗試一般解析
            if (DateTime.TryParse(dateString, out result))
            {
                return result;
            }
            // 若解析失敗則回傳現在時間
            return DateTime.Now;
        }
    }
}
