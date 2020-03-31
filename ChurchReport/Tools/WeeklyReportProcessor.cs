

using System;
using System.Runtime.Serialization;
using Microsoft.Win32;
using System.Collections.Generic;
using TraceNameSpace;

#region CRM 2011 reference
using Microsoft.Xrm.Sdk;
using ToolUtility;
using System.Collections;
using System.Globalization;
using System.IO;
#endregion

// 編譯後的執行指令
//"D:\音訊科技專案\CRM2011\PunchClockPlugIn\bin\Debug\IIS_Async_Reset and Copy - 直接.bat"
// $(SolutionDir)packages\ilmerge.2.14.1208\tools\ILMerge.exe  /keyfile:"D:\音訊科技專案\音訊科技金鑰\SpeechMessageCrmKey.snk"  /target:"library" /copyattrs /out:$(TargetDir)$(TargetName)$(TargetExt)  $(ProjectDir)$(IntermediateOutputPath)$(TargetName)$(TargetExt) $(ProjectDir)$(OutputPath)LineUtility.dll $(ProjectDir)$(OutputPath)ToolUtilityDynamics365.dll $(ProjectDir)$(OutputPath)YangMeillcStorLessonsPlugIn.dll $(SolutionDir)packages\RestSharp.105.2.3\lib\net45\RestSharp.dll
//測試

using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;

namespace ChurchReport.Tools
{
    public class WeeklyReportProcessor
    {
        #region 資料區
        #region 系統參數
        IOrganizationServiceFactory m_ServiceFactory;
        ITracingService m_TracingService;

        // Obtain the execution context from the service provider.
        //IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

        IServiceProvider m_ServiceProvider;
        IPluginExecutionContext m_Context;

        // Use the context service to create an instance of IOrganizationService.
        IOrganizationService m_CrmService;

        String m_OrganizationName = "";
        //</snippetFollowupPlugin4>
        #endregion
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass;
        LineUtilityClass m_LineUtilityClass;

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

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

        DateTime m_Sunday;
        String m_LoginType = "";
        String m_GroupType = "";
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        Entity m_ListEntity; // 小組名單實體紀錄
        Entity m_WeeklyReportEntity; // 週報實體紀錄
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長/區長
        Guid m_ShepherdLeaderId; // 區牧
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        Guid m_OwnerId; // 小組長的負責人 Id

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
                                                                       //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //private const String SET_IDENTITY_METHOD = "透過過去8週出席次數"; // 設定委身類型的方式
        private const String SET_IDENTITY_METHOD = "透過回報網頁手動設定"; // 設定委身類型的方式

        #endregion
        #region 初始值設定
        public WeeklyReportProcessor(IOrganizationService aCrmService, IPluginExecutionContext aContext, ToolUtilityClass aToolUtilityClass, LineUtilityClass aLineUtilityClass)
        {
            m_CrmService = aCrmService;
            this.m_Context = aContext;

            m_ToolUtilityClass = aToolUtilityClass;

            m_LineUtilityClass = aLineUtilityClass;

            m_OrganizationName = aContext.OrganizationName;
        }
        #endregion
        #region 下載資料區
        #region 主程式區
        public void GetListManager(String Account, String Password, DateTime aDownloadDate, ref String LoginFullName, ref Dictionary<String, String> WeeklyReportDictionary)
        {
            try
            {
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)aDownloadDate.DayOfWeek;
                this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
                #endregion

                #region 找登入使用者及其ID
                FindLoginUser(Account, Password); // 也就是設定 this.m_ContactEntity
                if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
                { return; } // 沒找到就回傳 null 
                else
                {
                    // 取得登入者的姓名
                    LoginFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");
                }
                #endregion

                #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單
                FindListCollection();
                #endregion

                #region 處理小組名單
                if (m_Lists.Entities.Count != 0)
                {
                    #region// 有找到要點名的名單，所以是小組長以上回報
                    #region 處理每個要點名的名單
                    ProcessListEntity(ref WeeklyReportDictionary);
                    #endregion

                    return;
                    #endregion
                }
                else
                {
                    #region// 沒找到任何要點名的名單，所以是個人回報，
                    return;
                    #endregion
                }
                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }


