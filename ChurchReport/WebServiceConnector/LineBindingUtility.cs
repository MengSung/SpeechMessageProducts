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
using ChurchReport.ViewModel;
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

        LineBindingViewModel m_LineBindingViewModel;

        #endregion
        #region 常數參數
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

        // 客製化
        // 南崁基督長老教會
        private const String CHANNEL_ACCESS_TOKEN = @"m7bC4vm/2pA8VEBbHZ1YHdr0iz4fmOMWqT1jEZg+62DFvGEEfY7JEJ7up5gNdpJ3DSZHFmr+YZpEu02B15B4ZMx7s03ZeLqZi1lSmpxsA04Zi6cOJlQemlXjlUMlh+HOKb3BfOhOPY+hYtMbH2tUXQdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #endregion
        #region 綁定
        public LineBindingUtility()
        {
        }

        public String VerifyContact(String UserLineId)
        {
            try
            {
                // 取得撥入者/申請者的 LineId 及使用者姓名
                //String LineId = "";
                String ContactFullName = "";

                if (UserLineId != "" && UserLineId != null)
                {
                    Entity LineLoginContact = null;
                    if (IsBindingAlreadyOrNot(ref UserLineId, ref ContactFullName, ref LineLoginContact) != true)
                    {
                        #region // 還沒有綁定註冊過!
                        return ContactFullName + "歡迎您進行註冊!";
                        #endregion
                    }
                    else
                    {
                        #region// 已經註冊註冊過了 或是 沒有找到註冊者
                        if (LineLoginContact != null)
                        {
                            return ContactFullName + "已經註冊過了!";
                        }
                        else
                        {
                            return "沒有找到註冊者!";
                        }
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

        public String RegisterContact(String UserLineId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                // 取得撥入者/申請者的 LineId 及使用者姓名
                //String LineId = "";
                String ContactFullName = "";

                if (UserLineId != "" && UserLineId != null)
                {
                    Entity LineLoginContact = null;
                    if (IsBindingAlready(ref UserLineId, ref ContactFullName, ref LineLoginContact) != true)
                    {
                        #region // 還沒有綁定註冊過!
                        return ProcessNotYetBinding(ContactFullName, UserLineId, "", EnteredFullName, EnteredOtherName, EnteredMobilePhone);
                        #endregion
                    }
                    else
                    {
                        #region// 已經註冊註冊過了 或是 沒有找到註冊者
                        if (LineLoginContact != null)
                        {
                            return ContactFullName + "已經註冊過了!";
                        }
                        else
                        {
                            // 沒有找到註冊者
                            return ProcessNotFindRegister(ContactFullName, UserLineId, "", EnteredFullName, EnteredOtherName, EnteredMobilePhone, aLineBindingViewModel);
                        }
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
                    UpdateRegisteredContact(LineId, EnteredFullName, EnteredOtherName, EnteredMobilePhone);
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
        private String ProcessNotFindRegister(String ContactFullName, String LineId, String DisplayId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region // 在系統中沒找到有註冊者ID的連絡人
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
                    #region 處理系統裡有相同名字的連絡人
                    String Result = "";
                    if ( ProcessEachContactWithSameName(aContactCollection, LineId, DisplayId, EnteredFullName, EnteredOtherName, EnteredMobilePhone, ref Result) == true )
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
                    CreateContactFromRegister(LineId, EnteredFullName, EnteredOtherName, EnteredMobilePhone,aLineBindingViewModel);
                    return "綁定成功"; // 綁定成功，並返回
                    #endregion
                }
                #endregion

                #region // 跳出了迴圈在系統裡有相同姓名的連絡人，但是電話都不一樣
                Entity aContactNoMobile = FindEmptyMobileContact(aContactCollection);

                if (aContactNoMobile != null)
                {
                    #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                    UpdateContactLineInfo(aContactNoMobile, LineId, DisplayId, EnteredFullName, EnteredOtherName, EnteredMobilePhone, aLineBindingViewModel);
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
        private bool IsBindingAlreadyOrNot(ref String LineId, ref String ContactFullName, ref Entity LineLoginContact)
        {
            try
            {
                // 取得撥入者的 LineId 及使用者姓名
                LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);
                if (LineLoginContact != null)
                {
                    ContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(LineLoginContact, "fullname");
                    String Mobile = this.m_ToolUtilityClass.GetEntityStringAttribute(LineLoginContact, "mobilephone");

                    if (ContactFullName.EndsWith("(Line)") && Mobile == "")
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
                    return true;// 沒有這個人
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private bool IsBindingAlready(ref String LineId, ref String ContactFullName, ref Entity LineLoginContact)
        {
            try
            {
                // 取得撥入者的 LineId 及使用者姓名
                LineLoginContact = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);
                if (LineLoginContact != null)
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
                    return true;// 沒有這個人
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private bool ProcessEachContactWithSameName(EntityCollection aContactCollection, String LineId, String DisplayId, String RegisterContactFullName, string RegisterContactOtherName, String RegisterContactMobilePhone, ref String Result)
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
        private bool ProcessEachSameNameContactButNoLineId(EntityCollection aContactCollection, String LineId, String DisplayId, String RegisterContactFullName, string RegisterContactOtherName, String RegisterContactMobilePhone, ref String Result)
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
                        #region 在系統裡已經有同名同姓而且手機相同的人，就認定是相同的連絡人，但是沒有LINE Profile
                        //CopyLineInfomation(aContactEntity, LineId, DisplayId, RegisterContactFullName, RegisterContactOtherName, RegisterContactMobilePhone);
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
                this.m_ToolUtilityClass.UpdateEntity(aContactNoMobile);
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

        #region 來賓QR CODE
        public String RigisterVisitorCard(String UserLineId, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                m_LineBindingViewModel = aLineBindingViewModel;
                // 取得撥入者/申請者的 LineId 及使用者姓名
                //String LineId = "";
                String ContactFullName = "";

                if (UserLineId != "" && UserLineId != null)
                {
                    return ProcessVisitorCard(ContactFullName, UserLineId, "", aLineBindingViewModel);
                }
                else
                {
                    if (UserLineId != null)
                    {
                        return "您的 Line Id=" + UserLineId + " ，無法辨識請洽辦公室行政人員為您服務喔!";
                    }
                    else
                    {
                        return "您的 Line Id= null，無法辨識請洽辦公室行政人員為您服務喔!";
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private String ProcessVisitorCard(String ContactFullName, String LineId, String DisplayId, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region // 還沒有註冊註冊過!
                // 取得寄件人，也就是想要註冊的人
                //Entity aSenderEntity = GetLineSender();

                #region // 註冊失敗過濾區
                // 想要註冊/註冊的姓名及手機號碼
                // Line 好友輸入的註冊姓名
                if (aLineBindingViewModel.FullName == "")
                {
                    //SendSimpleMessage(DisplayId, "註冊失敗!沒有姓名!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, "註冊失敗!沒有姓名!");
                    return "註冊失敗!沒有姓名!";
                }
                // Line 好友輸入的註冊行動號碼
                if (aLineBindingViewModel.Mobile == "")
                {
                    //SendSimpleMessage(DisplayId, "註冊失敗!沒有行動電話!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, "註冊失敗!沒有行動電話!");
                    return "註冊失敗!沒有行動電話!";
                }
                #endregion

                #region// 尋找註冊/註冊的姓名，也就是找到系統裡同名同姓的集合
                EntityCollection aContactCollection = this.m_ToolUtilityClass.RetrieveContactCollectionByName(aLineBindingViewModel.FullName);

                if (aContactCollection.Entities.Count > 0)
                {
                    #region 在系統裡有相同名字的連絡人，如果有 LineId 表示註冊過過，但是如果是換手機，則目前假設LineId仍然要被更新
                    String Result = "";
                    if (ProcessEachContactWithSameName(aContactCollection, LineId, DisplayId, aLineBindingViewModel, ref Result) == true)
                    {
                        // 已經成功註冊了
                        //return "成功註冊了";
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
                    if (m_ToolUtilityClass.RetrieveContactByLineId(LineId) == null)
                    {
                        // 而且也沒有相同LINE ID 的人，就要建立一個新連絡人
                        CreateContact(LineId, aLineBindingViewModel);
                        return "註冊成功"; // 註冊成功，並返回
                    }
                    else
                    {
                        return "不能註冊，因為已經有相同的LINE ID"; // 註冊成功，並返回
                    }
                    #endregion
                }
                #endregion

                #region // 跳出了迴圈在系統裡有相同姓名的連絡人，但是電話都不一樣
                Entity aContactNoMobile = FindEmptyMobileContact(aContactCollection);

                if (aContactNoMobile != null)
                {
                    #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                    CopyLineInfomation(aContactNoMobile, LineId, DisplayId, aLineBindingViewModel);
                    //SendSimpleMessage(LineId, RegisterContactFullName + "註冊已經完成了!");
                    //this.m_LineUtilityClass.ReplyTextMessage(ReplyToken, RegisterContactFullName + "註冊已經完成了!");
                    #endregion
                    return "註冊成功"; // 註冊完成，並返回
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
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private bool ProcessEachContactWithSameName(EntityCollection aContactCollection, String LineId, String DisplayId, LineBindingViewModel aLineBindingViewModel, ref String Result)
        {
            try
            {
                #region 在系統裡有相同名字的連絡人，如果有 LineId 表示註冊過過，但是如果是換手機，則目前假設LineId仍然要被更新
                foreach (Entity aContactEntity in aContactCollection.Entities)
                {
                    #region 一個一個處理所有相同姓名的人
                    String MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone");
                    MobilePhone = this.m_ToolUtilityClass.FilterDigit(MobilePhone);

                    if (aLineBindingViewModel.Mobile == MobilePhone)
                    {
                        #region 在系統裡已經有同名同姓而且手機相同的人，就認定是相同的連絡人，而且是還沒被註冊的連絡人
                        CopyLineInfomation(aContactEntity, LineId, DisplayId, aLineBindingViewModel);
                        Result = "註冊成功，並返回";
                        return true; // 註冊完成，並返回
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
                            Result = aLineBindingViewModel.FullName + "，發生同名同姓的問題!" + Environment.NewLine + "但是行動號碼不一樣!";
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
        private void CopyLineInfomation(Entity aContactNoMobile, String LineId, String DisplayId, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region 找到一個在系統裡已經有同名同姓，但是卻沒有手機號碼
                #region 系統中的連絡人進行註冊(註冊)，取得寄件人的 LINE 相關訊息(含 LineId )，並複製給系統中的連絡人

                // 在系統裡已經有同名同姓而且手機相同的人，就認定是相同的連絡人，而且是還沒被註冊的連絡人
                // 取得寄件人
                Entity EnteredLineContactEntity = this.m_ToolUtilityClass.RetrieveContactByLineId(LineId);

                // 取得寄件人的 LINE 相關訊息並複製給系統中的連絡人
                CopyLineInfomation(EnteredLineContactEntity, aContactNoMobile);

                CopyVistorCardInfo(aLineBindingViewModel, ref aContactNoMobile);

                // 更新系統中的連絡人
                this.m_ToolUtilityClass.UpdateEntity(aContactNoMobile);
                #endregion

                #region// 移除登錄者的LINE Id
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref SenderEntity, "new_lineid", "");
                //
                //// 更新系統中的登錄者
                //this.m_ToolUtilityClass.UpdateEntity(ref this.m_CrmService, SenderEntity);

                // 刪除系統中的登錄者
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(EnteredLineContactEntity, "fullname").EndsWith("(Line)") == true)
                {
                    // 必須確保是"真的"尚未註冊過的才能刪除
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
        private void CopyVistorCardInfo(LineBindingViewModel aLineBindingViewModel, ref Entity ToContact)
        {
            try
            {
                // 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref ToContact,
                    "mobilephone",
                    aLineBindingViewModel.Mobile
                );

                // 設定性別
                if (aLineBindingViewModel.Gender != null)
                {
                    if (aLineBindingViewModel.Gender == "男性")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "gendercode", 200000); }
                    else if (aLineBindingViewModel.Gender == "女性")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "gendercode", 200001); }
                    else { }
                }

                // 設定信仰狀態
                if (aLineBindingViewModel.Status != null)
                {
                    if (aLineBindingViewModel.Status == "-未知-")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000004); }
                    else if (aLineBindingViewModel.Status == "基督徒")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000001); }
                    else if (aLineBindingViewModel.Status == "已決志")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000002); }
                    else if (aLineBindingViewModel.Status == "慕道友")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000005); }
                    else if (aLineBindingViewModel.Status == "未信主")
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000003); }
                    else //-未知-
                    { this.m_ToolUtilityClass.SetOptionSetAttribute(ref ToContact, "new_spiriitual_identity", 100000004); }
                }

                // 設定生日
                if (aLineBindingViewModel.BirthDate != null && aLineBindingViewModel.BirthDate.Year > 1919)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref ToContact, "birthdate", aLineBindingViewModel.BirthDate.ToLocalTime());
                }

                // 設定到教會日
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref ToContact, "new_enter_church_date", DateTime.Now.ToLocalTime());
                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private async Task CreateContact(String LineId, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region 在系統裡沒有相同姓名的連絡人
                // 取得寄件人
                Entity aCreatedContactEntity = new Entity("contact");

                // 更新寄件人的姓名
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "lastname", aLineBindingViewModel.FullName);

                CopyVistorCardInfo(aLineBindingViewModel, ref aCreatedContactEntity);

                // 寫入LINE的個人基本資料
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_lineid", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_lineid_backup", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_displayname", m_LineBindingViewModel.DisplayName);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_picture_url", m_LineBindingViewModel.PictureUrl);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_status_message", m_LineBindingViewModel.StatusMessage);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_type", "個人");
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aCreatedContactEntity, "new_line_register", false);
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aCreatedContactEntity, "new_scan_qr_code", true);

                //設定LINE狀態為"新加入"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aCreatedContactEntity, "new_line_status", 100000001);

                // 委身類型客製化，客製委身類型欄位，每間教會委身類型都不一樣，喜樂城靈糧堂=>"訪客" = 100000000
                // 設定成為 訪客 的委身類型
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aCreatedContactEntity, "customertypecode", 100000000);

                // 設定系統中的連絡人為已註冊
                //this.m_ToolUtilityClass.SetEntityBoolAttribute(aSenderEntity, "new_line_register", true);

                // 更新系統中的登錄者
                this.m_ToolUtilityClass.CreateEntity(aCreatedContactEntity);

                return;
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private async Task UpdateContactLineInfo(Entity aContactNoMobile, String LineId, String DisplayId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region 在系統裡沒有相同姓名的連絡人
                // 取得寄件人

                // 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref aContactNoMobile,
                    "mobilephone",
                    EnteredMobilePhone
                );

                // 寫入LINE的個人基本資料
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_lineid", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_lineid_backup", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_line_displayname", aLineBindingViewModel.DisplayName);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_line_picture_url", aLineBindingViewModel.PictureUrl);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_line_status_message", aLineBindingViewModel.StatusMessage);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactNoMobile, "new_line_type", "個人");
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aContactNoMobile, "new_line_register", false);
                //this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aContactNoMobile, "new_scan_qr_code", true);


                // 更新系統中的登錄者
                this.m_ToolUtilityClass.UpdateEntity(aContactNoMobile);

                return;
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private async Task CreateContactFromRegister(String LineId, string EnteredFullName, string EnteredOtherName, String EnteredMobilePhone, LineBindingViewModel aLineBindingViewModel)
        {
            try
            {
                #region 在系統裡沒有相同姓名的連絡人
                // 取得寄件人
                Entity aCreatedContactEntity = new Entity("contact");

                // 更新寄件人的姓名
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "lastname", EnteredFullName);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_other_name", EnteredOtherName);

                // 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute
                (
                    ref aCreatedContactEntity,
                    "mobilephone",
                    EnteredMobilePhone
                );

                // 寫入LINE的個人基本資料
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_lineid", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_lineid_backup", LineId);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_displayname", aLineBindingViewModel.DisplayName);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_picture_url", aLineBindingViewModel.PictureUrl);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_status_message", aLineBindingViewModel.StatusMessage);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aCreatedContactEntity, "new_line_type", "個人");
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aCreatedContactEntity, "new_line_register", false);
                //this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aCreatedContactEntity, "new_scan_qr_code", true);

                //設定LINE狀態為"新加入"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aCreatedContactEntity, "new_line_status", 100000001);

                // 委身類型客製化，客製委身類型欄位，每間教會委身類型都不一樣，喜樂城靈糧堂=>"訪客" = 100000000
                // 設定成為 訪客 的委身類型
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aCreatedContactEntity, "customertypecode", 100000000);

                // 設定系統中的連絡人為已註冊
                //this.m_ToolUtilityClass.SetEntityBoolAttribute(aSenderEntity, "new_line_register", true);

                // 更新系統中的登錄者
                this.m_ToolUtilityClass.CreateEntity(aCreatedContactEntity);

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
