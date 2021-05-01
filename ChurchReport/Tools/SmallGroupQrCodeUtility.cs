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
        // 掃描者
        Entity m_Contact;

        // 小組實體
        Entity m_SmallGroupList;

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
        // 台中思恩堂豐富教會
        private const String CHANNEL_ACCESS_TOKEN = @"dhWNUj4LOTQFl10j0nvn+7/O3ffZkqfBz5+H6WKGoktwTpu32T+rdJYUfDSvT8HRz+VNkRcbttdJ74d81MecfD/q8AuUK5fhi8/eL9xFnDZBCCqLGP6q9lcZjvleoUXxN/OVfd2kcU3C4jk7sUP8pwdB04t89/1O/w1cDnyilFU=";

        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        private const String SAVED_FLAG_FIELD = @"new_saved_flag";

        #endregion
        #endregion
        #region 初始化
        public SmallGroupQrCodeUtility()
        {
            // 客製化，請選擇
            // 台中思恩堂豐富教會(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString(String QrCodeIdString, String DisplayName, String UserLineId, ref String SmallGroupName, ref String UserName, ref String OnboardType)
        {
            try
            {
                #region 設定區域變數
                m_UserLineId = UserLineId;

                // 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if (m_Contact == null)
                {
                    // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@

                    // 如果好友不存在，則新增好友，新加入好友
                    AddNewFriend(DisplayName, UserLineId);

                    //OnboardType = "錯誤 : " + DisplayName + "還沒有加入台中思恩堂豐富教會的 Line@";

                    //return;
                }
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");

                // 取得週報
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_WeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aGuid);

                // 取得小組名稱，並且回傳給網頁去顯示
                m_SmallGroupName = SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(m_WeeklyReport, "new_list_group_present_weekly_report");

                // 取得小組實體
                m_SmallGroupList = this.m_ToolUtilityClass.RetrieveEntity("list", this.m_ToolUtilityClass.GetEntityLookupAttribute(m_WeeklyReport, "new_list_group_present_weekly_report"));

                // 設定是簽到還是簽退
                m_OnboardType = arr[1];

                #endregion

                // 取得週報名稱
                String WeeklyReportName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_WeeklyReport, "new_name");

                // 個人聚會與靈修記錄進行簽到退 , 同時傳回結果
                SigningWeeklyReport(m_WeeklyReport, WeeklyReportName, UserName, m_Contact.Id.ToString(), m_OnboardType);

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
        public bool SigningWeeklyReport(Entity aWeeklyReport, String WeeklyReportName, String UserName, String UserId, String OnboardType)
        {
            try
            {
                // 取得與週報相關的個人聚會與靈修記錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXml(WeeklyReportName, aWeeklyReport.Id.ToString(), UserName, UserId);

                if (aPresentRecordCollection.Entities.Count > 0)
                {
                    // 有找到個人聚會與靈修記錄
                    Entity aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordCollection.Entities[0].Id);

                    // 進行簽到或是簽退
                    SigningProcess(aPresentRecord, OnboardType);

                    return true;
                }
                else
                {
                    // 沒找到個人聚會與靈修記錄

                    // 建立一個: 個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecordWithNoSmallGroup();

                    // 加到小組中
                    ConnectNewContactInMemberList(m_Contact.Id, m_SmallGroupList);

                    // 進行簽到或是簽退
                    SigningProcess(aPresentRecord, OnboardType);

                    //m_OnboardTypeInfo = "錯誤 : " + UserName + " 還沒有加入" + m_SmallGroupName;

                    return true;
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
                if (OnboardType == "On" || OnboardType == "on")
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
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, SigningPresentAttribute, 1);
                // 幸福小組出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, "new_happy_present", 1);

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
        public void AddNewFriend(String aDisplayName, String UserId)
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

                //await m_ToolUtilityClass.CreateEntityAsync(m_ToolUtilityClass.m_OrganizationService, m_Contact);
                m_ToolUtilityClass.CreateEntity(m_Contact);
                #endregion

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void ConnectNewContactInMemberList(Guid NewContactEntityId, Entity aListEntity)
        {
            try
            {
                if (aListEntity != null)
                {
                    #region 有找到被關聯的小組名單
                    bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "type");

                    if (ListType == false)
                    {
                        // 靜態名單
                        List<Guid> memberGuidList = new List<Guid>();
                        memberGuidList.Add(NewContactEntityId);
                        m_ToolUtilityClass.AddMembersToMarketingList(aListEntity.Id, memberGuidList);
                    }
                    else
                    {
                        // 動態名單
                        Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                        EntityReference aListEntityReference = new EntityReference("list", aListEntity.Id);

                        // 台中思恩堂豐富教會
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_list_contact", ref aListEntityReference);

                        this.m_ToolUtilityClass.UpdateEntity(ref aNewContactEntity);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void CreatePresentRecordOnSmallGroup()
        {
            try
            {
                // 找到聯絡人的所有要點名的小組(牧養小組，而非幸福小組)
                if (m_WeeklyReport != null)
                {
                    EntityCollection aPresentRecordCollection = this.m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndWeeklyReport(m_UserName, m_Contact.Id.ToString(), this.m_ToolUtilityClass.GetEntityStringAttribute(m_WeeklyReport, "new_name"), m_WeeklyReport.Id.ToString());

                    if (aPresentRecordCollection.Entities.Count > 0)
                    {
                        // 進行簽到或是簽退
                        Entity aPresentRecord = aPresentRecordCollection.Entities[0];

                        if (aPresentRecord != null)
                        {
                            aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);

                            SigningProcess(aPresentRecord, m_OnboardType);

                            // 更新出席紀錄單
                            //this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecord);

                            #region// 計算週報主日出席人數及出席率
                            if (m_OnboardTypeInfo.StartsWith("錯誤") != true)
                            {
                                this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_WeeklyReport, "new_saved_flag", "計算出席率");
                                this.m_ToolUtilityClass.UpdateEntity(ref m_WeeklyReport);
                            }
                            #endregion
                        }
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private Entity CreatePresentRecordWithNoSmallGroup()
        {
            try
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, ref this.m_Contact);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);

                //指派負責人
                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                //取得並回傳新建的聚會與靈修記錄
                return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, ref Entity aContactEntity)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = m_UserName + "_" + String.Format("{0:00}/{1:00}/{2:00} 出席紀錄", DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", PresentRecordName);
                #endregion
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 設定歸零
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);   // 設定主日出席
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);             // 設定主日出席率
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);    // 設定小組出席
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);        // 設定小組出席率
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);              // 設定幸福小組出席
                #endregion

                #region 設定主日聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref this.m_WeeklyReport, "new_sunday_date"));
                #endregion

                #region 設定小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", DateTime.Now);
                #endregion

                #region 設定週報
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", this.m_WeeklyReport.Id);
                #endregion

                #region 設定小組
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", this.m_SmallGroupList.Id);
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
