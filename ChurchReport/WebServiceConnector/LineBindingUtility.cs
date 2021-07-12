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
        // RPG復興禱告
        private const String CHANNEL_ACCESS_TOKEN = @"z9/Jcbkvfm24ZogW0nHlaqONQjFYB6A10y2EPp07kWmw6vXSQSrTYrno3OuzCfM+ewEFWlahpjSOYa4HzyYxhZuAFbvoTQVPI/gjkE2PYMx5BESvuwJLJRZ86u3my9lD7zzvDNdZwStZzJh+IHmPFwdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #endregion
        #region 初始化

        public LineBindingUtility()
        {
        }
        public String RegisterContact(String UserLineId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone)
        {
            try
            {
                // 取得撥入者/申請者的 LineId 及使用者姓名
                //String LineId = "";
                String ContactFullName = "";

                if (UserLineId != "" && UserLineId != null)
                {
                    if (IsBindingAlready(ref UserLineId, ref ContactFullName) != true)
                    {
                        #region // 還沒有綁定註冊過!
                        return ProcessNotYetBinding(ContactFullName, UserLineId, "", EnteredFullName, EnteredOtherName, EnteredMobilePhone);
                        #endregion
                    }
                    else
                    {
                        #region// 已經註冊註冊過了
                        return ContactFullName + "已經註冊過了!";
                        #endregion
                    }
                }
                else
                {
                    if (UserLineId != null)
                    {
                        return "您的 Line Id=" + UserLineId + " ，無法辨識請洽辦公室行政人員為您服務喔!或許您是否在加入時沒有同意授權呢?";
                    }
                    else
                    {
                        return "您的 Line Id= null，無法辨識請洽辦公室行政人員為您服務喔!或許您是否在加入時沒有同意授權呢?";
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        private String ProcessNotYetBinding(String ContactFullName, String LineId, String DisplayId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone)
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
                EntityCollection aContactCollection = this.m_ToolUtilityClass.RetrieveContactCollectionByName(EnteredFullName);

                if (aContactCollection.Entities.Count > 0)
                {
                    #region 在系統裡有相同名字的連絡人，如果有 LineId 表示綁定過過，但是如果是換手機，則目前假設LineId仍然要被更新
                    String Result = "";
                    if (ProcessEachContactWithSameName(aContactCollection, LineId, DisplayId, EnteredFullName, EnteredOtherName, EnteredMobilePhone, ref Result) == true)
                    {
                        // 已經成功綁定了
                        //return "成功綁定了";
                        return Result;
                    }
                    else
                    {
                        //return Result;
                    }
                    #endregion
                }
                else
                {
                    #region 在系統裡完全還沒有這個人，因為 aContactCollection.Entities.Count <= 0
                    UpdateRegisteredContact( LineId, EnteredFullName, EnteredOtherName, EnteredMobilePhone);
                    return "綁定成功"; // 綁定成功，並返回
                    #endregion
                }
                #endregion

                #region // 跳出了迴圈在系統裡有相同姓名的連絡人，但是電話都不一樣
                Entity aContactNoMobile = FindEmptyMobileContact(aContactCollection);

                if (aContactNoMobile != null)
                {
                    #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                    CopyLineInfomation(aContactNoMobile, LineId, DisplayId, EnteredFullName, EnteredOtherName, EnteredMobilePhone);
                    //SendSimpleMessage(LineId, RegisterContactFullName + "註冊已經完成了!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, RegisterContactFullName + "註冊已經完成了!");
                    #endregion
                    return "綁定成功"; // 綁定完成，並返回
                }
                else
                {
                    #region 在系統裡有相同姓名的連絡人，但是電話都不一樣，而且每個人都有手機號碼
                    //CreateNewContact(RegisterContactFullName, RegisterContactMobilePhone);
                    //UpdateRegisteredContact(RegisterContactFullName, RegisterContactMobilePhone);
                    //SendSimpleMessage(LineId, RegisterContactFullName + "註冊失敗!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, RegisterContactFullName + "註冊失敗!");
                    return "在系統裡有相同姓名的連絡人，但是電話都不一樣，而且每個人都有手機號碼";
                    #endregion
                }
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

        private bool ProcessEachContactWithSameName(EntityCollection aContactCollection, String LineId, String DisplayId ,String RegisterContactFullName, string RegisterContactOtherName, String RegisterContactMobilePhone, ref String Result )
        {
            try
            {
                #region 在系統裡有相同名字的連絡人，如果有 LineId 表示綁定過過，但是如果是換手機，則目前假設LineId仍然要被更新
                foreach (Entity aContactEntity in aContactCollection.Entities)
                {
                    #region 一個一個處理所有相同姓名的人
                    String MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone");
                    MobilePhone = this.m_ToolUtilityClass.FilterDigit(MobilePhone);

                    if (RegisterContactMobilePhone == MobilePhone)
                    {
                        #region 在系統裡已經有同名同姓而且手機相同的人，就認定是相同的連絡人，而且是還沒被綁定的連絡人
                        CopyLineInfomation(aContactEntity, LineId, DisplayId, RegisterContactFullName, RegisterContactOtherName, RegisterContactMobilePhone);
                        Result = "綁定成功，並返回";
                        return true; // 綁定完成，並返回
                        #endregion
                    }
                    else
                    {
                        #region 在系統裡已經有同名同姓而且手機卻不相同的人，就認定是不同的連絡人，必須是同名同姓，而且手機相同的才符合是同一人的條件
                        // 同名同姓，手機不同，就不做任何處理
                        if (MobilePhone != "")
                        {
                            //SendSimpleMessage(DisplayId, RegisterContactFullName + "，發生同名同姓的問題!" + Environment.NewLine + "但是行動號碼不一樣!");
                            //return RegisterContactFullName + "，發生同名同姓的問題!" + Environment.NewLine + "但是行動號碼不一樣!";
                            Result = RegisterContactFullName + "，發生同名同姓的問題!" + Environment.NewLine + "但是行動號碼不一樣!";
                            //return false;
                        }
                        #endregion
                    }
                    #endregion
                }

                // 在系統裡已經有同名同姓，但是手機號碼全都不一樣
                return false;
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        private void CopyLineInfomation(Entity aContactNoMobile, String LineId, String DisplayId, String RegisterContactFullName, string RegisteredOtherName, String RegisterContactMobilePhone)
        {
            try
            {
                #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                #region 系統中的連絡人進行註冊(綁定)，取得寄件人的 LINE 相關訊息(含 LineId )，並複製給系統中的連絡人

                // 在系統裡已經有同名同姓而且手機相同的人，就認定是相同的連絡人，而且是還沒被綁定的連絡人
                // 取得寄件人
                Entity EnteredLineContactEntity = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);

                // 取得寄件人的 LINE 相關訊息並複製給系統中的連絡人
                CopyLineInfomation(EnteredLineContactEntity, aContactNoMobile);

                // 填入其他姓名
                CopyOtherName(RegisteredOtherName, aContactNoMobile);

                // 填入行動電話
                CopyMobilePhoneNumber(RegisterContactMobilePhone, aContactNoMobile);

                // 更新系統中的連絡人
                this.m_ToolUtilityClass.UpdateEntity( aContactNoMobile);
                #endregion

                #region// 移除登錄者的LINE Id
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref SenderEntity, "new_lineid", "");
                //
                //// 更新系統中的登錄者
                //this.m_ToolUtilityClass.UpdateEntity(ref this.m_CrmService, SenderEntity);

                // 刪除系統中的登錄者
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(EnteredLineContactEntity, "fullname").EndsWith("(Line)") == true)
                {
                    // 必須確保是"真的"尚未綁定過的才能刪除
                    this.m_ToolUtilityClass.DeleteEntity("contact", EnteredLineContactEntity.Id);
                }

                #endregion

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        // 申請綁定的好友是否曾經綁定過了
        private Entity FindEmptyMobileContact(EntityCollection aContactCollection)
        {
            try
            {
                // 尋找沒有手機號碼的連絡人
                foreach (Entity aContactEntity in aContactCollection.Entities)
                {
                    String MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone");
                    MobilePhone = this.m_ToolUtilityClass.FilterDigit(MobilePhone);

                    if (MobilePhone == "")
                    {
                        return aContactEntity; // 找到沒有手機號碼的連絡人，並返回
                    }
                }

                return null;// 找不到沒有手機號碼的連絡人，並返回

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void CopyLineInfomation(Entity FromContact, Entity ToContact)
        {
            try
            {
                // LINE Id
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "new_lineid",
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref FromContact, "new_lineid")
                );

                // LINE Id 備份
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "new_lineid_backup",
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref FromContact, "new_lineid_backup")
                );

                // LINE 照片網址
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "new_line_picture_url",
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref FromContact, "new_line_picture_url")
                );
                // LINE 狀態訊息
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "new_line_status_message",
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref FromContact, "new_line_status_message")
                );
                // LINE 顯示名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "new_line_displayname",
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref FromContact, "new_line_displayname")
                );
                // LINE 狀態
                this.m_ToolUtilityClass.SetOptionSetAttribute
                (
                    ref ToContact,
                    "new_line_status",
                    this.m_ToolUtilityClass.GetOptionSetAttribute(ref FromContact, "new_line_status")
                );

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void CopyMobilePhoneNumber(String RegisteredMobilePhoneNumber, Entity ToContact)
        {
            try
            {
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(ref ToContact, "mobilephone") == "")
                {
                    // 設定行動電話
                    this.m_ToolUtilityClass.SetEntityStringAttribute
                    (
                        ref ToContact,
                        "mobilephone",
                        RegisteredMobilePhoneNumber
                    );

                }
                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void CopyOtherName(String RegisteredOtherName, Entity ToContact)
        {
            try
            {
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(ref ToContact, "new_other_name") == "")
                {
                    // 設定行動電話
                    this.m_ToolUtilityClass.SetEntityStringAttribute
                    (
                        ref ToContact,
                        "new_other_name",
                        RegisteredOtherName
                    );

                }
                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }


        private void UpdateRegisteredContact(String LineId, String RegisterContactFullName, string RegisterContactOtherName, String RegisterContactMobilePhone)
        {
            try
            {
                #region 在系統裡沒有相同姓名的連絡人
                // 取得寄件人
                Entity aEnteredContactEntity = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);

                // 更新寄件人的姓名
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aEnteredContactEntity, "lastname", RegisterContactFullName);

                // 更新寄件人的其他姓名
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aEnteredContactEntity, "new_other_name", RegisterContactOtherName);

                // 更新寄件人的行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aEnteredContactEntity, "mobilephone", RegisterContactMobilePhone);

                // 設定系統中的連絡人為已註冊
                //this.m_ToolUtilityClass.SetEntityBoolAttribute(aSenderEntity, "new_line_register", true);

                // 更新系統中的登錄者
                this.m_ToolUtilityClass.UpdateEntity(aEnteredContactEntity);

                return;
                #endregion

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
