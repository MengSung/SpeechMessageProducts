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
    public class SmallGroupQrCodeUtility
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
        private String m_SmallGroupName = "";
        //private String m_ClassIndex = "";
        private String m_OnboardType = "";
        private Entity m_WeeklyReport = null;

        //private String m_ClassIndexInfo = "";
        private String m_OnboardTypeInfo = "";

        private DateTime m_SigningTime;
        // 客製化
        // 永和禮拜堂
        private const String CHANNEL_ACCESS_TOKEN = @"HeuLkSEF5CX7hdZo4956IPpgJNdb8VqRZeL1Gu37kFFm+1F7DObAGjfeVYaggzwjZ5H4qraesvquODt7Y81jbtspNZkEq5n3oLDG+G32xQsRx1jCobkABL/Z7RKjkSACNT6h72bPQXsVn9aCuI5OogdB04t89/1O/w1cDnyilFU=";


        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        private const String SAVED_FLAG_FIELD = @"new_saved_flag";

        #endregion
        #endregion
        #region 初始化
        public SmallGroupQrCodeUtility()
        {
            // 客製化，請選擇
            // 永和禮拜堂(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString( String QrCodeIdString, String DisplayName, String UserLineId, ref String SmallGroupName, ref String UserName, ref String OnboardType)
        {
            try
            {
                #region 設定區域變數
                m_UserLineId = UserLineId;

                // 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if( m_Contact == null )
                {
                    // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@
                    //this.AddNewFriend(DisplayName, UserLineId);
                }
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");

                // 取得週報
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_WeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aGuid);

                // 取得小組名稱，並且回傳給網頁去顯示
                m_SmallGroupName = SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName( m_WeeklyReport, "new_list_group_present_weekly_report");

                // 設定是簽到還是簽退
                m_OnboardType = arr[1];

                #endregion

                // 取得週報名稱
                String WeeklyReportName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_WeeklyReport, "new_name");
                // 個人聚會與靈修記錄進行簽到退 , 同時傳回結果
                SigningWeeklyReport( m_WeeklyReport, WeeklyReportName, UserName, m_Contact.Id.ToString(), m_OnboardType );

                // 傳回給網頁簽到或簽退時間，及是否已簽到過了
                OnboardType = m_OnboardTypeInfo;

                // 計算週報出席人數及出席率
                if (m_OnboardTypeInfo.StartsWith("錯誤") != true)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_WeeklyReport, "new_saved_flag", "計算出席率");
                    this.m_ToolUtilityClass.UpdateEntity(ref m_WeeklyReport);
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 設定簽到簽退
        public bool SigningWeeklyReport(Entity aWeeklyReport, String WeeklyReportName, String UserName, String UserId,  String OnboardType )
        {
            try
            {
                // 取得與週報相關的個人聚會與靈修記錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXml(WeeklyReportName, aWeeklyReport.Id.ToString(), UserName, UserId);

                if ( aPresentRecordCollection.Entities.Count > 0 )
                {
                    // 有找到個人聚會與靈修記錄
                    Entity aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordCollection.Entities[0].Id);

                    // 進行簽到或是簽退
                    SigningProcess(aPresentRecord, OnboardType);
                    //SetStorLessonsClass(m_Lesson, LessonName, UserName, UserId, ClassIndex, OnboardType);

                    return true;
                }
                else
                {
                    // 沒找到個人聚會與靈修記錄

                    // 建立一個上課紀錄單
                    //Entity CreatededStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", CreateNewStorLesson(m_Contact, ref aLesson));

                    //SigningProcess(CreatededStorLessons, ClassIndex, OnboardType);

                    m_OnboardTypeInfo = "錯誤 : " + UserName + " 還沒有加入" + m_SmallGroupName; 

                    return false;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SigningProcess(Entity aRetrievedPresentRecord, String OnboardType)
        {
            try
            {
                // 取得個人聚會與靈修記錄
                //String SigningTimeAttribute = this.GetStorLessonsTimeAttribute(ClassIndex, OnboardType);
                //String SigningPresentAttribute = "new_" + ClassIndex + "_present";
                if ( OnboardType == "On" || OnboardType == "on" )
                {
                    // 簽到
                    DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedPresentRecord, "new_small_group_signing_time");
                    if (aSigningTime.Year <= 1)
                    {
                        // 設定簽到時間，一班小組、幸福小組打勾，更新個人聚會與靈修記錄
                        SetPresentRecordTimeAttribute(aRetrievedPresentRecord, "new_small_group_signing_time", "new_group_present_this_week");
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
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了" + Environment.NewLine + "， 可是您尚未綁定過喔!";
                        }

                    }
                }
                else
                {
                    // 簽退
                    // SetStorLessonsTimeAttribute(aRetrievedPresentRecord, "new_small_group_signing_time", "new_group_present_this_week");
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetPresentRecordTimeAttribute(Entity aRetrievedPresentRecord, String SigningTimeAttribute, String SigningPresentAttribute)
        {
            try
            {
                // 簽到或簽退
                // 設定簽到或簽退時間
                m_SigningTime = DateTime.Now;
                // 填寫簽到時間
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedPresentRecord, SigningTimeAttribute, m_SigningTime);
                // 一般小組出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute( ref aRetrievedPresentRecord, SigningPresentAttribute, 1 );
                // 幸福小組出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute( ref aRetrievedPresentRecord, "new_happy_present", 1 );

                // 更新個人聚會與靈修記錄
                this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);

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
                return this.m_ToolUtilityClass.CreateEntity(aNewStorLessonsEntity);
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
        #region 工具區
        public String GetNotifyMessageString()
        {
            try
            {
                // 取得簽到簽退時間
                String SigningTypeAndTime = "";
                if (m_OnboardType == "On" || m_OnboardType == "on")
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽到";
                }
                else
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽退";
                }

                                
                if (m_UserName.Contains("(Line)") != true)
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime;

                    // 回傳 LINE 要用到的訊息
                    return
                        "小組: " + m_SmallGroupName + Environment.NewLine +
                        "姓名: " + m_UserName + Environment.NewLine +
                        SigningTypeAndTime;
                }
                else
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime + Environment.NewLine + "，可是您尚未綁定過喔!";

                    // 回傳 LINE 要用到的訊息
                    return
                        "小組: " + m_SmallGroupName + Environment.NewLine +
                        "姓名: " + m_UserName + Environment.NewLine +
                        SigningTypeAndTime + Environment.NewLine +
                        "可是您尚未綁定過喔!";
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public async Task AddNewFriend( String aDisplayName, String UserId)
        {
            try
            {
                #region 如果好友不存在，則新增好友，新加入好友

                #region// 新加入
                //UserProfile aUserProfile = await GetProfile(UserId);
                //Task<UserProfile> aUserProfileTask = m_LineMessagingClient.GetUserProfileAsync(UserId);
                //UserProfile aUserProfile = await aUserProfileTask;

                //UserProfile aUserProfile = await m_LineMessagingClient.GetUserProfileAsync(UserId);

                m_Contact = new Entity("contact");

                // 寫入LINE的個人基本資料
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_lineid", UserId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_lineid_backup", UserId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_line_displayname", aDisplayName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_line_picture_url", aUserProfile.PictureUrl);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_line_status_message", aUserProfile.StatusMessage);
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref m_Contact, "new_line_register", false);

                // 委身類型客製化，客製委身類型欄位，每間教會委身類型都不一樣，台中思恩堂豐富教會豐富教會=>"新朋友" = 100000000
                // 設定成為 新朋友 的委身類型
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref m_Contact, "customertypecode", 100000000);

                // 設定在CRM 2011 的初始連絡人姓名
                //String Year = DateTime.Now.Year.ToString();
                //String Month = DateTime.Now.Month.ToString();
                //String Day = DateTime.Now.Day.ToString();
                //String Hour = DateTime.Now.Hour.ToString();
                //String Minute = DateTime.Now.Minute.ToString();
                //String Second = DateTime.Now.Second.ToString();

                //String LastName = "Line新加入者" + "-" + Year + "-" + Month + "-" + Day + "-" + Hour + "-" + Minute + "-" + Second;
                String LastName = aDisplayName + "(Line)";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "lastname", LastName);

                //設定LINE狀態為"新加入"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref m_Contact, "new_line_status", 100000001);

                await m_ToolUtilityClass.CreateEntityAsync(m_ToolUtilityClass.m_OrganizationService, m_Contact);
                #endregion

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public async Task<UserProfile> GetProfile(String UserId)
        {
            try
            {
                return await m_LineMessagingClient.GetUserProfileAsync(UserId);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        #endregion
    }
}
