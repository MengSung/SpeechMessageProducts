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
    public class PersonalQrCodeUtility
    {
        #region 資料區
        #region 參數資料
        Entity m_ScannerContact;// 掃描者
        Entity m_Contact; //被掃描者

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");
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

        private String m_PresentAtrrtibute;// 取得聚會統計紀錄中對應到出席紀錄單的簽到欄位
        private DateTime m_SigningTime;

        // FALSE，因為希望只送一次LINE通知
        bool m_NotifyLineFlag = false;

        // 客製化
        // 新莊靈糧堂
        private const String CHANNEL_ACCESS_TOKEN = @"O7kDZ6nG5nenmU7D2z5LzmKgRM9Pf/5/r08Z6zVlmduhTwbfV8HNObv0YceKtM5oiAvTeL3IaiSK8UEh7Y4fS+FSroM/PfHmEEIcvwmMSud3tZUASMEeLVKCy8bL38PfAws2toVIWsTf+qwcrXyHbgdB04t89/1O/w1cDnyilFU=";

        #endregion
        #endregion
        #region 初始化
        public PersonalQrCodeUtility()
        {
            // 客製化，請選擇
            // 新莊靈糧堂(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString(String QrCodeIdString, String DisplayName, String UserLineId, ref String SundayName, ref String CategoryName, ref String UserName, ref String OnboardType)
        {
            try
            {
                #region// 設定區域變數 : 掃描者、全名、聚會統計、簽到還是簽退
                m_QrCodeIdString = QrCodeIdString;

                m_UserLineId = UserLineId;
                #endregion
                #region// 取得掃描者，有可能將來必須是指定某些人才能去掃瞄別人的 QR CODE
                m_ScannerContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                #endregion
                #region// 取得被掃描者
                string[] arr;
                if (QrCodeIdString.Contains("@"))
                {
                    arr = QrCodeIdString.Split('@');
                }
                else
                {
                    arr = QrCodeIdString.Split('_');
                }
                if (arr.Length > 0)
                {
                    Guid aGuid = new Guid(arr[0]);
                    m_Contact = this.m_ToolUtilityClass.RetrieveEntity("contact", aGuid);
                    if ( m_Contact != null )
                    {
                        // 被掃描者存在系統，取得被掃描者全名
                        m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                    }
                    else
                    {
                        OnboardType = "錯誤 : 被掃描者不存在系統中";

                        return;
                    }
                }
                else
                {
                    OnboardType = "錯誤 : QR Code不含被掃描者ID";

                    return;
                }


                #endregion
                #region// 取得聚會統計紀錄
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)DateTime.Now.DayOfWeek;

                // 當週的星期日為認定的主日
                // 每周以星期六為第一日
                if (DayOfWeek != 6)
                {
                    // 如果不是星期六則是上個星期天
                    m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                }
                else
                {
                    // 如果是星期六則是下個星期天
                    m_Sunday = DateTime.Now.AddDays(1);
                }
                #endregion

                EntityCollection MeetingStatisticsCollection = this.m_ToolUtilityClass.RetrieveMeetingStatisticsByFetchXml(this.m_Sunday);
                if (MeetingStatisticsCollection.Entities.Count > 0)
                {
                    m_MeetingStatistics = this.m_ToolUtilityClass.RetrieveEntity("new_meeting_statistics", MeetingStatisticsCollection.Entities[0].Id);

                    // 取得聚會統計，主日聚會名稱
                    m_SundayName = SundayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_MeetingStatistics, "new_name");

                    if (m_SundayName == "")
                    {
                        m_SundayName = SundayName = "主日聚會";
                    }

                }
                else
                {
                    OnboardType = "錯誤 : 沒找到當週主日" + m_Sunday.ToString() + "的聚會統計紀錄";
                    return;
                }
                #endregion
                #region// 取得聚會統計紀錄中對應到出席紀錄單的簽到欄位
                if( (m_PresentAtrrtibute = GetSigningAttribute()) == "")
                {
                    OnboardType = "錯誤 : 聚會統計紀錄中沒找到對應到的簽到時間";
                    return;
                }
                else
                {
                    m_CategoryName = CategoryName = ConvertMeetingStatisticsQrName(m_MeetingStatisticsAttribute);
                    if( m_CategoryName == "")
                    {
                        m_CategoryName = CategoryName = GetDynamicCategoryName();
                    }
                }
                #endregion
                #region// 個人聚會與靈修記錄進行簽到退 , 同時傳回結果
                SigningMeetingStatistics(m_MeetingStatistics, UserName, m_Contact.Id.ToString(), m_OnboardType);
                #endregion

                #region// 傳回給網頁簽到或簽退時間，及是否已簽到過了
                OnboardType = m_OnboardTypeInfo;
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
        public bool SigningMeetingStatistics(Entity aMeetingStatistics, String UserName, String UserId, String OnboardType)
        {
            try
            {
                // 取得與聚會統計主日日期相關的個人聚會與靈修記錄
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndSundayDate(UserName, UserId, this.m_Sunday);

                bool RelateMeetingStatisticsFlag = false;
                if (aPresentRecordCollection.Entities.Count > 0)
                {
                    #region// 有找到個人聚會與靈修記錄
                    m_NotifyLineFlag = false;// 預設為FALSE，因為希望只送一次LINE通知
                    foreach (Entity aPresentRecord in aPresentRecordCollection.Entities)
                    {
                        Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);

                        // 進行簽到或是簽退
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
                // 取得個人聚會與靈修記錄簽的到或簽退時間
                DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedPresentRecord, m_PresentAtrrtibute);
                if (aSigningTime.Year <= 1)
                {
                    // 還沒簽到及簽退，設定簽到時間，主日出席設為1，更新個人聚會與靈修記錄
                    SetPresentRecordTimeAttribute(aRetrievedPresentRecord, m_PresentAtrrtibute, "new_sunday_present_this_week");
                }
                else
                {
                    String NotifyMessage = GetNotifyMessageString();

                    if (OnboardType == "On" || OnboardType == "on")
                    {
                        if (m_UserName.Contains("(Line)") != true)
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 掃描過了";
                        }
                        else
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 掃描過了" + Environment.NewLine + "， 可是尚未綁定過喔!";
                        }
                    }
                    else
                    {
                        if (m_UserName.Contains("(Line)") != true)
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 掃描過了";
                        }
                        else
                        {
                            m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 掃描過了" + Environment.NewLine + "， 可是尚未綁定過喔!";
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
                // 一般主日出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, SigningPresentAttribute, 1);
                // 一般小組出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, "new_group_present_this_week", 1);

                // 幸福小組出席設定為整數1
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aRetrievedPresentRecord, "new_happy_present", 1);

                // 更新個人聚會與靈修記錄
                this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);

                if (this.m_NotifyLineFlag == false)
                {
                    // 設為TRUE，因為希望只送一次LINE通知
                    this.m_NotifyLineFlag = true;
                    // 送出 LINE 訊息
                    String NotifyMessage = GetNotifyMessageString();
                    //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);
                    //m_PushUtility.SendMessage(m_UserLineId, NotifyMessage);
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
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() ;
                }
                else
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() ;
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
                return "主日第一堂簽到";
            }
            else if (MeetingStatisticsAttribute.Contains("new_sunday_second_qr"))
            {
                return "主日第二堂簽到";
            }
            else if (MeetingStatisticsAttribute.Contains("new_saturday_worship"))
            {
                return "週六崇拜簽到";
            }
            else if (MeetingStatisticsAttribute.Contains("new_yongmen"))
            {
                return "青年崇拜簽到";
            }
            else if (MeetingStatisticsAttribute.Contains("new_child"))
            {
                return "兒童主日學簽到";
            }
            else
            {
                return "";
            }

        }
        private String GetDynamicCategoryName()
        {
            return this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_MeetingStatistics, m_MeetingStatisticsAttribute );
        }
        private String GetSigningAttribute()
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                #region 靜態固定名稱的簽到欄位

                if ( VerifySigningAttribute("new_sunday_first_qr_on_time") )
                {
                    return m_MeetingStatisticsAttribute = "new_sunday_first_qr_on_time";
                }
                else if (VerifySigningAttribute("new_sunday_second_qr_on_time") )
                {
                    return m_MeetingStatisticsAttribute = "new_sunday_second_qr_on_time";
                }
                else if (VerifySigningAttribute("new_saturday_worship_on_time"))
                {
                    return m_MeetingStatisticsAttribute = "new_saturday_worship_on_time";
                }
                else if (VerifySigningAttribute("new_yongmen_on_time"))
                {
                    return m_MeetingStatisticsAttribute = "new_yongmen_on_time";
                }
                else if (VerifySigningAttribute("new_child_on_time"))
                {
                    return m_MeetingStatisticsAttribute = "new_child_on_time";
                }
                else
                {
                    //return "";
                }
                #endregion

                #region 動態自訂名稱的簽到欄位
                String TimeAttribute = "";
                for (int i = 1; i <= 10; i++)
                {
                    // 格式如右邊所示: new_1_sign_on_time
                    TimeAttribute = this.m_ToolUtilityClass.GetEntityStringAttribute(m_MeetingStatistics, "new_" + i.ToString() + "_sign_on_time");
                    if (CompareWithTimeNow(TimeAttribute) == true)
                    {
                        // 取得聚會統計紀錄中對應到出席紀錄單的簽到欄位
                        // 第N個簽到時間
                        m_MeetingStatisticsAttribute = "new_" + i.ToString() + "_sign_on_name";
                        return "new_" + i.ToString() + "_sign_on_time";
                    }
                    else
                    {
                        //return "";
                    }
                }
                #endregion


                //都沒找到對應的時間就回傳空白字串
                return "";

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private bool VerifySigningAttribute( String Attribute )
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                #region 靜態固定名稱的簽到欄位
                String TimeAttribute = "";

                if ((TimeAttribute = this.m_ToolUtilityClass.GetEntityStringAttribute(m_MeetingStatistics, Attribute)) != "")
                {
                    if (CompareWithTimeNow(TimeAttribute) == true)
                    {
                        // 取得聚會統計紀錄中對應到出席紀錄單的簽到欄位
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
                #endregion

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private bool CompareWithTimeNow(String TimeAttribute)
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                // TimeAttribute 格式 => 星期二,19:00~21:00
                string[] TimeAttributeArray;
                TimeAttributeArray = TimeAttribute.Split(',');

                if( TimeAttributeArray.Length >= 2 )
                {
                    if ( CompareWeekday(TimeAttributeArray[0]) == true )
                    {
                        // 有找到星期幾一致的
                        if( CompareTime(TimeAttributeArray[1]) == true )
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // 沒找到星期幾一致的
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private bool CompareWeekday(String WeekAttribute)
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                // 取得現在時間是星期幾
                int DayOfWeek = (int)DateTime.Now.DayOfWeek;

                if (DayOfWeek == ConvertWeekday(WeekAttribute.Replace(" ", "")))
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private bool CompareTime(String TimeAttribute)
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                // TimeAttribute 格式 => 19:00~21:00

                string[] TimeAttributeArray;
                TimeAttributeArray = TimeAttribute.Split('~');

                if (TimeAttributeArray.Length >= 2)
                {
                    String StartTimeString = TimeAttributeArray[0].Replace(" ", "");
                    String EndTimeString   = TimeAttributeArray[1].Replace(" ", "");

                    DateTime StartTime = GetMappingTime(StartTimeString);
                    DateTime EndTime = GetMappingTime(EndTimeString);

                    if (DateTime.Now >= StartTime && DateTime.Now <= EndTime)
                    { 
                        return true; 
                    }
                    else 
                    { 
                        return false; 
                    }
                }
                else
                {
                    return false;
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private DateTime GetMappingTime(String TimeString)
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                // TimeAttribute 格式 => 19:00
                string[] TimeAttributeArray;
                TimeAttributeArray = TimeString.Split(':');

                if (TimeAttributeArray.Length >= 2)
                {
                    return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, Convert.ToInt32(TimeAttributeArray[0].Replace(" ", "")), Convert.ToInt32(TimeAttributeArray[1].Replace(" ", "")),0);
                }
                else
                {
                    return new DateTime();
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
        }
        private int ConvertWeekday(String WeekAttribute)
        {
            #region 取得與現在時間吻合的簽到欄位
            try
            {
                switch (WeekAttribute.Replace(" ", ""))
                {
                    case "星期日":
                        return 0;
                    case "星期一":
                        return 1;
                    case "星期二":
                        return 2;
                    case "星期三":
                        return 3;
                    case "星期四":
                        return 4;
                    case "星期五":
                        return 5;
                    case "星期六":
                        return 6;
                    default:
                        return -1;
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
            #endregion
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
                if (m_Contact != null)
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
        private Entity CreatePresentRecordWithSmallGroup( Entity aWeeklyReportEntity )
        {
            try
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, ref this.m_Contact);

                // 週報
                if (aWeeklyReportEntity.Id != null && aWeeklyReportEntity.Id != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportEntity.Id);
                }

                // 小組
                Guid aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report");
                if (aGuid != null && aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aGuid);
                }

                // 小組長
                aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_");
                if (aGuid != null && aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "list", aGuid);
                }

                // 上代組長
                aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_contact_arealeader_weekly_report");
                if (aGuid != null && aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "list", aGuid);
                }

                // 小家長
                aGuid = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_group_head_group_present_weekly_r");
                if (aGuid != null && aGuid != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "list", aGuid);
                }
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
        private void CreatePresentRecordOnSmallGroup()
        {
            try
            {
                // 找到聯絡人的所有要點名的小組(牧養小組，而非幸福小組)
                EntityCollection aListCollection = m_ToolUtilityClass.RetrieveListByFetchXmlContact(m_UserName);

                if (aListCollection.Entities.Count > 0)
                {
                    #region// 有找到小組
                    foreach (Entity aListEntity in aListCollection.Entities)
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

                            if (aWeeklyReportEntity != null)
                            {
                                EntityCollection aPresentRecordCollection = this.m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndWeeklyReport(m_UserName, m_Contact.Id.ToString(), this.m_ToolUtilityClass.GetEntityStringAttribute(aWeeklyReportEntity, "new_name"), aWeeklyReportEntity.Id.ToString());

                                if (aPresentRecordCollection.Entities.Count > 0)
                                {
                                    // 找到與此建立的週報和聯絡人相關的出席紀錄單，所以進行簽到或是簽退
                                    Entity aPresentRecord = aPresentRecordCollection.Entities[0];

                                    if (aPresentRecord != null)
                                    {
                                        aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);

                                        #region 設定聚會統計關聯
                                        // RelateMeetingStatisticsFlag 的作用是如果建立 N 個出席紀錄單，但是我只要有一筆紀錄顯示在聚會統計即可，以免造成聚會統計有N筆掃描紀錄
                                        if (RelateMeetingStatisticsFlag == false)
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
                                    // 新增建立一個個人聚會與靈修記錄
                                    Entity aPresentRecord = CreatePresentRecordWithSmallGroup(aWeeklyReportEntity);

                                    //#region 個人聚會與靈修記錄
                                    // 進行簽到或是簽退
                                    if (aPresentRecord != null)
                                    {
                                        SigningProcess(aPresentRecord, m_OnboardType);
                                    }
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
