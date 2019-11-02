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

        // 客製化
        // 音訊教會
        private const String CHANNEL_ACCESS_TOKEN = @"k4/gFG2xonyaewMi8NIPgYdqpcIDnixEpemNIEswwFPzltmlm2kGB6i+uuvvmBaxg9l8wXympy37Y2h7ueq6ECUhTGyBovUXyqgH6lF6aa5R757vsN7sRX7o03dx7tPbj5J5dICcR1JRbvBvxvZ3KQdB04t89/1O/w1cDnyilFU=";

        #endregion
        #endregion
        public QrCodeUtility()
        {
            // 客製化，請選擇
            // 音訊教會(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
        }

        public void SetupQrCodeIdString(String QrCodeIdString, String UserLineId, ref String ClassName, ref String UserName, ref String ClassIndex, ref String OnboardType)
        {
            try
            {
                // 取得掃描者全名
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(UserLineId);
                UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                Entity aLesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(aLesson, "new_name");

                // 取得堂數
                ClassIndex = "第" + arr[1] + "堂課";
                String ClassIndexcontent = this.m_ToolUtilityClass.GetEntityStringAttribute( ref aLesson, this.GetClassAttribute(arr[1])) ;
                if (ClassIndexcontent != "")
                {
                    ClassIndex += "，" + ClassIndexcontent;
                }
                else
                { }

                // 取得簽到簽退時間
                if (arr[2] == "On")
                {
                    OnboardType = DateTime.Now.ToLocalTime().ToString() + " 簽到";
                }
                else
                {
                    OnboardType = DateTime.Now.ToLocalTime().ToString() + " 簽退";
                }

                String NotifyMessage = GetNotifyMessageString(ref ClassName, ref UserName, ref ClassIndex, ref OnboardType);

                //m_LineMessagingClient.PushMessageAsync(UserLineId, NotifyMessage);

                // 在上課紀錄單簽到退
                SetSigningLesson(aLesson, arr[1], arr[2]);

                // 送出 LINE 訊息
                m_PushUtility.SendMessage(UserLineId, NotifyMessage);

                //SmallGroupLeaderContactId = ContactIdString;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public String GetNotifyMessageString( ref String ClassName, ref String UserName, ref String ClassIndex, ref String OnboardType )
        {
            try
            {
                return
                    "課程名稱: " + ClassName + Environment.NewLine +
                    "姓名: " + UserName + Environment.NewLine +
                    "課堂資訊: " + ClassIndex + Environment.NewLine +
                    OnboardType;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #region 設定簽到簽退
        public bool SetSigningLesson(Entity aLesson, String ClassIndex, String OnboardType)
        {
            try
            {
                // 取得與課程相關的上課紀錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml("ContactName", "ContactId" );
                
                return true;
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
    }
}