        #endregion
        #region 處理小組名單
        private void ProcessListEntity(ref Dictionary<String, String> WeeklyReportDictionary)
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday( this.m_Sunday, "list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    // 尋找此小組的某一個主日的週報集合
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(this.m_Sunday, ListEntity.Id);

                    // 此小組的某一個主日的週報集合，應該僅有一個，也就是第0個的週報
                    Entity GroupWeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities.Count == 1 ? GroupWeeklyReportEntityCollection.Entities[0] : null;

                    //依據找到的週報有還是沒有來決定下一步:  
                    //      有: 建立GroupName及WeeklyReportId
                    //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                    //String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                    // 取得週報資料
                    // 小組長的負責人 Id
                    m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                    SetupWeeklyReportRecord(ref WeeklyReportDictionary, ListEntity, GroupWeeklyReportEntity);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public void SetupWeeklyReportRecord(ref Dictionary<String, String> WeeklyReportDictionary, Entity ListEntity, Entity GroupWeeklyReportEntity)
        {
            try
            {
                if (GroupWeeklyReportEntity != null)
                {
                    // 已經有週報
                    Entity RetrievedWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", GroupWeeklyReportEntity.Id);
                    String GroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref RetrievedWeeklyReport, "new_list_group_present_weekly_report");
                    AddToDictionary(ref WeeklyReportDictionary, GroupName, GroupWeeklyReportEntity.Id.ToString());
                    //WeeklyReportList.Add(GroupWeeklyReportEntity.Id.ToString());
                }
                else
                {
                    // 還沒有週報，要建立週報
                    CreateWeeklyReport(ref ListEntity, ref WeeklyReportDictionary);
                }

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

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
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ListEntityId);
                }
                else
                {
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ListEntityId);
                }
                else
                {
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            #endregion

            return MemberCollection.Entities.Count;

            #endregion
        }

        #endregion
        #region 處理個人回報
        private void FilterPersonalListEntity()
        {
            try
            {
                // 處理每個點名名單
                EntityCollection aLocalEntityCollection = new EntityCollection();
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    if (m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "new_app_named") == true)
                    {
                        aLocalEntityCollection.Entities.Add(ListEntity);
                    }
                }
                this.m_Lists.Entities.Clear();

