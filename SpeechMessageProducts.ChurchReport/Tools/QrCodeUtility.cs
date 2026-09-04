// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/QrCodeUtility.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class QrCodeUtility
// 主要成員：GetLineChannelAccessToken、SetupQrCodeIdString、SigningLesson、SigningProcess、SetStorLessonsTimeAttribute、SetStorLessonsEnrollTimeAttribute、GetStorLessonsTimeAttribute、GetClassAttribute、CreateNewStorLesson、UpdateNewStorLesson
// 引用命名空間：ChurchReport.WebServiceConnector、System、System.Collections.Generic、System.IO、System.Linq、System.Text.RegularExpressions、System.Threading.Tasks、System.Runtime.CompilerServices
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.WebServiceConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;
using ToolUtilityNameSpace;
using Microsoft.Extensions.Configuration;
using ChurchReport.Services;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ChurchReport.Models;
using Line.Messaging;
#endregion

namespace ChurchReport.Tools
{
    #region 課程 QR Code 簽到及簽退掃描
    public class QrCodeUtility : IDisposable
    {
        #region 資料區
        // 掃描者
        private Entity m_Contact;

        // 目前 request scope 的 CRM 工具；本工具不擁有也不釋放它。
        private readonly ToolUtilityClass m_ToolUtilityClass;
        private LineMessagingClient m_LineMessagingClient { get; set; }
        private PushUtility m_PushUtility { get; set; }
        private int m_Disposed;

        private string m_UserLineId = string.Empty;
        private string m_UserName = string.Empty;
        private string m_ClassName = string.Empty;
        private string m_ClassIndex = string.Empty;
        private string m_OnboardType = string.Empty;
        private Entity m_Lesson = null;

        private string m_ClassIndexInfo = string.Empty;
        private string m_OnboardTypeInfo = string.Empty;
        private DateTime m_SigningTime;

        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        private const string SAVED_FLAG_FIELD = "new_saved_flag";

        // 配置管理
        private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        // 追蹤等級
        private const int TOTAL_LEVEL = 1;
        private const int LEVEL_1 = 1;
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5;
        #endregion

        /// <summary>
        /// 釋放本工具建立的 LINE client；ToolUtility 由 request scope 擁有，不在此處釋放。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
            {
                return;
            }

            m_LineMessagingClient?.Dispose();
            m_LineMessagingClient = null;
            m_PushUtility = null;
            GC.SuppressFinalize(this);
        }

