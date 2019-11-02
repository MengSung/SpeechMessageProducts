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
    public class QrCodeUtility
    {
        #region 資料區
        #region 參數資料
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
        // 音訊教會
        private const String CHANNEL_ACCESS_TOKEN = @"k4/gFG2xonyaewMi8NIPgYdqpcIDnixEpemNIEswwFPzltmlm2kGB6i+uuvvmBaxg9l8wXympy37Y2h7ueq6ECUhTGyBovUXyqgH6lF6aa5R757vsN7sRX7o03dx7tPbj5J5dICcR1JRbvBvxvZ3KQdB04t89/1O/w1cDnyilFU=";

        #endregion
        #endregion
        #region 初始化

        public QrCodeUtility()
        {
            // 客製化，請選擇
            // 音訊教會(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }
        #endregion
        #region 主程式
        public void SetupQrCodeIdString(String QrCodeIdString, String UserLineId, ref String ClassName, ref String UserName, ref String ClassIndex, ref String OnboardType)
        {
            try
            {
                #region 設定區域變數
                m_UserLineId = UserLineId;

                // 取得掃描者全名
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                m_UserName = UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute( m_Lesson, "new_name");

                // 取得堂數
                m_ClassIndex = arr[1];

                // 設定是簽到還是簽退
                m_OnboardType = arr[2];

                #endregion

                // 在上課紀錄單進行簽到退
                SigningLesson(m_Lesson, ClassName, UserName, aContact.Id.ToString(), m_ClassIndex, m_OnboardType);

                ClassIndex = m_ClassIndexInfo ;

                OnboardType = m_OnboardTypeInfo ;

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
                // 取得與課程相關的上課紀錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml(LessonName, aLesson.Id.ToString(), UserName, UserId);

                if (aStorLessonsEntityCollection.Entities.Count > 0)
                {
                    // 有找到上課紀錄單
                    Entity RetrievedStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsEntityCollection.Entities[0].Id);

                    // 進行簽到或是簽退
                    SigningProcess(RetrievedStorLessons, ClassIndex, OnboardType);
                    //SetStorLessonsClass(m_Lesson, LessonName, UserName, UserId, ClassIndex, OnboardType);

                    return true;
                }
                else
                {
                    // 沒找到上課紀錄單
                    return false;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SigningProcess( Entity aRetrievedStorLessons, String ClassIndex, String OnboardType)
        {
            try
            {
                // 取得上課紀錄單
                String SigningTimeAttribute = this.GetStorLessonsTimeAttribute(ClassIndex, OnboardType);

                if (OnboardType == "On")
                {
                    // 簽到
                    DateTime aSigningTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute);
                    if ( aSigningTime.Year <= 1)
                    {
                        m_SigningTime = DateTime.Now;
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
                        this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

                        // 送出 LINE 訊息
                        String NotifyMessage = GetNotifyMessageString();
                        //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);
                        m_PushUtility.SendMessage( m_UserLineId, NotifyMessage);

                    }
                    else
                    {
                        String NotifyMessage = GetNotifyMessageString();

                        m_OnboardTypeInfo = "已經在 " + aSigningTime.ToLocalTime().ToString() + " 簽到過了";

                    }
                }
                else
                {
                    // 簽退
                    m_SigningTime = DateTime.Now;
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aRetrievedStorLessons, SigningTimeAttribute, m_SigningTime);
                    this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedStorLessons);

                    // 送出 LINE 訊息
                    String NotifyMessage = GetNotifyMessageString();
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
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽到";
                }
                else
                {
                    SigningTypeAndTime = m_SigningTime.ToLocalTime().ToString() + " 簽退";
                }

                m_ClassIndexInfo = LocalClassIndex;

                m_OnboardTypeInfo = SigningTypeAndTime;

                return
                    "課程名稱: " + m_ClassName + Environment.NewLine +
                    "姓名: " + m_UserName + Environment.NewLine +
                    "課堂資訊: " + LocalClassIndex + Environment.NewLine +
                    SigningTypeAndTime;
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
