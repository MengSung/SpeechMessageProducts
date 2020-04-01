using System;
using ToolUtilityNameSpace;
//using ChurchReport.Tools.WeeklyReportProcessor;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using ToolUtility;
using Line.Messaging;
using System.Collections.Generic;
#endregion

namespace ChurchReport.Tools
{
    public class SundayQrCodeUtility
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
        private String m_SundayName = "";
        private String m_CategoryName = "";
        //private String m_ClassIndex = "";
        private String m_OnboardType = "";

        private Entity m_MeetingStatistics = null;          //聚會統計紀錄
        private DateTime m_Sunday = DateTime.Now;
        private String m_MeetingStatisticsAttribute = "";   //聚會統計掃描QR CODE 欄位
        private String m_OnboardTypeInfo = "";              //簽到還是簽退

        private DateTime m_SigningTime;
        // 客製化
        // 永和禮拜堂
        private const String CHANNEL_ACCESS_TOKEN = @"HeuLkSEF5CX7hdZo4956IPpgJNdb8VqRZeL1Gu37kFFm+1F7DObAGjfeVYaggzwjZ5H4qraesvquODt7Y81jbtspNZkEq5n3oLDG+G32xQsRx1jCobkABL/Z7RKjkSACNT6h72bPQXsVn9aCuI5OogdB04t89/1O/w1cDnyilFU=";

        #endregion
        #endregion
        #region 初始化
        public SundayQrCodeUtility()
        {
            // 客製化，請選擇
            // 永和禮拜堂(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString( String QrCodeIdString, String DisplayName, String UserLineId, ref String SundayName, ref String CategoryName, ref String UserName, ref String OnboardType)
        {
            try
            {
                #region// 設定區域變數 : 掃描者、全名、聚會統計、簽到還是簽退
                m_UserLineId = UserLineId;

                #region// 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if( m_Contact == null )
                {
                    // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@
                    //this.AddNewFriend(DisplayName, UserLineId);

                    OnboardType = "錯誤 : " + DisplayName + "還沒有加入永和禮拜堂的 Line@";

                    return;
                }
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                #endregion

                #region// 取得聚會統計
                string[] arr = QrCodeIdString.Split('@');
                Guid aGuid = new Guid(arr[0]);
                m_MeetingStatistics = this.m_ToolUtilityClass.RetrieveEntity("new_meeting_statistics", aGuid);
                // 取得聚會統計，主日聚會名稱
                m_SundayName = SundayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_MeetingStatistics, "new_name");

                if(m_SundayName == "")
                {
                    m_SundayName = SundayName = "主日聚會";
                }

                // 取得聚會統計，主日日期
                this.m_Sunday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_MeetingStatistics, "new_sunday_date").ToLocalTime();
                #endregion

                #region// 取得聚會統計屬性
                m_MeetingStatisticsAttribute = arr[1];
                #endregion

                #region// 設定是簽到還是簽退
                m_OnboardType = arr[2];
                #endregion

                #endregion

                #region// 取得聚會統計名稱
                String MeetingStatisticsName = this.m_ToolUtilityClass.GetEntityStringAttribute( m_MeetingStatistics, "new_name" );

                m_CategoryName = CategoryName = ConvertMeetingStatisticsQrName(m_MeetingStatisticsAttribute);
                #endregion

                #region// 個人聚會與靈修記錄進行簽到退 , 同時傳回結果
                SigningMeetingStatistics( m_MeetingStatistics, UserName, m_Contact.Id.ToString(), m_OnboardType );
                #endregion

                #region// 傳回給網頁簽到或簽退時間，及是否已簽到過了
                OnboardType = m_OnboardTypeInfo;
                #endregion

                #region// 計算週報主日出席人數及出席率
                //if (m_OnboardTypeInfo.StartsWith("錯誤") != true)
                //{
                //    //this.m_ToolUtilityClass.SetEntityStringAttribute(ref m_MeetingStatistics, "new_saved_flag", "計算出席率");
                //    //this.m_ToolUtilityClass.UpdateEntity(ref m_MeetingStatistics);
                //}
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 設定簽到簽退
        public bool SigningMeetingStatistics(Entity aMeetingStatistics, String UserName, String UserId,  String OnboardType )
        {
            try
            {
                // 取得與聚會統計主日日期相關的個人聚會與靈修記錄
                DateTime aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aMeetingStatistics, "new_sunday_date").ToLocalTime();
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndSundayDate( UserName, UserId, aSundayDate);

                if ( aPresentRecordCollection.Entities.Count > 0 )
                {
                    #region// 有找到個人聚會與靈修記錄
                    Entity aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordCollection.Entities[0].Id);

                    // 進行簽到或是簽退
                    SigningProcess(aPresentRecord, OnboardType);

                    #region 設定聚會統計關聯
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_meeting_statistics_new_present_re", "new_meeting_statistics", this.m_MeetingStatistics.Id);
                    #endregion

                    this.m_ToolUtilityClass.UpdateEntity( ref aPresentRecord );

                    #region// 計算週報主日出席人數及出席率
                    Guid aWeeklyReportId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecord, "new_group_present_weekly_report_prese");

                    if (aWeeklyReportId != Guid.Empty)
                    {
                        Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);
                        if (aWeeklyReportEntity != null)
                        {
                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_saved_flag", "計算出席率");
                            this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                        }
                    }
                    #endregion

