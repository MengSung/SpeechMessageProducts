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
using ChurchReport.Models;
using ToolUtilityNameSpace.Factory;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class UploadIntegrateData
    {
        #region 資料區
        #region 參數資料
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        private const bool TRANSFER_IDENTITY_FLAG = false;

        //private const int MONTH_PERIOD = 2;      //幾個月內出席超過這次數就會改變委身類型=>小組組員
        private const int WEEK_PERIOD = 8;      //過去幾　WEEK_PERIOD　周內出席超過這次數就會改變委身類型=>小組組員
        private const int MINIMUM_THRESHOLD = 4;      //2個月內出席超過這次數就會改變委身類型=>小組組員

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #endregion
        #region 上傳資料時所需要的參數

        //使用者 Client端傳回來的資料
        MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        //沒有週報，需要初始化的Member Data
        MemberInfomationPackage m_InitializedMemberInfomationPackage = new MemberInfomationPackage();

        DateTime m_Sunday;
        String m_LoginType = "";
        String m_GroupType = "";
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        Entity m_ListEntity; // 小組名單實體紀錄
        Entity m_WeeklyReportEntity; // 週報實體紀錄
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給區長/小家長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 區長/小家長
        Guid m_ShepherdLeaderId; // 區牧
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        Guid m_OwnerId; // 小組長的負責人 Id

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        static readonly object m_UploadDataLocker = new object();//避免多人同時輸入"小組出席"，會產生2個週報或是改變"委身類型"、"裝備狀態"                                                                 //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //private const String SET_IDENTITY_METHOD = "透過過去8週出席次數"; // 設定委身類型的方式
        private const String SET_IDENTITY_METHOD = "透過回報網頁手動設定"; // 設定委身類型的方式

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion
        #region 上傳資料區
        #region 主程式
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Account"></param>
        /// <param name="Password"></param>
        /// <param name="ListEntityId"></param>
        /// <param name="WeeklyReportEntityId"></param>
        //
        /// <param name="aSmallGroupDate"></param>
        /// <param name="UploadCategory"> "主日點名"、"小組點名"</param>
        /// <param name="aSmallGroupData"></param>
        /// <param name="WeeklyReportData"></param>
        /// <param name="WeeklyReportAnalysis"></param>
        public void UploadData( DateTime aSelectedDate, String Account, String Password, String LoginType, String GroupType, String ListEntityId, ref String WeeklyReportEntityId, DateTime aSmallGroupDate, SmallGroupData aSmallGroupData, ref String WeeklyReportData, ref String WeeklyReportAnalysis, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                //ListManager
                // 設定初始值
                m_LoginType = LoginType;
                m_GroupType = GroupType;

                // 設定參數，設定主日日期，找到操作使用者登入的ENTITY及ID
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定參數");
                SetupCommonParameter(aSelectedDate, Account, Password, aSmallGroupDate, ListEntityId, WeeklyReportEntityId);

                Entity aGraceLeaderWeeklyReportEntity = null;// 區長的週報

                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "回傳結果");

                this.m_FeedBackReport.Clear();
                this.ResetDictionary(m_Sunday);

                #region 處理小組名稱、初始化字典
                // 從 APP 傳來的包含主日出席率及小組出席率之後的小組名稱
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname");

                // 去除掉主日出席率及小組出席率之後的小組名稱
                String FilteredGroupName = ToolUtilityClass.DeletePresentRate(GroupName);

                //聖谷行道會小組名稱含有數字
                //String FilteredOutDigitGroupName = Regex.Replace(FilteredGroupName, "[0-9]", "");//過濾掉數字
                String FilteredOutDigitGroupName = FilteredGroupName.Replace(" ", ""); // //過濾掉空白
                //AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", FilteredOutDigitGroupName + Environment.NewLine + "主日出席紀錄:");
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", "主日出席統計:");
                //AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭" , "");
                //AddToDictionary(ref this.m_FeedBackReport, "跟進統計表頭"     , "");

                Guid aWeeklyReportId = m_WeeklyReportEntity != null ? m_WeeklyReportEntity.Id : Guid.Empty;

                #endregion

                if (this.m_ListEntity != null)
                {
                    lock (m_UploadDataLocker) //避免多人同時輸入"小組出席"，會產生2個週報或是改變"委身類型"、"裝備狀態"
                    {
                        #region 有找到要點名的名單，而且登入的操作者與此名單或是與小組長ID、或是與小家長ID、或是與區長/小家長一致，或是個人回報也可以上傳
                        if (aWeeklyReportId == Guid.Empty)
                        {
                            #region // 要建立週報
                            #region // 依據有效的週報的小組組員名單當作週報出席率的分母
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據有效的週報的小組組員名單當作週報出席率的分母");
                            Double ValidNumber = this.GetEffecttiveSmallGroupNumber(m_ListEntity.Id);
                            #endregion
                            #region// 要建立週報
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "要建立週報");
                            //if (this.CreateWeeklyReportOrNot(ref m_ListEntity, m_Sunday)) // 判斷是否真要建立週報
                            //{
                            // 建立週報
                            Double aWeeklySundayRate = 0.0;
                            Double aWeeklySmallGroupRate = 0.0;
                            int aWeeklySundayNumber = 0;
                            int aWeeklySmallGroupNumber = 0;

                            GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid
                            {
                                WeeklyReportGuid = WeeklyReportEntityId != null && WeeklyReportEntityId != "" ? new Guid(WeeklyReportEntityId) : new Guid(),
                                GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname"),
                                SmallGroupLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref this.m_ListEntity, "new_contact_family_leader_list"),
                                SmallGroupDate = m_Sunday,
                                SmallGroupRate = 0,
                                SundayPresentRate = 0,
                            };

                            // 由於是新建立的週報，當回傳完成時，回到網頁操作，如果使用者又再繼續操作，就必須設定告知新建立的週報ID =WeeklyReportEntityId ，以免重複建立
                            aGraceLeaderWeeklyReportEntity = CreateWeeklyReportAndPresentRecord(GroupName, aGroupWeeklyReportGuid, ref WeeklyReportEntityId, ref m_ListEntity, "", ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                            //}
                            #endregion
                            #endregion
                        }
                        else
                        {
                            #region// 更新週報
                            //if (this.UpdateWeeklyReportOrNot(ref m_ListEntity)) // 判斷是否真要更新週報，只有事這組的小組長才能點名回報
                            //{
                            GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid
                            {
                                WeeklyReportGuid = WeeklyReportEntityId != null && WeeklyReportEntityId != "" ? new Guid(WeeklyReportEntityId) : new Guid(),
                                GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname"),
                                SmallGroupLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref this.m_ListEntity, "new_contact_family_leader_list"),
                                SmallGroupDate = m_Sunday,
                                SmallGroupRate = 0,
                                SundayPresentRate = 0,
                            };

                            aGraceLeaderWeeklyReportEntity = UpdateWeeklyReportProcess(aGroupWeeklyReportGuid, ref m_ListEntity, ref aWeeklyReportId, aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                            //}
                            #endregion
                        }
                        #region 如果登入者是區長，則他的週報要留下來，以便寫入他底下的小組長，所有的主日、小組出席紀錄
                        //if ( this.m_ContactId != aThisListGraceLeaderId )
                        //{
                        //    //　不是區長，週報設為ＮＵＬＬ
                        //    aGraceLeaderWeeklyReportEntity = null;
                        //}
                        #endregion

                        #endregion
                    }
                }
                else
                {
                    // 根本找不到這個要被點名的名單，所以就甚麼也不做
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        public void DeleteMember(String Account, String Password, String ListEntityId, Member aMemberToBeDeleted)
        {
            try
            {
                // 取得個人聚會與靈修記錄
                Entity PresentRecordEntity = null;

                try
                {
                    PresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", new Guid(aMemberToBeDeleted.PresentRecordId));
                }
                catch (System.Exception Exception)
                { }

                if (PresentRecordEntity != null)
                {
                    #region 有找到個人聚會與靈修記錄
                    // 將聯絡人從小組名單移除
                    m_ToolUtilityClass.RemoveMembersToMarketingList(new Guid(ListEntityId), this.m_ToolUtilityClass.GetEntityLookupAttribute(ref PresentRecordEntity, "new_contact_new_present_record"));

                    // 刪除個人聚會與靈修記錄
                    m_ToolUtilityClass.DeleteEntity("new_present_record", new Guid(aMemberToBeDeleted.PresentRecordId));
                    #endregion
                }
                else
                {
                    #region 沒找到個人聚會與靈修記錄
                    Entity aContact = GetContactFromList(new Guid(ListEntityId), aMemberToBeDeleted.FullName);

                    if (aContact != null)
                    {
                        // 將聯絡人從小組名單移除
                        m_ToolUtilityClass.RemoveMembersToMarketingList(new Guid(ListEntityId), aContact.Id);

                    }
                    #endregion
                }

                Entity aListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId));

                #region 關聯小組長屬性 找到小組長
                Entity LoginContact;
                if (Account != "LineIdLogin")
                {
                    LoginContact = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
                }
                else
                {
                    LoginContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
                }
                #endregion

                #region 通知權柄移除掉的訊息
                String LoginContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LoginContact, "fullname");

                //String Result = LoginContactFullName + " 將 " + this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref PresentRecordEntity, "new_contact_new_present_record") + " 從" + aMemberToBeDeleted.Group + "移除掉了!";
                String Result = LoginContactFullName + " 將 " + aMemberToBeDeleted.FullName + " 從" + aMemberToBeDeleted.Group + "移除掉了!";

                this.m_LineNotifyUtility.SendResultLine(Result, aListEntity);
                this.m_LineNotifyUtility.SendListMemberLine(aListEntity);

                #endregion

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Entity GetContactFromList(Guid ListEntityId, String aContactFullName )
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
                        if( this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "fullname" ) == aContactFullName)
                        {
                            return ContactEntity;
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return null;
        }
        private bool DeterminCalculateFlag(Guid m_ContactId, Guid aThisListFamilyHeadId, Guid aThisListSmallGroupLeaderId, Guid aThisListGraceLeaderId)
        {
            // Guid m_ContactId,                    登入者
            // Guid aThisListFamilyHeadId,          小家長
            // Guid aThisListSmallGroupLeaderId,    小組長
            // Guid aThisListGraceLeaderId，        區長/小家長
            try
            {
                if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId != aThisListGraceLeaderId)
                {
                    #region 登入者是小組長，但不是區長/小家長
                    return true;
                    #endregion
                }
                else if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是區長/小家長，而且也是區長/小家長
                    return true;
                    #endregion
                }
                else if (m_ContactId != aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是區長/小家長，而且也是區長/小家長
                    return false;
                    #endregion
                }
                else
                {
                    #region 登入者是區長/小家長，但不是小組長
                    return false;
                    #endregion
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReportStatus(String UploadCategory, ref Entity aWeeklyReportEntity)
        {
            try
            {
                // 設定週報點名狀態
                // 均未點名                 = 100,000,000
                // 均已點名                 = 100,000,001
                // 主日點名，小組未點名     = 100,000,003
                // 小組點名，主日未點名     = 100,000,004

                // 目前先設定均已點名
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);

                //int Status = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status");
                //if (Status == 100000000)
                //{
                //    #region 均未點名
                //    if (UploadCategory == "主日點名")
                //    {
                //        // 設定主日點名，小組未點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000003);
                //    }
                //    else if (UploadCategory == "小組點名")
                //    {
                //        // 小組點名，主日未點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000004);
                //    }
                //    else { }
                //    #endregion
                //}
                //else if (Status == 100000003)
                //{
                //    #region 主日點名，小組未點名
                //    if (UploadCategory == "小組點名")
                //    {
                //        // 均已點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                //    }
                //    else { }
                //    #endregion
                //}
                //else if (Status == 100000004)
                //{
                //    #region 小組點名，主日未點名
                //    if (UploadCategory == "主日點名")
                //    {
                //        // 均已點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                //    }
                //    else { }
                //    #endregion
                //}
                //else
                //{
                //    #region 均未點名
                //    if (UploadCategory == "主日點名")
                //    {
                //        // 設定主日點名，小組未點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000003);
                //    }
                //    else if (UploadCategory == "小組點名")
                //    {
                //        // 小組點名，主日未點名
                //        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000004);
                //    }
                //    else { }
                //    #endregion
                //}

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupCommonParameter(DateTime aSelectDate, String Account, String Password, DateTime aSmallGroupDate, String ListEntityId, String WeeklyReportEntityId)
        {
            try
            {
                // 設定主日日期
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)aSmallGroupDate.DayOfWeek;

                // 當週的星期日為認定的主日
                //this.m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                //DateTime aSunday;
                // 每周以星期六為第一日
                if (DayOfWeek != 6)
                {
                    // 如果不是星期六則是上個星期天
                    m_Sunday = aSelectDate.AddDays(-DayOfWeek);
                }
                else
                {
                    // 如果是星期六則是下個星期天
                    m_Sunday = aSelectDate.AddDays(1);
                }
                #endregion


                // 找到操作使用者登入的小組長ID
                if (Account != "LineIdLogin")
                {
                    this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
                }
                else
                {
                    this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
                }

                m_ContactId = m_ContactEntity.Id;

                // 小組長的負責人 Id
                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                #region 蒐集建立週報所需要的屬性
                // 搜尋小組長的門徒小組名單Lookup Id
                m_DecipleGroupListId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_deciple_group_list_contact");

                // 搜尋小家長的小組長 Lookup Id
                // 小組長 ID
                //this.m_GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                // 在思恩堂豐富教會有一個"上代組長"，是在個人資料裡面有一個LOOKUP 欄位 Lookup Id
                m_RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                // 取得小組名單實體紀錄
                this.m_ListEntity = ListEntityId != null && ListEntityId != "" ? m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId)) : null;
                
                // 取得週報實體紀錄
                this.m_WeeklyReportEntity = WeeklyReportEntityId != null && WeeklyReportEntityId != "" ? m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(WeeklyReportEntityId)) : null;

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private bool CreateWeeklyReportOrNot(ref Entity aListEntity, DateTime aSunday)
        {
            try
            {
                // 待完成事項，若是名單的相關週報已經有該週主日的週報則不建立
                //public EntityCollection QueryContactWeeklyReportBySunday(DateTime aSunday, String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
                //EntityCollection aWeeklyReportCollection = this.m_ToolUtilityClass.QueryWeeklyReportBySunday(aSunday, "list", "listid", aListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                EntityCollection aWeeklyReportCollection = this.m_ToolUtilityClass.QueryWeeklyReportBySunday(aSunday, aListEntity.Id);

                foreach (Entity aWeeklyReport in aWeeklyReportCollection.Entities)
                {
                    DateTime WeeklyReportSunday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReport, "new_sunday_date");

                    if (aSunday.Date == WeeklyReportSunday.Date)
                    {
                        // 已經有相同主日的週報了
                        return false;
                    }
                }
                //return true; // 永遠都是通過，可以建立周報、靈修紀錄單
                #region 沒有找到已經建立的週報，但是小家長不能替小組長建立週報
                if (aListEntity != null)
                {
                    #region// 有找到吻合的名單
                    // 名單裡的小組長 ID
                    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                    if (this.m_ContactId == aSmallGroupLeaderId)
                    {
                        // 登入者與名單的小組長是同一個人
                        return true;
                    }
                    else
                    {
                        // 登入者與名單的小組長不是同一個人
                        if (m_LoginType == "小組長")
                        {
                            // 小組長回報
                            return false;
                        }
                        else
                        {
                            // 個人回報
                            return true;
                        }

                    }
                    #endregion
                }
                else
                {
                    #region// 沒找到吻合的名單
                    return false;
                    #endregion
                }
                #endregion
                #region
                //if (aListEntity != null)
                //{
                //    #region// 有找到吻合的名單
                //    // 比對名單中的小組長跟族系組長是否是同一人
                //    // 名單裡的小組長 ID
                //    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                //    // 名單裡的區長 ID
                //    Guid aRaceGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                //    if (this.m_RaceLeaderId == Guid.Empty) // m_RaceLeaderId 是指小組長的族系組長ID
                //    {
                //        #region// 是族系組長
                //        if (aSmallGroupLeaderId == aRaceGroupLeaderId)
                //        {
                //            // 名單裡的小組長和族系組長是同一個
                //            // 族長自己的小組我已要新建週報
                //            return true;
                //        }
                //        else
                //        {
                //            if (RACE_LEADER_CAN_CREATE_WEEKLYREPORT == true)
                //            {
                //                // 族系組長能否幫小組長建立週報
                //                return true;
                //            }
                //            else
                //            {
                //                // 名單裡的小組長和族系組長不是同一個
                //                // 就不要建立週報，因為不希望族長幫小組長建立週報
                //                return false;
                //            }
                //        }
                //        #endregion
                //    }
                //    else
                //    {
                //        #region// 是小組長
                //        return true;
                //        #endregion
                //    }
                //    #endregion
                //}
                //else
                //{
                //    #region// 沒找到吻合的名單
                //    return false;
                //    #endregion
                //}
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private bool UpdateWeeklyReportOrNot(ref Entity aListEntity)
        {
            try
            {
                #region
                if (aListEntity != null)
                {
                    #region// 有找到吻合的名單
                    #region 先找到"小家長"、"小組長"、區長/小家長"

                    // 先找到這個名單的小家長 ID，聖谷行道會專用
                    //Guid aThisListFamilyHeadId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_familyhead_list");

                    // 找到這個名單的門徒 ID
                    Guid aThisListCoSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_list_vice_family_leader");

                    // 找到這個名單的小組長 ID
                    Guid aThisListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_family_leader_list");

                    // 找到這個名單的上代組長 ID
                    //Guid aThisListUpperGenerationLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_race_leager_list");

                    // 找到這個名單的區長/小家長 ID
                    //Guid aThisListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_list_arealeader");

                    #endregion
                    if (this.m_ContactId == aThisListSmallGroupLeaderId || this.m_ContactId == aThisListCoSmallGroupLeaderId)
                    {
                        // 登入者與名單的小組長或是門徒是同一個人
                        return true;
                    }
                    else
                    {
                        // 登入者與名單的小組長不是同一個人
                        if (m_LoginType == "小組長")
                        {
                            // 小組長回報
                            return false;
                        }
                        else
                        {
                            // 個人回報
                            // 登入者與名單的小組長不是同一個人，但是是個人回報
                            return true;
                        }
                    }
                    #endregion
                }
                else
                {
                    #region// 沒找到吻合的名單
                    return false;
                    #endregion
                }
                #endregion
                #region
                //if (aListEntity != null)
                //{
                //    #region// 有找到吻合的名單
                //    // 比對名單中的小組長跟族系組長是否是同一人
                //    // 名單裡的小組長 ID
                //    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                //    // 名單裡的區長 ID
                //    Guid aRaceGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                //    if (this.m_RaceLeaderId == Guid.Empty) // m_RaceLeaderId 是指小組長的族系組長ID
                //    {
                //        #region// 是族系組長
                //        if (aSmallGroupLeaderId == aRaceGroupLeaderId)
                //        {
                //            // 名單裡的小組長和族系組長是同一個
                //            // 族長自己的小組我已要新建週報
                //            return true;
                //        }
                //        else
                //        {
                //            if (RACE_LEADER_CAN_CREATE_WEEKLYREPORT == true)
                //            {
                //                // 族系組長能否幫小組長建立週報
                //                return true;
                //            }
                //            else
                //            {
                //                // 名單裡的小組長和族系組長不是同一個
                //                // 就不要建立週報，因為不希望族長幫小組長建立週報
                //                return false;
                //            }
                //        }
                //        #endregion
                //    }
                //    else
                //    {
                //        #region// 是小組長
                //        return true;
                //        #endregion
                //    }
                //    #endregion
                //}
                //else
                //{
                //    #region// 沒找到吻合的名單
                //    return false;
                //    #endregion
                //}
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        #endregion
        #region 建立的週報
        private String SetupWeeklyReportResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                return SetupSundayPresentResult(ref aWeeklyReportEntity);
                //SetupSmallGroupPresentResult(ref aWeeklyReportEntity );
                //SetupFollowupResult(ref aWeeklyReportEntity );
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private String SetupSundayPresentResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                //Emoji 清單列表
                // https://apps.timwhitlock.info/emoji/tables/unicode
                string Apple = "\uD83C\uDF4F";
                string Heart = "\uD83D\uDC96";
                String SundayResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "主日出席統計表頭") +
                    Environment.NewLine + Apple + "已出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計新人出席字串") +
                    Environment.NewLine + Heart + "未出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計小組組員未出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計新人未出席字串") 
                    ;

                //Emoji 清單列表
                // https://apps.timwhitlock.info/emoji/tables/unicode
                //string Apple = "\uD83C\uDF4F";
                //string Heart = "\uD83D\uDC96";
                String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                    Environment.NewLine + Apple + "已出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計新人出席字串") +
                    Environment.NewLine + Heart + "未出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計小組組員未出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計未入組出未席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "" , "小組統計新人未出席字串")                   
                    ;

                //String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                //    "\t" + "A.小組組員" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員未出席字串") + Environment.NewLine +
                //    "\t" + "B.不穩定組員" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出未席字串") + Environment.NewLine +
                //    "\t" + "C.新朋友" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人未出席字串") + Environment.NewLine
                //    ;
                //SmallGroupResult += "---------------------------------" + Environment.NewLine;
                //SundayResult += Environment.NewLine + Environment.NewLine;
                //SundayResult += Environment.NewLine;

                String FollowUpResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "跟進統計表頭") +
                    "\t" + "A.未入組跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "未入組跟進統計內容") + Environment.NewLine +
                    "\t" + "B.新朋友跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "新朋友跟進統計內容") + Environment.NewLine
                    ;
                FollowUpResult += "---------------------------------" + Environment.NewLine;

                // 主日出席寫入至週報
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_sunday_present_report", SmallGroupResult);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_sunday_present_report", SundayResult + SmallGroupResult + FollowUpResult);

                // 加總區長的主日出席
                AddToDictionary(ref this.m_FeedBackReport, "主日統計", SundayResult);

                //return SmallGroupResult;
                return 
                    SundayResult + Environment.NewLine + 
                    "---------------------------------" + Environment.NewLine + 
                    SmallGroupResult + Environment.NewLine + 
                    "---------------------------------" + Environment.NewLine + 
                    FollowUpResult;
                //return SundayResult + SmallGroupResult;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Guid CreateWeeklyReport(ref Entity aListEntity, GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                // 這是新建立的週報
                Entity aWeeklyReportEntity = new Entity("new_group_present_weekly_report");

                #region 指派週報的負責人
                //Guid ListOwnerId = aListEntity.GetAttributeValue<EntityReference>("ownerid").Id;
                //this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", aWeeklyReportEntity, ListOwnerId);
                #endregion

                // 小組聚會地點和時間
                m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_time");

                // 小家長 ID
                Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 上代組長/(原來是:區長) ID
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, m_DecipleGroupListId, aListEntity, m_Sunday, m_SmallGroupPlace, m_SmallGroupTime, aGroupWeeklyReportGuid);

                // 新增週報
                Guid CreatedWeeklyReportEntity = this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity);

                // 指派週報的負責人
                this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", CreatedWeeklyReportEntity), this.m_OwnerId);

                return CreatedWeeklyReportEntity;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Guid aDecipleGroupList, Entity ListEntity, DateTime aSunday, String SmallGroupPlace, String SmallGroupTime, GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                #region 設定週報名稱
                // 取得小組名單的名稱
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref ListEntity, "listname");
                String WeeklyReportName = GroupName + String.Format("-{0:00}/{1:00}/{2:00}", aSunday.Year, aSunday.Month, aSunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_name", WeeklyReportName);
                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_area_name", AreaName);
                #endregion
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_weekly_report_parents", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯區長/小家長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_group_head_group_present_weekly_r", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯上代組長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_arealeader_weekly_report", "contact", aShepherdLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (ListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report", "list", ListEntity.Id); }
                #endregion
                #region 關聯門徒小組名單 Lookup
                if (aDecipleGroupList != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_deciple_list_group_present_weekly", "list", aDecipleGroupList); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aSunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aSunday);

                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);

                }

                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", SmallGroupTime);
                #endregion
                #region 設定小組人數

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_member_number", GetSmallGroupMemberNumber(ListEntity.Id));

                #endregion
                #region 設定週報狀態，設定為均未點名，因為後面程式還會再設定一次
                // 均未點名 = 100000000
                // 均已點名 = 100000001
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private int GetSmallGroupMemberNumber(Guid ListEntityId)
        {
            #region // 初始化每個小組名單，建立原始的 Member Data

            #region 取得小組名單，一個一個的連絡人實體
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
            #endregion

            return MemberCollection.Entities.Count;

            #endregion
        }
        private Entity CreateWeeklyReportAndPresentRecord(String GroupName, GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref String WeeklyReportEntityId, ref Entity aListEntity, String UploadCategory, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, SmallGroupData aSmallGroupData, String WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                // 建立週報
                Guid aCreatedWeeklyReportId = CreateWeeklyReport(ref aListEntity, aGroupWeeklyReportGuid);

                // 由於是新建立的週報，當回傳完成實，回到網頁操作，如果使用者又再繼續操作，就必須設定告知新建立的週報ID，以免重複建立
                WeeklyReportEntityId = aCreatedWeeklyReportId.ToString();

                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 建立的個人聚會與靈修記錄
                // 同時整理並取得:主日出席回報、小組出席回報、新人跟進字串，因為這樣就可以一魚兩吃，比較有效能一點
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                EntityCollection aPresentRecordCollection;
                if (aSmallGroupData.LoginType == "小組長")
                {
                    // 小組長回報
                    aPresentRecordCollection = CreatePresentRecordList(aSmallGroupData, GroupName, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                }
                else
                {
                    // 個人回報
                    SmallGroupData aSmallGroupDataFromList = new SmallGroupData();
                    aSmallGroupDataFromList.Members = new List<Member>();

                    SetAllMemberDataByPersonalReport(GroupName, aListEntity.Id, ref aSmallGroupDataFromList);

                    aPresentRecordCollection = CreatePresentRecordListByList(aSmallGroupData, aSmallGroupDataFromList, GroupName, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                }


                return UpdateWeeklyReport(aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetAllMemberDataByPersonalReport(String GroupName, Guid ListEntityId, ref SmallGroupData aSmallGroupData)
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

                        #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址、生日、職業及專長
                        // 組員的全名
                        String FullName = "";
                        if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)ContactEntity.Attributes["fullname"];
                        }
                        // 組員的手機
                        String aMobilePhone = "";
                        if (ContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)ContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (ContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)ContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (ContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)ContactEntity.Attributes["address2_line1"];
                        }

                        // 組員的生日
                        DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref ContactEntity, "birthdate").ToLocalTime();

                        // 組員的職業及專長
                        String aIndustry = "";
                        if (ContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)ContactEntity.Attributes["new_industry"];
                        }

                        // 組員的裝備狀態
                        String aEquipmentStatus = "";
                        if (ContactEntity.Attributes.Contains("new_equipment_status"))
                        {
                            aEquipmentStatus = (string)ContactEntity.Attributes["new_equipment_status"];
                        }

                        #endregion

                        #region// 委身類型
                        String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                        // 去除掉數字、空白、逗號
                        //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                        //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                        //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                        #endregion


                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "未選擇";
                        String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);


                        #region 傳回給網頁的資料
                        //10.未入組結案" 不用進入 APP
                        if (aIdentity != "10. 未入組結案")
                        {
                            aSmallGroupData.Members.Add
                            (
                                new Member
                                {
                                    PresentRecordId = PresentRecordIdCounter++.ToString(),
                                    Group = GroupName,
                                    FullName = FullName,
                                    #region 個人基本資料

                                    Phone = DigitsOnly.Replace(aMobilePhone, ""),
                                    HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                                    Address = aAddress,
                                    //BirthDate = aBirthDate,
                                    Industry = aIndustry,
                                    EquipmentStatus = aEquipmentStatus,

                                    BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_introducer"),
                                    BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_relationship"),

                                    #endregion
                                    Status = aIdentity, // 委身類型
                                    SmallGroupName = GroupName,
                                    SectionName = GroupName,
                                    PrayItem = "",
                                    Sunday = false, //主日出席
                                    SmallGroup = false,//小組出席
                                    #region 新人跟進關懷
                                    FollowUpWeek = aFollowUpWeek,
                                    FollowUpResult = "",
                                    FollowUpOption = "",
                                    FollowUp = "",
                                    FollowUpNextStep = "",
                                    FollowUpNote = "",
                                    NewComerNote = aNewComerNote,
                                    #endregion
                                    #region 靈修、晨、晚禱
                                    SpiritualWork = 0, // 讀經次數
                                    MorningPray = 0, // 晨禱(家庭祭壇)
                                    GeneralCare = 0, // 晚禱(禱告會次數)
                                    #endregion
                                }
                            );
                        }
                        #endregion

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

        #endregion
        #region 建立的個人聚會與靈修記錄
        private EntityCollection CreatePresentRecordList(SmallGroupData aSmallGroupData, String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            if (aSmallGroupData.LoginType == "小組長")
            {
                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                    //指派負責人
                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                    if (aPresentRecord != null)
                    {
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
            }
            else
            {
                #region 個人回報
                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                    //指派負責人
                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                    if (aPresentRecord != null)
                    {
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
                #endregion
            }

            return PresentRecordEntityCollection;
        }
        private EntityCollection CreatePresentRecordListByList(SmallGroupData aSmallGroupData, SmallGroupData aSmallGroupDataFromList, String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            if (aSmallGroupData.LoginType == "小組長")
            {
                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                    //指派負責人
                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                    if (aPresentRecord != null)
                    {
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
            }
            else
            {
                #region 個人回報
                foreach (Member aMemberInfomation in aSmallGroupDataFromList.Members)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord;
                    if (aSmallGroupData.Members[0].FullName != aMemberInfomation.FullName)
                    {
                        aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }
                    else
                    {
                        aPresentRecord = CreatePresentRecord(aSmallGroupData.Members[0], ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }

                    if (aPresentRecord != null)
                    {
                        //指派負責人
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
                #endregion
            }

            return PresentRecordEntityCollection;
        }
        private Entity CreatePresentRecord(Member aMemberInfomation, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            // 必須是名單裡的人，不能是只有依據姓名更新
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);
            //Entity aContactEntity = m_ToolUtilityClass.RetrieveContactEntityByName(aMemberInfomation.Name);
            //Entity aSearchedContactEntity = m_ToolUtilityClass.RetrieveContactByNameAndMobile(ref m_ToolUtilityClass.m_OrganizationService, aMemberInfomation.Name, aMemberInfomation.Phone );

            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactEntity.Id);
            //Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSearchedContactEntity.Id);

            if (aContactEntity != null)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 但是不知何故，更新連絡人之後，委身類型卻會"自動變成" 新朋友，所以就先用一個可以受影響的Entity aToUpdateContactEntity，去更新連絡人
                UpdateContactInfomation(aListEntity.Id, aMemberInfomation, ref aToUpdateContactEntity, HappyWeekTopic);

                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);

                //aPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                //取得並回傳新建的聚會與靈修記錄
                return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
            }
            else
            {
                return null;
            }
        }
        private String GetNewComerFollowupInfo(Guid aNewComerId, ref String aFollowUpWeek)
        {
            try
            {
                // 取得新人的實體
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aNewComerId);

                String aFollowUpHistoryReport = "";

                if (VerifyNewComerIdentity(aContact))
                {
                    // 確認是新人或是未入組才要處理

                    // 確認是否是新人或是未入組
                    int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                    if (aIdentityNumber == 100000004)
                    {
                        #region// 未入組

                        String aStartTracking = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_start_tracking_date");
                        if (aStartTracking != "")
                        {
                            // 如果是未入組就有可能是死灰復燃，所以要依據"開始關懷日期"是否要重燃關懷的過程
                            DateTime aStartTrackingDate = DateTime.Parse(aStartTracking);

                            #region 先根據日期尋找當週主日日期，先根據日期尋找開始關懷日期的那週主日日期
                            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                            int DayOfWeek = (int)aStartTrackingDate.DayOfWeek;

                            // 當週的星期日為認定的主日
                            //this.m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                            DateTime aSunday;
                            // 每周以星期六為第一日
                            if (DayOfWeek != 6)
                            {
                                // 如果不是星期六則是上個星期天
                                aSunday = DateTime.Now.AddDays(-DayOfWeek);
                            }
                            else
                            {
                                // 如果是星期六則是下個星期天
                                aSunday = DateTime.Now.AddDays(1);
                            }
                            #endregion



                            aFollowUpHistoryReport = GetFollowUpWeekForUnGroup(aContact, ref aFollowUpWeek, aSunday);
                        }
                        else
                        {
                            // 不是死灰復燃的未入組，所以就按照正常程序關懷
                            aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                        }
                        #endregion
                    }
                    else
                    {
                        #region// 新朋友

                        // 如果是新朋友就按正常程序來關懷，不會有死灰復燃的問題，因為根本就是新人
                        // 處理對應的週次及歡迎紀錄和每週跟進歷程
                        aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                        #endregion
                    }
                }
                else
                {

                }
                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private bool VerifyNewComerIdentity(Entity aContact)
        {
            try
            {
                // 確認是否是新人或是未入組
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                if (aIdentityNumber == 100000000 || aIdentityNumber == 100000004)
                {
                    //    case 100000000:
                    //        return "8. 新朋友";
                    //    case 100000004:
                    //        return "7. 未入組";

                    return true;
                }
                else
                {
                    return false;
                }
                //switch (Identity)
                //{
                //    case 100000000:
                //        return "8. 新朋友";
                //    case 100000001:
                //        return "5. 神學生";
                //    case 100000002:
                //        return "4. 小組長";
                //    case 100000003:
                //        return "3. 全職同工";
                //    case 100000004:
                //        return "7. 未入組";
                //    case 100000005:
                //        return "1. 牧師";
                //    case 100000006:
                //        return "2, 師母";
                //    case 100000007:
                //        return "9. 外教會";
                //    case 100000008:
                //        return "10. 未入組結案";
                //    case 1:
                //        return "6. 小組組員";
                //    default:
                //        return ".";
                //}

            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private String GetFollowUpWeek(Entity aContact, ref String MatchedWeekDay)
        {
            try
            {
                String aFollowUpHistoryReport = "";

                #region 歷程記錄的表頭
                #region// 性別
                int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
                if (Gender == 200000)
                {
                    aFollowUpHistoryReport += "性別:男性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;

                }
                #endregion
                #region// 首次進入教會日期
                try
                {
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                }
                catch (System.Exception Exception)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                }
                #endregion
                #region// 取得歡迎紀錄
                String WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
                if (WelcomeRecord != "")
                {
                    aFollowUpHistoryReport += "歡迎紀錄:" + Environment.NewLine + WelcomeRecord + Environment.NewLine + Environment.NewLine;
                }
                #endregion
                #endregion

                // 取得與此新人相關的出席紀錄單
                //EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

                #region 關懷歷程記錄
                if (PresentRecordCollection.Entities.Count > 0)
                {
                    aFollowUpHistoryReport += "關懷歷程記錄:" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "沒有關懷歷程記錄!" + Environment.NewLine;
                }
                #endregion

                int WeekCounter = 1;
                MatchedWeekDay = "";
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    #region 處理一個一個的出席紀錄

                    #region 決定本週的週次
                    DateTime aSundayDate = DateTime.Now;
                    try
                    {
                        aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                        if (aSundayDate.Date == this.m_Sunday.Date)
                        {
                            // 轉化成為中文的週次，這是要SHOW給APP看的
                            MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);
                        }
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    #region 新人跟進相關資訊
                    //aFollowUpHistoryReport += aSundayDate.Date.ToShortDateString() + "， 第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，";
                    aFollowUpHistoryReport += "第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，" + aSundayDate.Date.ToShortDateString() + "，";
                    aFollowUpHistoryReport += "小組長:" + this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record") + "，";

                    //if (aSundayDate != DateTime.Now)
                    //{
                    //    aFollowUpHistoryReport += "跟進日期:" + aSundayDate.ToShortDateString() + "，";
                    //}

                    #region //跟進方式
                    int FollowUpOptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_followup_ways");
                    String aFollowUpOption = ConvertIndexToFollowUpOptionPicker(FollowUpOptionValue);
                    String aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
                    if (aFollowUpMethod != "")
                    {
                        aFollowUpHistoryReport += "跟進方式:" + aFollowUpOption + aFollowUpMethod + "，";
                    }
                    #endregion
                    #region//新人跟進結果
                    String aFollowUpResult = "";
                    if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                        aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                    }
                    if (aFollowUpResult != "" && aFollowUpResult != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進結果:" + aFollowUpResult + "，";
                    }
                    #endregion
                    #region//新人跟進下一步驟
                    String aFollowUpNextStep = "";
                    if (PresentRecordEntity.Attributes.Contains("new_next_step"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                        aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                    }
                    if (aFollowUpNextStep != "" && aFollowUpNextStep != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進下一步驟:" + aFollowUpNextStep + "，";
                    }
                    #endregion
                    #region//跟進描述
                    String aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
                    if (aExplanation != "")
                    {
                        aFollowUpHistoryReport += "跟進描述:" + aExplanation + Environment.NewLine + Environment.NewLine;
                    }
                    else
                    {
                        aFollowUpHistoryReport += Environment.NewLine + Environment.NewLine;
                    }
                    #endregion
                    #endregion

                    #region 自動幫忙重新設定關懷週次

                    try
                    {
                        int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                        this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);
                        //Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
                        }
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    // 自動把新朋友若是超過10週的關懷則設為未入組，把未入組若是超過或等於18週的關懷則設為未入組結案
                    TransferIdentity(aContact, WeekCounter, 10, 18);

                    WeekCounter++;
                    #endregion
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private String GetFollowUpWeekForUnGroup(Entity aContact, ref String MatchedWeekDay, DateTime aStartTrackingSunday)
        {
            try
            {
                String aFollowUpHistoryReport = "";

                #region 歷程記錄的表頭
                #region// 性別
                int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
                if (Gender == 200000)
                {
                    aFollowUpHistoryReport += "性別:男性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;

                }
                #endregion
                #region// 首次進入教會日期
                try
                {
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                }
                catch (System.Exception Exception)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                }
                #endregion
                #region// 取得歡迎紀錄
                String WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
                if (WelcomeRecord != "")
                {
                    aFollowUpHistoryReport += "歡迎紀錄:" + Environment.NewLine + WelcomeRecord + Environment.NewLine + Environment.NewLine;
                }
                #endregion
                #endregion

                // 取得與此新人相關的出席紀錄單
                //EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

                #region 關懷歷程記錄
                if (PresentRecordCollection.Entities.Count > 0)
                {
                    aFollowUpHistoryReport += "關懷歷程記錄:" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "沒有關懷歷程記錄!" + Environment.NewLine;
                }
                #endregion

                int WeekCounter = 1;
                MatchedWeekDay = "";
                bool FoundFlag = false;
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    #region 處理一個一個的出席紀錄

                    DateTime aPresentRecordSundayDate = DateTime.Now;

                    aPresentRecordSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");

                    if (FoundFlag == false)
                    {
                        if (aPresentRecordSundayDate.ToShortDateString() == aStartTrackingSunday.ToShortDateString())
                        {
                            // 找到了死灰復燃的那個主日日期
                            WeekCounter = 1; // 設定為第一周
                            FoundFlag = true; // 開始循序累加周次
                        }
                        else
                        {
                            continue;
                        }
                    }

                    #region 決定本週的週次
                    DateTime aSundayDate = DateTime.Now;
                    try
                    {
                        aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                        if (aSundayDate.Date == this.m_Sunday.Date)
                        {
                            // 轉化成為中文的週次，這是要SHOW給APP看的
                            MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);
                        }
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    #region 新人跟進相關資訊
                    //aFollowUpHistoryReport += aSundayDate.Date.ToShortDateString() + "， 第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，";
                    aFollowUpHistoryReport += "第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，" + aSundayDate.Date.ToShortDateString() + "，";
                    aFollowUpHistoryReport += "小組長:" + this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record") + "，";

                    //if (aSundayDate != DateTime.Now)
                    //{
                    //    aFollowUpHistoryReport += "跟進日期:" + aSundayDate.ToShortDateString() + "，";
                    //}

                    #region //跟進方式
                    String aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
                    if (aFollowUpMethod != "")
                    {
                        aFollowUpHistoryReport += "跟進方式:" + aFollowUpMethod + "，";
                    }
                    #endregion
                    #region//新人跟進結果
                    String aFollowUpResult = "";
                    if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                        aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                    }
                    if (aFollowUpResult != "" && aFollowUpResult != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進結果:" + aFollowUpResult + "，";
                    }
                    #endregion
                    #region//新人跟進下一步驟
                    String aFollowUpNextStep = "";
                    if (PresentRecordEntity.Attributes.Contains("new_next_step"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                        aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                    }
                    if (aFollowUpNextStep != "" && aFollowUpNextStep != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進下一步驟:" + aFollowUpNextStep + "，";
                    }
                    #endregion
                    #region//跟進描述
                    String aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
                    if (aExplanation != "")
                    {
                        aFollowUpHistoryReport += "跟進描述:" + aExplanation + Environment.NewLine + Environment.NewLine;
                    }
                    else
                    {
                        aFollowUpHistoryReport += Environment.NewLine + Environment.NewLine;
                    }
                    #endregion
                    #endregion

                    #region 自動幫忙重新設定關懷週次

                    try
                    {
                        int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                        this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);
                        //Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
                        }
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aRetrievedPresentRecordEntity);
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    // 因為這是未入組死灰復燃，把未入組若是超過或等於10週的關懷則設為未入組結案
                    TransferIdentity(aContact, WeekCounter, 10, 10);

                    WeekCounter++;
                    #endregion
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void TransferIdentity(Entity aContact, int Counter, int NewComeMaxiNumber, int UnGroupMaxiNumber)
        {
            //switch (Identity)
            //{
            //    case 100000000:
            //        return "8. 新朋友";
            //    case 100000001:
            //        return "5. 神學生";
            //    case 100000002:
            //        return "4. 小組長";
            //    case 100000003:
            //        return "3. 全職同工";
            //    case 100000004:
            //        return "7. 未入組";
            //    case 100000005:
            //        return "1. 牧師";
            //    case 100000006:
            //        return "2, 師母";
            //    case 100000007:
            //        return "9. 外教會";
            //    case 100000008:
            //        return "10. 未入組結案";
            //    case 1:
            //        return "6. 小組組員";
            //    default:
            //        return ".";
            //}


            // 確認是否是新人或是未入組
            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
            if (aIdentityNumber == 100000000)
            {

                //m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定

                // 新朋友
                if (Counter >= NewComeMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    if (TRANSFER_IDENTITY_FLAG == true)
                    {
                        // 新朋友變為未入組
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else { }
            }
            else if (aIdentityNumber == 100000004)
            {
                //未入組
                if (Counter >= UnGroupMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    if (TRANSFER_IDENTITY_FLAG == true)
                    {
                        // 未入組變為未入組結案(超過或是等於)
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000001);

                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }

                }
                else { }
            }
            else
            {

            }

        }
        private String ConvertNumberToFollowUpWeekPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 1:
                    return "一";
                case 2:
                    return "二";
                case 3:
                    return "三";
                case 4:
                    return "四";
                case 5:
                    return "五";
                case 6:
                    return "六";
                case 7:
                    return "七";
                case 8:
                    return "八";
                case 9:
                    return "九";
                case 10:
                    return "十";
                case 11:
                    return "十一";
                case 12:
                    return "十二";
                case 13:
                    return "十三";
                case 14:
                    return "十四";
                case 15:
                    return "十五";
                case 16:
                    return "十六";
                case 17:
                    return "十七";
                case 18:
                    return "十八";
                case 19:
                    return "十九";
                case 20:
                    return "二十";
                default:
                    return "二十";
            }
        }
        private int ConvertNumberToWeekIndex(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 1:
                    return 100000000;
                case 2:
                    return 100000001;
                case 3:
                    return 100000002;
                case 4:
                    return 100000003;
                case 5:
                    return 100000004;
                case 6:
                    return 100000005;
                case 7:
                    return 100000006;
                case 8:
                    return 100000007;
                case 9:
                    return 100000008;
                case 10:
                    return 100000009;
                case 11:
                    return 100000010;
                case 12:
                    return 100000011;
                case 13:
                    return 100000012;
                case 14:
                    return 100000013;
                case 15:
                    return 100000014;
                case 16:
                    return 100000015;
                case 17:
                    return 100000016;
                case 18:
                    return 100000017;
                case 19:
                    return 100000018;
                case 20:
                    return 100000019;
                default:
                    return 100000007;
            }
        }
        private String ConvertIndexToFollowUpResultPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "請選擇";
                case 100000001:
                    return "熱情回應";
                case 100000002:
                    return "渴慕認識信仰";
                case 100000003:
                    return "沒聯絡上";
                case 100000004:
                    return "反應冷淡";
                case 100000005:
                    return "考慮中，繼續跟進";
                case 100000006:
                    return "入小組";
                case 100000007:
                    return "來主日";
                case 100000008:
                    return "轉介";
                case 100000009:
                    return "其他";
                default:
                    return "";
            }
        }
        private String ConvertIndexToFollowUpNextStepPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "請選擇";
                case 100000001:
                    return "繼續跟進";
                case 100000002:
                    return "轉介";
                default:
                    return "";
            }
        }
        private String ConvertIndexToFollowUpOptionPicker(int FollowUpWays)
        {
            switch (FollowUpWays)
            {
                case 100000000:
                    return "電話";
                case 100000001:
                    return "探訪";
                case 100000002:
                    return "Line/FB";
                case 100000003:
                    return "出遊/吃飯";
                case 100000004:
                    return "懷鄉/其他課程";
                case 100000005:
                    return "約談";
                case 100000006:
                    return "沒跟進";
                case 100000007:
                    return "其他";
                default:
                    return "";
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, Member aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox )
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = aMemberInfomation.FullName + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", PresentRecordName);
                #endregion
                #region 指派主日小組靈修出席單的負責人
                //Guid ListOwnerId = aListEntity.GetAttributeValue<EntityReference>("ownerid").Id;
                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, ListOwnerId);
                #endregion
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 關聯週報 Lookup
                if (aWeeklyReportId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId); }
                #endregion
                #region 從名單取得 區名、小家長 ID、小組長 ID、小家長、區牧長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 區牧長 ID
                Guid aShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_area_name", AreaName);
                #endregion
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯族系組長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯區牧長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "contact", aShepherdLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (aListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                }
                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);
                #endregion
                #region 取得比較易懂的委身類型
                // 找到該組員的屬性
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                // 取得比較易懂的委身類型
                //String ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);
                #endregion
                #region 設定主日出席
                if (aMemberInfomation.Sunday == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                    aWeeklySundayNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                }
                #endregion
                #region 設定主日出席率
                if (aMemberInfomation.Sunday == true)
                {
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            aWeeklySundayRate += 1 / ValidNumber;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                }
                #endregion
                #region 設定小組出席
                if (aMemberInfomation.SmallGroup == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);

                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                }
                #endregion
                #region 設定小組出席率
                if (aMemberInfomation.SmallGroup == true)
                {
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            aWeeklySmallGroupRate += 1 / ValidNumber; ;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                }
                #endregion

                #region 禱告會次數
                if (aMemberInfomation.PrayerMeeting == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", 0);
                }
                #endregion

                #region 門徒訓練班次數
                if (aMemberInfomation.Child == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", 0);
                }
                #endregion

                #region 門徒大聚次數
                if (aMemberInfomation.BigDisciple == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", 0);
                }
                #endregion

                #region 小組長小講堂次數
                if (aMemberInfomation.LeadershipSmallLecture == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", 0);
                }
                #endregion

                #region 小組長大聚次數
                if (aMemberInfomation.Sunday == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", 0);
                }
                #endregion


                #region 設定幸福小組出席

                if (aMemberInfomation.SmallGroup == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);
                }
                #endregion
                #region 設定決志
                if (aMemberInfomation.Decision == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 0);
                }
                #endregion
                #region 設定附註或是代禱事項

                // 聖谷行道會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                // 聖谷行道會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region// 新人跟進

                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);

                // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                // 但是網頁現在可以了
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);

                #endregion
                #region// 讀經次數

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_spiritual_work", aMemberInfomation.SpiritualWork);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_morning_pray", aMemberInfomation.MorningPray);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_general_care", aMemberInfomation.GeneralCare);

                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.PrayNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.FamilyNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_evening_pray", aMemberInfomation.WorkAndCampusNumber);
                #endregion
                #region 設定小組是否暫停
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecord, "new_pause", PauseCheckBox);
                #endregion
                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private double GetEffecttiveSmallGroupNumber(Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");
            //EntityCollection MemberCollection = m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ref m_ToolUtilityClass.m_OrganizationService, ListEntityId);

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                MemberCollection = m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ListEntityId);
            }
            else
            {
                // 動態名單
                MemberCollection = m_ToolUtilityClass.RetrieveDynamicMemberList(ListEntityId);
            }

            Double EffectiveNumber = 0.0;
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


                // 每個組員
                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員
                        if (ContactEntity.Attributes.Contains("customertypecode"))
                        {
                            OptionSetValue aCustomerTypeCode = ContactEntity.Attributes["customertypecode"] as OptionSetValue;

                            // 如果是新朋友或是未入組則不列入累積，聖谷行道會
                            if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                            {
                                EffectiveNumber++;
                            }

                            // 如果是新朋友或是未入組則不列入累積，聖谷行道會
                            // 10.不穩定組員   =   100,000,008
                            // 11.新朋友       =   100,000,009
                            // 12.未入組       =   100,000,010
                            // 13.暫不入組     =   100,000,012
                            // 14.結案         =   100,000,011
                            //if (aCustomerTypeCode.Value != 100000008 && aCustomerTypeCode.Value != 100000009 && aCustomerTypeCode.Value != 100000010 && aCustomerTypeCode.Value != 100000012 && aCustomerTypeCode.Value != 100000011 && aCustomerTypeCode.Value != EMPTY_VALUE )
                            //{
                            //    EffectiveNumber++;
                            //}
                            //else
                            //{ }

                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref ContactEntity, "customertypecode", 100000000);

                            this.m_ToolUtilityClass.UpdateEntity(ref ContactEntity);
                        }
                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return EffectiveNumber;
        }
        private Entity UpdateContactInfomationFromList(String ContactName, Guid ListEntityId)
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

            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 名單中每個組員
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

                // 必須是使用中的連絡人
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
                            FullName = (string)ContactEntity.Attributes["fullname"];

                            if (FullName == ContactName)
                                return ContactEntity;
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return null;
        }
        #endregion
        #region 更新個人紀錄:手機、家裡電話、地址、設定委身類型
        private void UpdateContactInfomation(Guid aListEntityId, Member aMember, ref Entity aContactEntity, String HappyWeekTopic)
        {
            bool ModifyFlag = false;
            #region // 更新個人資料:手機、家裡電話、地址、設定委身類型
            // 組員的手機
            if (aMember.Phone != null)
            {
                if (DigitsOnly.Replace(aMember.Phone, "") != DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "mobilephone"), ""))
                {
                    // 系統裡的聯絡人家裡電話跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aMember.Phone);
                    ModifyFlag = true;
                }
            }
            // 組員的家裡電話
            if (aMember.HomePhone != null)
            {
                if (DigitsOnly.Replace(aMember.HomePhone, "") != DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "telephone2"), ""))
                {
                    // 系統裡的聯絡人家裡電話跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aMember.HomePhone);
                    ModifyFlag = true;
                }
            }
            // 組員的地址
            if (aMember.Address != null)
            {
                if (aMember.Address != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "address2_line1"))
                {
                    // 系統裡的聯絡人的地址跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aMember.Address);
                    ModifyFlag = true;
                }
            }
            //組員的生日
            if (aMember.BirthDate != null)
            {
                if (aContactEntity.Attributes.Contains("birthdate"))
                {
                    DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate").ToLocalTime().ToLocalTime();
                    if (aMember.BirthDate != aBirthDate)
                    {
                        if (aMember.BirthDate > DateTime.MinValue && aMember.BirthDate.Year > 1753)
                        {
                            // 系統裡的聯絡人職業及專長跟APP上傳的不一致
                            this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMember.BirthDate);
                            ModifyFlag = true;
                        }
                    }
                }
                else
                {
                    if (aMember.BirthDate > DateTime.MinValue && aMember.BirthDate.Year > 1753)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMember.BirthDate);
                        ModifyFlag = true;
                    }
                }
            }
            // 組員的職業及專長(聖谷行道會)
            if (aMember.Industry != null)
            {
                if (aMember.Industry != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_industry"))
                {
                    // 系統裡的聯絡人的組員的職業及專長跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aMember.Industry);
                    ModifyFlag = true;
                }
            }
            // 組員的介紹人
            if (aMember.BestIntroducer != null)
            {
                if (aMember.BestIntroducer != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_best_introducer"))
                {
                    // 系統裡的聯絡人的組員的職業及專長跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_best_introducer", aMember.BestIntroducer);
                    ModifyFlag = true;
                }
            }
            // 組員的介紹人關係
            if (aMember.BestRelationship != null)
            {
                String aBestRelationship = "";
                if (aContactEntity.Attributes.Contains("new_best_relationship"))
                {
                    aBestRelationship = (string)aContactEntity.Attributes["new_best_relationship"];
                    if (aMember.BestRelationship != aBestRelationship)
                    {
                        // 系統裡的連絡人介紹人跟APP上傳的不一致
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_best_relationship", aMember.BestRelationship);
                        ModifyFlag = true;
                    }
                }
            }
            if (aMember.BestLeader != null)
            {
                if (aMember.BestLeader != "" && this.m_GroupType == "幸福小組")
                {
                    Guid aSearchedContactSpiritLeaderId = GetContactSpiritLeaderId(aListEntityId, aMember.BestLeader);

                    if (aSearchedContactSpiritLeaderId != Guid.Empty)
                    {
                        Guid aContactSpiritLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aContactEntity, "new_contact_contact_spiritleader");

                        if (aContactSpiritLeaderId != aSearchedContactSpiritLeaderId)
                        {
                            this.m_ToolUtilityClass.SetEntityLookUpAttribute(aContactEntity, "new_contact_contact_spiritleader", "contact", aSearchedContactSpiritLeaderId);
                            ModifyFlag = true;
                        }
                    }
                }
            }
            #region 設定個人幸福小組出席紀錄次數與課程
            if (HappyWeekTopic != "" && aMember.SmallGroup == true && this.m_GroupType == "幸福小組")
            {
                //SetContactHappyTimesAndHistory(ref aContactEntity, HappyWeekTopic);

                //ModifyFlag = true;

                ModifyFlag = SetContactHappyTimesAndHistory(ref aContactEntity, HappyWeekTopic);
            }
            else
            {
                //RemoveContactHappyTimesAndHistory(ref BestContactEntity, HappyCourse);
            }
            #endregion

            #endregion

            // 經由最近8週的出席次數計算、設定委身類型

            //if (SET_IDENTITY_METHOD == "透過過去8週出席次數")
            //{
            //    SetIdentity(aListEntityId, ref aContactEntity);
            //}
            //else
            //{
            //    ModifyFlag = SetIdentityByUpload(ref aContactEntity, ref aMember);
            //}
            if (SetSpiritualIdentityByUpload(ref aContactEntity, ref aMember) == true)
            {
                // 有透過手動更改受洗狀態
                ModifyFlag = true;
            }

            if (SetBaptizedSituationByUpload(ref aContactEntity, ref aMember) == true)
            {
                // 有透過手動更改受洗狀態
                ModifyFlag = true;
            }

            // 設定 BEST 是否決志
            if (aMember.Decision == true)
            {
                if (this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity") == 100000004 || this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity") == 100000001 || this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity") == 100000005)
                {
                    // 如果系統裡的連絡人的原來信仰是: "未知" 或 "未受洗" 或 "未信主"
                    // 則設定連絡人的信仰是: "已決志" 
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000002);

                    // 則設定連絡人的決志日期是: 現在( DateTime.Now )
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "new_decide_date", DateTime.Now);

                    ModifyFlag = true;
                }
            }

            if (ModifyFlag != true)
            {
                #region 之前的人基本資料就已經有改過了
                if (SetIdentityByUpload(ref aContactEntity, ref aMember) == true)
                {
                    // 有透過手動更改委身類型
                    ModifyFlag = true;
                }
                else
                {
                    if (SET_IDENTITY_METHOD == "透過過去8週出席次數")
                    {
                        // 沒透過手動更改委身類型，就改為由系統判斷
                        // 透過過去8週出席次數，自動計算委身類型
                        ModifyFlag = SetIdentity(aListEntityId, ref aContactEntity);
                    }
                }
                #endregion
            }

            if (ModifyFlag == true)
            {
                // 更新聯絡人
                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
            }
        }

        private Guid GetContactSpiritLeaderId(Guid ListEntityId, String BestLeaderName)
        {
            try
            {
                // 每個組員
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(ListEntityId, ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    #region//取得名單中的每個組員
                    Entity aContactEntity;

                    if (ListType == false)
                    {
                        // 靜態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    }
                    else
                    {
                        // 動態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                    }
                    #endregion

                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname") == BestLeaderName)
                    {
                        return aContactEntity.Id;
                    }

                }

                return Guid.Empty;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private EntityCollection GetPersonalSmallGroupLeaderMemberData(Guid ListEntityId, ref bool ListType)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
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

            return MemberCollection;
            #endregion

        }

        #endregion
        #region 更新個人聚會與靈修記錄

        #region 更新出席紀錄
        private Entity UpdateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, EntityCollection PresentRecordCollection, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, SmallGroupData aSmallGroupData, String WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                #region 更新週報
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                aWeeklySundayNumber = 0;
                aWeeklySundayRate = 0.0;
                aWeeklySmallGroupNumber = 0;
                aWeeklySmallGroupRate = 0.0;

                foreach (Entity aMachedPresentRecordEntity in PresentRecordCollection.Entities)
                {
                    if (aMachedPresentRecordEntity != null)
                    {
                        #region 計算個人聚會與靈修記錄
                        // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                        String ClearIdentity = "";
                        bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

                        #region 取得主日出席
                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_sunday_present_this_week") == 1)
                        {
                            aWeeklySundayNumber += 1;
                            if (ValidNumber > 0)
                            {
                                if (AccumulateFlag == true)
                                {
                                    ValidSundayMemberNumber += 1;
                                    aWeeklySundayRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        { }
                        #endregion
                        #region 設定小組出席
                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_group_present_this_week") == 1)
                        {
                            aWeeklySmallGroupNumber += 1;
                            if (ValidNumber > 0)
                            {
                                if (AccumulateFlag == true)
                                {
                                    ValidSmallGroupMemberNumber += 1;
                                    aWeeklySmallGroupRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                        }
                        #endregion

                        #endregion
                    }
                    else
                    {
                    }
                }

                #endregion

                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                #region 設定小組聚會日期

                // 取得周報的小組長，若是與登入的回報者同一個人
                // 才有權限更改小組聚會日期
                if (this.m_ContactEntity.Id == this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_"))
                {
                    if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);
                    }
                }
                #endregion

                // 設定新人跟進報告

                // 設定週報點名狀態
                this.SetupWeeklyReportStatus("主日點名", ref aWeeklyReportEntity);

                #region 設定出席率、人數
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", aWeeklySundayRate);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", aWeeklySmallGroupRate);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", aWeeklySundayNumber);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", aWeeklySmallGroupNumber);

                // 更新週報
                //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aWeeklyReportEntity);

                // 加入百分比 
                //AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", ValidSundayMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySundayRate) + Environment.NewLine);
                //AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", ValidSmallGroupMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySmallGroupRate) + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", Environment.NewLine + "實到" + ValidSundayMemberNumber.ToString() + "人/應到" + ValidNumber.ToString() + "人，出席率" + String.Format("{0:0%}", aWeeklySundayRate));
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", Environment.NewLine + "實到" + ValidSmallGroupMemberNumber.ToString() + "人/應到" + ValidNumber.ToString() + "人，出席率" + String.Format("{0:0%}", aWeeklySmallGroupRate));
                #endregion

                // 建立週報主日出席、小組出席、新人跟進字串內容
                String SmallGroupResult = this.SetupWeeklyReportResult(ref aWeeklyReportEntity);

                // 設定小組日誌
                if ( WeeklyReportData != "不需更新小組日誌" )
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_memo", WeeklyReportData);
                    if (PauseCheckBox == true)
                    {
                        // 設定週報狀態為暫停
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000002);
                    }
                    else
                    {
                        // 設定週報狀態為均已點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                    }
                }
                // 設定幸福小組週次
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_weekly_index", HappyWeekIndex);
                // 設定幸福小組主題
                if ( HappyWeekTopic != "" && HappyWeekTopic != null )
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_topic", this.ConvertTopicToIndex(HappyWeekTopic));
                }
                else
                {
                    //this.m_ToolUtilityClass.RemoveAttribute(ref aWeeklyReportEntity, "new_topic");
                    //this.m_ToolUtilityClass.SetEntityAttributeToNull(ref aWeeklyReportEntity, "new_topic");
                    this.m_ToolUtilityClass.SetOptionSetAttributeNull(ref aWeeklyReportEntity, "new_topic");
                }

                if (this.m_GroupType == "幸福小組")
                {
                    #region 處理幸福小組，如果是第一週就填入幸福小組的核心同工名單，第二週以後就回傳幸福小組Best名單
                    String BestList = ProcessHappyGroupMembers(ref aListEntity, aWeeklyReportEntity, HappyWeekIndex, HappyWeekTopic);
                    #endregion

                    #region 計算幸福小組週報出席人數
                    CalculateWeeklyReportTotalNumber(ref aWeeklyReportEntity);
                    #endregion

                    #region 建立幸福小組的 BEST
                    //List<BestRecord>  BestRecordList = new List<BestRecord>();
                    //CreateDefaultBestList(ref aWeeklyReportEntity, AreaName, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, aListEntity, aHappyGroupWeeklyReportToBeAdded.MeetingDate, HappyGroupStartTime, HappyGroupEndTime, m_SmallGroupPlace, m_SmallGroupTime, ref aHappyGroupWeeklyReportListClassToBeAdded, ref aHappyGroupWeeklyReportToBeAdded, BestList);
                    #endregion
                }

                // 透過 LINE 回報權柄
                if (aSmallGroupData.LoginType == "小組長" && WeeklyReportData != "不需更新小組日誌")
                {
                    // 個人回報就不LINE給權柄了，如果是上傳組員基本資料也不必回報LINE給權柄了
                    this.m_LineNotifyUtility.SendSmallGroupResultLine(this.m_ContactEntity, SmallGroupResult, aGroupWeeklyReportGuid, aWeeklyReportId, ref aListEntity, ref aSmallGroupData, WeeklyReportData, PauseCheckBox);
                }

                // 更新週報
                this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);

                // 指派週報的負責人
                this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", aWeeklyReportEntity, this.m_OwnerId);

                #endregion

                #region 回傳至 APP
                aGroupWeeklyReportGuid.WeeklyReportGuid = aWeeklyReportId;         // 回傳至 APP 的週報 Id
                aGroupWeeklyReportGuid.SundayPresentRate = aWeeklySundayRate;       // 回傳至 APP 的主日出席率
                aGroupWeeklyReportGuid.SmallGroupRate = aWeeklySmallGroupRate;     // 回傳至 APP 的小組出席率
                #endregion

                // 傳回建立的週報
                return aWeeklyReportEntity;

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Entity UpdateWeeklyReportProcess(GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref Entity aListEntity, ref Guid aWeeklyReportId, SmallGroupData aSmallGroupData, String WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                #region 更新週報流程
                Double aWeeklySundayRate = 0.0;
                Double aWeeklySmallGroupRate = 0.0;
                int aWeeklySundayNumber = 0;
                int aWeeklySmallGroupNumber = 0;

                Double ValidNumber = 0.0F; // 依據之前點名的靈修紀錄的有效組員當作週報出席率的分母

                // 已經有週報所以從靈修出席紀錄單計算出席率的分母
                #region 取得跟週報相關的靈修紀錄單
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion

                // 依據靈修出席紀錄單計算出席率的分母
                ValidNumber = GetValidMemberNumber(aPresentRecordCollection);

                #region// 更新個人資料:手機、家裡電話、地址、設定委身類型 + 更新個人聚會與靈修記錄
                UpdatePresentRecord(this.m_GroupNamedListMemberInfomation, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, aSmallGroupData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                #endregion

                #region 更新週報
                return UpdateWeeklyReport(aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                #endregion

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void UpdatePresentRecord(List<MemberInfomation> aGroupNamedListMemberInfomation, EntityCollection PresentRecordCollection, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, SmallGroupData aSmallGroupData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                #region 更新每個靈修紀錄
                foreach (Member aMember in aSmallGroupData.Members)
                {
                    //if (aMember.ModifyFlag == false) continue;

                    // 上傳的點名與系統的靈修紀錄對應尋找
                    Entity aMachedPresentRecordEntity = SearchPresentRecordByName(aMember.FullName, ref PresentRecordCollection);

                    if (aMachedPresentRecordEntity != null)
                    {
                        // 有找到靈修紀錄
                        // 更新個人資料:手機、家裡電話、地址、設定委身類型
                        // 更新個人聚會與靈修記錄
                        #region 更新個人資料:手機、家裡電話、地址、委身類型
                        EntityReference aFullNameEntityReference = new EntityReference();
                        if (aMachedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                        {
                            aFullNameEntityReference = (EntityReference)aMachedPresentRecordEntity.Attributes["new_contact_new_present_record"];
                        }
                        Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);

                        // 更新個人資料:手機、家裡電話、地址、設定委身類型
                        // 但是不知何故，更新連絡人之後，委身類型卻會"自動變成" 新朋友，所以就先用一個可以受影響的Entity aToUpdateContactEntity，去更新連絡人
                        Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
                        var aContactToBeSentToDynamics = new Entity("contact", aToUpdateContactEntity.Id);
                        UpdateContactInfomation(aListEntity.Id, aMember, ref aToUpdateContactEntity, HappyWeekTopic);

                        #endregion

                        #region 設定及更新個人聚會與靈修記錄
                        // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                        String ClearIdentity = ""; //順便取的委身類型
                        bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

                        #region 設定主日出席
                        if (aMember.Sunday == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 1);

                            AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);

                            aWeeklySundayNumber += 1;
                        }
                        else
                        {
                            AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 0);
                        }
                        #endregion
                        #region 設定主日出席率
                        if (aMember.Sunday == true)
                        {
                            if (ValidNumber > 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    aWeeklySundayRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 0.0);
                        }
                        #endregion
                        #region 設定小組出席
                        if (aMember.SmallGroup == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 1);
                            AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                            aWeeklySmallGroupNumber += 1;
                        }
                        else
                        {
                            AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 0);
                        }
                        #endregion
                        #region 設定小組出席率
                        if (aMember.SmallGroup == true)
                        {
                            if (ValidNumber > 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    aWeeklySmallGroupRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 0);
                        }
                        #endregion

                        #region 禱告會次數
                        if (aMember.PrayerMeeting == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_prayer_meeting_number", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_prayer_meeting_number", 0);
                        }
                        #endregion

                        #region 門徒訓練班次數
                        if (aMember.Child == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_child_number", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_child_number", 0);
                        }
                        #endregion

                        #region 門徒大聚次數
                        if (aMember.BigDisciple == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_big_disciple_number", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_big_disciple_number", 0);
                        }
                        #endregion

                        #region 小組長小講堂次數
                        if (aMember.LeadershipSmallLecture == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_leadership_small_lecture_number", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_leadership_small_lecture_number", 0);
                        }
                        #endregion

                        #region 小組長大聚次數
                        if (aMember.Sunday == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_leaders_gather_number", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_leaders_gather_number", 0);
                        }
                        #endregion

                        #region 設定幸福小組出席

                        if (aMember.SmallGroup == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_present", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_present", 0);
                        }
                        #endregion
                        #region 設定決志

                        if (aMember.Decision == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_decision", 1);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_decision", 0);
                        }
                        #endregion
                        #region 設定附註或是代禱事項
                        // 聖谷行道會
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_name", aMemberInfomation.Note);
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMember.PrayItem);

                        // 聖谷行道會
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_memo", aMemberInfomation.Note);
                        #endregion
                        #region 聖谷行道會的欄位

                        #region// 牧養狀態
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_shepherd_situation", aMemberInfomation.ShepherdStatus);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_onebyone_situation", aMemberInfomation.OneOnOne);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_training_system", aMemberInfomation.Training);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_equipment_class", aMemberInfomation.Incubate);
                        #endregion

                        #region// 新人跟進

                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMember.FollowUpWeek));
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMember.FollowUpResult));
                        if (aMember.FollowUpNextStep != "")
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMember.FollowUpNextStep));
                        }
                        if (aMember.FollowUpOption != "")
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_followup_ways", ConvertFollowUpOptionToIndex(aMember.FollowUpOption));
                        }
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_follow_up", aMember.FollowUpOption);

                        // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                        // 但是網頁現在可以了
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMember.PrayItem);

                        AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMember);

                        #endregion

                        #endregion
                        #region// 讀經次數

                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMember.SpiritualWork); // 讀經次數
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMember.MorningPray);
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMember.GeneralCare); // 禱告次數

                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.PrayNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.FamilyNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_evening_pray", aMemberInfomation.WorkAndCampusNumber);
                        #endregion
                        #region 設定小組是否暫停
                        this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_pause", PauseCheckBox);
                        #endregion
                        #region//設定個人聚會與靈修記錄"停止提醒"為"是" + 設定個人聚會與靈修記錄"不要顯示在回報網頁"為"是"
                        //設定個人聚會與靈修記錄"停止提醒"為"是"
                        m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_stop_notify", true);

                        if ( aMember.AssignedGroup != "" && aMember.AssignedGroup != null )
                        {
                            if (aMember.AssignedGroup.Contains("關懷") != true)
                            {
                                // 如果被指派的不是"關懷"小組，才要停止顯示
                                //設定個人聚會與靈修記錄"不要顯示在回報網頁"為"是"
                                m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_not_display", true);
                            }
                        }
                        #endregion
                        #region 更新個人聚會與靈修記錄
                        //this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
                        #endregion

                        //指派負責人
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aMachedPresentRecordEntity, this.m_OwnerId);

                        #endregion

                        if (aMember.AssignedGroup != "" && aMember.AssignedGroup != null)
                        {
                            // 有重新指派小組
                            AssignNewSmallGroup(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);

                            //aSmallGroupData.Members.Remove(aMember);
                        }
                        else if (aMember.FollowUpNextStep == "轉介")
                        {
                            TerminateNewPersonFollowUp(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);

                            //aSmallGroupData.Members.Remove(aMember);
                        }
                        else
                        {
                            //if ( m_ToolUtilityClass.GetEntityDateTimeAttribute(aMachedPresentRecordEntity, "new_care_expire_date") != null )
                            //{
                            //    TerminateNewPersonCareWorkflow(aMachedPresentRecordEntity, aMember.AssignedGroup);
                            //}
                        }

                        #region 更新個人聚會與靈修記錄
                        this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
                        #endregion

                    }
                    else
                    {
                        // 沒找到靈修紀錄
                        //CreatePresentRecord(aMember, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }
                }

                for (int i = 0; i < aSmallGroupData.Members.Count; i++)
                {
                    if (aSmallGroupData.Members[i].AssignedGroup != "" && aSmallGroupData.Members[i].AssignedGroup != null)
                    {
                        aSmallGroupData.Members.RemoveAt(i);
                    }
                    else if (aSmallGroupData.Members[i].FollowUpNextStep == "轉介")
                    {
                        aSmallGroupData.Members.RemoveAt(i);
                    }
                    else { }
                }
                //foreach (Member aMember in aSmallGroupData.Members)
                //{
                //    if (aMember.AssignedGroup != "" && aMember.AssignedGroup != null)
                //    {
                //        aSmallGroupData.Members.Remove(aMember);
                //    }
                //    else if (aMember.FollowUpNextStep == "轉介")
                //    {
                //        aSmallGroupData.Members.Remove(aMember);
                //    }
                //    else
                //    {
                //    }
                //}
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private bool SetContactHappyTimesAndHistory(ref Entity BestContactEntity, String HappyCourse)
        {
            try
            {
                // 每個組員
                String OriginalHappyHistory = this.m_ToolUtilityClass.GetEntityStringAttribute(BestContactEntity, "new_happy_history");

                if (OriginalHappyHistory.Contains(HappyCourse) != true)
                {
                    OriginalHappyHistory += HappyCourse + ",";

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "new_happy_history", OriginalHappyHistory);

                    String[] CourseCounter = OriginalHappyHistory.Split(',');

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref BestContactEntity, "new_happy_times", CourseCounter.Length - 1);

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
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void RemoveContactHappyTimesAndHistory(ref Entity BestContactEntity, String HappyCourse)
        {
            try
            {
                // 每個組員
                String OriginalHappyHistory = this.m_ToolUtilityClass.GetEntityStringAttribute(BestContactEntity, "new_happy_history");

                if (OriginalHappyHistory.Contains(HappyCourse) == true)
                {
                    OriginalHappyHistory = OriginalHappyHistory.Replace(HappyCourse + ",", "");

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "new_happy_history", OriginalHappyHistory);

                    String[] CourseCounter = OriginalHappyHistory.Split(',');

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref BestContactEntity, "new_happy_times", CourseCounter.Length - 1);
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        #region 計算幸福小組週報出席人數
        public void CalculateWeeklyReportTotalNumber(ref Entity HappyWeeklyReport)
        {
            try
            {
                #region 取得跟週報所有的相關的靈修出席紀錄單
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", HappyWeeklyReport.Id.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion
                #region 計算幸福小組週報出席及決志人數
                int TotalHappyPresent = 0;
                int TotalHappyDecision = 0;

                int BestPresentNumber = 0;  // Best 出席人數
                int BestDecisionNumber = 0; // Best 決志人數
                int WrokerNumber = 0;       // 同工出席人數

                String BestPresentList = "";    //Best出席名單
                String BestDecisionList = "";   //Best決志名單
                String WorkerList = "";         //同工出席名單

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    int HappyPresent = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_present");
                    int HappyDecision = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_decision");
                    TotalHappyPresent += HappyPresent > 0 ? HappyPresent : 0 ;
                    TotalHappyDecision += HappyDecision > 0 ? HappyDecision : 0;

                    // 取得出席紀錄單的連絡人實體
                    String Identity = "";
                    Guid aContactId = new Guid();
                    if ((aContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(PresentRecordEntity, "new_contact_new_present_record")) != Guid.Empty)
                    {
                        Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                        // 取得出席紀錄單連絡人實體的委身類型
                        Identity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));
                    }
                    else
                    {
                        continue;
                    }

                    if (Identity.Contains("幸福BEST") || Identity.Contains("未入組") || Identity.Contains("新朋友"))
                    {
                        // 
                        BestPresentNumber += HappyPresent;
                        BestDecisionNumber += HappyDecision;

                        if (HappyPresent == 1)
                            BestPresentList += this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record") + ",";
                        if (HappyDecision == 1)
                            BestDecisionList += this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record") + ",";
                    }
                    else
                    {
                        WrokerNumber += HappyPresent;
                        if (HappyPresent == 1)
                            WorkerList += this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record") + ",";

                    }
                }

                ToolUtilityClass.DeleteLastChar(ref BestPresentList);
                ToolUtilityClass.DeleteLastChar(ref BestDecisionList);
                ToolUtilityClass.DeleteLastChar(ref WorkerList);
                #endregion

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_small_group_number", TotalHappyPresent);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_decision_number", TotalHappyDecision);

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_best_attend_number", BestPresentNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_best_attend_list", BestPresentList);

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_best_decision_number", BestDecisionNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_best_decision_list", BestDecisionList);

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_worker_attend_number", WrokerNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_worker_attend_list", WorkerList);

                //this.m_ToolUtilityClass.UpdateEntity(ref HappyWeeklyReport);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion

        private String ConvertIndexToClearIdentity(int Identity)
        {
            // 取得比較易懂的委身類型
            switch (Identity)
            {
                case 100000000:
                    return "新朋友";
                case 100000004:
                    return "未入組";
                case 100000007://其實是外教會，不過我把他歸類為未入組
                    return "未入組";
                case 1:
                    return "小組組員";
                default:
                    return "小組組員";
            }
        }
        private int ConvertFollowUpWeekPickerToIndex(String FollowUpWeek)
        {
            switch (FollowUpWeek)
            {
                case "一":
                    return 100000000;
                case "二":
                    return 100000001;
                case "三":
                    return 100000002;
                case "四":
                    return 100000003;
                case "五":
                    return 100000004;
                case "六":
                    return 100000005;
                case "七":
                    return 100000006;
                case "八":
                    return 100000007;
                case "九":
                    return 100000008;
                case "十":
                    return 100000009;
                case "十一":
                    return 100000010;
                case "十二":
                    return 100000011;
                case "十三":
                    return 100000012;
                case "十四":
                    return 100000013;
                case "十五":
                    return 100000014;
                case "十六":
                    return 100000015;
                case "十七":
                    return 100000016;
                case "十八":
                    return 100000017;
                case "十九":
                    return 100000018;
                case "二十":
                    return 100000019;
                default:
                    return 100000008;
            }
        }
        private int ConvertFollowUpResultPickerToIndex(String FollowUpResult)
        {
            switch (FollowUpResult)
            {
                case "請選擇":
                    return 100000000;
                case "熱情回應":
                    return 100000001;
                case "渴慕認識信仰":
                    return 100000002;
                case "沒聯絡上":
                    return 100000003;
                case "反應冷淡":
                    return 100000004;
                case "考慮中，繼續跟進":
                    return 100000005;
                case "入小組":
                    return 100000006;
                case "來主日":
                    return 100000007;
                case "轉介":
                    return 100000008;
                case "其他":
                    return 100000009;
                default:
                    return 100000000;
            }
        }
        private int ConvertFollowUpNextStepPickerToIndex(String FollowUpNextStep)
        {
            switch (FollowUpNextStep)
            {
                case "請選擇":
                    return 100000000;
                case "繼續跟進":
                    return 100000001;
                case "轉介":
                    return 100000002;
                default:
                    return 100000000;
            }
        }
        private int ConvertFollowUpOptionToIndex(String FollowUpNextStep)
        {
            switch (FollowUpNextStep)
            {
                case "電話":
                    return 100000000;
                case "探訪":
                    return 100000001;
                case "Line/FB":
                    return 100000002;
                case "出遊/吃飯":
                    return 100000003;
                case "懷鄉/其他課程":
                    return 100000004;
                case "約談":
                    return 100000005;
                case "沒跟進":
                    return 100000006;
                case "其他":
                    return 100000007;
                default:
                    return 100000000;
            }
        }
        private int ConvertTopicToIndex(String Topic)
        {
            switch (Topic)
            {
                case "預備週":
                    return 100000000;
                case "真幸福":
                    return 100000001;
                case "真相大白":
                    return 100000002;
                case "萬世巨星":
                    return 100000003;
                case "幸福連線":
                    return 100000004;
                case "當上帝來敲門":
                    return 100000005;
                case "十字架的勝利":
                    return 100000006;
                case "釋放與自由":
                    return 100000007;
                case "幸福的教會":
                    return 100000008;
                default:
                    return 100000000;
            }
        }
        private Entity SearchPresentRecordByName(String Name, ref EntityCollection PresentRecordCollection)
        {
            //回傳與上傳姓名符合的靈修單
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                String aPresentRecordName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record");
                if (Name == aPresentRecordName)
                {
                    // 重新取得出席單，為要取得出席單所有的欄位
                    Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);

                    #region 取得出席單上面的連絡人，為要取得委身狀態
                    EntityReference aFullNameEntityReference = new EntityReference();
                    if (aRetrievedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                    {
                        aFullNameEntityReference = (EntityReference)aRetrievedPresentRecordEntity.Attributes["new_contact_new_present_record"];
                    }
                    Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
                    #endregion

                    if (m_ToolUtilityClass.GetOptionSetAttribute(aContactEntity, "customertypecode") != 100000001 && m_ToolUtilityClass.GetEntityBoolAttribute(aRetrievedPresentRecordEntity, "new_not_display") == false)
                    {
                        // 連絡人委身狀態不是"結案"，出席單的"不要顯示在回報網頁"值為="否"
                        return PresentRecordEntity;
                    }
                }
            }

            return null;
        }
        public Double GetValidMemberNumber(EntityCollection aPresentRecordCollection)
        {
            try
            {
                Double ValidMemberNumber = 0;
                #region// 處理每個個人聚會與靈修記錄
                foreach (Entity PresentRecordEntity in aPresentRecordCollection.Entities)
                {
                    // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                    String ClearIdentity = "";
                    if (this.IsValidMember(PresentRecordEntity, ref ClearIdentity) == true)
                    {
                        ValidMemberNumber++;
                    }
                }
                #endregion

                return ValidMemberNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool IsValidMember(Entity PresentRecordEntity, ref String ClearIdentity)
        {
            try
            {
                #region// 處理每個個人聚會與靈修記錄

                // 每個出席紀錄
                if (PresentRecordEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的每個出席紀錄
                        if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                        {
                            // 個人聚會與靈修記錄的組員
                            EntityReference aEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

                            // 取得連絡人姓名
                            //ContactName = aEntityReference.Name;

                            // 個人聚會與靈修記錄的組員 id
                            Guid aContactId = aEntityReference.Id;

                            // 取得組員實體
                            Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                            if (aContactEntity.Attributes.Contains("customertypecode"))
                            {
                                // 找到該組員的屬性
                                OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                                // 取得比較易懂的委身類型
                                ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);

                                // 版本轉換
                                //// 如果是新朋友、未入組、外教會則不列入累積，聖谷行道會
                                //if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                                //{
                                //    return true;
                                //}
                                //else
                                //{
                                //    return false;
                                //}

                                // 這邊每間教會都不一樣，要客製化尋巡一巡
                                // 所有的委身類型都是要列入計算統計的
                                //return true;

                                //如果是新朋友或是未入組則不列入累積，聖谷行道會
                                // 08. 幸福BEST = 100,000,005
                                // 11. 外教會 = 100,000,007
                                // 10.不穩定組員 = 100,000,008
                                // 11.新朋友 = 100,000,009
                                // 12.未入組 = 100,000,010
                                // 13.暫不入組 = 100,000,012
                                // 14.結案 = 100,000,011
                                // 新朋友和未入組是要列入分母的
                                if (aCustomerTypeCode.Value != 100000005 && aCustomerTypeCode.Value != 10000007 && aCustomerTypeCode.Value != 100000001)
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
                                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);

                                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);

                                return false;
                            }
                        }
                        else
                        { return false; }
                        #endregion
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
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private const int EMPTY_VALUE = -999999999;
        public bool IsValidContact(Entity aContactEntity)
        {
            try
            {
                #region// 處理組員是否列入計算

                // 找到該組員的屬性
                int aCustomerTypeCodeValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                // 如果是新朋友或是未入組則不列入累積，聖谷行道會
                if (aCustomerTypeCodeValue != 100000004 && aCustomerTypeCodeValue != 100000000 && aCustomerTypeCodeValue != 100000007 && aCustomerTypeCodeValue != EMPTY_VALUE)
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // 如果是新朋友或是未入組則不列入累積，聖谷行道會
                // 10.不穩定組員   =   100,000,008
                // 11.新朋友       =   100,000,009
                // 12.未入組       =   100,000,010
                // 13.暫不入組     =   100,000,012
                // 14.結案         =   100,000,011
                //if (aCustomerTypeCodeValue != 100000008 && aCustomerTypeCodeValue != 100000009 && aCustomerTypeCodeValue != 100000010 && aCustomerTypeCodeValue != 100000012 && aCustomerTypeCodeValue != 100000011 && aCustomerTypeCodeValue != EMPTY_VALUE )
                //{
                //    return true;
                //}
                //else
                //{
                //    return false;
                //}
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion

        #endregion
        #region 處理幸福小組
        private String ProcessHappyGroupMembers(ref Entity HappyGroupListEntity, Entity aWeeklyReportEntity, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                // 取得核心同工名單
                String CoreMembers = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members");

                // 客製化
                if (HappyWeekIndex == "第一週" && CoreMembers == "")
                //if (HappyWeekTopic == "第一單元 真幸福" && CoreMembers == "")
                {
                    // 第一單元 真幸福回報，而且核心同工名單是空的字串
                    #region 第一單元 真幸福設定幸福小組核心同工名單，為了要能區隔過濾出BEST名單
                    ProcessCoreMembers(ref HappyGroupListEntity, HappyWeekIndex, HappyWeekTopic);

                    return "";
                    #endregion
                }
                else
                {
                    // 第二週以後
                    #region 設定幸福小組Best名單
                    // 找到所有成員，減掉核心同工，就是Best名單
                    String BestList = ExtractBestList(ref HappyGroupListEntity);
                    #region 設定幸福小組 Best 名單
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_best_name_list", BestList);


                    #endregion

                    return BestList;

                    #endregion
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void ProcessCoreMembers(ref Entity HappyGroupListEntity, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                #region 設定幸福小組核心同工名單，為了要能區隔過濾出BEST名單
                if (HappyWeekTopic != "")
                {
                    String CoreMembers = this.GetCoreMembers(HappyGroupListEntity.Id, HappyWeekIndex, HappyWeekTopic);
                    if (CoreMembers != "")
                    {
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members", CoreMembers);
                    }
                }
                this.m_ToolUtilityClass.UpdateEntity(ref HappyGroupListEntity);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private String GetCoreMembers(Guid ListEntityId, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                // 客製化
                if (HappyWeekIndex == "第一週")
                //if (HappyWeekTopic == "第一單元 真幸福")
                {
                    bool ListType = false;
                    EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(ListEntityId, ref ListType);

                    String CoreMembers = "";
                    foreach (Entity MemberEntity in MemberCollection.Entities)
                    {
                        // 每個組員
                        Entity aContactEntity;

                        if (ListType == false)
                        {
                            // 靜態名單
                            aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                        }
                        else
                        {
                            // 動態名單
                            aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                        }

                        // 取得出席紀錄單連絡人實體的委身類型
                        String Identity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));

                        if (!Identity.Contains("幸福BEST") && !Identity.Contains("未入組") && !Identity.Contains("新朋友"))
                        {
                            CoreMembers += this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname") + ",";
                        }
                    }

                    ToolUtilityClass.DeleteLastChar(ref CoreMembers);

                    return CoreMembers;
                }
                else
                {
                    return "";
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private String ExtractBestList(ref Entity HappyGroupListEntity)
        {
            try
            {
                #region 設定幸福小組Best名單
                // 取得核心同工名單
                String CoreMembers = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members");
                if (CoreMembers != "")
                {
                    bool ListType = false;
                    EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(HappyGroupListEntity.Id, ref ListType);

                    String BestList = ""; // 幸福小組Best名單
                    foreach (Entity MemberEntity in MemberCollection.Entities)
                    {
                        // 每個組員
                        Entity aContactEntity;

                        if (ListType == false)
                        {
                            // 靜態名單
                            aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                        }
                        else
                        {
                            // 動態名單
                            aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                        }

                        // 成員姓名
                        String aContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname");

                        if (CoreMembers.Contains(aContactFullName) != true)
                        {
                            BestList += aContactFullName + ",";
                        }
                    }

                    ToolUtilityClass.DeleteLastChar(ref BestList);

                    //if( aHappyGroupWeeklyReportToBeAdded.HappyGroupWeeklyReportId != null && aHappyGroupWeeklyReportToBeAdded.HappyGroupWeeklyReportId != "" )
                    //{
                    //    Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(aHappyGroupWeeklyReportToBeAdded.HappyGroupWeeklyReportId));

                    //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_core_members", BestList);
                    //}

                    return BestList;
                }
                else
                {
                    return "";
                }

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        #endregion
        #endregion
        #region 指派小組
        public String AssignNewSmallGroup(Entity aPresentRecordEntity, String AssignedSmallGroupName, Entity aActiveListEntity)
        {
            try
            {
                // 取得被指派會友紀錄
                Entity aAssignedContact = GetAssignedContact(aPresentRecordEntity);

                // 取得要轉移的小組
                Entity aAssingedSmallGroupEntity = this.m_ToolUtilityClass.RetrieveListEntityByName(AssignedSmallGroupName);

                AssignContactToList(AssignedSmallGroupName, aAssignedContact, aActiveListEntity, aAssingedSmallGroupEntity);

                if (AssignedSmallGroupName.Contains("關懷") != true)
                {
                    // 如果被指派的不是"關懷"小組，才要停止顯示
                    // 設定個人聚會與靈修記錄"停止提醒"為"是"
                    SetNotRemindFlag(aAssignedContact);
                }

                // 原來小組的出席紀錄單要被刪除
                //if (aPresentRecordEntity != null)
                //{
                //    this.m_ToolUtilityClass.DeleteEntity("new_present_record", aPresentRecordEntity.Id);
                //}

                // 原來小組的出席紀錄單不要再出現在前台網頁
                if (aPresentRecordEntity != null)
                {
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);
                }
                return "指派小組";
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }

        }
        public Entity GetAssignedContact(Entity aPresentRecordEntity)
        {
            try
            {
                // 取得出席紀錄單姓名的LookUp 的Guid
                Guid AssignedContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecordEntity, "new_contact_new_present_record");

                // 回傳被指派會友紀錄
                return this.m_ToolUtilityClass.RetrieveEntity("contact", AssignedContactId);
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        public String AssignContactToList(String AssignedSmallGroupName, Entity aAssignedContact, Entity aActiveListEntity, Entity aAssignedListEntity)
        {
            try
            {
                #region 要加的新人是在"新人關懷"小組裡
                var aContactToBeSentToDynamics = new Entity("contact", aAssignedContact.Id);

                // 將剛剛新增的聯絡人加入至成員名單
                ConnectNewContactInMemberList(aContactToBeSentToDynamics.Id, AssignedSmallGroupName, aAssignedListEntity);

                // 將連絡人從舊的名單中移除
                try
                {
                    m_ToolUtilityClass.RemoveMembersToMarketingList(aActiveListEntity.Id, aContactToBeSentToDynamics.Id);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }

                if (aAssignedListEntity != null)
                {
                    if (AssignedSmallGroupName.Contains("關懷") != true)
                    {
                        // 有找到被關聯的小組名單，建立出席紀錄單
                        CreateAssignedContactPresentRecord(aAssignedListEntity, aContactToBeSentToDynamics.Id, AssignedSmallGroupName);
                    }
                    else
                    {
                        // 由於是指派到關懷小組，所以就不產生關懷小組的出席紀錄單
                        // 關懷小組的出席單會由工作流程產生有截止日期的N個出席單
                    }
                }
                #region 關聯主要小組
                //Entity aExistedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactToBeSentToDynamics.Id);
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aContactToBeSentToDynamics, "new_cell_list_contact", "list", aAssignedListEntity.Id);
                #endregion

                #region 更新新建立的連絡人// 設定被指派會友，被指派小組的日期
                //this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactToBeSentToDynamics, "new_assigne_group_date", DateTime.Now);
                #endregion

                #region 更新新建立的連絡人
                //aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                this.m_ToolUtilityClass.UpdateEntity(ref aContactToBeSentToDynamics);
                #endregion

                String LoginContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname");
                String ExistContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname");

                // 指派新增連絡人的負責人
                // 領袖的負責人 Id
                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);
                if (m_OwnerId != Guid.Empty)
                {
                    this.m_ToolUtilityClass.AssignOwner("contact", aAssignedContact, this.m_OwnerId);
                }

                //String AddedGroupName = m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");
                //String DestinyGroupName = m_ToolUtilityClass.GetEntityStringAttribute(aExistList, "listname");

                String Result = LoginContactFullName + " 成功的加入 " + ExistContactFullName + " 到 " + AssignedSmallGroupName + "小組中";

                // LINE通知心的小組的權柄
                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aAssignedListEntity, "指派");
                this.m_LineNotifyUtility.SendListMemberLine(aAssignedListEntity, "指派");

                // LINE通知原來小組的權柄
                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aActiveListEntity, "指派");
                this.m_LineNotifyUtility.SendListMemberLine(aActiveListEntity, "指派");

                return Result;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void CreateAssignedContactPresentRecord(Entity aListEntity, Guid NewContactEntityId, String GroupName)
        {
            try
            {
                #region 建立個人聚會與靈修記錄

                if (aListEntity == null || NewContactEntityId == null || NewContactEntityId == Guid.Empty)
                { return; }

                //// 取得每個需要點名的名單裡的每個週報
                //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", aListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");
                //if (GroupWeeklyReportEntityCollection == null || GroupWeeklyReportEntityCollection.Entities.Count <= 0)
                //{ return; }

                // 根據日期看有沒有那個週報
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)DateTime.Now.DayOfWeek;

                // 當週的星期日為認定的主日
                //this.m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                //DateTime aSunday;
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

                //Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);
                //if (GroupWeeklyReportEntity == null)
                //{ return; }

                // 尋找此小組的某一個主日的週報集合
                EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(this.m_Sunday, aListEntity.Id);

                // 此小組的某一個主日的週報集合，應該僅有一個，也就是第0個的週報
                //Entity GroupWeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities.Count == 1 ? GroupWeeklyReportEntityCollection.Entities[0] : null;
                if (GroupWeeklyReportEntityCollection.Entities.Count == 1)
                {
                    EntityCollection PresentRecordEntityCollection = m_ToolUtilityClass.QueryPresentRecordInWeeklyReportByContactId(NewContactEntityId, GroupWeeklyReportEntityCollection.Entities[0].Id);

                    if (PresentRecordEntityCollection.Entities.Count == 0)
                    {
                        // 還沒有出席紀錄單才要建立
                        // 這是新建立的個人聚會與靈修記錄
                        Entity aPresentRecord = new Entity("new_present_record");

                        // 設定個人聚會與靈修記錄相關屬性
                        #region 準備所需要的參數

                        #region 個人資料
                        if (NewContactEntityId == null || NewContactEntityId == Guid.Empty)
                        { return; }
                        Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                        if (aNewContactEntity == null)
                        { return; }
                        Guid aWeeklyReportId = GroupWeeklyReportEntityCollection.Entities[0].Id;

                        // 組員的全名
                        String FullName = "";
                        if (aNewContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)aNewContactEntity.Attributes["fullname"];
                        }
                        // 組員的手機
                        String aMobilePhone = "";
                        if (aNewContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)aNewContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (aNewContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)aNewContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (aNewContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)aNewContactEntity.Attributes["address2_line1"];
                        }
                        #endregion

                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "";
                        String aNewComerNote = GetNewComerFollowupInfo(NewContactEntityId, ref aFollowUpWeek);

                        MemberInfomation aMemberInfomation = new MemberInfomation()
                        {
                            Group = GroupName,
                            Name = FullName,
                            Phone = DigitsOnly.Replace(aMobilePhone, ""),
                            HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                            Address = aAddress,
                            Note = "",
                            Date = "2015/10/6",
                            Number = 5,
                            SundayPresent = false,
                            SmallGroupPresent = false,

                            PrayNumber = 0,
                            SpiritNumber = 0,
                            FamilyNumber = 0,
                            WorkAndCampusNumber = 0,
                            ShepherdStatus = "",
                            OneOnOne = "",
                            Training = "",
                            Incubate = "",
                            FollowUpWeek = ".",
                            FollowUpResult = ".",
                            FollowUpNextStep = ".",
                            FollowUp = "",
                            FollowUpNote = "",
                            NewComerNote = aNewComerNote,
                            #region 靈修、晨、晚禱
                            SpiritualWork = 0,
                            MorningPray = 0,
                            GeneralCare = 0,
                            #endregion

                        };

                        Double DUM_DOUBLE = 0;
                        int DUM_INT = 0;
                        #endregion

                        this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aNewContactEntity, ref aListEntity, ref aWeeklyReportId, DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_INT, ref DUM_INT, ref DUM_INT, ref DUM_INT);

                        // 新增個人聚會與靈修記錄
                        Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                        Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                        //指派負責人
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId(aNewContactEntity));

                    }
                }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void ConnectNewContactInMemberList(Guid NewContactEntityId, String GroupName, Entity aListEntity)
        {
            try
            {
                if (aListEntity != null)
                {
                    #region 有找到被關聯的小組名單
                    bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "type");

                    if (ListType == false)
                    {
                        // 靜態名單
                        List<Guid> memberGuidList = new List<Guid>();
                        memberGuidList.Add(NewContactEntityId);
                        m_ToolUtilityClass.AddMembersToMarketingList(aListEntity.Id, memberGuidList);
                    }
                    else
                    {
                        // 動態名單
                        Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                        EntityReference aListEntityReference = new EntityReference("list", aListEntity.Id);

                        // 聖谷行道會
                        //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_cell_list_contact", ref aListEntityReference);
                        // 聖谷行道會
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_list_contact", ref aListEntityReference);

                        this.m_ToolUtilityClass.UpdateEntity(ref aNewContactEntity);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, MemberInfomation aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber)
        {
            try
            {
                //設定名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname") + "-" + m_Sunday.ToShortDateString() + " 出席紀錄");

                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 關聯週報 Lookup
                if (aWeeklyReportId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId); }
                #endregion
                #region 從名單取得 小家長 ID、小組長 ID、小家長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                #endregion
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯族系組長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (aListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", this.m_Sunday);
                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);
                #endregion
                #region 取得比較易懂的委身類型
                // 找到該組員的屬性
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                // 取得比較易懂的委身類型
                //String ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);
                #endregion
                #region 設定主日出席
                if (aMemberInfomation.SundayPresent == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                    aWeeklySundayNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                }
                #endregion
                #region 設定主日出席率
                if (aMemberInfomation.SundayPresent == true)
                {
                    if (ValidNumber != 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            ValidSundayMemberNumber++;
                            aWeeklySundayRate += 1 / ValidNumber;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                }
                #endregion
                #region 設定小組出席
                if (aMemberInfomation.SmallGroupPresent == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);

                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                }
                #endregion
                #region 設定小組出席率
                if (aMemberInfomation.SmallGroupPresent == true)
                {
                    if (ValidNumber != 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            ValidSmallGroupMemberNumber++;
                            aWeeklySmallGroupRate += 1 / ValidNumber; ;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                }
                #endregion
                #region 設定附註或是代禱事項

                // 聖谷行道會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 聖谷行道會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region// 新人跟進

                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.FollowUpNote);

                //AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);

                #endregion
                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
        #region 結案處理
        public String TerminateNewPersonFollowUp(Entity aPresentRecordEntity, String AssignedSmallGroupName, Entity aActiveListEntity)
        {
            try
            {
                // 取得被指派會友紀錄
                Entity aAssignedContact = GetAssignedContact(aPresentRecordEntity);

                // 設定個人聚會與靈修記錄"停止提醒"為"是"
                SetNotRemindFlag(aAssignedContact);

                #region 設定主要小組為空白
                this.m_ToolUtilityClass.SetEntityLookUpToNull(ref aAssignedContact, "new_cell_list_contact");
                #endregion

                // 設定結案日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAssignedContact, "new_closed_date", DateTime.Now);

                // 設定委身類型=>結案
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAssignedContact, "customertypecode", 100000001);

                // 更新聯絡人
                this.m_ToolUtilityClass.UpdateEntity(ref aAssignedContact);

                // 取得目前要被轉移的小組(目前所在的小組)
                //Entity aActiveListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ActiveListId));

                // 將連絡人從舊的名單中移除
                try
                {
                    m_ToolUtilityClass.RemoveMembersToMarketingList(aActiveListEntity.Id, aAssignedContact.Id);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }

                // 原來小組的出席紀錄單不要再出現在前台網頁
                if (aPresentRecordEntity != null)
                {
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);
                }

                String Result = this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname") + "從" + this.m_ToolUtilityClass.GetEntityStringAttribute(aActiveListEntity, "listname") + " 被結案了";
                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aActiveListEntity);

                return "指派小組";
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }

        }
        #endregion
        #region 設定個人聚會與靈修記錄"停止提醒"為"是"
        public void SetNotRemindFlag(Entity aAssignedContact)
        {
            try
            {
                // 取得與聯絡人相關的出席紀錄單並且有"關懷期限"
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndContainEpiredDate(this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname"), aAssignedContact.Id.ToString());

                if (aPresentRecordCollection.Entities.Count > 0)
                {
                    #region// 有找到個人聚會與靈修記錄
                    foreach (Entity aPresentRecord in aPresentRecordCollection.Entities)
                    {
                        // 取得個人聚會與靈修記錄
                        Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);

                        //設定個人聚會與靈修記錄"停止提醒"為"是"
                        m_ToolUtilityClass.SetEntityBoolAttribute(ref aRetrievedPresentRecord, "new_stop_notify", true);

                        //設定個人聚會與靈修記錄"不要顯示在回報網頁"為"是"
                        m_ToolUtilityClass.SetEntityBoolAttribute(ref aRetrievedPresentRecord, "new_not_display", true);

                        // 更新個人聚會與靈修記錄
                        this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);
                    }
                    #endregion
                }
                else
                {
                    #region// 沒找到個人聚會與靈修記錄
                    #endregion
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        #endregion
        #region 有關懷到，停止關懷期限到期工作流程
        public void TerminateNewPersonCareWorkflow(Entity aPresentRecordEntity, String AssignedSmallGroupName)
        {
            try
            {
                //this.m_ContactEntity = aLoginContact;

                //設定個人聚會與靈修記錄"停止提醒"為"是"
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_stop_notify", true);

                //設定個人聚會與靈修記錄"不要顯示在回報網頁"為"是"
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);

                // 更新個人聚會與靈修記錄
                this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecordEntity);

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }

        }
        #endregion
        #region 設定委身類型
        public bool SetIdentityByUpload( ref Entity aContact, ref Member aMember)
        {
            try
            {
                // 先找到系統的委身類型指標
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");

                //// 在轉換回報的委身類型指標 
                int CustomerTypeCode = ConvertIdentityToIndex(aMember.Status);

                if (aIdentity > 0)
                {
                    if (aIdentity != CustomerTypeCode)
                    {
                        // 委身類型系統原來的旱小組長上傳的有不一致
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", CustomerTypeCode);

                        return true;
                    }
                    else
                    {
                        // 委身類型系統原來的和小組長上傳的一致
                        return false;
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool SetIdentity(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                // 先找到委身類型
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");

                // 轉換委身類型的中文名稱
                String aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
                {
                    // 如果委身型態是"未入組"或是"新朋友"
                    // 先搜尋過去2個月的靈修出席紀錄
                    // 如果主日次數+小組次數 大於等於 8 次，則委身類型設定為"小組組員"
                    if (PassOrFail(aListEntityId, ref aContact) == true)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);

                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (aIdentityType == "06. 小組組員")
                {
                    // 如果主日次數+小組次數 小於 8 次，則委身類型設定為"未入組"
                    if (PassOrFail(aListEntityId, ref aContact) == false)
                    {
                        // 被MARK掉了，就表示不會降階
                        //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);

                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else { return false; }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetIdentity_BACK(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                // 先找到委身類型
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");

                // 轉換委身類型的中文名稱
                String aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
                {
                    // 如果委身型態是"未入組"或是"新朋友"
                    // 先搜尋過去2個月的靈修出席紀錄
                    // 如果主日次數+小組次數 大於等於 8 次，則委身類型設定為"小組組員"
                    if (PassOrFail(aListEntityId, ref aContact) == true)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);
                        // 更新連絡人
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else if (aIdentityType == "06. 小組組員")
                {
                    // 如果主日次數+小組次數 小於 8 次，則委身類型設定為"未入組"
                    if (PassOrFail(aListEntityId, ref aContact) == false)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        // 更新連絡人
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            // 被MARK掉了，就表示不會降階
                            //this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else { }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetIdentity(Guid aListEntityId, ref Entity aContact, ref MemberInfomation aMemberInfomation)
        {
            try
            {
                // 先找到委身類型
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");

                String aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
                {
                    // 如果委身型態是"未入組"或是"新朋友"
                    // 先搜尋過去2個月的靈修出席紀錄
                    // 如果主日次數+小組次數 大於等於 8 次，則委身類型設定為"小組組員"
                    if (PassOrFail(aListEntityId, ref aContact) == true)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);
                        // 更新連絡人
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else if (aIdentityType == "06. 小組組員")
                {
                    // 如果主日次數+小組次數 小於 8 次，則委身類型設定為"未入組"
                    if (PassOrFail(aListEntityId, ref aContact) == false)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        // 更新連絡人
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            // 被MARK掉了，就表示不會降階
                            //this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else { }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int GetPresentNumber(Guid WeeklyReportId, String Type, ref Entity aContact)
        {
            try
            {
                // 過去幾週的靈修出席紀錄
                EntityCollection PresentRecordCollection = this.m_ToolUtilityClass.QueryPresentRecordByContactIdAndSunday(WeeklyReportId, aContact.Id, WEEK_PERIOD);

                int TotalNumber = 0;

                if (Type == "主日")
                {
                    foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                    {
                        // 主日次數
                        int Number = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_sunday_present_this_week");
                        if (Number >= 0)
                            TotalNumber += Number;
                    }

                    return TotalNumber;

                }
                else if (Type == "小組")
                {
                    foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                    {
                        // 小組次數
                        int Number = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_group_present_this_week");
                        if (Number >= 0)
                            TotalNumber += Number;
                    }

                    return TotalNumber;


                }

                return TotalNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool PassOrFail(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                int TotalNumber = GetPresentNumber(aListEntityId, "小組", ref aContact);

                // 如果主日次數+小組次數 大於等於 MINIMUM_THRESHOLD 次，則委身類型設定為"小組組員"
                if (TotalNumber >= MINIMUM_THRESHOLD)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        // 聖谷行道會
        // 委身類型客製化
        private int ConvertIdentityToIndex(String Identity)
        {
            switch (Identity)
            {
                case "牧師師母":
                    return 100000006;
                case "01. 牧師師母":
                    return 100000006;
                case "區牧":
                    return 100000002;
                case "02. 區牧":
                    return 100000002;
                case "小區長":
                    return 100000003;
                case "03. 小區長":
                    return 100000003;
                case "小組長":
                    return 100000008;
                case "04. 小組長":
                    return 100000008;
                case "副小組長":
                    return 100000009;
                case "05. 副小組長":
                    return 100000009;
                case "核心同工":
                    return 100000012;
                case "06. 核心同工":
                    return 100000012;
                case "小組組員":
                    return 1;
                case "07. 小組組員":
                    return 1;
                case "幸福BEST":
                    return 100000005;
                case "08. 幸福BEST":
                    return 100000005;
                case "未入組":
                    return 100000004;
                case "09. 未入組":
                    return 100000004;
                case "新朋友":
                    return 100000000;
                case "10. 新朋友":
                    return 100000000;
                case "外教會":
                    return 100000007;
                case "11. 外教會":
                    return 100000007;
                case "結案":
                    return 100000001;
                case "12. 結案":
                    return 100000001;
                default:
                    return 100000000;
            }
        }
        // 聖谷行道會
        // 委身類型客製化
        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000006:
                    return "01. 牧師師母";
                case 100000002:
                    return "02. 區牧";
                case 100000003:
                    return "03. 小區長";
                case 100000008:
                    return "04. 小組長";
                case 100000009:
                    return "05. 副小組長";
                case 100000012:
                    return "06. 核心同工";
                case 1:
                    return "05. 小組組員";
                case 100000005:
                    return "06. 幸福BEST";
                case 100000004:
                    return "07. 未入組";
                case 100000000:
                    return "08. 新朋友";
                case 100000007:
                    return "09. 外教會";
                case 100000001:
                    return "10. 結案";
                default:
                    return ".";
            }
        }
        #endregion
        #region 設定受洗狀態
        public bool SetSpiritualIdentityByUpload(ref Entity aContact, ref Member aMember)
        {
            try
            {
                // 先找到系統的受洗狀態指標
                int aSpiritualIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_spiriitual_identity");

                if (aSpiritualIdentity > 0)
                {
                    //// 在轉換回報的受洗狀態指標 SpiritualIdentity
                    int SpiritualIdentityCode = ConvertSpiritualIdentityToIndex(aMember.SpiritualIdentity);

                    if (aSpiritualIdentity != SpiritualIdentityCode)
                    {
                        // 受洗狀態系統原來的與小組長上傳的有不一致
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "new_spiriitual_identity", SpiritualIdentityCode);

                        return true;
                    }
                    else
                    {
                        // 委身類型系統原來的和小組長上傳的一致
                        return false;
                    }
                }
                else
                {
                    // 系統的受洗狀態指標值小於0
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private int ConvertSpiritualIdentityToIndex(String SpiritualIdentity)
        {
            switch (SpiritualIdentity)
            {
                case "-未知-":
                    return 100000004;
                case "基督徒":
                    return 100000001;
                case "已決志":
                    return 100000002;
                case "慕道友":
                    return 100000005;
                case "未信主":
                    return 100000003;
                default:
                    return 100000004;
            }
        }
        #endregion

        #region 設定組員的洗禮狀態(長老教會專用)
        public bool SetBaptizedSituationByUpload(ref Entity aContact, ref Member aMember)
        {
            try
            {
                // 先找到系統的組員的洗禮狀態(長老教會專用)指標
                int aBaptizedSituation = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_baptized_situation");

                //// 在轉換回報的受洗狀態指標 BaptizedSituation 
                int aBaptizedSituationCode = ConvertBaptizedSituationToIndex(aMember.BaptizedSituation);

                if (aMember.BaptizedSituation != "")
                {
                    if (aBaptizedSituation != aBaptizedSituationCode)
                    {
                        // 洗禮狀態系統原來的與小組長上傳的有不一致
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "new_baptized_situation", aBaptizedSituationCode);

                        return true;
                    }
                    else
                    {
                        // 洗禮狀態系統原來的和小組長上傳的一致
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private int ConvertBaptizedSituationToIndex(String BaptizedSituation)
        {
            switch (BaptizedSituation)
            {
                case "堅信禮(籍在)":
                    return 100000000;
                case "成人禮(籍在)":
                    return 100000001;
                case "轉籍(籍在)":
                    return 100000002;
                case "小兒禮(籍不在)":
                    return 100000003;
                case "未受洗(籍不在)":
                    return 100000004;
                default:
                    return -999999999;
            }
        }
        #endregion

        #region 字典處理函式庫
        private void ResetDictionary(DateTime aSunday)
        {
            try
            {
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", "主日日期:" + aSunday.ToLocalTime().ToShortDateString() +  Environment.NewLine + "---------------------------------" + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", "小組出席統計:");
                //AddToDictionary(ref this.m_FeedBackReport, "跟進統計表頭", "跟進統計報告" + Environment.NewLine);

                //AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", "");
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", "");
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", "");
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", "");
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", "");
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "");
                //AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "未入組跟進" + Environment.NewLine);
                //AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "新朋友跟進" + Environment.NewLine);

                AddToDictionary(ref this.m_FeedBackReport, "主日統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進", "");

            }
            catch (FormatException)
            {
                return;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private void AddToDictionaryByIdentity(Guid aListEntityId, String Type, ref String Identity, ref Entity aContact, bool Presentflag)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                switch (Identity)
                {
                    case "小組組員":
                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }

                        return;
                    case "未入組":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }

                        return;
                    case "新朋友":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) " + Environment.NewLine);
                            }
                        }

                        return;
                    default:
                        return;
                }
            }
            catch (FormatException)
            {
                return;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private void AddToDictionaryFollowByIdentity(ref String Identity, ref Entity aContact, Member aMemberInfomation)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                String PersonalFollowUp = SetFollowUpString(ref aMemberInfomation);

                String FollowUp = "";
                if (PersonalFollowUp != "")
                {
                    FollowUp = "\t\t" + ContactName + Environment.NewLine + PersonalFollowUp + Environment.NewLine;
                }
                else
                {
                    FollowUp = "\t\t" + ContactName + " : 沒有跟進活動" + Environment.NewLine;
                }

                switch (Identity)
                {
                    case "未入組":
                        AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", FollowUp);

                        return;
                    case "新朋友":

                        AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", FollowUp);

                        return;
                    default:
                        return;
                }
            }
            catch (FormatException)
            {
                return;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private String SetFollowUpString(ref Member aMemberInfomation)
        {
            try
            {
                return
                    AppendHeadString("\t\t\t跟進週次:", aMemberInfomation.FollowUpWeek) +
                    AppendHeadString("\t\t\t跟進方式:", aMemberInfomation.FollowUpOption) +
                    AppendHeadString("\t\t\t跟進結果:", aMemberInfomation.FollowUpResult) +
                    AppendHeadString("\t\t\t下一步驟:", aMemberInfomation.FollowUpNextStep) +
                    AppendHeadString("\t\t\t跟進摘要:", aMemberInfomation.FollowUpNote)
                    ;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private String AppendHeadString(String HeadString, String BodyString)
        {
            try
            {
                if (BodyString != "" && BodyString != "." && BodyString != "請選擇")
                {
                    return HeadString + BodyString + Environment.NewLine;
                }
                else
                {
                    return "";
                }
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private bool AddToDictionary(ref Dictionary<String, String> aDictionary, String Method, String Content)
        {
            try
            {
                if (aDictionary.ContainsKey(Method))
                {
                    // 關鍵( Key ) 已經在字典裡了
                    aDictionary[Method] += Content;
                    return true;
                }
                else
                {
                    // 關鍵( Key )還沒有在字典裡
                    aDictionary.Add(Method, Content);
                    return false;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String Method)
        {
            try
            {
                return aDictionary[Method];
            }
            catch (FormatException)
            {
                return "";
            }
            catch (System.Exception e)
            {
                return "";
            }
        }
        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String HeadString, String Method)
        {
            try
            {
                if (aDictionary[Method] != "")
                {
                    return HeadString + aDictionary[Method] + Environment.NewLine;
                }
                else
                {
                    return "";
                }
            }
            catch (FormatException)
            {
                return "";
            }
            catch (System.Exception e)
            {
                return "";
            }
        }
        #endregion
    }
}
