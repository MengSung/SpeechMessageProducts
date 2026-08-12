// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：SetupHeaderData、SetupShepherdData、SortAndCleanMemberStatus、SetupWeeklyReportData、SetupHappyGroupWeeklyData、SetupCommonWeeklyData、SetupWeeklyReportChartData、InitializeChartData、SetSmallGroupData、SetNewPersonFollowUpData
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Text、ChurchReport.Models、ChurchReport.Services、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChurchReport.Models;
using ChurchReport.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 設定相關方法
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 標頭設定

        /// <summary>
        /// 設定標頭資料
        /// </summary>
        public void SetupHeaderData(
            string Account,
            string Password,
            DateTime aDownloadDate,
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 找登入使用者及其ID
            FindLoginUser(Account, Password);
            if (m_ContactId == Guid.Empty)
            {
                return; // 沒找到就回傳
            }

            aListSmallGroupWeeklyReport.LoadFlag = true;
            this.m_ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId));

            aListSmallGroupWeeklyReport.ListEntityName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ListEntity, "listname");
            aListSmallGroupWeeklyReport.GroupType = aListSmallGroupWeeklyReport.ListEntityName.Contains("幸福") ? "幸福小組" : "一般小組";

            aListSmallGroupWeeklyReport.WeeklyReportEntityId = WeeklyReportEntityId;
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                this.m_WeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(WeeklyReportEntityId));
            }

            aListSmallGroupWeeklyReport.LoginType = this.m_LoginType;
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = m_ToolUtilityClass.GetEntityLookupDisplayName(ref m_ListEntity, "new_contact_family_leader_list");
            aListSmallGroupWeeklyReport.SundayPrayers = aDownloadDate;

            // 「小組日期對應到主日期間」不能再硬編碼為「主日前 6 天到主日」，
            // 必須依照 appsettings.json 的 WeeklySchedule:每週的第一日 動態決定。
            // 例如：
            // - 星期一起始：區間為 星期一 ~ 星期日
            // - 星期六起始：區間為 星期六 ~ 星期五
            // - 星期日起始：區間為 星期日 ~ 星期六
            DateTime weekStart = SundayCalculator.CalculateWeekStart(m_Sunday, WeeklyScheduleProvider.FirstDayOfWeek);
            DateTime weekEnd = SundayCalculator.CalculateWeekEnd(m_Sunday, WeeklyScheduleProvider.FirstDayOfWeek);
            aListSmallGroupWeeklyReport.SundayPeriod = $"小組日期對應到主日期間是: {weekStart.ToShortDateString()} ~ {weekEnd.ToShortDateString()}";

            aListSmallGroupWeeklyReport.SmallGroupLeaderContactId = m_ContactId.ToString();
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");
        }

        /// <summary>
        /// 使用呼叫端借用的 CRM service 建立目前操作的標頭資料。
        ///
        /// <para>
        /// 此 overload 尚未由 Core 入口啟用，僅為後續 request-local 轉送預先建立安全邊界。
        /// <paramref name="organizationService"/> 的唯一 owner 是呼叫端 lease；本方法僅在同步呼叫
        /// 堆疊中使用它，絕不寫入 <see cref="DownloadIntegrateData"/> instance、static、cache、
        /// Factory 或 <c>ToolUtility</c>，也絕不 Dispose、Close、Abort 或包裝它。所有實體與輸出
        /// 只存在於目前 <paramref name="aListSmallGroupWeeklyReport"/>，呼叫結束即由上層擁有。
        /// </para>
        ///
        /// <para>
        /// 登入查詢、名單與週報讀取均直接呼叫傳入 service；找不到登入者時不再嘗試 legacy
        /// ToolUtility fallback，並以既有的空白輸出語意返回。這可阻止 session 快取的上層物件把
        /// 前一次 profile 的連線或回應資料帶入下一次操作。
        /// </para>
        /// </summary>
        /// <param name="Account">目前操作提供的帳號或 Line 登入識別；不記錄或快取。</param>
        /// <param name="Password">目前操作的驗證資料；只在此同步比對，不記錄或快取。</param>
        /// <param name="aDownloadDate">目前回應所屬的下載日期。</param>
        /// <param name="ListEntityId">上層已驗證授權的名單識別。</param>
        /// <param name="WeeklyReportEntityId">可選的既有週報識別。</param>
        /// <param name="aListSmallGroupWeeklyReport">僅屬於目前操作的輸出模型。</param>
        /// <param name="organizationService">呼叫端借用且仍由其 owner 釋放的 CRM service。</param>
        /// <exception cref="ArgumentNullException">當 service 或輸出模型未提供時擲回。</exception>
        /// <exception cref="FormatException">當上層傳入的名單或週報 ID 非 Guid 時擲回。</exception>
        private void SetupHeaderData(
            string Account,
            string Password,
            DateTime aDownloadDate,
            string ListEntityId,
            string WeeklyReportEntityId,
            string LoginType,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport,
            IOrganizationService organizationService)
        {
            ArgumentNullException.ThrowIfNull(aListSmallGroupWeeklyReport);
            ArgumentNullException.ThrowIfNull(organizationService);

            var loginContact = FindLoginUser(Account, Password, organizationService);
            if (loginContact == null || loginContact.Id == Guid.Empty)
            {
                return;
            }

            var listId = Guid.Parse(ListEntityId);
            var listEntity = RetrieveOperationLocalLeaderList(
                organizationService,
                listId,
                loginContact.Id);

            Entity weeklyReportEntity = null;
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                weeklyReportEntity = organizationService.Retrieve(
                    "new_group_present_weekly_report",
                    Guid.Parse(WeeklyReportEntityId),
                    new ColumnSet(true));
            }

            var listName = listEntity.GetAttributeValue<string>("listname") ?? string.Empty;
            var leaderReference = listEntity.GetAttributeValue<EntityReference>("new_contact_family_leader_list");
            var sunday = SundayCalculator.CalculateSunday(aDownloadDate, WeeklyScheduleProvider.FirstDayOfWeek);
            var weekStart = SundayCalculator.CalculateWeekStart(sunday, WeeklyScheduleProvider.FirstDayOfWeek);
            var weekEnd = SundayCalculator.CalculateWeekEnd(sunday, WeeklyScheduleProvider.FirstDayOfWeek);

            aListSmallGroupWeeklyReport.LoadFlag = true;
            aListSmallGroupWeeklyReport.ListEntityId = ListEntityId;
            aListSmallGroupWeeklyReport.ListEntityName = listName;
            aListSmallGroupWeeklyReport.GroupType = listName.Contains("幸福", StringComparison.Ordinal) ? "幸福小組" : "一般小組";
            aListSmallGroupWeeklyReport.WeeklyReportEntityId = WeeklyReportEntityId;
            // 登入型態與 service 同屬本次明確呼叫輸入；不可讀取 m_LoginType，因為
            // DownloadIntegrateData 可能被 session 容器重用，舊 instance 值會跨操作污染回應。
            aListSmallGroupWeeklyReport.LoginType = LoginType;
            aListSmallGroupWeeklyReport.SmallGroupLeaderContactId = loginContact.Id.ToString();
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = loginContact.GetAttributeValue<string>("fullname")
                ?? leaderReference?.Name
                ?? string.Empty;
            // 圖表 helper 僅能從 request-local report 取得日期；不得回落至可能被 Session
            // 重用的 DownloadIntegrateData.m_Sunday。這裡在任何後續 CRM I/O 前寫入該輸入快照。
            aListSmallGroupWeeklyReport.SundayPrayers = aDownloadDate;
            aListSmallGroupWeeklyReport.SundayPeriod = $"小組日期對應到主日期間是: {weekStart.ToShortDateString()} ~ {weekEnd.ToShortDateString()}";

            // 目前只在方法內讀取週報以建立完整的 operation-local header 邊界。不能把實體回寫至
            // m_WeeklyReportEntity，因為 DownloadIntegrateData 可能被上層 session 容器重用。
            _ = weeklyReportEntity;
        }

        /// <summary>
        /// 以登入聯絡人與名單小組長關係，伺服器端驗證目前 operation 可讀取的唯一名單。
        ///
        /// <para>
        /// <paramref name="requestedListId"/> 只是呼叫端提供的候選鍵，絕不是授權依據。查詢必須同時
        /// 限制名單主鍵與 <c>new_contact_family_leader_list</c> 等於已由同一 borrowed service 驗證的登入
        /// 聯絡人；找不到資料即在取得名單名稱、成員、週報或圖表前 fail closed。如此即使有效小組長
        /// 將另一個小組的 GUID 傳入，也無法讀取其內容。
        /// </para>
        ///
        /// <para>
        /// QueryExpression、Entity 與結果只存在目前同步呼叫堆疊。此 helper 不會保存、包裝、Dispose
        /// 或回傳 <paramref name="organizationService"/>；其 lease、timeout/fault eviction 與清理仍由
        /// 最外層 operation owner 負責。缺少完整關係、重複結果或 paging continuation 都視為不可證明
        /// 的授權，不可降級為以 ID Retrieve 或 ToolUtility fallback。
        /// </para>
        /// </summary>
        /// <param name="organizationService">呼叫端借用且仍由其 owner 回收的 CRM service。</param>
        /// <param name="requestedListId">呼叫端提出、必須由伺服器關係驗證的名單候選鍵。</param>
        /// <param name="validatedLeaderContactId">已成功登入的聯絡人唯一識別。</param>
        /// <returns>只在精確名單－小組長關係存在時取得的名單投影。</returns>
        /// <exception cref="ArgumentNullException">當 CRM service 未提供時擲回。</exception>
        /// <exception cref="InvalidOperationException">當關係不存在、模糊或可分頁時擲回。</exception>
        private static Entity RetrieveOperationLocalLeaderList(
            IOrganizationService organizationService,
            Guid requestedListId,
            Guid validatedLeaderContactId)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (requestedListId == Guid.Empty || validatedLeaderContactId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "operation-local 名單授權缺少有效的伺服器端候選鍵或登入聯絡人；已在名單讀取前拒絕。");
            }

            var query = new QueryExpression("list")
            {
                ColumnSet = new ColumnSet("listname", "new_contact_family_leader_list"),
                TopCount = 2,
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            query.Criteria.AddCondition("listid", ConditionOperator.Equal, requestedListId);
            query.Criteria.AddCondition(
                "new_contact_family_leader_list",
                ConditionOperator.Equal,
                validatedLeaderContactId);

            var authorizedLists = organizationService.RetrieveMultiple(query);
            if (authorizedLists == null || authorizedLists.MoreRecords || authorizedLists.Entities.Count != 1)
            {
                throw new InvalidOperationException(
                    "operation-local 名單授權未能證明登入小組長與指定名單具有唯一關係；已在成員與週報讀取前拒絕。");
            }

            var listEntity = authorizedLists.Entities[0];
            var leaderReference = listEntity.GetAttributeValue<EntityReference>("new_contact_family_leader_list");
            if (!string.Equals(listEntity.LogicalName, "list", StringComparison.Ordinal) ||
                listEntity.Id != requestedListId ||
                leaderReference?.Id != validatedLeaderContactId)
            {
                throw new InvalidOperationException(
                    "operation-local 名單授權結果不符合精確名單－小組長關係；已在後續 CRM 讀取前拒絕。");
            }

            return listEntity;
        }

        /// <summary>
        /// 以 operation-local service 查詢並驗證登入聯絡人。
        ///
        /// <para>
        /// 帳號登入先以啟用中的帳號精確查詢，再於記憶體中比對密碼；Line 登入以啟用中的 Line ID
        /// 精確查詢。查詢結果不快取、不寫入 instance 欄位且不回落至 ToolUtility，因此 A/B 操作的
        /// 聯絡人資料只會存在於各自呼叫堆疊。此 helper 不是 service owner，沒有任何釋放行為。
        /// </para>
        /// </summary>
        /// <param name="Account">帳號登入識別，或既有的 <c>LineIdLogin</c> sentinel。</param>
        /// <param name="Password">帳號密碼或 Line 使用者 ID；不可記錄。</param>
        /// <param name="organizationService">目前操作唯一允許使用的 CRM service。</param>
        /// <returns>驗證成功的聯絡人；找不到或密碼不符時為 <see langword="null"/>。</returns>
        /// <exception cref="ArgumentNullException">當 service 未提供時擲回。</exception>
        private static Entity FindLoginUser(
            string Account,
            string Password,
            IOrganizationService organizationService)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (string.Equals(Account, "LineIdLogin", StringComparison.Ordinal))
            {
                var lineQuery = new QueryByAttribute("contact")
                {
                    ColumnSet = new ColumnSet(true),
                    TopCount = 1
                };
                lineQuery.Attributes.AddRange("new_lineid", "statecode");
                lineQuery.Values.AddRange(Password, 0);

                return organizationService.RetrieveMultiple(lineQuery).Entities.FirstOrDefault();
            }

            var accountQuery = new QueryByAttribute("contact")
            {
                ColumnSet = new ColumnSet(true),
                TopCount = 1
            };
            accountQuery.Attributes.AddRange("new_app_acount", "statecode");
            accountQuery.Values.AddRange(Account, 0);

            var contact = organizationService.RetrieveMultiple(accountQuery).Entities.FirstOrDefault();
            return contact != null && string.Equals(
                contact.GetAttributeValue<string>("new_app_pass"),
                Password,
                StringComparison.Ordinal)
                ? contact
                : null;
        }

        #endregion

        #region 牧養資料設定

        /// <summary>
        /// 設定牧養資料
        /// </summary>
        public void SetupShepherdData(
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化 SmallGroupDataList
            aListSmallGroupWeeklyReport.m_SmallGroupDataList = new SmallGroupDataList();

            // 取得所有成員資料
            this.GetAllMemeberDataList(ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 根據小組類型設定資料
            if (!aListSmallGroupWeeklyReport.GroupType.Contains("幸福"))
            {
                this.SetSmallGroupData(ref aListSmallGroupWeeklyReport);
                // SetNewPersonFollowUpData 已整合入 SetSmallGroupData 的單次遍歷
            }
            else
            {
                this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);
            }

            // ? 極速：所有小組名稱清單幾乎不變，快取 30 分鐘省去 CRM 查詢
            // Session 安全：小組名稱為系統公開資料，所有使用者共享相同清單
            const string listCacheKey = "AllGroupList_v1";
            if (!_optionSetCache.TryGetValue(listCacheKey, out EntityCollection aListEntityCollection))
            {
                aListEntityCollection = m_ToolUtilityClass.RetrieveListByFetchXml();
                _optionSetCache.Set(listCacheKey, aListEntityCollection,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(30)));
            }
            aListSmallGroupWeeklyReport.GroupArray.Clear();
            foreach (Entity aList in aListEntityCollection.Entities)
            {
                aListSmallGroupWeeklyReport.GroupArray.Add(m_ToolUtilityClass.GetEntityStringAttribute(aList, "listname"));
            }

            // 排序委身類型並清理格式
            SortAndCleanMemberStatus(ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 排序並清理成員狀態
        /// ? 極速：List.Sort() 原地排序取代 OrderBy().ToList()，省去 4 次 List 建構
        /// </summary>
        private static void SortAndCleanMemberStatus(ref ListSmallGroupWeeklyReport report)
        {
            // 原地排序，無需建立新 List
            report.m_SmallGroupDataList.m_AllMemeberData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
            report.m_SmallGroupDataList.m_SmallGroupData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
            report.m_SmallGroupDataList.m_NewPersonFollowUpData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
            report.m_SmallGroupDataList.m_HappyGroup?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));

            // 去除數字、空白、逗號
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_AllMemeberData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_SmallGroupData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_NewPersonFollowUpData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_HappyGroup?.Members);
        }

        #endregion

        #region 週報資料設定

        /// <summary>
        /// 設定週報資料
        /// </summary>
        public void SetupWeeklyReportData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            if (aListSmallGroupWeeklyReport.GroupType == "幸福小組")
            {
                SetupHappyGroupWeeklyData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);
            }

            SetupCommonWeeklyData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 使用目前操作借用的 CRM service 設定週報欄位。
        ///
        /// <para>
        /// 此 private overload 為尚未接入 Core 的 service-aware 轉送點；它直接讀取傳入 service，
        /// 不使用 ToolUtilityFacade 或 legacy ToolUtility fallback。週報實體只在本方法存活，結果複製
        /// 到目前輸出模型後即失去參考；service 也不保存、不 Dispose，維持 caller lease owner 的
        /// 單一資源所有權。
        /// </para>
        /// </summary>
        /// <param name="WeeklyReportEntityId">可選且已由上層授權的週報識別。</param>
        /// <param name="aListSmallGroupWeeklyReport">目前操作唯一的輸出模型。</param>
        /// <param name="organizationService">呼叫端借用且必須由其釋放的 CRM service。</param>
        /// <exception cref="ArgumentNullException">當輸出模型或 service 未提供時擲回。</exception>
        /// <exception cref="FormatException">當非空週報 ID 非 Guid 時擲回。</exception>
        private void SetupWeeklyReportData(
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport,
            IOrganizationService organizationService)
        {
            ArgumentNullException.ThrowIfNull(aListSmallGroupWeeklyReport);
            ArgumentNullException.ThrowIfNull(organizationService);

            if (string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                aListSmallGroupWeeklyReport.HappyWeekIndex = string.Empty;
                aListSmallGroupWeeklyReport.HappyWeekTopic = string.Empty;
                aListSmallGroupWeeklyReport.WeeklyReportData = string.Empty;
                aListSmallGroupWeeklyReport.WeeklyReportAnalysis = string.Empty;
                aListSmallGroupWeeklyReport.PauseCheckBox = false;
                return;
            }

            var weeklyReportEntity = organizationService.Retrieve(
                "new_group_present_weekly_report",
                Guid.Parse(WeeklyReportEntityId),
                new ColumnSet("new_weekly_index", "new_topic", "new_memo", "new_sunday_present_report", "new_weekly_report_status"));

            if (string.Equals(aListSmallGroupWeeklyReport.GroupType, "幸福小組", StringComparison.Ordinal))
            {
                aListSmallGroupWeeklyReport.HappyWeekIndex = weeklyReportEntity.GetAttributeValue<string>("new_weekly_index") ?? string.Empty;
                aListSmallGroupWeeklyReport.HappyWeekTopic = ConvertIndexToTopic(
                    weeklyReportEntity.GetAttributeValue<OptionSetValue>("new_topic")?.Value ?? 0);
            }

            aListSmallGroupWeeklyReport.WeeklyReportData = weeklyReportEntity.GetAttributeValue<string>("new_memo") ?? string.Empty;
            aListSmallGroupWeeklyReport.WeeklyReportAnalysis = weeklyReportEntity.GetAttributeValue<string>("new_sunday_present_report") ?? string.Empty;
            aListSmallGroupWeeklyReport.PauseCheckBox =
                weeklyReportEntity.GetAttributeValue<OptionSetValue>("new_weekly_report_status")?.Value == 100000002;
        }

        /// <summary>
        /// 設定幸福小組週報資料
        /// </summary>
        private void SetupHappyGroupWeeklyData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport report)
        {
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                report.HappyWeekIndex = m_ToolUtilityClass.GetEntityStringAttribute(this.m_WeeklyReportEntity, "new_weekly_index");
                report.HappyWeekTopic = ConvertIndexToTopic(m_ToolUtilityClass.GetOptionSetAttribute(this.m_WeeklyReportEntity, "new_topic"));
            }
            else
            {
                report.HappyWeekIndex = "";
                report.HappyWeekTopic = "";
            }
        }

        /// <summary>
        /// 設定通用週報資料（小組日誌、分析及暫停）
        /// </summary>
        private void SetupCommonWeeklyData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport report)
        {
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                report.WeeklyReportData = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_memo");
                report.WeeklyReportAnalysis = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_sunday_present_report");
                report.PauseCheckBox = this.m_ToolUtilityClass.GetOptionSetAttribute(ref this.m_WeeklyReportEntity, "new_weekly_report_status") == 100000002;
            }
            else
            {
                report.WeeklyReportData = "";
                report.WeeklyReportAnalysis = "";
                report.PauseCheckBox = false;
            }
        }

        #endregion

        #region 週報圖表資料設定

        /// <summary>
        /// 設定週報圖表資料
        /// </summary>
        public void SetupWeeklyReportChartData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化圖表資料
            InitializeChartData(ref aListSmallGroupWeeklyReport);

            // ? 極速：圖表資料依「小組ID + 主日」快取 15 分鐘，所有使用者共享
            // Session 安全：圖表僅含出席人數統計，無個人資料
            string chartCacheKey = $"WeeklyReportChart_{this.m_ListEntity.Id:N}_{this.m_Sunday:yyyyMMdd}";
            if (!_optionSetCache.TryGetValue(chartCacheKey, out EntityCollection GroupWeeklyReportEntityCollection))
            {
                GroupWeeklyReportEntityCollection = this.m_ToolUtilityClass.QueryWeeklyReportBeforeTowMonthOfSunday(this.m_Sunday, this.m_ListEntity.Id);
                _optionSetCache.Set(chartCacheKey, GroupWeeklyReportEntityCollection,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(15)));
            }

            // 填充圖表資料
            foreach (Entity aWeeklyReporEntity in GroupWeeklyReportEntityCollection.Entities)
            {
                int aSundayNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_sunday_present_number");
                int aSmallNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_small_group_number");

                aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList.Add(new ChartData
                {
                    WeeklyReportEntityId = aWeeklyReporEntity.Id.ToString(),
                    SundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReporEntity, "new_sunday_date").ToLocalTime().ToShortDateString(),
                    SundayNumber = Math.Max(aSundayNumber, 0),
                    SmallNumber = Math.Max(aSmallNumber, 0),
                });
            }
        }

        /// <summary>
        /// 使用 operation-local service 讀取週報圖表資料。
        ///
        /// <para>
        /// 這個尚未由 Core 入口啟用的 private overload 不讀寫共用圖表快取；圖表結果可能反映授權
        /// 範圍，若沒有完整 validated isolation boundary 的 cache key 就不可共享。查詢採明確名單
        /// ID 與固定兩個月期間，避免全表掃描。service 只由當次同步查詢使用，且不會被保存、包裝或
        /// Dispose。
        /// </para>
        /// </summary>
        /// <param name="aListSmallGroupWeeklyReport">含目前已驗證名單 ID 與下載日期的輸出模型。</param>
        /// <param name="organizationService">呼叫端借用、仍由其 owner 回收的 CRM service。</param>
        /// <exception cref="ArgumentNullException">當輸出模型或 service 未提供時擲回。</exception>
        /// <exception cref="InvalidOperationException">當缺少可驗證的名單 ID 或下載日期時擲回。</exception>
        private void SetupWeeklyReportChartData(
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport,
            IOrganizationService organizationService)
        {
            ArgumentNullException.ThrowIfNull(aListSmallGroupWeeklyReport);
            ArgumentNullException.ThrowIfNull(organizationService);

            if (!Guid.TryParse(aListSmallGroupWeeklyReport.ListEntityId, out var listId)
                || aListSmallGroupWeeklyReport.SundayPrayers == default)
            {
                throw new InvalidOperationException(
                    "operation-local 週報圖表查詢需要已驗證的名單識別與下載日期，拒絕回落至 DownloadIntegrateData instance state。");
            }

            InitializeChartData(ref aListSmallGroupWeeklyReport);
            var sunday = SundayCalculator.CalculateSunday(
                aListSmallGroupWeeklyReport.SundayPrayers,
                WeeklyScheduleProvider.FirstDayOfWeek);
            var weeklyReports = organizationService.RetrieveMultiple(CreateWeeklyReportChartQuery(sunday, listId));

            foreach (var weeklyReport in weeklyReports.Entities)
            {
                var sundayDate = weeklyReport.GetAttributeValue<DateTime?>("new_sunday_date") ?? default;
                var sundayNumber = weeklyReport.GetAttributeValue<int?>("new_sunday_present_number") ?? 0;
                var smallNumber = weeklyReport.GetAttributeValue<int?>("new_small_group_number") ?? 0;

                aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList.Add(new ChartData
                {
                    WeeklyReportEntityId = weeklyReport.Id.ToString(),
                    SundayDate = sundayDate == default ? string.Empty : sundayDate.ToLocalTime().ToShortDateString(),
                    SundayNumber = Math.Max(sundayNumber, 0),
                    SmallNumber = Math.Max(smallNumber, 0)
                });
            }
        }

        /// <summary>
        /// 建立 operation-local 週報圖表的固定範圍查詢。
        /// 此方法只建構短生命期 <see cref="QueryExpression"/>，不保存名單 ID、日期、service 或查詢
        /// 結果。上游已驗證 <paramref name="listId"/> 的授權；缺少驗證時呼叫端必須在進入本 helper
        /// 前 fail closed，而不是把 caller-provided 路由值當作 authority。
        /// </summary>
        /// <param name="sunday">目前操作週期所對應的主日。</param>
        /// <param name="listId">上游已驗證授權的名單 ID。</param>
        /// <returns>只涵蓋目前名單與固定兩個月時間窗的查詢。</returns>
        private static QueryExpression CreateWeeklyReportChartQuery(DateTime sunday, Guid listId)
        {
            var query = new QueryExpression("new_group_present_weekly_report")
            {
                ColumnSet = new ColumnSet(
                    "new_sunday_date",
                    "new_sunday_present_number",
                    "new_small_group_number"),
                Criteria = new FilterExpression(LogicalOperator.And),
                Orders = { new OrderExpression("new_sunday_date", OrderType.Ascending) }
            };

            query.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.OnOrAfter, sunday.AddMonths(-2));
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.OnOrBefore, sunday);
            return query;
        }

        /// <summary>
        /// 初始化圖表資料結構
        /// </summary>
        private void InitializeChartData(ref ListSmallGroupWeeklyReport report)
        {
            if (report.m_WeeklyReportChart == null)
            {
                report.m_WeeklyReportChart = new ChartDataList
                {
                    m_ChartDataList = new List<ChartData>()
                };
            }
            else
            {
                if (report.m_WeeklyReportChart.m_ChartDataList != null)
                {
                    report.m_WeeklyReportChart.m_ChartDataList.Clear();
                }
                else
                {
                    report.m_WeeklyReportChart.m_ChartDataList = new List<ChartData>();
                }
            }
        }

        #endregion

        #region 小組資料分類

        /// <summary>
        /// 設定小組牧養資料（過濾掉新朋友和未入組）
        /// ? 極速：與 SetNewPersonFollowUpData 合併為單次遍歷，省去第二次迭代
        /// </summary>
        private void SetSmallGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            var allMembers = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members;
            int capacity = allMembers.Count;

            var smallGroupMembers   = new List<Member>(capacity);
            var newPersonMembers    = new List<Member>(capacity);

            foreach (Member aMember in allMembers)
            {
                bool isNewComer = aMember.Status.Contains("新朋友") || aMember.Status.Contains("未入組");

                if (isNewComer)
                {
                    newPersonMembers.Add(aMember);
                }
                else if (!aMember.Status.Contains("外教會") && !aMember.Status.Contains("結案"))
                {
                    smallGroupMembers.Add(aMember);
                }
            }

            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData = new SmallGroupData { Members = smallGroupMembers };
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData = new SmallGroupData { Members = newPersonMembers };
        }

        /// <summary>
        /// 設定新人跟進資料（已整合入 SetSmallGroupData，保留以維持相容性）
        /// </summary>
        private static void SetNewPersonFollowUpData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 已在 SetSmallGroupData 的單次遍歷中完成，無需重複處理
        }

        /// <summary>
        /// 設定幸福小組資料（包含所有成員）
        /// </summary>
        private void SetHappyGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup = new SmallGroupData
            {
                Members = new List<Member>()
            };

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members.Add(aMember);
            }
        }

        #endregion
    }
}