        #region 初始化
        /// <summary>
        /// 建立課程 QR Code 工具，使用呼叫端目前 request scope 的 ToolUtility。
        /// 此 ToolUtility 由呼叫端的 request scope 提供，本型別不擁有、不釋放。
        /// </summary>
        /// <param name="toolUtility">目前 request scope 的 ToolUtility。</param>
        public QrCodeUtility(ToolUtilityClass toolUtility)
        {
            m_ToolUtilityClass = toolUtility ?? throw new ArgumentNullException(nameof(toolUtility));

            // 從配置讀取 LINE Channel Access Token
            string channelAccessToken = GetLineChannelAccessToken();

            // 初始化 LINE Messaging Client
            m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            // 初始化 Push Utility
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion

        #region 配置讀取方法
        /// <summary>
        /// 從配置讀取 LINE Channel Access Token
        /// 根據組織名稱讀取對應的 Token，若找不到則使用預設組織
        /// </summary>
        /// <returns>LINE Channel Access Token</returns>
        /// <remarks>
        /// 讀取順序：
        /// 1. 嘗試從 CRM 連接配置讀取組織名稱
        /// 2. 根據組織名稱讀取對應的 Token (LineMessaging:{Organization}:ChannelAccessToken)
        /// 3. 若找不到，使用預設組織的 Token
        /// 4. 若預設組織也找不到，返回空字串並記錄錯誤
        ///
        /// 配置結構範例：
        /// "LineMessaging": {
        ///   "Jesus": { "ChannelAccessToken": "xxx" },
        ///   "JesusBack": { "ChannelAccessToken": "xxx" },
        ///   "DefaultOrganization": "jesus"
        /// }
        /// </remarks>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                // 從 CRM 連接配置取得組織名稱
                string organization = m_Configuration["CrmConnection:Organization"];

                if (!string.IsNullOrEmpty(organization))
                {
                    // 將組織名稱轉換為配置鍵格式 (首字母大寫)
                    // 例如: "jesuslove" -> "Jesuslove"
                    string configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();

                    // 嘗試讀取指定組織的 Token
                    string token = m_Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }

                // 若找不到指定組織的設定，使用預設組織
                string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                string defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[QrCodeUtility] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QrCodeUtility] 錯誤: 讀取 LINE Token 配置失敗 - {ex.Message}");
                return string.Empty;
            }
        }
        #endregion

        #region 主程式
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public void SetupQrCodeIdString(string QrCodeIdString, string DisplayName, string UserLineId, ref string ClassName, ref string UserName, ref string ClassIndex, ref string OnboardType)
        {
            try
            {
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "003 : 好牧人: 資訊 => " + DisplayName + "，" + UserName);

                m_UserLineId = UserLineId;

                m_Contact = m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if (m_Contact == null)
                {
                    OnboardType = "錯誤 : " + DisplayName + "還沒有加入好牧人的 Line@";
                    return;
                }

                m_UserName = UserName = m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "004 : 好牧人: 資訊 => " + m_UserName);

                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = ClassName = m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");

                m_ClassIndex = arr.Length >= 2 ? arr[1] : string.Empty;

                if (!m_ClassIndex.Contains("enroll"))
                {
                    m_OnboardType = arr[2];
                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "005 : 好牧人: 資訊 => " + m_OnboardType);

                    SigningLesson(m_Lesson, ClassName, UserName, m_Contact.Id.ToString(), m_ClassIndex, m_OnboardType);

                    ClassIndex = m_ClassIndexInfo;
                    OnboardType = m_OnboardTypeInfo;
                }
                else
                {
                    SigningLesson(m_Lesson, ClassName, UserName, m_Contact.Id.ToString(), m_ClassIndex, m_OnboardType);
                    OnboardType = m_OnboardTypeInfo;
                }
            }
            catch (Exception ex)
            {
                string error = "錯誤訊息 : FullName = " + GetType().FullName + " , Time = " + DateTime.Now + " , Description = " + ex;
                throw;
            }
        }
        #endregion

        #region 設定簽到簽退
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public bool SigningLesson(Entity aLesson, string LessonName, string UserName, string UserId, string ClassIndex, string OnboardType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] >>> SigningLesson 開始 - Line 177");
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "006 : 好牧人: 資訊 => " + m_OnboardType);

                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml(LessonName, aLesson.Id.ToString(), UserName, UserId);
                System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 查詢到 {aStorLessonsEntityCollection.Entities.Count} 筆課程記錄");

                if (aStorLessonsEntityCollection.Entities.Count > 0)
                {
                    Entity retrievedStorLessons = m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsEntityCollection.Entities[0].Id);
                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "007 : 好牧人: 資訊 => SigningProcess( RetrievedStorLessons, ClassIndex, OnboardType );");

                    System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] >>> 準備調用 SigningProcess - Line 182 前");
                    SigningProcess(retrievedStorLessons, ClassIndex, OnboardType);
                    System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] <<< SigningProcess 返回 - Line 182 後");

                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "008 : 好牧人: 資訊 => " + m_OnboardType);
                    return true;
                }

                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "009 : 好牧人: 資訊 => " + m_OnboardType);

                if (m_ClassIndex.Contains("enroll"))
                {
                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "010 : 好牧人: 資訊 => " + m_OnboardType);

                    Entity createdStorLessons = m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", CreateNewStorLesson(m_Contact, ref aLesson));

                    if (m_ToolUtilityClass.GetEntityMoneyAttribute(ref m_Lesson, "new_lessons_fee").Value > 0)
                    {
                        CreateFee(createdStorLessons, "Amount");
                    }

                    SigningProcess(createdStorLessons, ClassIndex, OnboardType);
                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "011 : 好牧人: 資訊 => " + m_OnboardType);
                }
                else
                {
                    m_OnboardTypeInfo = m_UserName + "您還沒有報名" + m_ClassName + Environment.NewLine + "所以無法簽到!";
                    // 此 legacy API 是同步 action；等待通知完成後才能 Dispose client，避免 using
                    // 提前關閉 HTTP 傳輸而遺失通知。
                    m_PushUtility.SendMessage(m_UserLineId, m_OnboardTypeInfo).GetAwaiter().GetResult();
                }

                return false;
            }
            catch (Exception ex)
            {
                string error = "錯誤訊息 : FullName = " + GetType().FullName + " , Time = " + DateTime.Now + " , Description = " + ex;
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, error);
                System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] !!! 例外發生: {ex.Message}");
                throw;
            }
        }



        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public void SigningProcess(Entity aRetrievedStorLessons, string ClassIndex, string OnboardType)
        {
            System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] >>> SigningProcess 開始 - Line 234");
            System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 參數: ClassIndex={ClassIndex}, OnboardType={OnboardType}");

            try
            {
                if (!m_ClassIndex.Contains("enroll"))
                {
                    string signingTimeAttribute = GetStorLessonsTimeAttribute(ClassIndex, OnboardType);
                    string signingPresentAttribute = "new_" + ClassIndex + "_present";

                    if (OnboardType == "On")
                    {
                        DateTime aSigningTime = m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedStorLessons, signingTimeAttribute);
                        if (aSigningTime.Year <= 1)
                        {
                            SetStorLessonsTimeAttribute(aRetrievedStorLessons, signingTimeAttribute, signingPresentAttribute);
                        }
                        else
                        {
                            if (!m_UserName.Contains("(Line)"))
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime() + " 簽到過了";
                            }
                            else
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime() + " 簽到過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
                            }
                        }
                    }
                    else
                    {
                        SetStorLessonsTimeAttribute(aRetrievedStorLessons, signingTimeAttribute, signingPresentAttribute);
                    }
                }
                else
                {
                    DateTime aSigningTime = m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedStorLessons, "new_enroll_time");
                    if (aSigningTime.Year <= 1)
                    {
                        SetStorLessonsEnrollTimeAttribute(aRetrievedStorLessons, "new_enroll_time");
                    }
                    else
                    {
                        if (!m_UserName.Contains("(Line)"))
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime() + " 報名過了";
                        }
                        else
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime() + " 報名過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] <<< SigningProcess 結束");
            }
            catch (Exception ex)
            {
                string error = "錯誤訊息 : FullName = " + GetType().FullName + " , Time = " + DateTime.Now + " , Description = " + ex;
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, error);
                System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] !!! SigningProcess 例外: {ex.Message}");
                throw;
            }
        }

        private void SetStorLessonsTimeAttribute(Entity aRetrievedStorLessons, string SigningTimeAttribute, string SigningPresentAttribute)
        {
            m_SigningTime = DateTime.Now;
            m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
            m_ToolUtilityClass.SetEntityBoolAttribute(ref aRetrievedStorLessons, SigningPresentAttribute, true);
            m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

            string notifyMessage = GetNotifyMessageString();
            // 必須等送出完成，才能讓 action 結束時的 using 正確釋放 client。
            m_PushUtility.SendMessage(m_UserLineId, notifyMessage).GetAwaiter().GetResult();
        }

        private void SetStorLessonsEnrollTimeAttribute(Entity aRetrievedStorLessons, string SigningTimeAttribute)
        {
            m_SigningTime = DateTime.Now;
            m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
            m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

            string notifyMessage = GetEnrollNotifyMessageString();
            // 必須等送出完成，才能讓 action 結束時的 using 正確釋放 client。
            m_PushUtility.SendMessage(m_UserLineId, notifyMessage).GetAwaiter().GetResult();
        }

        public string GetStorLessonsTimeAttribute(string ClassIndex, string OnboardType)
        {
            return OnboardType == "On"
                ? "new_" + ClassIndex + "_signon_time"
                : "new_" + ClassIndex + "_signoff_time";
        }
        #endregion

        #region 取得課堂欄位名稱
        private string GetClassAttribute(string ClassIndex)
        {
            return ClassIndex switch
            {
                "1" => "new_l1_name",
                "2" => "new_l2_name",
                "3" => "new_l3_name",
                "4" => "new_l4_name",
                "5" => "new_l5_name",
                "6" => "new_l6_name",
                "7" => "new_l7_name",
                "8" => "new_l8_name",
                "9" => "new_l9_name",
                "10" => "new_l10_name",
                "11" => "new_l11_name",
                "12" => "new_l12_name",
                "13" => "new_l13_name",
                "14" => "new_l14_name",
                "15" => "new_l15_name",
                _ => "new_l1_name"
            };
        }
        #endregion

        #region 新增、修改課程記錄
        public Guid CreateNewStorLesson(Entity aContact, ref Entity aDiscepleLessons)
        {
            Entity aNewStorLessonsEntity = new Entity("new_stor_lessons");

            CopyDisceipleAttributes(ref aContact, ref aNewStorLessonsEntity, ref aDiscepleLessons);
            SetupNewStorLessonsEntityAttributes(ref aNewStorLessonsEntity, aContact, ref aDiscepleLessons);

            Guid newId = m_ToolUtilityClass.CreateEntity(aNewStorLessonsEntity);

            try
            {
                m_ToolUtilityClass.AssignOwner("new_stor_lessons", m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", newId), m_ToolUtilityClass.GetOwnerId(aContact));
            }
            catch { }

            return newId;
        }

        public void UpdateNewStorLesson(ref Entity aNewStorLessonEntity, string[] aDetailAttributesArray, ref IPluginExecutionContext aContext)
        {
            UpdateNewStorLessonsEntityAttributes(ref aNewStorLessonEntity, aDetailAttributesArray);
            m_ToolUtilityClass.UpdateEntity(aNewStorLessonEntity);
        }

        static readonly object m_RetrieveStorLessonsLocker = new object();
        public Entity RetrieveStorLessonsById(ref IOrganizationService aOrganizationService, string IdNumber)
        {
            lock (m_RetrieveStorLessonsLocker)
            {
                QueryByAttribute querybyexpression = new QueryByAttribute("new_stor_lessons")
                {
                    ColumnSet = new ColumnSet { AllColumns = true }
                };
                querybyexpression.Attributes.AddRange("new_lesson_id", "statecode");
                querybyexpression.Values.AddRange(IdNumber, 0);

                EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);
                return retrieved.Entities.Count > 0 ? retrieved.Entities[0] : null;
            }
        }

        private void SetupNewStorLessonsEntityAttributes(ref Entity aNewStorLessonsEntity, Entity aContactEntity, ref Entity aDiscepleLessons)
        {
            if (aDiscepleLessons.Id != Guid.Empty)
            {
                m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons", aDiscepleLessons.Id);
            }

            if (aContactEntity != null && aContactEntity.Id != Guid.Empty)
            {
                m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_contact_new_stor_lessons", "contact", aContactEntity.Id);
            }
        }

        private void UpdateNewStorLessonsEntityAttributes(ref Entity aNewStorLessonsEntity, string[] aDetailAttributesArray)
        {
            try
            {
                if (aDetailAttributesArray.Length > 4 && !string.IsNullOrEmpty(aDetailAttributesArray[4]))
                {
                    bool presentFlag = aDetailAttributesArray[4] == "true";
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_god_student", presentFlag);
                }

                if (aDetailAttributesArray.Length > 5 && !string.IsNullOrEmpty(aDetailAttributesArray[5]))
                {
                    Money fee = new Money(Convert.ToDecimal(aDetailAttributesArray[5]));
                    m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", fee);
                }
                else
                {
                    m_ToolUtilityClass.SetEntityMoneyAttributeToNull(aNewStorLessonsEntity, "new_fee");
                }

                if (aDetailAttributesArray.Length > 6 && !string.IsNullOrEmpty(aDetailAttributesArray[6]))
                {
                    DateTime payDate = Convert.ToDateTime(aDetailAttributesArray[6]);
                    m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_pay_date", payDate);
                }
                else
                {
                    m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_pay_date");
                }

                if (aDetailAttributesArray.Length > 30 && !string.IsNullOrEmpty(aDetailAttributesArray[30]))
                {
                    m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_estimated_credit", Convert.ToSingle(aDetailAttributesArray[30]));
                }

                if (aDetailAttributesArray.Length > 31 && !string.IsNullOrEmpty(aDetailAttributesArray[31]))
                {
                    m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_achieved_credit", Convert.ToSingle(aDetailAttributesArray[31]));
                }

                if (aDetailAttributesArray.Length > 32 && !string.IsNullOrEmpty(aDetailAttributesArray[32]))
                {
                    m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_score", Convert.ToSingle(aDetailAttributesArray[32]));
                }

                SetupPresentAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
                SetupDateTimeAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
                SetupScoreAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
            }
            catch
            {
                // swallow to align with original behavior
            }
        }
        #endregion

        #region 屬性複製
        private void CopyDisceipleAttributes(ref Entity aRetrievedContact, ref Entity aNewStorLessonsEntity, ref Entity aDiscipleLessons)
        {
            const int EMPTY_VALUE = -999999999;

            m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_elijah", m_ToolUtilityClass.GetEntityBoolAttribute(aDiscipleLessons, "new_elijah_class"));

            Guid rollCardId = m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_roll_card_new_stor_lessons");
            Guid regFormId = m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_registration_form_new_stor_lesson");
            if (rollCardId != Guid.Empty && regFormId != Guid.Empty)
            {
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_god_student", true);
                m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", new Money(GOD_STUDENT_FEE));
            }
            else
            {
                m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", m_ToolUtilityClass.GetEntityMoneyAttribute(aDiscipleLessons, "new_lessons_fee"));
            }

            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l1_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l1_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l2_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l2_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l3_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l3_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l4_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l4_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l5_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l5_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l6_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l6_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l7_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l7_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l8_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l8_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l9_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l9_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l10_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l10_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l11_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l11_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l12_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l12_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l13_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l13_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l14_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l14_name"));
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l15_name", m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l15_name"));

            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_first_date", "new_l1_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_2_date", "new_l2_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_3_date", "new_l3_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_4_date", "new_l4_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_5_date", "new_l5_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_6_date", "new_l6_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_7_date", "new_l7_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l8_date", "new_l8_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l9_date", "new_l9_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l10_date", "new_l10_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l11_date", "new_l11_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l12_date", "new_l12_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l13_date", "new_l13_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l14_date", "new_l14_date");
            SetupDateTimeAttributes(ref aNewStorLessonsEntity, "new_l15_date", "new_l15_date");

            int classificationValue = m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_classification");
            if (classificationValue != EMPTY_VALUE)
            {
                try
                {
                    var optionSetService = new OptionSetMetadataService(m_ToolUtilityClass.m_Crm2011OrganizationService);
                    var validValues = optionSetService
                        .GetOptionSetMapping("new_stor_lessons", "new_classification")
                        .Values
                        .ToHashSet();

                    if (validValues.Contains(classificationValue))
                    {
                        m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_classification", classificationValue);
                        System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 設定分類值: {classificationValue}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 忽略設定 new_classification，值 {classificationValue} 不在 new_stor_lessons 的有效選項中");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 取得 new_classification 選項集失敗，忽略此欄位設定 - {ex.Message}");
                }
            }

            int semesterValue = m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_semester");
            if (semesterValue != EMPTY_VALUE)
            {
                try
                {
                    var optionSetService = new OptionSetMetadataService(m_ToolUtilityClass.m_Crm2011OrganizationService);
                    var validValues = optionSetService
                        .GetOptionSetMapping("new_stor_lessons", "new_semester")
                        .Values
                        .ToHashSet();

                    if (validValues.Contains(semesterValue))
                    {
                        m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_semester", semesterValue);
                        System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 設定學期值: {semesterValue}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 忽略設定 new_semester，值 {semesterValue} 不在 new_stor_lessons 的有效選項中");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QrCodeUtility] 取得 new_semester 選項集失敗，忽略此欄位設定 - {ex.Message}");
                }
            }

            if (m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit") >= 0)
            {
                m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_credit", m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit"));
            }
            if (m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present") >= 0)
            {
                m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_present", m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present"));
            }
            if (m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework") >= 0)
            {
                m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_homework", m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework"));
            }
            if (m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice") >= 0)
            {
                m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_practice", m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice"));
            }
            if (m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam") >= 0)
            {
                m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_exam", m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam"));
            }

            string lessonDisplayName = m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_name");
            string fullName = m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "fullname");
            m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_name", lessonDisplayName + "_" + fullName);

            if (m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee").Value >= 0)
            {
                m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee"));
            }

            m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_paid_amount", new Money(0));
            m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_rollup_fee", new Money(0));
        }
        #endregion

        #region 新增、修改收費單
        public Entity CreateFee(Entity aStorLessonEntity, string Type)
        {
            Entity aFee = new Entity("new_fee");

            m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_contact_new_fee", "new_fee", m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));

            Guid discipleLessonsEntityId = m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_new_disciple_lessons_new_stor_les");
            m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_disciple_lessons_new_fee", "new_fee", discipleLessonsEntityId);
            m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_stor_lessons_new_fee", "new_fee", aStorLessonEntity.Id);

            Entity aDiscipleLessonsEntity = m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", discipleLessonsEntityId);
            Money moneyShouldPay = m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessonsEntity, "new_lessons_fee");
            if (moneyShouldPay.Value >= 0)
            {
                m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFee, "new_fee_shoud_pay", moneyShouldPay);
            }

            if (Type == "Amount")
            {
                m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFee, "new_pay_date", DateTime.Now);
                m_ToolUtilityClass.SetOptionSetAttribute(ref aFee, "new_pay_way", 100000000); // 現金
            }

            Guid aFeeId = m_ToolUtilityClass.CreateEntity(aFee);
            Entity aRetrievedFee = m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

            try
            {
                Entity aRetrievedContact = m_ToolUtilityClass.RetrieveEntity("contacct", m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));
                m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, m_ToolUtilityClass.GetOwnerId(aRetrievedContact));
            }
            catch { }

            return aRetrievedFee;
        }

        public void SetFeePayWay(string Value, ref Entity aFeeEntity)
        {
            switch (Value)
            {
                case "未知":
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
                case "現金":
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000000);
                    break;
                case "信用卡":
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);
                    break;
                case "ATM轉帳":
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000002);
                    break;
                case "超商付款":
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000003);
                    break;
                default:
                    m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
            }
        }
        #endregion

        #region 工具區
        public string GetNotifyMessageString()
        {
            string localClassIndex = "第" + m_ClassIndex + "堂課";
            string classIndexContent = m_ToolUtilityClass.GetEntityStringAttribute(ref m_Lesson, GetClassAttribute(m_ClassIndex));
            if (!string.IsNullOrEmpty(classIndexContent))
            {
                localClassIndex += "，" + classIndexContent;
            }

            string signingTypeAndTime = m_OnboardType == "On"
                ? m_SigningTime.ToLocalTime() + " 簽到成功"
                : m_SigningTime.ToLocalTime() + " 簽退成功";

            m_ClassIndexInfo = localClassIndex;

            if (!m_UserName.Contains("(Line)"))
            {
                m_OnboardTypeInfo = signingTypeAndTime;
                return "名稱: " + m_ClassName + Environment.NewLine +
                       "姓名: " + m_UserName + Environment.NewLine +
                       "資訊: " + localClassIndex + Environment.NewLine +
                       signingTypeAndTime;
            }
            else
            {
                m_OnboardTypeInfo = signingTypeAndTime + Environment.NewLine + "，可是您尚未註冊過喔!";
                return "名稱: " + m_ClassName + Environment.NewLine +
                       "姓名: " + m_UserName + Environment.NewLine +
                       "資訊: " + localClassIndex + Environment.NewLine +
                       signingTypeAndTime + Environment.NewLine +
                       "可是您尚未註冊過喔!";
            }
        }

        public string GetEnrollNotifyMessageString()
        {
            string signingTypeAndTime = m_SigningTime.ToLocalTime() + " 報名";

            if (!m_UserName.Contains("(Line)"))
            {
                m_OnboardTypeInfo = signingTypeAndTime;
                return "課程名稱: " + m_ClassName + Environment.NewLine +
                       "姓名: " + m_UserName + Environment.NewLine +
                       signingTypeAndTime;
            }
            else
            {
                m_OnboardTypeInfo = signingTypeAndTime + Environment.NewLine + "，可是您尚未註冊過喔!";
                return "課程名稱: " + m_ClassName + Environment.NewLine +
                       "姓名: " + m_UserName + Environment.NewLine +
                       signingTypeAndTime + Environment.NewLine +
                       "可是您尚未註冊過喔!";
            }
        }
        #endregion

        #region 輔助設定方法
        private void SetupPresentAttributes(ref Entity entity, string[] detailAttributes)
        {
            // detailAttributes[7..21] 對應第1~15堂課的出席
            for (int i = 1; i <= 15; i++)
            {
                int index = 6 + i;
                if (detailAttributes.Length > index && !string.IsNullOrEmpty(detailAttributes[index]))
                {
                    bool present = detailAttributes[index].Equals("true", StringComparison.OrdinalIgnoreCase) || detailAttributes[index] == "1";
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref entity, $"new_{i}_present", present);
                }
            }
        }

        private void SetupDateTimeAttributes(ref Entity entity, string targetAttribute, string sourceAttribute)
        {
            if (m_Lesson != null)
            {
                DateTime value = m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_Lesson, sourceAttribute);
                if (value.Year > 1)
                {
                    m_ToolUtilityClass.SetEntityDateTimeAttribute(ref entity, targetAttribute, value);
                }
            }
        }

        private void SetupDateTimeAttributes(ref Entity entity, string[] detailAttributes)
        {
            // detailAttributes[22..29] 可對應 A~H 作業日期 (若有設計)
            string[] targetAttrs = { "new_a_expired_date", "new_b_expired_date", "new_c_expired_date", "new_d_expired_date", "new_e_expired_date", "new_f_expired_date", "new_g_expired_date", "new_h_expired_date" };
            for (int i = 0; i < targetAttrs.Length; i++)
            {
                int idx = 22 + i;
                if (detailAttributes.Length > idx && !string.IsNullOrEmpty(detailAttributes[idx]))
                {
                    DateTime date = Convert.ToDateTime(detailAttributes[idx]);
                    m_ToolUtilityClass.SetEntityDateTimeAttribute(ref entity, targetAttrs[i], date);
                }
            }
        }

        private void SetupScoreAttributes(ref Entity entity, string[] detailAttributes)
        {
            // 預留：若未提供分數資料，保持現狀
        }
        #endregion
    }
    #endregion
}
