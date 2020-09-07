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
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class DownloadListManager
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

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
        #region 下載資料時所需要的參數

        //MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        public MultiGroupList m_MultiGroupList = new MultiGroupList();

        public MultiGroupChartDataList m_MultiGroupChartDataList = new MultiGroupChartDataList();

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        #endregion
        #region 下載資料區
        #region 主程式區
        public void GetListManager( String Account, String Password, DateTime aDownloadDate, ref MultiGroupList aMultiGroupList, ref MultiGroupChartDataList aMultiGroupChartDataList, ref String LoginType, ref String UserType, ref String LoginFullName, ref String ActiveListId )
        {
            try
            {
                #region 多小組需要的資料結構，在此配置記憶體，並回傳給上層呼叫者
                m_MultiGroupList.m_WeeklyReportRecordListData = new List<WeeklyReportRecord>();

                aMultiGroupList = m_MultiGroupList;

                //m_MultiGroupChartDataList.m_MultiGroupChartDataList = new List<MultiGroupChartData>();

                m_MultiGroupChartDataList = new MultiGroupChartDataList
                {
                    m_MultiGroupChartDataList = new List<MultiGroupChartData>
                    {
                        new MultiGroupChartData
                        {
                            ID = "001",
                            Name= "總人數",
                            Number = 0
                        },
                        new MultiGroupChartData
                        {
                            ID = "002",
                            Name= "主日人數",
                            Number = 0
                        },
                        new MultiGroupChartData
                        {
                            ID = "003",
                            Name= "小組人數",
                            Number = 0
                        }
                    }
                };

            aMultiGroupChartDataList = m_MultiGroupChartDataList;
            #endregion

                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)aDownloadDate.DayOfWeek;
                this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
                #endregion

                #region 找登入使用者及其ID
                FindLoginUser(Account, Password); // 也就是設定 this.m_ContactEntity
                if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
                { return ; } // 沒找到就回傳 null 
                else
                {
                    // 取得登入者的姓名
                    LoginFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");
                }
                #endregion

                SetUserType(ref UserType);

                #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單

                // 先把名單清乾淨
                this.m_Lists.Entities.Clear();

                FindListCollection();
                #endregion

                #region 處理小組名單
                if (m_Lists.Entities.Count != 0)
                {
                    #region// 有找到要點名的名單，所以是小組長以上回報
                    LoginType = "小組長";
                    ActiveListId = this.m_Lists.Entities[0].Id.ToString();
                    #region 處理每個要點名的名單
                    ProcessListEntity();
                    #endregion

                    return ;
                    #endregion
                }
                else
                {
                    #region// 沒找到任何要點名的名單，所以是個人回報
                    LoginType = "個人回報";

                    #region 取得個人回報的名單
                    this.m_Lists = this.m_ToolUtilityClass.QueryListOfContactManyToMany(this.m_ContactEntity.Id);
                    FilterPersonalListEntity();

                    if (this.m_Lists.Entities.Count > 0)
                    {
                        ActiveListId = this.m_Lists.Entities[0].Id.ToString();
                    }
                    #endregion

                    #region 處理每個要點名的名單
                    m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
                    ProcessPersonalListEntity();
                    #endregion

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
        private void ProcessListEntity()
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
                    SetupWeeklyReportRecord(this.m_MultiGroupList.m_WeeklyReportRecordListData, ListEntity, GroupWeeklyReportEntity);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public void SetupWeeklyReportRecord(List<WeeklyReportRecord> aWeeklyReportRecordListData, Entity ListEntity, Entity GroupWeeklyReportEntity)
        {
            try
            {
                if (GroupWeeklyReportEntity != null)
                {
                    // 已經有週報

                    // 聚會人數就以當時上傳的人數為準
                    String TotalMemberNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_small_group_member_number").ToString();
                    aWeeklyReportRecordListData.Add
                    (
                         new WeeklyReportRecord
                         {
                             // 小組名單 ID
                             ListEntityId = ListEntity.Id.ToString(),
                             // 週報的 ID
                             WeeklyReportEntityId = GroupWeeklyReportEntity.Id.ToString(),
                             // 小組名稱
                             Name = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname"),
                             // 小組總人數
                             TotalNumber = TotalMemberNumber != "-9999"? TotalMemberNumber : GetSmallGroupMemberNumber(ListEntity.Id).ToString(),
                             // 主日出席人數
                             SundayNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_sunday_present_number").ToString(),
                             // 主日出席率
                             SundayRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_sunday_present_rate").ToString(),
                             // 小組出席人數
                             SmallGroupNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_small_group_number").ToString(),
                             // 小組出席率
                             SmallGroupRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_small_group_rate").ToString(),
                             // 小組日誌
                             ReportContent = m_ToolUtilityClass.GetEntityStringAttribute(GroupWeeklyReportEntity, "new_memo"),
                         }
                    );

                    // 圓餅圖所需的數據
                    // 成員人數:萬一不知怎麼的週報的應該聚會人數不存在，就只好以現在的成員人數為準
                    int aTotalMemberNumber = TotalMemberNumber != "-9999" ? Convert.ToInt32(TotalMemberNumber) : GetSmallGroupMemberNumber(ListEntity.Id);
                    this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number += aTotalMemberNumber;
                    this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "002").First().Number += m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_sunday_present_number");
                    this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "003").First().Number += m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_small_group_number");
                }
                else
                {
                    aWeeklyReportRecordListData.Add
                    (
                         new WeeklyReportRecord
                         {
                             ListEntityId = ListEntity.Id.ToString(),
                             WeeklyReportEntityId = "",
                             Name = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname"),
                             TotalNumber = GetSmallGroupMemberNumber(ListEntity.Id).ToString(),
                             SundayNumber = "0",
                             SundayRate = "0",
                             SmallGroupNumber = "0",
                             SmallGroupRate = "0",
                         }
                    );

                    int aTotalMemberNumber = GetSmallGroupMemberNumber(ListEntity.Id);
                    //this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number = this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number + aTotalMemberNumber;
                    this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number += aTotalMemberNumber;
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
                        if (this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname").Contains("幸福") != true)
                        {
                            // 個人回報不須回報至幸福小組
                            aLocalEntityCollection.Entities.Add(ListEntity);
                        }
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

        private void ProcessPersonalListEntity()
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    if (m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "new_app_named") == true)
                    {
                        #region// 這個名單是需要被點名的
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
                        String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                        SetupWeeklyReportRecord(this.m_MultiGroupList.m_WeeklyReportRecordListData, ListEntity, GroupWeeklyReportEntity);
                        #endregion
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
                // 小組同工 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 小組同工
                //this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 小組同工
                //MergeCollectionSmallGroupAhead(ref this.m_Lists);
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 小組同工
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 小組長/小組同工 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/小組同工
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
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

                // 共同區牧 new_contact_list_co_arealeader
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_co_arealeader");  // 共同區牧
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void MergeCollectionSmallGroupAhead(ref EntityCollection aListEntityCollection )
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

                            if( aHappyStartDate.Year != 1  )
                            {
                                // 小組開始日期有填
                                if (aHappyEndDate.Year != 1)
                                {
                                    // 小組開始日期有填，小組結束日期有填
                                    if (DateTime.Now >= aHappyStartDate  && DateTime.Now <= aHappyEndDate )
                                    {
                                        // 現在比小組開始日期還晚 ，比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 小組開始日期有填，小組結束日期沒填
                                    if (DateTime.Now >= aHappyStartDate )
                                    {
                                        // 現在比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                            }
                            else
                            {
                                // 小組開始日期沒填
                                if (aHappyEndDate.Year != 1)
                                {
                                    // 小組開始日期沒填，小組結束日期有填
                                    if (DateTime.Now <=aHappyEndDate )
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
                return ;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void SetUserType( ref String UserType)
        {
            // 找登入使用者及其ID
            if( this.m_ToolUtilityClass.GetEntityLookupAttribute( ref this.m_ContactEntity, "new_replace_contact") != Guid.Empty && this.m_ToolUtilityClass.GetEntityLookupAttribute(ref this.m_ContactEntity, "new_manager_contact") != Guid.Empty)
            {
                UserType = "行政同工";
            }
            else
            {
                UserType = "非行政同工";
            }
        }

        #endregion
        #endregion
    }
}