                if (aLocalEntityCollection.Entities.Count > 0)
                {
                    this.m_Lists.Entities.Add(aLocalEntityCollection.Entities[0]);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 副程式呼叫
        private void FindLoginUser(String Account, String Password)
        {
            // 找登入使用者及其ID
            if (Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }
        private void FindListCollection()
        {
            try
            {
                // 初始化 m_Lists
                // 共同組長 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 共同組長
                //this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 共同組長
                //MergeCollectionSmallGroupAhead(ref this.m_Lists);
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 共同組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 小組長/副組長 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/副組長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/副組長
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/副組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同區長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同區長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同區長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 上代組長 new_contact_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");  // 上代組長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_race_leager_list");  // 上代組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 族系族長 new_contact_list_arealeader
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_arealeader", "list");  // 族系族長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_arealeader");  // 族系族長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void MergeCollectionSmallGroupAhead(ref EntityCollection aListEntityCollection)
        {
            try
            {
                // 族系族長或是區長的名單若是與小組長名單重疊，則要過濾出僅有族長/區長的名單
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                // 一個一個處理族系名單
                foreach (Entity aListEntity in aListEntityCollection.Entities)
                {
                    // 處理每一個要被確認是否已在m_Lists之中的名單
                    bool SearchedFlag = false;
                    foreach (Entity m_ListEntity in this.m_Lists.Entities)
                    {
                        // 比對每一個小組名單
                        if (aListEntity.Id == m_ListEntity.Id)
                        {
                            // 族系族長的名單與小組長的名單有相同的了
                            SearchedFlag = true;
                            break;
                        }
                    }

                    if (SearchedFlag == false)
                    {
                        // 族系族長的名單沒有與小組長名單相同的
                        if (this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "new_app_named") == true)
                        {
                            // 點名有打勾
                            DateTime aHappyStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aListEntity, "new_happy_start_date").ToLocalTime();
                            DateTime aHappyEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aListEntity, "new_happy_end_date").ToLocalTime();

                            if (aHappyStartDate.Year != 1000)
                            {
                                // 小組開始日期有填
                                if (aHappyEndDate.Year != 1000)
                                {
                                    // 小組開始日期有填，小組結束日期有填
                                    if (DateTime.Now >= aHappyStartDate && DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比小組開始日期還晚 ，比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 小組開始日期有填，小組結束日期沒填
                                    if (DateTime.Now >= aHappyStartDate)
                                    {
                                        // 現在比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                            }
                            else
                            {
                                // 小組開始日期沒填
                                if (aHappyEndDate.Year != 1000)
                                {
                                    // 小組開始日期沒填，小組結束日期有填
                                    if (DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 小組開始日期沒填，小組結束日期沒填
                                    m_Lists.Entities.Add(aListEntity);
                                }
                            }
                        }
                    }
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #endregion
        #region 上傳資料區
        #region 主程式
        private bool DeterminCalculateFlag(Guid m_ContactId, Guid aThisListFamilyHeadId, Guid aThisListSmallGroupLeaderId, Guid aThisListGraceLeaderId)
        {
            // Guid m_ContactId,                    登入者
            // Guid aThisListFamilyHeadId,          小家長
            // Guid aThisListSmallGroupLeaderId,    小組長
            // Guid aThisListGraceLeaderId，        族系族長/區長
            try
            {
                if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId != aThisListGraceLeaderId)
                {
                    #region 登入者是小組長，但不是族系族長/區長
                    return true;
                    #endregion
                }
                else if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是族系族長/區長，而且也是族系族長/區長
                    return true;
                    #endregion
                }
                else if (m_ContactId != aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是族系族長/區長，而且也是族系族長/區長
                    return false;
                    #endregion
                }
                else
                {
                    #region 登入者是族系族長/區長，但不是小組長
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
        private void SetupCommonParameter(String Account, String Password, DateTime aSmallGroupDate, String ListEntityId, String WeeklyReportEntityId)
        {
            try
            {
                // 設定主日日期
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)aSmallGroupDate.DayOfWeek;
                this.m_Sunday = aSmallGroupDate.AddDays(-DayOfWeek);
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
                #region 沒有找到已經建立的週報，但是區長不能替小組長建立週報
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
                //    // 名單裡的族系族長 ID
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
                    #region 先找到"小家長"、"小組長"、族系族長/區長"

                    // 先找到這個名單的小家長 ID，內壢得勝靈糧堂專用
                    //Guid aThisListFamilyHeadId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_familyhead_list");

                    // 找到這個名單的共同組長 ID
                    Guid aThisListCoSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_list_vice_family_leader");

                    // 找到這個名單的小組長 ID
                    Guid aThisListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_family_leader_list");

                    // 找到這個名單的上代組長 ID
                    //Guid aThisListUpperGenerationLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_race_leager_list");

                    // 找到這個名單的族系族長/區長 ID
                    //Guid aThisListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_list_arealeader");

                    #endregion
                    if (this.m_ContactId == aThisListSmallGroupLeaderId || this.m_ContactId == aThisListCoSmallGroupLeaderId)
                    {
                        // 登入者與名單的小組長或是共同組長是同一個人
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
                //    // 名單裡的族系族長 ID
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
        private Guid CreateWeeklyReport(ref Entity aListEntity, ref Dictionary<String, String> WeeklyReportDictionary)
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

                // 區長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 上代組長/(原來是:族系族長) ID
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, m_DecipleGroupListId, aListEntity, m_Sunday, m_SmallGroupPlace, m_SmallGroupTime);

                // 重設主日、小組:出席率、人數 歸零
                ResetWeeklyReortNumber(ref aWeeklyReportEntity);

                // 新增週報
                Guid CreatedWeeklyReportEntityId = this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity);

                // 指派週報的負責人
                this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", CreatedWeeklyReportEntityId), this.m_OwnerId);

                // 建立此小組的出席紀錄單
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                EntityCollection aPresentRecordCollection = CreatePresentRecordList(GroupName, ref aListEntity, ref CreatedWeeklyReportEntityId, 0, 0.0, 0.0, 0, 0, "", "", false);

                // 回傳要建立 QR CODE 所需要的週報 ID 字串
                AddToDictionary(ref WeeklyReportDictionary, this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname"), CreatedWeeklyReportEntityId.ToString());

                return CreatedWeeklyReportEntityId;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Guid aDecipleGroupList, Entity ListEntity, DateTime aSunday, String SmallGroupPlace, String SmallGroupTime)
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
                #region 關聯族系族長/區長屬性
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

                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);

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
        private void ResetWeeklyReortNumber(ref Entity aWeeklyReportEntity)
        {
            try
            {
                #region 設定出席率、人數
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", 0.0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", 0.0);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", 0);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", 0);
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
        #region 建立的個人聚會與靈修記錄
        private EntityCollection CreatePresentRecordList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, Double aWeeklySundayRate, Double aWeeklySmallGroupRate, int aWeeklySundayNumber, int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

            EntityCollection PresentRecordEntityCollection = new EntityCollection();
            foreach (Entity aMemberInfomation in MemberCollection.Entities)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 新增個人聚會與靈修記錄
                Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                //指派負責人
                this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                if (aPresentRecord != null)
                {
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }

            return PresentRecordEntityCollection;
        }
        private EntityCollection CreatePresentRecordListByList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            foreach (Entity aMemberInfomation in MemberCollection.Entities)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 新增個人聚會與靈修記錄
                Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                //指派負責人
                this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                if (aPresentRecord != null)
                {
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }

            return PresentRecordEntityCollection;
        }
        private Entity CreatePresentRecord(Entity MemberEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            //Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);

            Entity ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
            //Entity aToUpdateContactEntity_001 = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)(aMemberInfomation.Attributes["entityid"])    .Id);

            //Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSearchedContactEntity.Id);

            if (ContactEntity != null)
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, MemberEntity, ref ContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

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
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ListEntityId);
                }
                else
                {
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ListEntityId);
                }
                else
                {
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
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
                    return "考慮中";
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
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, Entity aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = m_ToolUtilityClass.GetEntityStringAttribute(ref aMemberInfomation, "fullname") + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
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
                #region 從名單取得 區名、小家長 ID、小組長 ID、區長、區牧長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 區長 ID
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
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", DateTime.Now);
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
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                #endregion
                #region 設定主日出席率
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                #endregion
                #region 設定小組出席
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                #endregion
                #region 設定小組出席率
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                #endregion
                #region 設定幸福小組出席
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);
                #endregion
                #region 設定決志
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 0);
                #endregion
                #region 設定附註或是代禱事項

                // 永和禮拜堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定小組是否暫停
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecord, "new_pause", PauseCheckBox);
                #endregion

                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "mobilephone"));
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
        #region 更新個人聚會與靈修記錄

        #region 更新出席紀錄
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
                case "考慮中":
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
                case "福音的能力":
                    return 100000000;
                case "幸福小組是符合聖經的佈道小組":
                    return 100000001;
                case "真幸福":
                    return 100000002;
                case "欺騙者":
                    return 100000003;
                case "救贖者耶穌":
                    return 100000004;
                case "垂聽禱告的上帝":
                    return 100000005;
                case "你要遇見神":
                    return 100000006;
                case "耶穌基督十字架的勝利":
                    return 100000007;
                case "釋放與自由":
                    return 100000008;
                case "帶來幸福的教會":
                    return 100000009;
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
                    return PresentRecordEntity;
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
                                //// 如果是新朋友、未入組、外教會則不列入累積，永和禮拜堂
                                if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }

                                // 如果是新朋友或是未入組則不列入累積，內壢得勝靈糧堂
                                // 10.不穩定組員   =   100,000,008
                                // 11.新朋友       =   100,000,009
                                // 12.未入組       =   100,000,010
                                // 13.暫不入組     =   100,000,012
                                // 14.結案         =   100,000,011
                                //if (aCustomerTypeCode.Value != 100000008 && aCustomerTypeCode.Value != 100000009 && aCustomerTypeCode.Value != 100000010 && aCustomerTypeCode.Value != 100000012 && aCustomerTypeCode.Value != 100000011 )
                                //{
                                //    return true;
                                //}
                                //else
                                //{
                                //    return false;
                                //}
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

                // 如果是新朋友或是未入組則不列入累積，永和禮拜堂
                if (aCustomerTypeCodeValue != 100000004 && aCustomerTypeCodeValue != 100000000 && aCustomerTypeCodeValue != 100000007 && aCustomerTypeCodeValue != EMPTY_VALUE)
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // 如果是新朋友或是未入組則不列入累積，內壢得勝靈糧堂
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
        #endregion
        #region 設定委身類型
        // 永和禮拜堂
        // 委身類型客製化
        private int ConvertIdentityToIndex(String Identity)
        {
            switch (Identity)
            {
                case "牧師師母":
                    return 100000006;
                case "區長":
                    return 100000003;
                case "小組長":
                    return 100000008;
                case "副組長":
                    return 100000012;
                case "小組組員":
                    return 1;
                case "幸福BEST":
                    return 100000005;
                case "未入組":
                    return 100000004;
                case "新朋友":
                    return 100000000;
                case "外教會.訪客":
                    return 100000007;
                case "結案":
                    return 100000001;
                default:
                    return 100000000;
            }
        }
        // 永和禮拜堂
        // 委身類型客製化
        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000006:
                    return "01. 牧師師母";
                case 100000003:
                    return "02. 區長";
                case 100000008:
                    return "03. 小組長";
                case 100000012:
                    return "04. 副組長";
                case 1:
                    return "05. 小組組員";
                case 100000005:
                    return "06. 幸福BEST";
                case 100000004:
                    return "07. 未入組";
                case 100000000:
                    return "08. 新朋友";
                case 100000007:
                    return "09. 外教會.訪客";
                case 100000001:
                    return "10. 結案";
                default:
                    return ".";
            }
        }
        #endregion
        #region 字典處理函式庫
        private void ResetDictionary(DateTime aSunday)
        {
            try
            {
                //AddToDictionary(ref this.m_SigningReport, "主日出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
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
        #endregion

    }
}


