using ChurchReport.WebServiceConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;
using ChurchReport.Models;
using ToolUtility;
using Line.Messaging;
#endregion

namespace ChurchReport.Tools
{
    #region 課程 QR Code 簽到及簽退掃描
    public class QrCodeUtility
    {
        #region 資料區
        #region 參數資料
        Entity m_Contact;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");
        private LineMessagingClient m_LineMessagingClient { get; set; }

        private PushUtility m_PushUtility { get; set; }

        private String m_UserLineId = "";
        private String m_UserName = "";
        private String m_ClassName = "";
        private String m_ClassIndex = "";
        private String m_OnboardType = "";
        private Entity m_Lesson = null;

        private String m_ClassIndexInfo = "";
        private String m_OnboardTypeInfo = "";

        private DateTime m_SigningTime;
        // 客製化
        // 聖谷行道會
        private const String CHANNEL_ACCESS_TOKEN = @"O7kDZ6nG5nenmU7D2z5LzmKgRM9Pf/5/r08Z6zVlmduhTwbfV8HNObv0YceKtM5oiAvTeL3IaiSK8UEh7Y4fS+FSroM/PfHmEEIcvwmMSud3tZUASMEeLVKCy8bL38PfAws2toVIWsTf+qwcrXyHbgdB04t89/1O/w1cDnyilFU=";


        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        private const String SAVED_FLAG_FIELD = @"new_saved_flag";

        #endregion
        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion
        #endregion
        #region 初始化
        public QrCodeUtility()
        {
            // 客製化，請選擇
            // 聖谷行道會(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString( String QrCodeIdString, String DisplayName, String UserLineId, ref String ClassName, ref String UserName, ref String ClassIndex, ref String OnboardType)
        {
            try
            {
                #region 設定區域變數
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "003 : 聖谷行道會: 資訊 => " + DisplayName + "，" + UserName);

                m_UserLineId = UserLineId;

                // 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if( m_Contact == null )
                {
                    // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@
                    //this.AddNewFriend( DisplayName, UserLineId );

                    OnboardType = "錯誤 : " + DisplayName + "還沒有加入聖谷行道會的 Line@" ;

                    return;
                }
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");

                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "004 : 聖谷行道會: 資訊 => " + m_UserName);

                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");
                #endregion


                // 取得堂數
                m_ClassIndex = arr.Length >= 2?arr[1]: "" ;

                if ( m_ClassIndex.Contains("enroll") != true )
                {
                    #region 課程簽到及簽退

                    // 設定是簽到還是簽退
                    m_OnboardType = arr[2];

                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "005 : 聖谷行道會: 資訊 => " + m_OnboardType);

                    // 在上課紀錄單進行簽到退
                    SigningLesson(m_Lesson, ClassName, UserName, m_Contact.Id.ToString(), m_ClassIndex, m_OnboardType);

                    // 傳回給網頁第幾堂課及其名稱
                    ClassIndex = m_ClassIndexInfo;

                    // 傳回給網頁簽到或簽退時間，及是否已簽到過了
                    OnboardType = m_OnboardTypeInfo;
                    #endregion
                }
                else if (m_ClassIndex.Contains("enroll") == true)
                {
                    #region 課程報名
                    // 在上課紀錄單進行報名
                    SigningLesson(m_Lesson, ClassName, UserName, m_Contact.Id.ToString(), m_ClassIndex, m_OnboardType);

                    // 傳回給網頁簽到或簽退時間，及是否已簽到過了
                    OnboardType = m_OnboardTypeInfo;

                    #endregion
                }
                else { }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 設定簽到簽退
        public bool SigningLesson(Entity aLesson, String LessonName, String UserName, String UserId, String ClassIndex, String OnboardType)
        {
            try
            {
                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "006 : 聖谷行道會: 資訊 => " + m_OnboardType);

                // 取得與課程相關的上課紀錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml( LessonName, aLesson.Id.ToString(), UserName, UserId );

                if (aStorLessonsEntityCollection.Entities.Count > 0)
                {
                    // 有找到上課紀錄單
                    Entity RetrievedStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsEntityCollection.Entities[0].Id);

                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "007 : 聖谷行道會: 資訊 => " + "SigningProcess( RetrievedStorLessons, ClassIndex, OnboardType );");