                    #endregion

                    return true;
                }
                else
                {
                    #region// 沒找到個人聚會與靈修記錄
                    // 建立一個個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord();

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
                // 依據掃描網址取得個人聚會與靈修記錄簽到或簽退時間的欄位屬性
                String aPresentRecordSigningAttribute = this.ConvertMeetingStatisticsToPresentRecordAttribute(this.m_MeetingStatisticsAttribute);

                // 取得個人聚會與靈修記錄簽的到或簽退時間
                DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedPresentRecord, aPresentRecordSigningAttribute);
                if (aSigningTime.Year <= 1)
                {
                    // 還沒簽到及簽退，設定簽到時間，主日出席設為1，更新個人聚會與靈修記錄
                    SetPresentRecordTimeAttribute(aRetrievedPresentRecord, aPresentRecordSigningAttribute, "new_sunday_present_this_week");
                }
                else
                {
                    String NotifyMessage = GetNotifyMessageString();

                    if (OnboardType == "On" || OnboardType == "on")
                    {
                        if (m_UserName.Contains("(Line)") != true)
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了";
                        }
                        else
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了" + Environment.NewLine + "， 可是您尚未綁定過喔!";
                        }
                    }
                    else
                    {
                        if (m_UserName.Contains("(Line)") != true)
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽退過了";
                        }
                        else
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽退過了" + Environment.NewLine + "， 可是您尚未綁定過喔!";
                        }
                    }

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
                        "主日: " + m_SundayName + Environment.NewLine +
                        "類型: " + m_CategoryName + Environment.NewLine +
                        "姓名: " + m_UserName + Environment.NewLine +
                        SigningTypeAndTime;
                }
                else
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime + Environment.NewLine + "，可是您尚未綁定過喔!";

                    // 回傳 LINE 要用到的訊息
                    return
                        "主日: " + this.m_SundayName + Environment.NewLine +
                        "類型: " + m_CategoryName + Environment.NewLine +
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
        private String ConvertMeetingStatisticsQrName(String MeetingStatisticsAttribute)
        {
            if (MeetingStatisticsAttribute.Contains("new_sunday_first_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "主日第一堂簽到";
                }
                else
                {
                    return "主日第一堂簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_sunday_second_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "主日第二堂簽到";
                }
                else
                {
                    return "主日第二堂簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_saturday_worship"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "週六崇拜簽到";
                }
                else
                {
                    return "週六崇拜簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_yongmen"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "青年崇拜簽到";
                }
                else
                {
                    return "青年崇拜簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_child"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "兒童主日學簽到";
                }
                else
                {
                    return "兒童主日學簽退";
                }
            }
            else
            {
                return "";
            }

        }
        #endregion
        #region 個人聚會與靈修記錄
        private String ConvertMeetingStatisticsToPresentRecordAttribute(String MeetingStatisticsAttribute)
        {
            if (MeetingStatisticsAttribute.Contains("new_sunday_first_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_sunday_first_qr_on_time";
                }
                else
                {
                    return "new_sunday_first_qr_off";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_sunday_second_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_sunday_second_qr_on_time";
                }
                else
                {
                    return "new_sunday_second_qr_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_saturday_worship"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_saturday_worship_on_time";
                }
                else
                {
                    return "new_saturday_worship_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_yongmen"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_yongmen_on_time";
                }
                else
                {
                    return "new_yongmen_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_child"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_child_on_time";
                }
                else
                {
                    return "new_child_off_time";
                }
            }
            else
            {
                return "";
            }

        }
        public Entity CreatePresentRecord()
        {
            try
            {
                if ( m_Contact != null )
                {
                    // 有加入到教會的官方的LINE@
                    return CreatePresentRecordOnSmallGroup();
                }
                else
                {
                    return null;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, ref Entity aContactEntity)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = m_UserName + "_" + this.m_SundayName + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", PresentRecordName);
                #endregion
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 設定歸零
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);             // 設定主日出席率
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);    // 設定小組出席
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);        // 設定小組出席率
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);              // 設定幸福小組出席
                #endregion
                #region 設定主日聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                #endregion
                #region 設定聚會統計關聯
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_meeting_statistics_new_present_re", "new_meeting_statistics", this.m_MeetingStatistics.Id);
                #endregion
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
        private Entity CreatePresentRecordOnSmallGroup()
        {
            try
            {
                // 找到聯絡人的所有要點名的小組(牧養小組，而非幸福小組)
                EntityCollection aListCollection = m_ToolUtilityClass.RetrieveListByFetchXmlContact(m_UserName);

                if ( aListCollection.Entities.Count > 0 )
                {
                    // 有找到小組
                    foreach ( Entity aListEntity in aListCollection.Entities )
                    {
                        // 取得小組名單實體
                        Entity aRetrievedListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", aListEntity.Id);

                        // 取得小組長紀錄
                        Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aRetrievedListEntity, "new_contact_family_leader_list");
                        Entity aSmallGroupLeaderEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSmallGroupLeaderId);

                        #region 建立週報及出席紀錄單
                        WeeklyReportProcessor aWeeklyReportProcessor = new WeeklyReportProcessor(this.m_ToolUtilityClass);
                        Dictionary<String, String> WeeklyReportDictionary = new Dictionary<String, String>();
                        aWeeklyReportProcessor.CreateWeeklyReportAndPresentRecord(aSmallGroupLeaderEntity, DateTime.Now, ref WeeklyReportDictionary);
                        #endregion

                        foreach (KeyValuePair<string, string> WeeklyReportKeyValuePair in WeeklyReportDictionary)
                        {
                            #region 找到與此建立的週報和聯絡人相關的出席紀錄單
                            Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity(@"new_group_present_weekly_report", new Guid(WeeklyReportKeyValuePair.Value));

                            if( aWeeklyReportEntity != null )
                            {
                                EntityCollection aPresentRecordCollection = this.m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndWeeklyReport(m_UserName, m_Contact.Id.ToString(), this.m_ToolUtilityClass.GetEntityStringAttribute(aWeeklyReportEntity, "new_name"), aWeeklyReportEntity.Id.ToString());

                                if( aPresentRecordCollection.Entities.Count > 0 )
                                {
                                    // 進行簽到或是簽退
                                    Entity aPresentRecord = aPresentRecordCollection.Entities[0];

                                    if ( aPresentRecord != null)
                                    {
                                        aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);

                                        SigningProcess(aPresentRecord, m_OnboardType);

                                        #region 設定聚會統計關聯
                                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_meeting_statistics_new_present_re", "new_meeting_statistics", this.m_MeetingStatistics.Id);
                                        #endregion

                                        // 更新出席紀錄單
                                        this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecord);

                                        #region// 計算週報主日出席人數及出席率
                                        if (m_OnboardTypeInfo.StartsWith("錯誤") != true)
                                        {
                                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_saved_flag", "計算出席率");
                                            this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                                        }
                                        #endregion
                                    }

                                    return aPresentRecordCollection.Entities[0];
                                }
                                else 
                                { 
                                    return null; 
                                }
                            }
                            else
                            {
                                return null;
                            }
                            #endregion
                        }
                    }

                    return null;
                }
                else
                {
                    // 還沒有小組

                    // 新增建立一個個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecordWithNoSmallGroup();

                    //#region 個人聚會與靈修記錄
                    // 進行簽到或是簽退
                    if (aPresentRecord != null)
                    {
                        SigningProcess(aPresentRecord, m_OnboardType);
                    }
                    

                    return aPresentRecord;

                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
        //#endregion
        #endregion

    }
}
