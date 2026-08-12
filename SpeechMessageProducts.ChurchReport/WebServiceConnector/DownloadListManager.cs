// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadListManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadListManager
// 主要成員：GetListManager、ProcessListEntity、SetupWeeklyReportRecord、GetSmallGroupMemberNumber、FilterPersonalListEntity、ProcessPersonalListEntity、FindLoginUser、FindListCollection、MergeCollectionSmallGroupAhead、SetUserType
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.Xrm.Sdk.Client
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
using ToolUtilityNameSpace.Factory;
using System.Text.RegularExpressions;
using ChurchReport.Models;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.Constants;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class DownloadListManager
    {
        #region 資料區
        #region 參數資料
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        // ✅ 效能優化：加入 RegexOptions.Compiled，JIT 編譯正則表達式以加速匹配
        private static Regex DigitsOnly = new Regex(@"[^\d]", RegexOptions.Compiled);

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
        #region 下載資料時所需要的參數

        //MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給區長/小家長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 區長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        public MultiGroupList m_MultiGroupList = new MultiGroupList();

        public MultiGroupChartDataList m_MultiGroupChartDataList = new MultiGroupChartDataList();

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        #endregion
        #region 下載資料區
        #region 主程式區
        /// <summary>
        /// 在單一下載操作內建立登入者可見的小組與週報資料。
        ///
        /// <para>
        /// <paramref name="organizationService"/> 若由上層借用，僅能在此呼叫鏈以參數
        /// 傳遞；絕不可寫入 Factory 共用的 <see cref="ToolUtilityClass"/>。這可避免
        /// 下一個 HTTP request、使用者或 Dynamics profile 取得前一個操作的可變連線狀態。
        /// service 的建立、lease 歸還與 Dispose 仍由呼叫端擁有，本方法不轉移所有權。
        /// </para>
        /// </summary>
        /// <param name="Account">已由上層驗證的登入帳號識別；不得以此參數選擇 CRM profile。</param>
        /// <param name="Password">既有登入流程的受保護驗證資料；本方法不記錄或快取它。</param>
        /// <param name="aDownloadDate">用於計算本週主日的操作範圍日期。</param>
        /// <param name="aMultiGroupList">回傳本次操作建立的多小組資料。</param>
        /// <param name="aMultiGroupChartDataList">回傳本次操作建立的統計資料。</param>
        /// <param name="LoginType">回傳固定登入類別。</param>
        /// <param name="UserType">回傳由 CRM 資料判定的使用者類別。</param>
        /// <param name="LoginFullName">回傳既有登入者名稱欄位；呼叫端負責依授權範圍呈現。</param>
        /// <param name="ActiveListId">回傳當次選定名單識別。</param>
        /// <param name="organizationService">可選的 operation-scoped CRM service；借用且不保存、不 Dispose。</param>
        public void GetListManager( String Account, String Password, DateTime aDownloadDate, ref MultiGroupList aMultiGroupList, ref MultiGroupChartDataList aMultiGroupChartDataList, ref String LoginType, ref String UserType, ref String LoginFullName, ref String ActiveListId, IOrganizationService organizationService = null )
        {
            try
            {
                // 借用 service 的唯一合法資料流是 method parameter → private helper；
                // 不可回寫 Factory singleton，否則不同 request 會共用連線、認證與故障狀態。

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
                // 透過集中式服務依設定的每週第一日計算主日，
                // 讓不同週起始規則都能回傳同一週區間內的星期日。
                m_Sunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                    aDownloadDate,
                    ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);
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

                #region 先尋找帶領族系名單，若找到表示就是區長，若沒有則在繼續尋找帶領小組名單

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
                    ProcessListEntity(organizationService);
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
                    ProcessPersonalListEntity(organizationService);
                    #endregion

                    return;
                    #endregion
                }
                    #endregion
            }
            catch (System.Exception)
            {
                // 保留原始 stack trace，讓上層只能依受控分類處理錯誤，而非失去真正失敗邊界。
                throw;
            }
        }

        #endregion
        #region 處理小組名單
        /// <summary>
        /// 處理小組長以上可見的名單，並將借用 service 繼續限制在目前操作呼叫鏈。
        /// </summary>
        /// <param name="organizationService">可選的 operation-scoped service；不保存也不釋放。</param>
        private void ProcessListEntity(IOrganizationService organizationService)
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
                    SetupWeeklyReportRecord(this.m_MultiGroupList.m_WeeklyReportRecordListData, ListEntity, GroupWeeklyReportEntity, organizationService);
                }
                return;
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 將一筆名單與其零或一筆週報投影為操作範圍資料列。
        ///
        /// <para>
        /// 名單成員計數可使用借用 service，但只透過參數傳遞給即時查詢；不會保存到
        /// 共用工具，也不會在本方法 Dispose，確保 service 的 owner 仍是外層 lease。
        /// </para>
        /// </summary>
        /// <param name="aWeeklyReportRecordListData">本次操作輸出集合。</param>
        /// <param name="ListEntity">已由上游查詢取得的名單。</param>
        /// <param name="GroupWeeklyReportEntity">同一名單與主日的零或一筆週報。</param>
        /// <param name="organizationService">可選的 operation-scoped CRM service。</param>
        public void SetupWeeklyReportRecord(List<WeeklyReportRecord> aWeeklyReportRecordListData, Entity ListEntity, Entity GroupWeeklyReportEntity, IOrganizationService organizationService = null)
        {
            try
            {
                if (GroupWeeklyReportEntity != null)
                {
                    // 已經有週報

                    // 聚會人數就以當時上傳的人數為準
                    //String TotalMemberNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_small_group_member_number").ToString();
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
                              TotalNumber = GetSmallGroupMemberNumber(ListEntity.Id, organizationService).ToString(),
                             // 主日出席人數
                             SundayNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_sunday_present_number").ToString(),
                             // 主日出席率
                             SundayRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_sunday_present_rate").ToString(),
                             // 小組出席人數
                             SmallGroupNumber = m_ToolUtilityClass.GetEntityIntAttribute(GroupWeeklyReportEntity, "new_small_group_number").ToString(),
                             // 小組出席率
                             SmallGroupRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_small_group_rate").ToString(),
                             // 週報狀態
                             ReportStatus = GetReportStatus(GroupWeeklyReportEntity),
                             // 小組日誌
                             ReportContent = m_ToolUtilityClass.GetEntityStringAttribute(GroupWeeklyReportEntity, "new_memo"),
                         }
                    );

                    // 圓餅圖所需的數據
                    // 成員人數:萬一不知怎麼的週報的應該聚會人數不存在，就只好以現在的成員人數為準
                    int aTotalMemberNumber = GetSmallGroupMemberNumber(ListEntity.Id, organizationService);
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
                              TotalNumber = GetSmallGroupMemberNumber(ListEntity.Id, organizationService).ToString(),
                             SundayNumber = "0",
                             SundayRate = "0",
                             SmallGroupNumber = "0",
                             SmallGroupRate = "0",
                         }
                    );

                    int aTotalMemberNumber = GetSmallGroupMemberNumber(ListEntity.Id, organizationService);
                    //this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number = this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number + aTotalMemberNumber;
                    this.m_MultiGroupChartDataList.m_MultiGroupChartDataList.Where(e => e.ID == "001").First().Number += aTotalMemberNumber;
                }

                return;
            }
            catch (System.Exception)
            {
                throw;
            }
        }
        /// <summary>
        /// 使用單一操作的 service 讀取名單類型與成員數，且絕不把它寫回共用工具。
        ///
        /// <para>
        /// 傳入 service 優先於既有 deployment-owned ToolUtility service；兩者皆不存在時
        /// 立即 fail closed。查詢僅保留局部變數，方法結束即失去參考，避免把連線、
        /// profile 或使用者相關狀態延伸至下一個 request。service 的 Dispose/lease release
        /// 仍由建立者負責，這個 helper 不取得所有權。
        /// </para>
        /// </summary>
        /// <param name="ListEntityId">已授權名單的固定識別。</param>
        /// <param name="organizationService">可選的借用 operation-scoped service。</param>
        /// <returns>名單成員數量。</returns>
        private int GetSmallGroupMemberNumber(Guid ListEntityId, IOrganizationService organizationService)
        {
            #region // 初始化每個小組名單，建立原始的 Member Data

            IOrganizationService serviceToUse = organizationService
                ?? this.m_ToolUtilityClass.m_Crm2011OrganizationService
                ?? this.m_ToolUtilityClass.m_OrganizationService;

            if (serviceToUse == null)
            {
                throw new ArgumentNullException(nameof(serviceToUse), "organizationService 為 null，無法查詢小組成員數量");
            }

            // ✅ 只查詢必要的欄位（避免查詢不存在的欄位）
            // 只需要 type 欄位來判斷名單類型（靜態/動態）
            var listEntity = serviceToUse.Retrieve("list", ListEntityId, new ColumnSet("type"));

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(listEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // helper 的 ref 只指向方法區域變數，不能把借用 service 保存到 ToolUtility 欄位。
                MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref serviceToUse, ListEntityId);
            }
            else
            {
                // 同一 ownership 規則適用於動態名單；逾時或例外由呼叫端決定 lease 淘汰。
                MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref serviceToUse, ListEntityId);
            }

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
            catch (System.Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 處理個人回報名單，將借用 service 保持在單一同步操作範圍內。
        /// </summary>
        /// <param name="organizationService">可選的 operation-scoped service；不保存也不釋放。</param>
        private void ProcessPersonalListEntity(IOrganizationService organizationService)
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

                        SetupWeeklyReportRecord(this.m_MultiGroupList.m_WeeklyReportRecordListData, ListEntity, GroupWeeklyReportEntity, organizationService);
                        #endregion
                    }
                }
                return;
            }
            catch (System.Exception)
            {
                throw;
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
                // 門徒 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 門徒
                //this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 門徒
                //MergeCollectionSmallGroupAhead(ref this.m_Lists);
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 門徒
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 小組長/門徒 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/門徒
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/門徒
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/門徒
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同小家長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同小家長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同小家長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 上代組長 new_contact_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");  // 上代組長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_race_leager_list");  // 上代組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 區長 new_contact_list_arealeader
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_arealeader", "list");  // 區長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_arealeader");  // 區長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同區牧 new_contact_list_co_arealeader
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_co_arealeader");  // 共同區牧
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                #region 按照名單名字排序
                if (m_Lists.Entities.Count > 0)
                {
                    EntityCollection aLists = new EntityCollection();
                    foreach (Entity aEntity in m_Lists.Entities.OrderBy(o => o.Attributes["listname"]).ToList())
                    {
                        aLists.Entities.Add(aEntity);
                    }

                    m_Lists.Entities.Clear();
                    foreach (Entity aEntity in aLists.Entities)
                    {
                        m_Lists.Entities.Add(aEntity);
                    }
                }
                #endregion

                return;
            }
            catch (System.Exception)
            {
                // 查詢例外可能帶有上游 transport 細節；保留 stack 供受控診斷分類，
                // 並避免在本層建立或留存原始例外文字的複本。
                throw;
            }
        }
        private void MergeCollectionSmallGroupAhead(ref EntityCollection aListEntityCollection )
        {
            try
            {
                // 區長或是小家長的名單若是與小組長名單重疊，則要過濾出僅有族長/小家長的名單
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
                            // 區長的名單與小組長的名單有相同的了
                            SearchedFlag = true;
                            break;
                        }
                    }

                    if (SearchedFlag == false)
                    {
                        // 區長的名單沒有與小組長名單相同的
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
            catch (System.Exception)
            {
                throw;
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
        private String GetReportStatus(Entity GroupWeeklyReportEntity)
        {
            switch (m_ToolUtilityClass.GetOptionSetAttribute(GroupWeeklyReportEntity, "new_weekly_report_status"))
            {
                case 100000000:
                    return "均未點名";
                case 100000001:
                    return "均已點名";
                case 100000003:
                    return "主日點名，小組未點名";
                case 100000004:
                    return "小組點名，主日未點名";
                case 100000002:
                    return "暫停";
                default:
                    return "均未點名";
            }
        }
        #endregion
        #endregion
    }
}
