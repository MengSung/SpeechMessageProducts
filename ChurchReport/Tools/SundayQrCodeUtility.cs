using System;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using Microsoft.Extensions.Configuration;
//using ChurchReport.Tools.WeeklyReportProcessor;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
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

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        private LineMessagingClient m_LineMessagingClient { get; set; }

        private PushUtility m_PushUtility { get; set; }

        private String m_QrCodeIdString = "";
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

        static readonly object m_UpdateSundayWeeklyReportLocker = new object();//避免多人同時輸入"小組出席"，會產生2個週報或是改變"委身類型"、"裝備狀態"                                                                 //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        // 掃描 QR CODE之後是否發送 LINE通知
        private const bool SEND_LINE_NOTIFICATION = false;

        // 配置管理
        private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

        #endregion
        #endregion
        
        #region 初始化
        public SundayQrCodeUtility()
        {
            // 從配置讀取 LINE Channel Access Token
            string channelAccessToken = GetLineChannelAccessToken();
            
            // 初始化 LINE Messaging Client
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

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
                    System.Diagnostics.Trace.WriteLine("[SundayQrCodeUtility] 警告: LINE Channel Access Token 未設定");
                }
                
                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SundayQrCodeUtility] 錯誤: 讀取 LINE Token 配置失敗 - {ex.Message}");
                return string.Empty;
            }
        }
        #endregion

        #region 主程式
        public void SetupQrCodeIdString( String QrCodeIdString, String DisplayName, String UserLineId, ref String SundayName, ref String CategoryName, ref String UserName, ref String OnboardType)
        {
            try
            {
                #region// 設定區域變數 : 掃描者、全名、聚會統計、簽到還是簽退
                m_QrCodeIdString = QrCodeIdString;

                m_UserLineId = UserLineId;

                #region// 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                if( m_Contact == null )
                {
                    // 透過 LINE ID 找不到此好友，可能還沒加入官LINE@
                    //this.AddNewFriend(DisplayName, UserLineId);

                    OnboardType = "錯誤 : " + DisplayName + "還沒有加入新莊靈糧堂的 Line@";

                    return;
                }
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                #endregion

                #region// 取得聚會統計
                string[] arr;
                if ( QrCodeIdString.Contains("@") )
                {
                    arr = QrCodeIdString.Split('@');
                }
                else 
                {
                    arr = QrCodeIdString.Split('_');
                }

                Guid aGuid = new Guid(arr[0]);
                m_MeetingStatistics = this.m_ToolUtilityClass.RetrieveEntity("new_meeting_statistics", aGuid);
                // 取得聚會統計，主日聚會名稱
                m_SundayName = SundayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_MeetingStatistics, "new_name");

                if (m_SundayName == "")
                {
                    m_SundayName = SundayName = "主日聚會";
                }

                // 取得聚會統計，主日日期
                this.m_Sunday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_MeetingStatistics, "new_sunday_date").ToLocalTime();

                #region// 取得聚會統計屬性
                m_MeetingStatisticsAttribute = arr[1];
                #endregion

                #region// 設定是簽到還是簽退
                m_OnboardType = arr[2];
                #endregion

                #endregion


                #endregion

                #region// 取得聚會統計名稱
                String MeetingStatisticsName = this.m_ToolUtilityClass.GetEntityStringAttribute( m_MeetingStatistics, "new_name" );

                // 取得類別名稱
                if (QrCodeIdString.Contains("@"))
                {
                    m_CategoryName = CategoryName = ConvertMeetingStatisticsQrName(m_MeetingStatisticsAttribute);
                }
                else
                {
                    m_CategoryName = CategoryName = GetDynamicCategoryName();
                }

                #endregion

                #region// 個人聚會與靈修記錄進行簽到退 , 同時傳回結果
                SigningMeetingStatistics( m_MeetingStatistics, UserName, m_Contact.Id.ToString(), m_OnboardType );
                #endregion

                #region// 傳回給網頁簽到或簽退時間，及是否已簽到過了
                OnboardType = m_OnboardTypeInfo;
                #endregion

                #region //如果是週間的聚會，就只顯示類別名稱，而不顯示主日聚會主旨
                if (this.m_SundayName == "")
                {
                    SundayName = m_SundayName;
                }
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
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndSundayDate( UserName, UserId, this.m_Sunday);

                bool RelateMeetingStatisticsFlag = false;
                if ( aPresentRecordCollection.Entities.Count > 0 )
                {
                    #region// 有找到個人聚會與靈修記錄
                    foreach (Entity aPresentRecord in aPresentRecordCollection.Entities)
                    {
                        Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);


                        // 進行簽到或是簽退 => 整理彈跳網頁訊息 + 送出 LINE 訊息
                        // 還沒簽到及簽退，設定簽到時間，主日出席設為1，更新個人聚會與靈修記錄
                        SigningProcess(aRetrievedPresentRecord, OnboardType);

                        #region 設定聚會統計關聯
                        // RelateMeetingStatisticsFlag 的作用是如果建立 N 個出席紀錄單，但是我只要有一筆紀錄顯示在聚會統計即可，以免造成聚會統計有N筆掃描紀錄
                        if (RelateMeetingStatisticsFlag == false)
                        {
                            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aRetrievedPresentRecord, "new_meeting_statistics_new_present_re", "new_meeting_statistics", this.m_MeetingStatistics.Id);
                            RelateMeetingStatisticsFlag = true;
                        }
                        #endregion

                        // 更新個人聚會與靈修記錄
                        this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);

                        #region// 計算週報主日出席人數及出席率
                        lock (m_UpdateSundayWeeklyReportLocker)//避免多人同時掃描"，會產生2個週報或是改變"委身類型"、"裝備狀態"  
                        {
                            Guid aWeeklyReportId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aRetrievedPresentRecord, "new_group_present_weekly_report_prese");

                            if (aWeeklyReportId != Guid.Empty)
                            {
                                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);
                                if (aWeeklyReportEntity != null)
                                {
                                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_saved_flag", "計算出席率");
                                    this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                                }
                            }
                        }
                        #endregion

                    }
                    return true;
                    #endregion
                }
                else
                {
                    #region// 沒找到個人聚會與靈修記錄
                    // 建立一個個人聚會與靈修記錄
                    CreatePresentRecord();

                    return false;
                    #endregion
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
                String aPresentRecordSigningAttribute = "";
                String aPresentRecordSigningNumberAttribute = ""; // 簽到或簽退的次數欄位屬性
                if ( m_QrCodeIdString.Contains("@") )
                {
                    // 取得固定的QR Code掃描時間欄位
                    aPresentRecordSigningAttribute = this.ConvertMeetingStatisticsToPresentRecordAttribute(this.m_MeetingStatisticsAttribute, ref aPresentRecordSigningNumberAttribute);
                }
                else
                {
                    // 取得自定義的QR Code掃描時間欄位
                    aPresentRecordSigningAttribute = this.GetDynamicPresentRecordAttribute();

                    // 因為是週間的聚會，所以只顯示類別名稱，而不顯示主日聚會主旨
                    this.m_SundayName = "";
                }

                // 取得個人聚會與靈修記錄簽的到或簽退時間
                DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedPresentRecord, aPresentRecordSigningAttribute);
                if (aSigningTime.Year <= 1)
                {
                    // 還沒簽到及簽退，設定簽到時間，主日出席設為1，更新個人聚會與靈修記錄 + 送出 LINE 訊息
                    SetPresentRecordTimeAttribute(aRetrievedPresentRecord, aPresentRecordSigningAttribute, aPresentRecordSigningNumberAttribute, "new_sunday_present_this_week");
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
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
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
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽退過了" + Environment.NewLine + "， 可是您尚未註冊過喔!";
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
        private void SetPresentRecordTimeAttribute(Entity aRetrievedPresentRecord, String SigningTimeAttribute, String aPresentRecordSigningNumberAttribute, String SigningPresentAttribute)
        {
            try
            {
                // 簽到或簽退
                // 設定簽到或簽退時間
                m_SigningTime = DateTime.Now;

                // 填寫簽到時間
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedPresentRecord, SigningTimeAttribute, m_SigningTime);

                if (SigningTimeAttribute.Contains("sunday") || SigningTimeAttribute.Contains("saturday") || SigningTimeAttribute.Contains("yongmen"))
                {
                    // 掃描 : 主日第一堂、主日第二堂、週六崇拜、青年崇拜
                    // 主日出席設定為整數1
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, SigningPresentAttribute, 1);

                    // 幸福小組出席設定為整數1
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, "new_happy_present", 1);
                }
                else
                {
                    // 掃描 : 禱告會、門徒訓練班、門徒大聚、小組長小講堂、小組長大聚
                    if ( aPresentRecordSigningNumberAttribute != "" )
                    {
                        // 禱告會、門徒訓練班、門徒大聚、小組長小講堂、小組長大聚出席設定為整數1
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, aPresentRecordSigningNumberAttribute, 1);
                    }
                }

                // 更新個人聚會與靈修記錄
                this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);

                // 送出 LINE 訊息
                String NotifyMessage = GetNotifyMessageString();

                if (SEND_LINE_NOTIFICATION == true)
                {
                    //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);
                    m_PushUtility.SendMessage(m_UserLineId, NotifyMessage);
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
                        "主日: " + m_SundayName + Environment.NewLine +
                        "類型: " + m_CategoryName + Environment.NewLine +
                        "姓名: " + m_UserName + Environment.NewLine +
                        SigningTypeAndTime;
                }
                else
                {
                    // 彈跳要用到的簽到退時間資訊
                    m_OnboardTypeInfo = SigningTypeAndTime + Environment.NewLine + "，可是您尚未註冊過喔!";

                    // 回傳 LINE 要用到的訊息
                    return
                        "主日: " + this.m_SundayName + Environment.NewLine +
                        "類型: " + m_CategoryName + Environment.NewLine +
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
            else if (MeetingStatisticsAttribute.Contains("new_prayer_meeting_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "禱告會簽到";
                }
                else
                {
                    return "禱告會簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_child"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "門徒禱告訓練班簽到";
                }
                else
                {
                    return "門徒禱告訓練班簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_big_disciple_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "門徒大聚簽到";
                }
                else
                {
                    return "門徒大聚簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_leadership_small_lecture_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "小組長小講堂簽到";
                }
                else
                {
                    return "小組長小講堂簽退";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_leaders_gather_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "小組長大聚簽到";
                }
                else
                {
                    return "小組長大聚簽退";
                }
            }
            else
            {
                return "";
            }

        }
        private String GetDynamicCategoryName()
        {
            if( this.m_OnboardType == "On")
            {
                return this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_MeetingStatistics, "new_" + m_MeetingStatisticsAttribute + "_sign_on_name");
            }
            else {
                return this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_MeetingStatistics, "new_" + m_MeetingStatisticsAttribute + "_sign_off_name");
            }
        }
        #endregion
        #region 個人聚會與靈修記錄
        private String ConvertMeetingStatisticsToPresentRecordAttribute(String MeetingStatisticsAttribute, ref String aPresentRecordSigningNumberAttribute)
        {
            if (MeetingStatisticsAttribute.Contains("new_sunday_first_qr"))
            {
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    // 主日第一堂簽到時間
                    return "new_sunday_first_qr_on_time";
                }
                else
                {
                    return "new_sunday_first_qr_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_sunday_second_qr"))
            {
                
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    // 主日第二堂簽到時間
                    return "new_sunday_second_qr_on_time";
                }
                else
                {
                    // 主日第二堂簽退時間
                    return "new_sunday_second_qr_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_saturday_worship"))
            {
                // 週六崇拜簽到時間
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
                // 青年崇拜簽到時間
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    return "new_yongmen_on_time";
                }
                else
                {
                    return "new_yongmen_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_prayer_meeting"))
            {
                // 禱告會
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    aPresentRecordSigningNumberAttribute = "new_prayer_meeting_number";
                    return "new_prayer_meeting_on_time";
                }
                else
                {
                    aPresentRecordSigningNumberAttribute = "new_prayer_meeting_number";
                    return "new_prayer_meeting_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_child"))
            {
                // 門徒訓練班
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    aPresentRecordSigningNumberAttribute = "new_child_number";
                    return "new_child_on_time";
                }
                else
                {
                    aPresentRecordSigningNumberAttribute = "new_child_number";
                    return "new_child_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_big_disciple"))
            {
                // 門徒大聚
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    aPresentRecordSigningNumberAttribute = "new_big_disciple_number";
                    return "new_big_disciple_on_time";
                }
                else
                {
                    aPresentRecordSigningNumberAttribute = "new_big_disciple_number";
                    return "new_big_disciple_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_leadership_small_lecture"))
            {
                // 小組長小講堂
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    aPresentRecordSigningNumberAttribute = "new_leadership_small_lecture_number";
                    return "new_leadership_small_lecture_on_time";
                }
                else
                {
                    aPresentRecordSigningNumberAttribute = "new_leadership_small_lecture_number";
                    return "new_leadership_small_lecture_off_time";
                }
            }
            else if (MeetingStatisticsAttribute.Contains("new_leaders_gather"))
            {
                // 小組長大聚
                if (m_OnboardType == "on" || m_OnboardType == "On")
                {
                    aPresentRecordSigningNumberAttribute = "new_leaders_gather_number";
                    return "new_leaders_gather_on_time";
                }
                else
                {
                    aPresentRecordSigningNumberAttribute = "new_leaders_gather_number";
                    return "new_leaders_gather_off_time";
                }
            }
            else
            {
                return "";
            }

        }
        private String GetDynamicPresentRecordAttribute()
        {
            // new_1_sign_on_time
            if (this.m_OnboardType == "On")
            {
                return "new_" + m_MeetingStatisticsAttribute + "_sign_on_time";
            }
            else
            {
                return "new_" + m_MeetingStatisticsAttribute + "_sign_off_time";
            }
        }
        public void CreatePresentRecord()
        {
            try
            {
                if ( m_Contact != null )
                {
                    // 有加入到教會的官方的LINE@
                    CreatePresentRecordOnSmallGroup();
                }
                else
                {
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
                Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                //指派負責人
                if (m_Contact != null)
                {
                    Guid aContactId = this.m_ToolUtilityClass.GetOwnerId(m_Contact);

                    if (aContactId != null && aContactId != Guid.Empty)
                    {
                        try
                        {
                            this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, aContactId);
                        }
                        catch (System.Exception e)
                        {
                            String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                        }
                    }
                }

                //取得並回傳新建的聚會與靈修記錄
                return aRetrievedPresentRecord;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void CreatePresentRecordOnSmallGroup()
        {
            try
            {
                // 找到聯絡人的所有要點名的小組(牧養小組，而非幸福小組)
                EntityCollection aListCollection = m_ToolUtilityClass.RetrieveListByFetchXmlContact(m_UserName);

                if ( aListCollection.Entities.Count > 0 )
                {
                    #region// 有找到小組
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
                        aWeeklyReportProcessor.CreateWeeklyReportAndPresentRecord(aSmallGroupLeaderEntity, this.m_Sunday, ref WeeklyReportDictionary);
                        #endregion

                        bool RelateMeetingStatisticsFlag = false;
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

                                        #region 設定聚會統計關聯
                                        // RelateMeetingStatisticsFlag 的作用是如果建立 N 個出席紀錄單，但是我只要有一筆紀錄顯示在聚會統計即可，以免造成聚會統計有N筆掃描紀錄
                                        if ( RelateMeetingStatisticsFlag == false )
                                        {
                                            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_meeting_statistics_new_present_re", "new_meeting_statistics", this.m_MeetingStatistics.Id);
                                            RelateMeetingStatisticsFlag = true;
                                        }
                                        #endregion

                                        SigningProcess(aPresentRecord, m_OnboardType);

                                        // 更新出席紀錄單
                                        //this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecord);

                                        #region// 計算週報主日出席人數及出席率
                                        if (m_OnboardTypeInfo.StartsWith("錯誤") != true)
                                        {
                                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_saved_flag", "計算出席率");
                                            this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
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
                            #endregion
                        }
                    }
                    //return null;
                    #endregion
                }
                else
                {
                    #region// 還沒有小組
                    // 新增建立一個個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecordWithNoSmallGroup();

                    //#region 個人聚會與靈修記錄
                    // 進行簽到或是簽退
                    if (aPresentRecord != null)
                    {
                        SigningProcess(aPresentRecord, m_OnboardType);
                    }

                    //return aPresentRecord;
                    #endregion
                }
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
