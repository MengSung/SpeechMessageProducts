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
        // 音訊教會(免費版)
        private const String JESUS_CHANNEL_ACCESS_TOKEN = @"yvyzlpbDY4ctjVuC0vEYFDF4Gz9Ed6VR57AOmqEfRPqNSFa4tmlvgFqydqOsv8C5vOG3Ew1vPtBfZoJ7Psm69HH+oKtRA4UeMWi1EZp6j4hzhjC1ePmBRQOdfcbcGgDjJzC60Q8HAI/Err6YjFZwOwdB04t89/1O/w1cDnyilFU=";

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #endregion
        #region 初始化

        public LineNotifyUtility()
        {
            // 客製化，請選擇
            // 音訊教會(免費版)
            this.m_LineMessagingClient = new LineMessagingClient(JESUS_CHANNEL_ACCESS_TOKEN);

            //m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
            //m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

            // 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            //m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);
            //m_LiffClient = new LiffClient(JESUS_CHANNEL_ACCESS_TOKEN);

            //m_PushUtility.SendMessage(MENGSUNG_LINE_ID, "我主掌管天地萬有!");

        }


        public void SendSmallGroupResultLine(Entity LoginContact, String SmallGroupResult, GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, ref SmallGroupData aSmallGroupData, String WeeklyReportData)
        {
            try
            {
                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                SmallGroupResult = ProcessLineMessage(LoginContact, SmallGroupResult, ref aListEntity, ref aSmallGroupData, WeeklyReportData);

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, aSmallGroupData.LoginType), SmallGroupResult);

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

        public void SendListMemberLine( Entity aListEntity)
        {
            try
            {
                #region 傳送LINE 訊息關於加入新人的結果給權柄

                String Result = m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname") + Environment.NewLine;

                Result += "小組人數 = " + m_ToolUtilityClass.GetEntityIntAttribute(ref aListEntity, "membercount") + Environment.NewLine;

                Result += "組員姓名 : " + Environment.NewLine;

                String MemberName = "";
                GetAllMemberNameFromList(ref MemberName, aListEntity.Id );

                Result += MemberName;

                m_PushUtility.MultiCastTextMessageAsync(GetLineRecieverList(ref aListEntity, "小組長"), Result);

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void GetAllMemberNameFromList( ref String Result, Guid ListEntityId)
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

            return;
        }

        private String ProcessLineMessage(Entity LoginContact, String SmallGroupResult, ref Entity aListEntity, ref SmallGroupData aSmallGroupData, String WeeklyReportData)
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

                    SmallGroupResult += "小組日誌:" + Environment.NewLine + WeeklyReportData + Environment.NewLine;

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
                        PersonalReply += "靈修次數: " + aMemberInfomation.SpiritualWork + Environment.NewLine;
                        PersonalReply += "晨禱次數: " + aMemberInfomation.MorningPray + Environment.NewLine;
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
                #region 先找到"小家長"、"小組長"、族系族長/區長"
                Entity aContact;

                // 區牧 LINE ID
                String aListGraceLeaderLineId = "";
                Guid aListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");
                if (aListGraceLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListGraceLeaderId);
                    aListGraceLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 區長 LINE ID
                String aAreaLeaderLineId = "";
                Guid aAreaLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                if (aAreaLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aAreaLeaderId);
                    aAreaLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                }

                // 小組長 ID
                String aListSmallGroupLeaderLineId = "";
                Guid aListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                if (aListSmallGroupLeaderId != Guid.Empty)
                {
                    aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aListSmallGroupLeaderId);
                    aListSmallGroupLeaderLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
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
                    if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    {
                        if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                        if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    }
                    else
                    {
                        if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    }
                }
                else
                {
                    // 個人回報
                    if (aListGraceLeaderLineId != aAreaLeaderLineId)
                    {
                        if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                        if (aAreaLeaderLineId != "") aList.Add(aAreaLeaderLineId);
                    }
                    else
                    {
                        if (aListGraceLeaderLineId != "") aList.Add(aListGraceLeaderLineId);
                    }

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
