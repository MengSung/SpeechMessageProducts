using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models.CrmTransmitModule;

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
using ToolUtility;
using Line.Messaging;
using ChurchReport.Models;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class LineBindingUtility
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        private LineMessagingClient m_LineMessagingClient { get; set; }

        private PushUtility m_PushUtility { get; set; }
        #endregion
        #region 常數參數
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

        // 客製化
        // 音訊靈糧堂
        private const String CHANNEL_ACCESS_TOKEN = @"ZO0wZiAnnPLjFf/5DhlMVXzBWNzCU+xaW8r0vCfBolXg/NLlurOf2VxdR1ZvkRFkDThc2Tlhbqpj6rFnvDs8NtlepAHBnrPvecvuTUhV6Ld9e7p0EmuNvFsqCitOMvRKlLCkR1etr/UBO82MJSTRzwdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #endregion
        #region 初始化

        public LineBindingUtility()
        {
        }


        public String RegisterContact(String DisplayId, string EnteredFullName, String EnteredMobilePhone)
        {
            try
            {
                // 取得撥入者/申請者的 LineId 及使用者姓名
                String LineId = "";
                String ContactFullName = "";

                if (IsBindingAlready(ref LineId, ref ContactFullName) != true)
                {
                    #region // 還沒有綁定註冊過!
                    return ProcessNotYetBinding(ContactFullName, LineId, DisplayId, EnteredFullName, EnteredMobilePhone);
                    #endregion
                }
                else
                {
                    #region// 已經綁定註冊過了
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, ContactFullName + "已經綁定過了請再次點選要報名的課程/活動!");

                    //SendSimpleMessage(DisplayId, ContactFullName + "已經綁定過了請再次點選要報名的課程/活動!");

                    return ContactFullName + "已經綁定過了請再次點選要報名的課程/活動!";
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private String ProcessNotYetBinding(String ContactFullName, String LineId, String DisplayId, string EnteredFullName, String EnteredMobilePhone)
        {
            try
            {
                #region // 還沒有綁定註冊過!
                // 取得寄件人，也就是想要綁定的人
                //Entity aSenderEntity = GetLineSender();

                #region // 註冊失敗過濾區

                // 想要註冊/綁定的姓名及手機號碼
                // Line 好友輸入的綁定姓名
                if (EnteredFullName == "")
                {
                    //SendSimpleMessage(DisplayId, "綁定失敗!沒有姓名!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, "綁定失敗!沒有姓名!");
                    return "綁定失敗!沒有姓名!";
                }
                // Line 好友輸入的綁定行動號碼
                if (EnteredMobilePhone == "")
                {
                    //SendSimpleMessage(DisplayId, "綁定失敗!沒有行動電話!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, "綁定失敗!沒有行動電話!");
                    return "綁定失敗!沒有行動電話!";
                }
                #endregion

                #region// 尋找註冊/綁定的姓名，也就是找到系統裡同名同姓的集合
                Entity aContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByName(ContactFullName);

                if ( aContactEntity != null )
                {
                    #region 在系統裡有相同名字的連絡人，如果有 LineId 表示綁定過過，但是如果是換手機，則目前假設LineId仍然要被更新
                    //if (ProcessEachContactWithSameName(aContactCollection, LineId, DisplayId, ReplyToken, RegisterContactFullName, RegisterContactMobilePhone) == true)
                    //{
                    //    // 已經成功綁定了
                    //    return;
                    //}
                    #endregion
                }
                else
                {
                    #region 在系統裡完全還沒有這個人，因為 aContactCollection.Entities.Count <= 0
                    //UpdateRegisteredContact(RegisterContactFullName, RegisterContactMobilePhone);
                    //return; // 綁定完成，並返回
                    #endregion
                }
                #endregion

                #region // 在系統裡有相同姓名的連絡人，但是電話都不一樣
                //Entity aContactNoMobile = FindEmptyMobileContact(aContactCollection);

                //if (aContactNoMobile != null)
                //{
                //    #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                //    CopyLineInfomation(aContactNoMobile, LineId, DisplayId, ReplyToken, RegisterContactFullName, RegisterContactMobilePhone);
                //    //SendSimpleMessage(LineId, RegisterContactFullName + "註冊已經完成了!");
                //    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, RegisterContactFullName + "註冊已經完成了!");
                //    #endregion
                //    return; // 綁定完成，並返回
                //}
                //else
                //{
                //    #region 在系統裡有相同姓名的連絡人，但是電話都不一樣，而且每個人都有手機號碼
                //    //CreateNewContact(RegisterContactFullName, RegisterContactMobilePhone);
                //    //UpdateRegisteredContact(RegisterContactFullName, RegisterContactMobilePhone);
                //    //SendSimpleMessage(LineId, RegisterContactFullName + "註冊失敗!");
                //    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, RegisterContactFullName + "註冊失敗!");
                //    return;
                //    #endregion
                //}
                #endregion
                #endregion

                return "已經成功綁定了";
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        // 申請綁定的好友是否曾經綁定過了
        private bool IsBindingAlready(ref String LineId, ref String ContactFullName)
        {
            try
            {
                // 取得撥入者的 LineId 及使用者姓名
                Entity LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);
                if ( LineLoginContact != null )
                {
                    ContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(LineLoginContact, "fullname");

                    if (ContactFullName.EndsWith("(Line)"))
                    {
                        return false;// 還沒Binding
                    }
                    else
                    {
                        return true;// 已經Binding過了
                    }
                }
                else
                {
                    return true;// 已經Binding過了
                }

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
