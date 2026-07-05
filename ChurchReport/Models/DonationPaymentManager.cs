// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/DonationPaymentManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class DonationPaymentManager
// 主要成員：GetLineChannelAccessToken、NotifyDonationPaymentError、NotifyDonationRegistrationError、SaveKeyInDedication、QueryKeyInDedication、AuditQueryDedication、UpdateKeyInDedication、SetDedicationFeeList、SetDonationPaymentModel、SetDonationPaymentFormModel
// 引用命名空間：ChurchReport.Payments、ChurchReport.Services、ChurchReport.Tools、ChurchReport.ViewModel、ChurchReport.WebServiceConnector、Line.Messaging、LineMessagingProcessor、LineMessagingProcessor.Workflows
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Payments;
using ChurchReport.Services;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.WebServiceConnector;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
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

        /// <summary>
        /// ChurchReport CRM contact 的建立、篩選與補欄位服務。
        /// 這些規則直接依賴 Dynamics 365 的 contact 欄位與奉獻者登入邏輯，
        /// 因此保留在 ChurchReport 專案，不抽入通用金流核心或 ASP.NET Core 共用層。
        /// </summary>
        private readonly DonationContactService m_DonationContactService;

        /// <summary>
        /// 手動輸入奉獻資料的查詢與更新流程。
        /// 這是 ChurchReport 的 CRM/AJAX 流程，不屬於通用金流核心。
        /// </summary>
        private readonly DonationKeyInDedicationService m_DonationKeyInDedicationService;

        /// <summary>
        /// ChurchReport 認獻單清單與取消流程服務。
        /// new_dedication_booking、LINE 通知與定期定額中止結果備註都屬於 ChurchReport 產品流程，
        /// 不應留在大型 manager，也不應抽進通用金流核心。
        /// </summary>
        private readonly DonationBookingService m_DonationBookingService;

        /// <summary>
        /// 查無新人時建立 ChurchReport contact 與奉獻編號規則的服務。
        /// 這些規則直接綁定 CRM contact.pager 與 ChurchReport 奉獻編號慣例。
        /// </summary>
        private readonly DonationContactCreationService m_DonationContactCreationService;

        /// <summary>
        /// 奉獻付款頁面模型組裝器。
        /// Manager 只保留公開入口與目前 contact 狀態，CRM task、OptionSet、清單初始化由 assembler 負責。
        /// </summary>
        private readonly DonationPaymentModelAssembler m_DonationPaymentModelAssembler;

        /// <summary>
        /// 官網奉獻登入 contact 比對服務。
        /// 身分證、姓名與新 contact 建立策略屬於 ChurchReport CRM 身分流程，manager 只保留公開入口。
        /// </summary>
        private readonly DonationLoginContactService m_DonationLoginContactService;

        /// <summary>
        /// 奉獻查詢表單刷新服務。
        /// 這個服務負責 contact 欄位投影與 new_fee 清單刷新，避免 manager 直接組裝表單欄位。
        /// </summary>
        private readonly DonationDedicationFeeFormService m_DonationDedicationFeeFormService;

        // 登入的連絡人
        public Entity m_LoginContact;

        // 收費單的連絡人
        public Entity m_Contact;

        public String LoginType { get; set; } = "網頁登入";   //登入方式

        private LineMessagingClient m_LineMessagingClient { get; set; }
        private PushUtility m_PushUtility { get; set; }
        private readonly ILineNotificationWorkflow? m_LineNotificationWorkflow;
        private readonly ILineReplyWorkflow? m_LineReplyWorkflow;

        #endregion
        #region 初始化
        public DonationPaymentManager()
            : this((IDonationPaymentCreateGatewayAdapter)null, null, null)
        {
        }

        public DonationPaymentManager(
            DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this((IDonationPaymentCreateGatewayAdapter)donationPaymentCreateGatewayAdapter, null, null)
        {
        }

        public DonationPaymentManager(
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
            : this(donationPaymentCreateGatewayAdapter, null, null)
        {
        }

        public DonationPaymentManager(
            IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineReplyWorkflow? lineReplyWorkflow)
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
            m_LineNotificationWorkflow = lineNotificationWorkflow;
            m_LineReplyWorkflow = lineReplyWorkflow;

            // Donation/payment UI stays in ChurchReport, while LINE push delivery
            // is centralized through PushUtility and the shared processor-backed workflow.
            m_PushUtility = new PushUtility(m_LineMessagingClient, m_LineNotificationWorkflow);

            m_DonationPaymentCreateGatewayAdapter = donationPaymentCreateGatewayAdapter;
            m_DonationContactService = new DonationContactService(m_ToolUtilityClass);
            m_DonationBookingService = new DonationBookingService(m_ToolUtilityClass);
            m_DonationPaymentProcessor = new DonationPaymentProcessor(
                m_LineMessagingClient,
                m_PushUtility,
                new ReplyUtility(m_LineMessagingClient, m_LineReplyWorkflow),
                m_DonationPaymentCreateGatewayAdapter);
            m_DonationKeyInDedicationService = new DonationKeyInDedicationService(
                m_ToolUtilityClass,
                m_DonationPaymentFormModel,
                m_DonationPaymentProcessor,
                Json,
                NotifyDonationPaymentError);
            m_DonationContactCreationService = new DonationContactCreationService(
                m_ToolUtilityClass,
                Json,
                RedirectToAction,
                NotifyDonationRegistrationError);
            m_DonationPaymentModelAssembler = new DonationPaymentModelAssembler(
                m_ToolUtilityClass,
                m_DonationBookingService,
                ProcessCreditCard);
            m_DonationLoginContactService = new DonationLoginContactService(
                m_ToolUtilityClass,
                m_DonationContactService);
            m_DonationDedicationFeeFormService = new DonationDedicationFeeFormService(m_ToolUtilityClass);

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

        /// <summary>
        /// 保留既有錯誤通知行為，讓拆出去的 ChurchReport service 可以共用同一個通知出口。
        /// 這不是通用金流錯誤處理；它含有 ChurchReport 既有 LINE 管理者通知對象。
        /// </summary>
        private static void NotifyDonationPaymentError(string errorString)
        {
            ChurchReportLineAdminNotificationService.NotifyDefaultError("好牧人", errorString);
        }

        /// <summary>
        /// 保留查無新人建立 contact 失敗時的舊管理者通知文字。
        /// 這個通知是 ChurchReport 後台維運流程，不屬於通用金流核心。
        /// </summary>
        private static void NotifyDonationRegistrationError(string errorString)
        {
            ChurchReportLineAdminNotificationService.NotifyDefaultError("好牧人", "註冊錯誤", errorString);
        }
        #endregion
        #region Line 單獨登入
        public async Task<IActionResult> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            return await m_DonationKeyInDedicationService.SaveAsync(DonationPaymentFormModel);
        }

        public async Task<IActionResult> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel, Entity aLoginContact)
        {
            this.m_LoginContact = aLoginContact;
            m_DonationPaymentProcessor.m_LoginContact = aLoginContact;

            return await m_DonationKeyInDedicationService.SaveAsync(DonationPaymentFormModel);
        }

        public async Task<IActionResult> QueryKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            return await m_DonationKeyInDedicationService.QueryAsync(DonationPaymentFormModel);
        }

        public async Task<IActionResult> AuditQueryDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            return await m_DonationKeyInDedicationService.AuditQueryAsync(
                DonationPaymentFormModel,
                contact => SetDedicationFeeList(contact));
        }

        public async Task<IActionResult> UpdateKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            return await m_DonationKeyInDedicationService.UpdateAsync(DonationPaymentFormModel);
        }

        public DonationPaymentFormModel SetDedicationFeeList(String UserLineId)
        {
            try
            {
                return m_DonationDedicationFeeFormService.FillFromLineId(
                    m_DonationPaymentFormModel,
                    UserLineId,
                    this.m_Contact);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public DonationPaymentFormModel SetDedicationFeeList(Entity LineLoginContact)
        {
            try
            {
                return m_DonationDedicationFeeFormService.FillFromContact(
                    m_DonationPaymentFormModel,
                    LineLoginContact);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
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

                // 表單欄位、CRM task 設定、OptionSet、信用卡與認獻清單組裝都由專責 assembler 處理。
                // Manager 保留公開入口，是為了讓既有 Controller/View 呼叫面穩定。
                return m_DonationPaymentModelAssembler.Build(aContact, m_DonationPaymentFormModel);
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
                string validationMessage = DonationPaymentSubmissionService.ValidateDonationForm(donationModel);
                if (!string.IsNullOrWhiteSpace(validationMessage))
                {
                    return Json(new { status = "2", message = validationMessage });
                }

                string dedicationResult = await m_DonationPaymentProcessor.CreateFeeAsync(m_Contact, donationModel);
                DonationPaymentSubmissionResult classifiedResult = DonationPaymentSubmissionService.ClassifyCreatePaymentResult(dedicationResult);

                if (classifiedResult.Status == "2")
                {
                    return Json(new { status = classifiedResult.Status, message = classifiedResult.Message });
                }

                return Json(new
                {
                    status = classifiedResult.Status,
                    message = classifiedResult.Message,
                    DedicationResult = classifiedResult.DedicationResult,
                    PayWay = classifiedResult.PayWay
                });
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                NotifyDonationPaymentError(ErrorString);

                throw e;
            }
        }

        #endregion
        #region 與官網整合串連，決定登入者
        public Entity GetDonationPaymentLoginContact(GalleryViewModel aGalleryViewModel, ref String QueryResult)
        {
            try
            {
                return m_DonationLoginContactService.GetDonationPaymentLoginContact(
                    aGalleryViewModel,
                    ref QueryResult);
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
                return m_DonationContactService.CreateContact(aGalleryViewModel);
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
                return m_DonationContactService.FilterByFullName(aGalleryViewModel, aContactEntityCollection);
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
                return m_DonationContactService.FilterByNationId(aGalleryViewModel, aContactEntityCollection);
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
                return m_DonationContactService.FilterByMobile(aGalleryViewModel, aContactEntityCollection);
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
                m_DonationContactService.UpdateMissingFields(aGalleryViewModel, ref aContact);
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
            var cards = DonationCreditCardProfileService.ParseCreditCards(
                this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "new_visa_info"));

            m_DonationPaymentFormModel.CreditCardList.Clear();
            m_DonationPaymentFormModel.CreditCardList.AddRange(cards);
            m_DonationPaymentFormModel.SelectedCreditCard = cards.Count > 0 ? cards[0].CCToken : null;
        }
        public void GetCreditCardList(Entity aContact)
        {
            var cards = DonationCreditCardProfileService.ParseCreditCards(
                this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info"));

            m_DonationPaymentFormModel.CreditCardList.AddRange(cards);
        }
        public String ProcessExpireDate(String aExpiredDate)
        {
            return DonationCreditCardProfileService.FormatExpireDate(aExpiredDate);
        }
        public void DeleteCreditCard(CreditCard aCreditCardToDelete)
        {
            m_DonationPaymentFormModel.CreditCardList.Remove(aCreditCardToDelete);

            string visaInfo = DonationCreditCardProfileService.SerializeCreditCards(m_DonationPaymentFormModel.CreditCardList);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_visa_info", visaInfo);
            this.m_ToolUtilityClass.UpdateEntity(ref m_Contact);
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
                m_DonationBookingService.FillBookingList(m_DonationPaymentFormModel, this.m_Contact);
                m_DonationBookingService.SelectDefaultBooking(m_DonationPaymentFormModel);
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void GetDedicationBookingList(Entity aContact)
        {
            m_DonationBookingService.FillBookingList(m_DonationPaymentFormModel, aContact);
        }
        public async Task<string> DeleteDedicationBooking(DedicationBooking aDedicationBookingToDelete)
        {
            try
            {
                return await m_DonationBookingService.DeleteBookingAsync(
                    m_DonationPaymentFormModel,
                    m_Contact,
                    aDedicationBookingToDelete,
                    OrderMaintain,
                    m_PushUtility);
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
            // 這個 public 方法仍回傳 Task<IActionResult>，保留 Controller/AJAX 呼叫面；
            // 實際查重、建立 contact、錯誤通知與奉獻編號規則由 ChurchReport service 負責。
            return await Task.FromResult(m_DonationContactCreationService.CreateContact(FullName));
        }

        #endregion
        #region 工具區
        private String ConvertToPayway(Entity aFeeEntity)
        {
            return DonationFeeQueryService.ConvertPayWay(this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way"));
        }
        public String ConvertToCategory(Entity aFeeEntity)
        {
            return DonationFeeQueryService.ConvertCategory(aFeeEntity);
        }
        public String ConvertToDedicationBookingStatus(int OptionSetValue)
        {
            return DonationBookingService.ConvertStatus(OptionSetValue);
        }
        public String ProcessSpecialCategoryString(String SpecialCategory)
        {
            return DonationPaymentFormBuilder.ResolveSpecialCategory(SpecialCategory, DateTime.Now);
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
                        Description = "Payment order maintenance is not part of the reusable payment core first release."
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
                return m_DonationDedicationFeeFormService.GetFeesByContactId(contactId, m_DonationPaymentFormModel);
            }
            catch (Exception)
            {
                return new List<object>();
            }
        }
        private DateTime ParseDateTime(string dateString)
        {
            return DonationPaymentFormBuilder.ParseDateTime(dateString);
        }
    }
}