                    // 進行簽到或是簽退或是報名
                    SigningProcess( RetrievedStorLessons, ClassIndex, OnboardType );

                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "008 : 聖谷行道會: 資訊 => " + m_OnboardType);

                    return true;
                }
                else
                {
                    m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "009 : 聖谷行道會: 資訊 => " + m_OnboardType);

                    // 沒找到上課紀錄單
                    if (m_ClassIndex.Contains("enroll") == true)
                    {
                        m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "010 : 聖谷行道會: 資訊 => " + m_OnboardType);

                        #region 沒找到上課紀錄單，但是是課程報名所以要建立一個上課紀錄單
                        // 建立一個上課紀錄單
                        Entity CreatededStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", CreateNewStorLesson(m_Contact, ref aLesson));

                        if (this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref m_Lesson, "new_lessons_fee").Value > 0)
                        {
                            // 課程有課程費用，就要建立收費單
                            CreateFee(CreatededStorLessons, "Amount");
                        }

                        // 進行簽到或是簽退或是報名
                        SigningProcess(CreatededStorLessons, ClassIndex, OnboardType);

                        #endregion

                        m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "011 : 聖谷行道會: 資訊 => " + m_OnboardType);

                    }
                    else
                    {
                        m_OnboardTypeInfo = m_UserName + "您還沒有報名" + m_ClassName + Environment.NewLine + "所以無法簽到!";

                        m_PushUtility.SendMessage(m_UserLineId, m_OnboardTypeInfo);
                    }

                    return false;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        public void SigningProcess(Entity aRetrievedStorLessons, String ClassIndex, String OnboardType)
        {
            try
            {
                if (m_ClassIndex.Contains("enroll") != true) { 
                    #region 進行簽到或是簽退
                    // 取得上課紀錄單
                    String SigningTimeAttribute = this.GetStorLessonsTimeAttribute(ClassIndex, OnboardType);
                    String SigningPresentAttribute = "new_" + ClassIndex + "_present";
                    if (OnboardType == "On")
                    {
                        // 簽到
                        DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute);
                        if (aSigningTime.Year <= 1)
                        {
                            SetStorLessonsTimeAttribute(aRetrievedStorLessons, SigningTimeAttribute, SigningPresentAttribute);
                        }
                        else
                        {
                            String NotifyMessage = GetNotifyMessageString();
                            if (m_UserName.Contains("(Line)") != true)
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了";
                            }
                            else
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
                            }
                        }
                    }
                    else
                    {
                        // 簽退
                        SetStorLessonsTimeAttribute(aRetrievedStorLessons, SigningTimeAttribute, SigningPresentAttribute);
                    }
                    #endregion
                }
                else
                {
                    #region 進行報名
                        DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute( ref aRetrievedStorLessons, "new_enroll_time");
                        if (aSigningTime.Year <= 1)
                        {
                            SetStorLessonsEnrollTimeAttribute(aRetrievedStorLessons, "new_enroll_time");
                        }
                        else
                        {
                            String NotifyMessage = GetEnrollNotifyMessageString();
                            if (m_UserName.Contains("(Line)") != true)
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 報名過了";
                            }
                            else
                            {
                                m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 報名過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
                            }
                        }
                    #endregion
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetStorLessonsTimeAttribute(Entity aRetrievedStorLessons, String SigningTimeAttribute, String SigningPresentAttribute)
        {
            try
            {
                // 簽到或簽退
                // 設定簽到或簽退時間
                m_SigningTime = DateTime.Now;
                // 填寫簽到時間
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
                // 打勾出席
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aRetrievedStorLessons, SigningPresentAttribute, true);
                this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

                // 送出 LINE 訊息
                String NotifyMessage = GetNotifyMessageString();
                //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);
                m_PushUtility.SendMessage(m_UserLineId, NotifyMessage);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetStorLessonsEnrollTimeAttribute(Entity aRetrievedStorLessons, String SigningTimeAttribute)
        {
            try
            {
                // 設定報名時間
                m_SigningTime = DateTime.Now;
                // 填寫報名時間
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
                this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

                // 送出 LINE 訊息
                String NotifyMessage = GetEnrollNotifyMessageString();
                //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);
                m_PushUtility.SendMessage(m_UserLineId, NotifyMessage);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public String GetStorLessonsTimeAttribute(String ClassIndex, String OnboardType)
        {
            try
            {
                if (OnboardType == "On")
                {
                    // new_1_signon_time
                    return "new_" + ClassIndex + "_signon_time";
                }
                else
                {
                    // new_1_signoff_time
                    return "new_" + ClassIndex + "_signoff_time";
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 取得課堂欄位名稱

        private String GetClassAttribute(String ClassIndex)
        {
            switch (ClassIndex)
            {
                case "1":
                    return "new_l1_name";
                case "2":
                    return "new_l2_name";
                case "3":
                    return "new_l3_name";
                case "4":
                    return "new_l4_name";
                case "5":
                    return "new_l5_name";
                case "6":
                    return "new_l6_name";
                case "7":
                    return "new_l7_name";
                case "8":
                    return "new_l8_name";
                case "9":
                    return "new_l9_name";
                case "10":
                    return "new_l10_name";
                case "11":
                    return "new_l11_name";
                case "12":
                    return "new_l12_name";
                case "13":
                    return "new_l13_name";
                case "14":
                    return "new_l14_name";
                case "15":
                    return "new_l15_name";
                default:
                    return "new_l1_name";
            }
        }

        #endregion
        #region 新增、修改課程記錄
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
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }

                return aNewStorLessonsEntityId;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void UpdateNewStorLesson(ref Entity aNewStorLessonEntity, String[] aDetailAttributesArray, ref IPluginExecutionContext aContext)
        {
            try
            {
                // 尋找學員上課記錄
                // Entity aNewStorLessonsEntity = this.RetrieveStorLessonsById(ref this.m_CrmService, aDetailAttributesArray[33]);
                //Entity aNewStorLessonsEntity = this.FindMatchLessonRecord(aDetailAttributesArray[33], aDetailAttributesArray[1]);

                // 設定學員上課記錄相關屬性
                this.UpdateNewStorLessonsEntityAttributes(ref aNewStorLessonEntity, aDetailAttributesArray);

                // 設定學員上課記錄
                this.m_ToolUtilityClass.UpdateEntity( aNewStorLessonEntity );
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        static readonly object m_RetrieveStorLessonsLocker = new object();
        public Entity RetrieveStorLessonsById(ref IOrganizationService aOrganizationService, String IdNumber)
        {
            try
            {
                lock (m_RetrieveStorLessonsLocker)
                {
                    //  Create query using querybyattribute
                    QueryByAttribute querybyexpression = new QueryByAttribute("new_stor_lessons");
                    querybyexpression.ColumnSet = new ColumnSet();
                    querybyexpression.ColumnSet.AllColumns = true;
                    //  Attribute to query
                    querybyexpression.Attributes.AddRange("new_lesson_id", "statecode");
                    //  Value of queried attribute to return
                    querybyexpression.Values.AddRange(IdNumber, 0);

                    //  Query passed to the service proxy
                    EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);

                    if (retrieved.Entities.Count > 0 && retrieved != null)
                    {
                        return retrieved.Entities[0];
                    }
                    else
                    {
                        return null;
                    }
                }
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
        private void UpdateNewStorLessonsEntityAttributes(ref Entity aNewStorLessonsEntity, String[] aDetailAttributesArray)
        {
            try
            {
                #region 設定是否是神學生
                if (aDetailAttributesArray[4] != "")
                {
                    bool PresentFlag = aDetailAttributesArray[4] == "true" ? true : false;
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_god_student", PresentFlag);
                }
                #endregion

                #region 設定課程費用
                if (aDetailAttributesArray[5] != "")
                {
                    Money Fee = new Money(Convert.ToDecimal(aDetailAttributesArray[5]));
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", Fee);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityMoneyAttributeToNull(aNewStorLessonsEntity, "new_fee");
                }
                #endregion

                #region 設定繳交費用日期
                DateTime aDateTime = new DateTime();
                if (aDetailAttributesArray[6] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[6]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_pay_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_pay_date");
                }
                #endregion

                #region 設定學分
                #region 設定預估學分 030 
                if (aDetailAttributesArray[30] != "")
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_estimated_credit", Convert.ToSingle(aDetailAttributesArray[30]));
                }
                #endregion
                #region 設定實得學分 031 
                if (aDetailAttributesArray[31] != "")
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_achieved_credit", Convert.ToSingle(aDetailAttributesArray[31]));
                }
                #endregion
                #region 設定實得總分 032
                if (aDetailAttributesArray[32] != "")
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_score", Convert.ToSingle(aDetailAttributesArray[32]));
                }
                #endregion
                #endregion

                #region 設定出席
                SetupPresentAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
                #endregion

                #region 設定作業日期
                SetupDateTimeAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
                #endregion

                #region 設定分數
                SetupScoreAttributes(ref aNewStorLessonsEntity, aDetailAttributesArray);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                //throw e; Mrak掉就是吸收錯誤
            }
        }
        #region  JAVASCRIPT 
        //function BuildSavedString(i)
        //{
        //
        //    var SavedString = "";
        //
        //    // 001 姓名
        //    if (data1[i].ContactName != null)
        //    { SavedString += data1[i].ContactName + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 002 教會名稱
        //    if (data1[i].ChurchDepratName != null)
        //    { SavedString += data1[i].ChurchDepratName + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 003 行動電話
        //    if (data1[i].Mobile != null)
        //    { SavedString += data1[i].Mobile + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 004 是否是神學生
        //    if (data1[i].Identity != null)
        //    { SavedString += data1[i].Identity + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 005 課程費用
        //    if (data1[i].Fee != null)
        //    { SavedString += data1[i].Fee + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 006 繳費日期
        //    if (data1[i].date)
        //    { SavedString += data1[i].date + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //
        //
        //
        //
        //    // 007 第一課
        //    if (data1[i].Class1 != null)
        //    { SavedString += data1[i].Class1 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 008 第二課
        //    if (data1[i].Class2 != null)
        //    { SavedString += data1[i].Class2 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 009 第三課
        //    if (data1[i].Class3 != null)
        //    { SavedString += data1[i].Class3 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 010 第四課
        //    if (data1[i].Class4 != null)
        //    { SavedString += data1[i].Class4 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 011 第五課
        //    if (data1[i].Class5 != null)
        //    { SavedString += data1[i].Class5 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 012 第六課
        //    if (data1[i].Class6 != null)
        //    { SavedString += data1[i].Class6 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 013 第七課
        //    if (data1[i].Class7 != null)
        //    { SavedString += data1[i].Class7 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 014 第八課
        //    if (data1[i].Class8 != null)
        //    { SavedString += data1[i].Class8 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 015 第九課
        //    if (data1[i].Class9 != null)
        //    { SavedString += data1[i].Class9 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 016 第十課
        //    if (data1[i].Class10 != null)
        //    { SavedString += data1[i].Class10 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 017 第十一課
        //    if (data1[i].Class11 != null)
        //    { SavedString += data1[i].Class11 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 018 第十二課
        //    if (data1[i].Class12 != null)
        //    { SavedString += data1[i].Class12 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 019 第十三課
        //    if (data1[i].Class13 != null)
        //    { SavedString += data1[i].Class13 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 020 第十四課
        //    if (data1[i].Class14 != null)
        //    { SavedString += data1[i].Class14 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //    // 021 第十五課
        //    if (data1[i].Class15 != null)
        //    { SavedString += data1[i].Class15 + "|"; }
        //    else
        //    { SavedString += "false|"; }
        //
        //
        //
        //
        //    // 022 A.繳交/參加日
        //    if (data1[i].Adate != null)
        //    { SavedString += data1[i].Adate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 023 B.繳交/參加日
        //    if (data1[i].Bdate != null)
        //    { SavedString += data1[i].Bdate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 024 C.繳交/參加日
        //    if (data1[i].Cdate != null)
        //    { SavedString += data1[i].Cdate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 025 D.繳交/參加日
        //    if (data1[i].Ddate != null)
        //    { SavedString += data1[i].Ddate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 026 E.繳交/參加日
        //    if (data1[i].Edate != null)
        //    { SavedString += data1[i].Edate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 027 F.繳交/參加日
        //    if (data1[i].Fdate != null)
        //    { SavedString += data1[i].Fdate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 028 G.繳交/參加日
        //    if (data1[i].Gdate != null)
        //    { SavedString += data1[i].Gdate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 029 H.繳交/參加日
        //    if (data1[i].Hdate != null)
        //    { SavedString += data1[i].Hdate + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //
        //
        //    // 030 預估學分
        //    if (data1[i].EstimatedCredit != null)
        //    { SavedString += data1[i].EstimatedCredit + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 031 實得學分
        //    if (data1[i].AchievedCredit != null)
        //    { SavedString += data1[i].AchievedCredit + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //    // 032 實得總分
        //    if (data1[i].Score != null)
        //    { SavedString += data1[i].Score + "|"; }
        //    else
        //    { SavedString += "|"; }
        //
        //
        //    // 033
        //    if (data1[i].id != null)
        //    { SavedString += data1[i].id + "|"; }
        //    else
        //    { SavedString += ""; }
        //
        //
        //    return SavedString;
        //}

        #endregion
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
                #region 設定上課日期
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_first_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l1_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_2_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l2_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_3_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l3_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_4_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l4_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_5_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l5_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_6_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l6_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_7_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l7_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l8_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l8_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l9_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l9_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l10_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l10_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l11_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l11_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l12_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l12_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l13_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l13_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l14_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l14_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_l15_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_l15_date"));


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

                #endregion
                #region 設定截止日期
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_a_expired_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_a_due_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_b_expired_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_b_due_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_c_expired_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_c_due_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_d_expired_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_d_due_date"));
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_e_expired_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_e_due_date"));
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
                    catch (System.Exception e) { }
                }
                #endregion
                #region 學期

                int SemesterValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_semester");
                if (SemesterValue != EMPTY_VALUE)
                {
                    try { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_semester", SemesterValue); }
                    catch (System.Exception e) { }
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
        private void SetupDateTimeAttributes(ref Entity aDiscipleLessons, String aDiscipleLessonsAttribute, String AttributeName)
        {
            try
            {
                DateTime TimeToSet = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, AttributeName);
                if (TimeToSet.Year > 1)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aDiscipleLessons, aDiscipleLessonsAttribute, TimeToSet);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupPresentAttributes(ref Entity aNewStorLessonsEntity, String[] aDetailAttributesArray)
        {
            try
            {
                #region 設定出席
                bool PresentFlag = aDetailAttributesArray[7] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_1_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[8] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_2_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[9] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_3_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[10] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_4_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[11] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_5_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[12] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_6_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[13] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_7_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[14] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_8_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[15] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_9_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[16] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_10_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[17] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_11_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[18] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_12_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[19] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_13_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[20] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_14_present", PresentFlag);

                PresentFlag = aDetailAttributesArray[21] == "true" ? true : false;
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_15_present", PresentFlag);

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupDateTimeAttributes(ref Entity aNewStorLessonsEntity, String[] aDetailAttributesArray)
        {
            try
            {
                #region 設定作業日期
                DateTime aDateTime = new DateTime();
                if (aDetailAttributesArray[22] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[22]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_a_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_a_complete_date");
                }

                if (aDetailAttributesArray[23] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[23]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_b_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_b_complete_date");
                }

                if (aDetailAttributesArray[24] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[24]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_c_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_c_complete_date");
                }

                if (aDetailAttributesArray[25] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[25]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_d_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_d_complete_date");
                }

                if (aDetailAttributesArray[26] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[26]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_e_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_e_complete_date");
                }

                if (aDetailAttributesArray[27] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[27]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_f_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_f_complete_date");
                }

                if (aDetailAttributesArray[28] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[28]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_g_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_g_complete_date");
                }

                if (aDetailAttributesArray[29] != "")
                {
                    aDateTime = Convert.ToDateTime(aDetailAttributesArray[29]);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewStorLessonsEntity, "new_h_complete_date", aDateTime);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aNewStorLessonsEntity, "new_h_complete_date");
                }

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupScoreAttributes(ref Entity aNewStorLessonsEntity, String[] aDetailAttributesArray)
        {
            try
            {
                #region 設定分數
                Double Score = 0;
                if (aDetailAttributesArray[34] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[34]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_a_score", Score);
                }
                if (aDetailAttributesArray[35] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[35]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_b_score", Score);
                }
                if (aDetailAttributesArray[36] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[36]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_c_score", Score);
                }
                if (aDetailAttributesArray[37] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[37]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_d_score", Score);
                }
                if (aDetailAttributesArray[38] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[38]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_e_score", Score);
                }
                if (aDetailAttributesArray[39] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[39]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_f_score", Score);
                }
                if (aDetailAttributesArray[40] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[40]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_g_score", Score);
                }
                if (aDetailAttributesArray[41] != "")
                {
                    Score = Convert.ToDouble(aDetailAttributesArray[41]);
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_h_score", Score);
                }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
        #region 新增、修改收費單
        public Entity CreateFee( Entity aStorLessonEntity, String Type )
        {
            // 取得與上課紀錄相關的收費單
            //Entity aStorLessonEntity = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", new Guid(StorLessonsId));

            Entity aFee = new Entity("new_fee");

            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_contact_new_fee", "new_fee", this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));

            Guid DiscipleLessonsEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_new_disciple_lessons_new_stor_les");
            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_disciple_lessons_new_fee", "new_fee", DiscipleLessonsEntityId);

            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_stor_lessons_new_fee", "new_fee", aStorLessonEntity.Id);

            Entity aDiscipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", DiscipleLessonsEntityId);

            Money MoneyShouldPay = this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessonsEntity, "new_lessons_fee");

            if (MoneyShouldPay.Value >= 0)
            {
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFee, "new_fee_shoud_pay", MoneyShouldPay);
            }

            if (Type == "Amount")
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFee, "new_pay_date", DateTime.Now);
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFee, "new_pay_way", 100000004); // 預設繳費是未知
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFee, "new_pay_way", 100000000); // 預設繳費是現金
            }

            // 新增收費單
            Guid aFeeId = this.m_ToolUtilityClass.CreateEntity(aFee);
            Entity aRetrievedFee = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

            //指派負責人
            Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity( "contacct",this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));
            try
            {
                this.m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, this.m_ToolUtilityClass.GetOwnerId(aRetrievedContact));
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
            }

            return aRetrievedFee;
        }

        public void SetFeePayWay(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "未知":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
                case "現金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000000);
                    break;
                case "信用卡":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);
                    break;
                case "ATM轉帳":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000002);
                    break;
                case "超商付款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000003);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;

            }
        }

        #endregion
        #region 工具區
        public String GetNotifyMessageString()
        {
            try
            {

                String LocalClassIndex = "第" + m_ClassIndex + "堂課";
                String ClassIndexcontent = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Lesson, this.GetClassAttribute(m_ClassIndex));
                if (ClassIndexcontent != "")
                {
                    LocalClassIndex += "，" + ClassIndexcontent;
                }
                else
                { }

                // 取得簽到簽退時間
                String SigningTypeAndTime = "";
                if (m_OnboardType == "On")
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽到成功";
                }
                else
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽退成功";
                }

                m_ClassIndexInfo = LocalClassIndex;

                if (m_UserName.Contains("(Line)") != true)
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime;

                    // 回傳 LINE 要用到的訊息
                    return
                    "名稱: " + m_ClassName + Environment.NewLine +
                    "姓名: " + m_UserName + Environment.NewLine +
                    "資訊: " + LocalClassIndex + Environment.NewLine +
                    SigningTypeAndTime;
                }
                else
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime + Environment.NewLine + "，可是您尚未註冊過喔!";

                    // 回傳 LINE 要用到的訊息
                    return
                    "名稱: " + m_ClassName + Environment.NewLine +
                    "姓名: " + m_UserName + Environment.NewLine +
                    "資訊: " + LocalClassIndex + Environment.NewLine +
                    SigningTypeAndTime + Environment.NewLine +
                    "可是您尚未註冊過喔!";
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public String GetEnrollNotifyMessageString()
        {
            try
            {
                // 取得簽到簽退時間
                String SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 報名";

                if (m_UserName.Contains("(Line)") != true)
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime;

                    // 回傳 LINE 要用到的訊息
                    return
                    "課程名稱: " + m_ClassName + Environment.NewLine +
                    "姓名: " + m_UserName + Environment.NewLine +
                    SigningTypeAndTime;
                }
                else
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime + Environment.NewLine + "，可是您尚未註冊過喔!";

                    // 回傳 LINE 要用到的訊息
                    return
                    "課程名稱: " + m_ClassName + Environment.NewLine +
                    "姓名: " + m_UserName + Environment.NewLine +
                    SigningTypeAndTime + Environment.NewLine +
                    "可是您尚未註冊過喔!";
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        #endregion
    }
    #endregion
}
