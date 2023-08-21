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
    public class LineNotifyUtility
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
        // 神住611靈糧堂
        private const String CHANNEL_ACCESS_TOKEN = @"e4DmmyIWDuKndlRjHR3BscuVYoqlk9SVxhFXhoZJyhCmBKzIKk9j89bMKLPBoX/Foxvpm/H5XKqA5yu8xjDyxRtdc04LPNvcBRDwzdu1ovcX1L3ErJZkL06pHSRfjvOKBQTMZdiA6j7TnisCPUqwXwdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #endregion
        #region 初始化

        public LineNotifyUtility()
        {
            // 客製化，請選擇
            // 神住611靈糧堂(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(CHANNEL_ACCESS_TOKEN);

            //m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
            //m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            //m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);
            //m_LiffClient = new LiffClient(JESUS_CHANNEL_ACCESS_TOKEN);

            //m_PushUtility.SendMessage(MENGSUNG_LINE_ID, "我主掌管天地萬有!");

        }


        public void SendSmallGroupResultLine(Entity LoginContact, String SmallGroupResult, GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, ref SmallGroupData aSmallGroupData, String WeeklyReportData, bool PauseCheckBox)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                //if ( aSmallGroupData.LoginType == "小組長" )
                {
                    SmallGroupResult = ProcessLineMessage(LoginContact, SmallGroupResult, ref aListEntity, ref aSmallGroupData, WeeklyReportData, PauseCheckBox);

                    //m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, aSmallGroupData.LoginType), SmallGroupResult);
                    m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref LoginContact, ref aListEntity, aSmallGroupData.LoginType), SmallGroupResult);
                }

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SendAddNewPersonResultLine(String AddNewPersonResult, Entity aListEntity)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, "小組長"), AddNewPersonResult);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SendAddNewPersonResultLine(String AddNewPersonResult, Entity aListEntity, String LoginType)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, LoginType), AddNewPersonResult);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SendResultLine(String Result, Entity aListEntity)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, "小組長"), Result);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public void SendListMemberLine(Entity aListEntity)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                String GroupName = m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname") + Environment.NewLine;

                //String TotalNumber = "小組人數 = " + m_ToolUtilityClass.GetEntityIntAttribute(ref aListEntity, "membercount") + Environment.NewLine;
                String TotalNumber = "小組人數 = ";

                String MemberList = "組員姓名 : " + Environment.NewLine;

                String MemberName = "";
                int TotalMemberNumber = GetAllMemberNameFromList(ref MemberName, aListEntity.Id);

                MemberList += MemberName;

                TotalNumber += TotalMemberNumber + Environment.NewLine;

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, "小組長"), GroupName + TotalNumber + MemberList);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SendListMemberLine(Entity aListEntity, String LoginType)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                String GroupName = m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname") + Environment.NewLine;

                //String TotalNumber = "小組人數 = " + m_ToolUtilityClass.GetEntityIntAttribute(ref aListEntity, "membercount") + Environment.NewLine;
                String TotalNumber = "小組人數 = ";

                String MemberList = "組員姓名 : " + Environment.NewLine;

                String MemberName = "";
                int TotalMemberNumber = GetAllMemberNameFromList(ref MemberName, aListEntity.Id);

                MemberList += MemberName;

                TotalNumber += TotalMemberNumber + Environment.NewLine;

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, LoginType), GroupName + TotalNumber + MemberList);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private int GetAllMemberNameFromList(ref String Result, Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }

            int PresentRecordIdCounter = 0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 每個組員
                Entity ContactEntity;

                if (ListType == false)
                {
                    // 靜態名單
                    ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                }
                else
                {
                    // 動態名單
                    ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                }

                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員

                        // 組員的全名
                        String FullName = "";
                        if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            Result += (string)ContactEntity.Attributes["fullname"] + Environment.NewLine;
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return MemberCollection.Entities.Count;
        }

        private String ProcessLineMessage(Entity LoginContact, String SmallGroupResult, ref Entity aListEntity, ref SmallGroupData aSmallGroupData, String WeeklyReportData, bool PauseCheckBox)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                if (aSmallGroupData.LoginType == "小組長")
                {
                    // 取得小組名稱
                    String GroupName = "小組名稱: " + m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname") + Environment.NewLine;

                    SmallGroupResult = GroupName + SmallGroupResult + Environment.NewLine;

                    // 取得代禱事項
                    SmallGroupResult += GetAllPersonalReply(ref aSmallGroupData) + Environment.NewLine;

                    if (PauseCheckBox != true)
                    {
                        SmallGroupResult += "小組日誌:" + Environment.NewLine + WeeklyReportData + Environment.NewLine;
                    }
                    else
                    {
                        SmallGroupResult += "小組暫停!" + Environment.NewLine ;
                    }

                    return SmallGroupResult;
                }
                else
                {
                    // 個人回報
                    String GroupName = "小組名稱: " + m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                    String LoginContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LoginContact, "fullname");

                    return
                        GroupName + Environment.NewLine +
                        LoginContactName + " 個人回報:" + Environment.NewLine +
                        GetPersonalReply(LoginContactName, ref aSmallGroupData);
                }
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public String GetPersonalReply(String LoginContactName, ref SmallGroupData aSmallGroupData)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                // 取得小組名稱
                String PersonalReply = "";

                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    if (aMemberInfomation.FullName == LoginContactName)
                    {
                        PersonalReply += aMemberInfomation.Sunday == true ? "主日有出席" + Environment.NewLine : "主日沒出席" + Environment.NewLine;
                        PersonalReply += aMemberInfomation.SmallGroup == true ? "小組有出席" + Environment.NewLine : "小組沒出席" + Environment.NewLine;
                        PersonalReply += aMemberInfomation.PrayItem != "" ? "代禱事項: " + aMemberInfomation.PrayItem : "" + Environment.NewLine;
                        PersonalReply += "讀經次數: " + aMemberInfomation.SpiritualWork + Environment.NewLine;
                        PersonalReply += "屬靈書籍: " + aMemberInfomation.MorningPray + Environment.NewLine;
                        PersonalReply += "禱告次數: " + aMemberInfomation.GeneralCare + Environment.NewLine;
                    }
                }

                return PersonalReply;
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public String GetAllPersonalReply(ref SmallGroupData aSmallGroupData)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                // 取得小組名稱
                //String PersonalReply = "代禱事項: " + Environment.NewLine;
                String PersonalReply = "";

                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    if (aMemberInfomation.PrayItem != "")
                    {
                        PersonalReply += aMemberInfomation.FullName + " : " + aMemberInfomation.PrayItem + Environment.NewLine;
                    }
                }

                if (PersonalReply != "")
                {
                    PersonalReply = "代禱事項: " + Environment.NewLine + PersonalReply;
                }

                return PersonalReply;
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SendWeeklyReportLine(String WeeklyReportContent, Entity aListEntity)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                // 取得小組名稱
                //String GroupName = "小組日誌回報 :" + Environment.NewLine + "小組名稱 = " + m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                String GroupName = "\"" + m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname") + "\"" + "小組日誌回報";

                WeeklyReportContent = GroupName + Environment.NewLine + "回報內容: " + Environment.NewLine + WeeklyReportContent + Environment.NewLine;

                //m_PushUtility.SendMessage(MENGSUNG_LINE_ID, SmallGroupResult);

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, "小組長"), WeeklyReportContent);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public List<String> GetLineRecieverList(ref Entity aListEntity, String LoginType)
        {
            try
            {
                #region 先找到"小家長"、"小組長"、族系族長/小家長"
                Entity aContact;

                // 上代族系族長 LINE ID
                String aListGraceLeaderLineId = "";
                Guid aListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");
                if (aListGraceLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListGraceLeaderId);
                    aListGraceLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 共同上代族系族長 LINE ID
                String aCoListGraceLeaderLineId = "";
                Guid aCoListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_co_arealeader");
                if (aCoListGraceLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aCoListGraceLeaderId);
                    aCoListGraceLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 小家長 LINE ID
                String aAreaLeaderLineId = "";
                Guid aAreaLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                if (aAreaLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aAreaLeaderId);
                    aAreaLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 共同小家長 LINE ID
                String aCoAreaLeaderLineId = "";
                Guid aCoAreaLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_co_race_leager_list");
                if (aCoAreaLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aCoAreaLeaderId);
                    aCoAreaLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 小組長 ID
                String aListSmallGroupLeaderLineId = "";
                Guid aListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                if (aListSmallGroupLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListSmallGroupLeaderId);
                    aListSmallGroupLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 共同小組長 ID
                String aListCoSmallGroupLeaderLineId = "";
                Guid ListCoSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_vice_family_leader");
                if (ListCoSmallGroupLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", ListCoSmallGroupLeaderId);
                    aListCoSmallGroupLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }


                // 回報通知窗口 ID
                String aReportNotifyWindowLineId = "";
                Guid aReportNotifyWindowId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_report_window");
                if (aReportNotifyWindowId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aReportNotifyWindowId);
                    aReportNotifyWindowLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                #endregion

                List<String> aList = new List<String>();


                if (LoginType == "小組長")
                {
                    // 小組長個人回報
                    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    if (aCoListGraceLeaderLineId != "") aList.Add(aCoListGraceLeaderLineId);
                    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);

                    //if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    //    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    //    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    //}
                    //else
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //}
                }
                else if ( LoginType == "指派" )
                {
                    // 指派回報
                    // 上代族系族長 LINE ID
                    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    // 共同上代族系族長 LINE ID
                    if (aCoListGraceLeaderLineId != "") aList.Add(aCoListGraceLeaderLineId);
                    // 小家長 LINE ID
                    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    // 共同小家長 LINE ID
                    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    // 小組長 ID
                    if (aListSmallGroupLeaderLineId != "") aList.Add(aListSmallGroupLeaderLineId);
                    // 共同小組長 ID
                    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);

                    //if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    //    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    //    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    //}
                    //else
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //}
                }
                else
                {
                    // 個人回報
                    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    if (aCoListGraceLeaderLineId != "") aList.Add(aCoListGraceLeaderLineId);
                    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);


                    //if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    //    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    //    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    //}
                    //else
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //}

                    if (aListSmallGroupLeaderLineId != "") aList.Add(aListSmallGroupLeaderLineId);
                }

                // 如果回報通知窗口有加入成為好友
                if (aReportNotifyWindowLineId != "") aList.Add(aReportNotifyWindowLineId);

                return aList;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public List<String> GetLineRecieverList(ref Entity LoginContact, ref Entity aListEntity, String LoginType)
        {
            try
            {
                #region 先找到"小家長"、"小組長"、族系族長/小家長"
                Entity aContact;

                // 上代族系族長 LINE ID
                String aListGraceLeaderLineId = "";
                Guid aListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");
                if (aListGraceLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != aListGraceLeaderId)
                    {
                        // 登入回報者與此人是不一樣的ID
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListGraceLeaderId);
                        aListGraceLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }

                // 共同上代族系族長 LINE ID
                String aCoListGraceLeaderLineId = "";
                Guid aCoListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_co_arealeader");
                if (aCoListGraceLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != aCoListGraceLeaderId)
                    {
                        // 登入回報者與此人是不一樣的ID
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aCoListGraceLeaderId);
                        aCoListGraceLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }

                // 共同小家長 LINE ID
                String aCoAreaLeaderLineId = "";
                Guid aCoAreaLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_co_race_leager_list");
                if (aCoAreaLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != aCoAreaLeaderId)
                    {
                        // 登入回報者與此人是不一樣的ID
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aCoAreaLeaderId);
                        aCoAreaLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }

                // 小家長 LINE ID
                String aAreaLeaderLineId = "";
                Guid aAreaLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                if (aAreaLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != aAreaLeaderId)
                    {
                        // 登入回報者與此人是不一樣的ID
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aAreaLeaderId);
                        aAreaLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }

                // 小組長 ID
                String aListSmallGroupLeaderLineId = "";
                Guid aListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                if (aListSmallGroupLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != aListSmallGroupLeaderId)
                    {
                        // 登入回報者與此人是不一樣的ID
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListSmallGroupLeaderId);
                        aListSmallGroupLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }

                // 共同小組長 ID
                String aListCoSmallGroupLeaderLineId = "";
                Guid ListCoSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_vice_family_leader");
                if (ListCoSmallGroupLeaderId != Guid.Empty)
                {
                    if (LoginContact.Id != ListCoSmallGroupLeaderId)
                    {
                        aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", ListCoSmallGroupLeaderId);
                        aListCoSmallGroupLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                    }
                }


                // 回報通知窗口 ID
                String aReportNotifyWindowLineId = "";
                Guid aReportNotifyWindowId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_report_window");
                if (aReportNotifyWindowId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aReportNotifyWindowId);
                    aReportNotifyWindowLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                #endregion

                List<String> aList = new List<String>();

                if (LoginType == "小組長")
                {
                    // 小組長個人回報
                    // 上代族系族長=> aListGraceLeaderLineId
                    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);

                    // 共同上代族系族長=> aCoListGraceLeaderLineId
                    if (aCoListGraceLeaderLineId != "") aList.Add(aCoListGraceLeaderLineId);

                    // 小家長(族系族長)=> aAreaLeaderLineId
                    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);

                    // 共同小家長(共同族系族長)=> aCoAreaLeaderLineId
                    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);

                    // 小組長=> aListSmallGroupLeaderLineId
                    if (aListSmallGroupLeaderLineId != "") aList.Add(aListSmallGroupLeaderLineId);

                    // 共同小組長=> aListCoSmallGroupLeaderLineId
                    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);

                    // 回報通知窗口=> aReportNotifyWindowLineId
                    if (aReportNotifyWindowLineId != "") aList.Add(aReportNotifyWindowLineId);

                    //if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    //    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    //    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    //}
                    //else
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //}
                }
                else
                {
                    // 個人回報
                    // 上代族系族長=> aListGraceLeaderLineId
                    //if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);

                    // 共同上代族系族長=> aCoListGraceLeaderLineId
                    //if (aCoListGraceLeaderLineId != "") aList.Add(aCoListGraceLeaderLineId);

                    // 小家長=> aAreaLeaderLineId
                    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);

                    // 共同小家長=> aCoAreaLeaderLineId
                    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);

                    // 小組長=> aListSmallGroupLeaderLineId
                    if (aListSmallGroupLeaderLineId != "") aList.Add(aListSmallGroupLeaderLineId);

                    // 共同小組長=> aListCoSmallGroupLeaderLineId
                    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);

                    // 回報通知窗口=> aReportNotifyWindowLineId
                    //if (aReportNotifyWindowLineId != "") aList.Add(aReportNotifyWindowLineId);

                    //if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //    if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    //    if (aListCoSmallGroupLeaderLineId != "") aList.Add(aListCoSmallGroupLeaderLineId);
                    //    if (aCoAreaLeaderLineId != "") aList.Add(aCoAreaLeaderLineId);
                    //}
                    //else
                    //{
                    //    if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    //}

                    if (aListSmallGroupLeaderLineId != "") aList.Add(aListSmallGroupLeaderLineId);
                }

                // 如果回報通知窗口有加入成為好友
                if (aReportNotifyWindowLineId != "") aList.Add(aReportNotifyWindowLineId);

                return aList;
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
