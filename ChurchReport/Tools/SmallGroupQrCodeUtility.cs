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
using Microsoft.CodeAnalysis;
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

        static readonly object m_UpdateSmallGroupWeeklyReportLocker = new object();//避免多人同時輸入"小組出席"，會產生2個週報或是改變"委身類型"、"裝備狀態"                                                                 //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

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
                lock (m_UpdateSmallGroupWeeklyReportLocker)
                {
                    #region 設定區域變數
                    m_UserLineId = UserLineId;

                    // 取得掃描者全名
                    m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                    if (m_Contact == null)
                    {
                        // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@
                        //this.AddNewFriend(DisplayName, UserLineId);

                        OnboardType = "錯誤 : " + DisplayName + "還沒有加入永和禮拜堂的 Line@";

                        return;
                    }
                    m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");

                    // 取得週報
                    string[] arr = QrCodeIdString.Split('_');
                    Guid aGuid = new Guid(arr[0]);
                    m_WeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aGuid);

                    // 取得小組名稱，並且回傳給網頁去顯示
                    m_SmallGroupName = SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(m_WeeklyReport, "new_list_group_present_weekly_report");

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
        #region 工具區
        public String GetNotifyMessageString()
        {
            try
            {
                // 取得簽到簽退時間
                String SigningTypeAndTime = "";
                if (m_OnboardType == "On" || m_OnboardType == "on")
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽到成功";
                }
                else
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽退成功";
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
        #endregion
    }
}
