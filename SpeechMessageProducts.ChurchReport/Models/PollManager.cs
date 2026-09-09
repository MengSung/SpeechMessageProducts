// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/PollManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PollManager
// 主要成員：SavePoll、RetrieveStorLesson、SetDisplayFlag、GetClassName、GetPollResult、UpdateContactPollResult、UpdateStorLesson、NotifyPollError、CreateNewStorLesson、SetupNewStorLessonsEntityAttributes
// 引用命名空間：ChurchReport.WebServiceConnector、ChurchReport.Services、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Threading.Tasks、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.WebServiceConnector;
using ChurchReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    public class PollManager : Controller
    {
        #region 資料區
        private readonly ToolUtilityClass m_ToolUtilityClass;

        public PollModel m_PollModel { get; set; } = new PollModel();


        public Entity m_Contact;
        private Entity m_Lesson = null;
        private String m_UserLineId = "";
        private String m_UserName = "";
        private String m_ClassName = "";

        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        #endregion

        #region 建構函數
        /// <summary>
        /// 預設建構函數，使用 Factory 模式獲取 ToolUtilityClass 實例
        /// </summary>
        public PollManager()
        {
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        }

        /// <summary>
        /// 建構函數，使用 Dependency Injection 模式
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public PollManager(IToolUtilityProvider toolUtilityProvider)
        {
            if (toolUtilityProvider == null)
                throw new ArgumentNullException(nameof(toolUtilityProvider));

            m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
        }
        #endregion

        #region 電腦網頁登入
        /// <summary>
        /// 儲存目前 LINE 使用者的問卷，依 QR 課程查詢紀錄後依序更新課程與聯絡人的回覆。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public async Task<IActionResult> SavePoll(PollModel PollModel, String QrCodeIdString, String LineUserId)
        {
            try
            {
                m_UserLineId = LineUserId;
                // 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineUserId);
                if (m_Contact != null)
                {
                    m_UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                }

                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");

                // 取得上課紀錄單
                Entity StorLessonEntity = RetrieveStorLesson(m_Lesson, m_ClassName, m_UserName, m_Contact.Id.ToString());

                UpdateStorLesson(PollModel, StorLessonEntity);

                UpdateContactPollResult(PollModel);

                return Json(new { status = "1", message = "謝謝" + m_UserName + "的參與，牧區及事工單位的同工將主動與" + m_UserName + "接洽！" });

            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }

        public Entity RetrieveStorLesson(Entity aLesson, String LessonName, String UserName, String UserId)
        {
            try
            {
                // 取得與課程相關的上課紀錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml(LessonName, aLesson.Id.ToString(), UserName, UserId);

                if (aStorLessonsEntityCollection.Entities.Count > 0)
                {
                    // 有找到上課紀錄單
                    return this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsEntityCollection.Entities[0].Id);
                }
                else
                {
                    // 沒找到上課紀錄單

                    // 建立一個上課紀錄單
                    return this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", CreateNewStorLesson(m_Contact, ref aLesson));
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        /// <summary>
        /// 依 QR 所指課程名稱建立問卷顯示旗標；結果只屬於本次操作，不發布為共用快取。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public PollModel SetDisplayFlag(String QrCodeIdString)
        {
            try
            {
                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");

                PollModel aPollModel = new PollModel();

                if (m_ClassName.Contains("成⾧班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = false;
                    aPollModel.DisplayLeaderFlag = false;
                }
                else if (m_ClassName.Contains("門徒班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = false;
                }
                else if (m_ClassName.Contains("小組長班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = true;
                }
                else
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = true;
                }
                return aPollModel;
            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }
        /// <summary>
        /// 查詢 QR 所指課程並回傳顯示名稱；CRM 讀取失敗保持失敗語意，不以空名稱掩蓋。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public String GetClassName(String QrCodeIdString)
        {
            try
            {
                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                return this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");
            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }
        /// <summary>
        /// 將本次表單的服事項目轉成既有 CRM 問卷文字；表單內容不進入告警佇列。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public String GetPollResult(PollModel PollModel)
        {
            try
            {

                String PollResult = "";

                if (PollModel.SundayTreat == true)
                {
                    PollResult += "主日招待同工，";
                }
                if (PollModel.SaturdayChild == true)
                {
                    PollResult += "週六兒主服事同工，";
                }
                if (PollModel.SundaydayChild == true)
                {
                    PollResult += "主日兒主同工，";
                }
                if (PollModel.SundayNewFriend == true)
                {
                    PollResult += "主日新人接待同工，";
                }
                if (PollModel.DisplayPpt == true)
                {
                    PollResult += "主日控台同工，";
                }
                if (PollModel.WorshipVocal == true)
                {
                    PollResult += "主日敬拜團(人聲)，";
                }
                if (PollModel.WorshipInstrument == true)
                {
                    PollResult += "主日敬拜團(樂器)，";
                }
                if (PollModel.Instrument != "")
                {
                    PollResult += "樂器名稱=" + PollModel.Instrument + "，";
                }
                if (PollModel.CommunityProfit == true)
                {
                    PollResult += "社區福音行動(益人學苑)，";
                }
                if (PollModel.CommunityFlower == true)
                {
                    PollResult += "社區福音行動(恩朵協會)，";
                }
                if (PollModel.IncubateCampaign == true)
                {
                    PollResult += "培育營會行政同工，";
                }
                if (PollModel.SundayPrayer == true)
                {
                    PollResult += "主日禱告服事，";
                }
                if (PollModel.IncubateCampaignLeader == true)
                {
                    PollResult += "培育營會帶組同工，";
                }
                if (PollModel.Others != "")
                {
                    PollResult += "其他=" + PollModel.Others + "，";
                }
                return PollResult;
            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }
        /// <summary>
        /// 將此次新增的服事意願寫入目前聯絡人，保留既有選項且由 CRM 更新決定成功與否。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public void UpdateContactPollResult(PollModel PollModel)
        {
            try
            {
                String OrignalContactPollResult = m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "new_poll_result");

                String PollResult = "";
                if (PollModel.SundayTreat == true)
                {
                    if (!OrignalContactPollResult.Contains("主日招待同工"))
                    {
                        PollResult += "主日招待同工，";
                    }
                }
                if (PollModel.SaturdayChild == true)
                {
                    if (!OrignalContactPollResult.Contains("週六兒主服事同工"))
                    {
                        PollResult += "週六兒主服事同工，";
                    }
                }
                if (PollModel.SundaydayChild == true)
                {
                    if (!OrignalContactPollResult.Contains("主日兒主同工"))
                    {
                        PollResult += "主日兒主同工，";
                    }
                }
                if (PollModel.SundayNewFriend == true)
                {
                    if (!OrignalContactPollResult.Contains("主日新人接待同工"))
                    {
                        PollResult += "主日新人接待同工，";
                    }
                }
                if (PollModel.DisplayPpt == true)
                {
                    if (!OrignalContactPollResult.Contains("主日控台同工"))
                    {
                        PollResult += "主日控台同工，";
                    }
                }
                if (PollModel.WorshipVocal == true)
                {
                    if (!OrignalContactPollResult.Contains("主日敬拜團(人聲)"))
                    {
                        PollResult += "主日敬拜團(人聲)，";
                    }
                }
                if (PollModel.WorshipInstrument == true)
                {
                    if (!OrignalContactPollResult.Contains("主日敬拜團(樂器)"))
                    {
                        PollResult += "主日敬拜團(樂器)，";
                    }
                }
                if (PollModel.Instrument != "")
                {
                    if (!OrignalContactPollResult.Contains("樂器名稱=" + PollModel.Instrument))
                    {
                        PollResult += "樂器名稱=" + PollModel.Instrument + "，";
                    }
                }
                if (PollModel.CommunityProfit == true)
                {
                    if (!OrignalContactPollResult.Contains("社區福音行動(益人學苑)"))
                    {
                        PollResult += "社區福音行動(益人學苑)，";
                    }
                }
                if (PollModel.CommunityFlower == true)
                {
                    if (!OrignalContactPollResult.Contains("社區福音行動(恩朵協會)"))
                    {
                        PollResult += "社區福音行動(恩朵協會)，";
                    }
                }
                if (PollModel.IncubateCampaign == true)
                {
                    if (!OrignalContactPollResult.Contains("培育營會行政同工"))
                    {
                        PollResult += "培育營會行政同工，";
                    }
                }
                if (PollModel.SundayPrayer == true)
                {
                    if (!OrignalContactPollResult.Contains("主日禱告服事"))
                    {
                        PollResult += "主日禱告服事，";
                    }
                }
                if (PollModel.IncubateCampaignLeader == true)
                {
                    if (!OrignalContactPollResult.Contains("培育營會帶組同工"))
                    {
                        PollResult += "培育營會帶組同工，";
                    }
                }
                if (PollModel.Others != "")
                {
                    if (!OrignalContactPollResult.Contains("其他=" + PollModel.Others))
                    {
                        PollResult += "其他=" + PollModel.Others + "，";
                    }
                }

                m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_poll_result", OrignalContactPollResult + PollResult);

                m_ToolUtilityClass.UpdateEntity(ref m_Contact);

            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }
        /// <summary>
        /// 將本次問卷內容與結果寫入指定課程紀錄；任一步 CRM 更新失敗都交回呼叫端處理。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public void UpdateStorLesson(PollModel PollModel, Entity StorLessonEntity)
        {
            try
            {
                m_ToolUtilityClass.SetEntityStringAttribute(ref StorLessonEntity, "new_poll_result", GetPollResult(PollModel));
                m_ToolUtilityClass.SetEntityStringAttribute(ref StorLessonEntity, "new_poll_content", PollModel.PollContent);

                m_ToolUtilityClass.UpdateEntity(ref StorLessonEntity);

            }
            catch (System.Exception exception)
            {
                NotifyPollError(exception);
                throw;
            }
        }

        /// <summary>
        /// 以實際例外交由共用告警服務，避免字串重新包裝破壞同一 incident 的去重依據。
        /// </summary>
        /// <param name="exception">catch 原始例外；owner 只投影安全摘要，不把例外物件圖留在佇列。</param>
        /// <param name="operation">編譯器提供的呼叫端方法名稱；只能由程式碼指定，不得使用 request 輸入。</param>
        /// <remarks>
        /// 共用 owner 負責先完成 Exception.log flush 再排入 LINE，使用弱鍵去重且不延長 request 壽命。
        /// 此同步轉交不保存 manager、Session、憑證或 CRM 實體，通知失敗也不得取代原始失敗。
        /// </remarks>
        private static void NotifyPollError(Exception exception, [CallerMemberName] string operation = null)
        {
            ChurchReportLineAdminNotificationService.ReportException(nameof(PollManager) + "." + operation, exception);
        }
        #endregion
        #region 新增、修改課程記錄
        /// <summary>
        /// 建立聯絡人的課程紀錄並嘗試指派負責人；建立成功後即保留 ID，指派失敗只通知而不重建記錄。
        /// </summary>
        /// <remarks>
        /// 同一原始例外會交給共用 owner 先寫入並 flush Exception.log，再排入 LINE；
        /// 再次經過外層 catch 時以同一例外去重。不得傳送 QR、姓名、表單或 CRM 實體；
        /// 本入口不建立通知背景工作、不保存例外，維持目前 request 的資料與資源邊界。
        /// </remarks>
        public Guid CreateNewStorLesson(Entity aContact, ref Entity aDiscepleLessons)
        {
            try
            {
                //// 新增課程記錄
                Entity aNewStorLessonsEntity = new Entity("new_stor_lessons");

                // 設定課程記錄相關屬性
                // 複製上層的欄位過來
                this.CopyDisceipleAttributes(ref aContact, ref aNewStorLessonsEntity, ref aDiscepleLessons);

                // 建立可編輯式檢視表所編輯的欄位值
                this.SetupNewStorLessonsEntityAttributes(ref aNewStorLessonsEntity, aContact, ref aDiscepleLessons);

                // 新增課程記錄
                Guid aNewStorLessonsEntityId = this.m_ToolUtilityClass.CreateEntity(aNewStorLessonsEntity);

                //指派負責人
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_stor_lessons", this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aNewStorLessonsEntityId), this.m_ToolUtilityClass.GetOwnerId(aContact));
                }
                catch (System.Exception e)
                {
                    // 記錄已建立，重試整段會新增第二筆；維持既有 ID 回傳並通知指派失敗，
                    // 不把聯絡人、owner ID 或 CRM 資料送入通知，亦不在此啟動重試工作。
                    NotifyPollError(e, nameof(CreateNewStorLesson));
                }

                return aNewStorLessonsEntityId;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void SetupNewStorLessonsEntityAttributes(ref Entity aNewStorLessonsEntity, Entity aContactEntity, ref Entity aDiscepleLessons)
        {
            try
            {
                #region 關聯雙翼養育課程屬性
                if (aDiscepleLessons.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons", aDiscepleLessons.Id); }
                #endregion

                #region 關聯姓名屬性
                if (aContactEntity != null && aContactEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_contact_new_stor_lessons", "contact", aContactEntity.Id); }
                else
                { }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        /// <summary>
        /// 將來源課程的欄位、費用與課程名稱複製到此次新建的上課紀錄，不共用可變候選資料。
        /// </summary>
        /// <remarks>
        /// 選項欄位 setter 會處理缺欄，真正寫入例外仍保留既有繼續組裝行為並明確告警；
        /// 告警僅傳同一例外與固定方法名，由共用 owner 去重並在 Exception.log flush 後排入 LINE。
        /// 本方法不把聯絡人、課程內容或 CRM 實體傳給通知佇列，也不建立額外資源或背景工作。
        /// </remarks>
        private void CopyDisceipleAttributes(ref Entity aRetrievedContact, ref Entity aNewStorLessonsEntity, ref Entity aDiscipleLessons)
        {
            try
            {
                const int EMPTY_VALUE = -999999999;

                #region 是否是以利亞之家課程
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_elijah", this.m_ToolUtilityClass.GetEntityBoolAttribute(aDiscipleLessons, "new_elijah_class"));
                #endregion
                #region 是否是神學生資格

                // 學籍卡ID
                Guid aRollCardEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_roll_card_new_stor_lessons");
                // 註冊單ID
                Guid aRegistrationFormEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_registration_form_new_stor_lesson");

                if (aRollCardEntityId != Guid.Empty && aRegistrationFormEntityId != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_god_student", true);
                    #region 設定費用
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", new Money(GOD_STUDENT_FEE));
                    #endregion
                }
                else
                {
                    #region 設定費用
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDiscipleLessons, "new_lessons_fee"));
                    #endregion

                }

                #endregion
                #region 設定上課名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l1_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l1_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l2_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l2_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l3_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l3_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l4_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l4_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l5_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l5_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l6_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l6_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l7_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l7_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l8_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l8_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l9_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l9_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l10_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l10_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l11_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l11_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l12_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l12_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l13_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l13_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l14_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l14_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l15_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l15_name"));
                #endregion
                #region 設定作業特會名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_a_homework1", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_a_homework1"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_b_homework2", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_b_homework2"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_c_homework3", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_c_homework3"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_d_homework4", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_d_homework4"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_e_homework5", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_e_homework5"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_f_homework6", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_f_homework6"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_g_homework7", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_g_homework7"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_h_homework8", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_h_homework8"));
                #endregion
                #region 課程類別
                int ClassificationValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_classification");

                if (ClassificationValue != EMPTY_VALUE)
                {
                    try { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_classification", ClassificationValue); }
                    catch (System.Exception e)
                    {
                        // Setter 會自行新增缺少的欄位；抵達 catch 代表實際欄位寫入失敗，不能當作正常缺欄。
                        // 維持既有繼續組裝語意，但把同一例外交給有界 owner 先落檔 flush 後通知。
                        NotifyPollError(e, nameof(CopyDisceipleAttributes));
                    }
                }
                #endregion
                #region 學期

                int SemesterValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_semester");
                if (SemesterValue != EMPTY_VALUE)
                {
                    try { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_semester", SemesterValue); }
                    catch (System.Exception e)
                    {
                        // Setter 會自行新增缺少的欄位；抵達 catch 代表實際欄位寫入失敗，不能當作正常缺欄。
                        // 維持既有繼續組裝語意，但把同一例外交給有界 owner 先落檔 flush 後通知。
                        NotifyPollError(e, nameof(CopyDisceipleAttributes));
                    }
                }

                #endregion
                #region 學分
                if (this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_credit", this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit"));
                }
                #endregion
                #region 上課(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_present", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present"));
                }
                #endregion
                #region 作業(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_homework", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework"));
                }
                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_homework", this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_homework"));
                #endregion
                #region 實習(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_practice", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice"));
                }
                #endregion
                #region 考試(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_exam", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam"));
                }
                #endregion

                #region 課程名稱

                // 取得課程名稱
                String LessonDisplayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_name");

                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "fullname");

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_name", LessonDisplayName + "_" + FullName);

                #endregion

                #region 費用
                if (m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee").Value >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee"));
                }

                // 繳費金額預設為0
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_paid_amount", new Money(0));

                // 總計收費預設為0
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_rollup_fee", new Money(0));

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
    }
}
