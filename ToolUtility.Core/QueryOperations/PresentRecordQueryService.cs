/*
 * 檔案整體說明
 *
 * 目的與定位：出席與週報八個純SDK單頁查詢；保留原欄位、排序、filter位置、日期CLR型別及UTC動態界線。 ID與metadata只作查詢驗證，不構成授權；host須先驗使用者、租戶、教會及名單scope。
 *
 * 主要功能與流程：
 * 本檔宣告 PresentRecordQueryService。
 * - 保持原建構簽章及null拒絕；logger只驗證不保存或反射，SDK只借用當前已授權scope。
 * - 搜尋主日日期是最近N週的靈修單 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
 * - 根據主日日期排序查詢出席記錄 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
 * - 使用 FetchXML 查詢最近N週的出席記錄 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
 * 主要操作入口包含 QueryPresentRecordByContactIdAndSunday、QueryPresentRecordSortBySunday、
 * QueryPresentRecordSortBySundayFetchXml、QueryPresentRecordInWeeklyReportByContactId、QueryEntityListByDate、
 * QueryWeeklyReportBySunday、QueryWeeklyReportBeforeTwoMonthOfSunday、QueryListByContactId；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Tests/DataOperations/PresentQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/PresentQuerySafetyTests.cs、ToolUtility/Core/ToolUtilityFacade.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using System.Globalization;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 出席與週報八個純SDK單頁查詢；保留原欄位、排序、filter位置、日期CLR型別及UTC動態界線。
    /// ID與metadata只作查詢驗證，不構成授權；host須先驗使用者、租戶、教會及名單scope。
    /// </summary>
    /// <remarks>
    /// 不保存logger、結果、credential或跨request可變狀態；不建cache、timer、queue或背景Task。
    /// 每頁最多5000列，驗完整候選後回傳原metadata，不按姓名合併或冒稱全表。
    /// 借入SDK由host負責timeout與drain；Dispose只拒新入口並清參照，已開始同步I/O可排空，不Dispose外部連線。
    /// 舊日期算法保留於本次抽取，未把Unspecified或本地日期暗轉UTC；P1-06另有明確教會時鐘契約。
    /// </remarks>
    public class PresentRecordQueryService : IPresentRecordQueryService, IDisposable
    {
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>保持原建構簽章及null拒絕；logger只驗證不保存或反射，SDK只借用當前已授權scope。</summary>
        /// <param name="logger">舊來源相容參數，不作request、Session或秘密的owner。</param>
        /// <param name="organizationService">host借入的SDK，不能把此服務跨使用者註冊為singleton。</param>
        public PresentRecordQueryService(object logger, IOrganizationService organizationService)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (organizationService == null) throw new ArgumentNullException(nameof(organizationService));
#else
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(organizationService);
#endif
            _organizationService = organizationService;
        }

        /// <summary>
        /// 搜尋主日日期是最近N週的靈修單
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int weekPeriod)
        {
            var sdk = GetService();
            RequireId(listEntityId); RequireId(contactId); RequireWeeks(weekPeriod);
            var query = new QueryExpression
            {
                EntityName = "new_present_record",
                ColumnSet = new ColumnSet("new_sunday_present_this_week", "new_group_present_this_week")
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition("new_list_new_present_record", ConditionOperator.Equal, listEntityId);
            filter.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
            filter.AddCondition("new_sunday_date", ConditionOperator.LastXWeeks, weekPeriod);
            query.Criteria = filter;

            query.AddOrder("new_sunday_date", OrderType.Descending);

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 根據主日日期排序查詢出席記錄
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            var sdk = GetService();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var query = new QueryExpression
            {
                EntityName = childEntityName,
                ColumnSet = new ColumnSet(true)
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition(associationName, ConditionOperator.Equal, parentEntityId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            filter.AddCondition("new_sunday_date", ConditionOperator.GreaterEqual, DateTime.UtcNow.AddDays(-32));
            query.Criteria = filter;

            query.AddOrder("new_sunday_date", OrderType.Ascending);

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 使用 FetchXML 查詢最近N週的出席記錄
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
        {
            var sdk = GetService();
            RequireWeeks(lastWeeks);
            var fields = new[] { "new_present_recordid", "new_name", "createdon", "new_sunday_date",
                "new_groupleader_present_record", "new_followup_ways", "new_follow_up", "new_conclusion_choise",
                "new_next_step", "new_explanation" };
            var entity = SdkDataQuery.Entity("new_present_record", fields, "new_name");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Lookup("new_contact_new_present_record", contactName, "contact", contactId),
                SdkDataQuery.Condition("new_sunday_date", "last-x-weeks", lastWeeks.ToString(CultureInfo.InvariantCulture))));
            return SdkDataQuery.Run(sdk, entity, executeRequest: true);
        }

        /// <summary>
        /// 根據週報和聯絡人ID查詢出席記錄
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId)
        {
            var sdk = GetService();
            RequireId(contactId); RequireId(weeklyReportEntityId);
            var query = new QueryExpression
            {
                EntityName = "new_present_record",
                ColumnSet = new ColumnSet(true)
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition("new_group_present_weekly_report_prese", ConditionOperator.Equal, weeklyReportEntityId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            filter.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
            query.Criteria = filter;

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 根據日期範圍查詢實體清單
        /// 修復：new_class_end_date 是父實體的欄位，需要使用 LinkEntity 關聯
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            var sdk = GetService();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var query = new QueryExpression
            {
                EntityName = childEntityName,
                ColumnSet = new ColumnSet(true)
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition(associationName, ConditionOperator.Equal, parentEntityId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria = filter;

            // 父實體欄位歸屬：new_class_end_date 是父實體 (new_disciple_lessons) 的欄位
            // 需要透過 LinkEntity 關聯到父實體，並在父實體上添加日期過濾條件
            if (parentEntityName == "new_disciple_lessons")
            {
                var linkEntity = new LinkEntity
                {
                    LinkFromEntityName = childEntityName,
                    LinkToEntityName = parentEntityName,
                    LinkFromAttributeName = associationName,
                    LinkToAttributeName = parentEntityIdName,
                    JoinOperator = JoinOperator.Inner
                };

                // 在父實體 (課程) 上添加日期過濾：課程結束日期在最近7天內或之後
                var linkFilter = new FilterExpression(LogicalOperator.And);
                linkFilter.AddCondition("new_class_end_date", ConditionOperator.OnOrAfter, DateTime.UtcNow.AddDays(-7));
                linkEntity.LinkCriteria = linkFilter;

                query.LinkEntities.Add(linkEntity);
            }

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 查詢週報(根據主日日期)
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId)
        {
            var sdk = GetService();
            RequireId(listEntityId);
            var query = new QueryExpression
            {
                EntityName = "new_group_present_weekly_report",
                ColumnSet = new ColumnSet("new_small_group_member_number", "new_sunday_present_number",
                    "new_sunday_present_rate", "new_small_group_number", "new_small_group_rate",
                    "new_memo", "new_weekly_report_status")
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listEntityId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            filter.AddCondition("new_sunday_date", ConditionOperator.On, sunday);
            query.Criteria = filter;

            query.AddOrder("new_sunday_date", OrderType.Descending);

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 查詢週報(主日日期前兩個月)
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime sunday, Guid listEntityId)
        {
            var sdk = GetService();
            RequireId(listEntityId);
            var query = new QueryExpression
            {
                EntityName = "new_group_present_weekly_report",
                ColumnSet = new ColumnSet(true)
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listEntityId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            filter.AddCondition("new_sunday_date", ConditionOperator.OnOrAfter, sunday.AddMonths(-2));
            filter.AddCondition("new_sunday_date", ConditionOperator.OnOrBefore, sunday);
            query.Criteria = filter;

            query.AddOrder("new_sunday_date", OrderType.Ascending);

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// 本次操作僅持有局部候選，整頁正式ID驗證成功才發布；SDK故障原樣傳至host共用告警。
        /// </summary>
        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            var sdk = GetService();
            RequireId(contactId); SdkDataQuery.RequireLogicalName(associationName, nameof(associationName));
            var query = new QueryExpression
            {
                EntityName = "list",
                ColumnSet = new ColumnSet(true)
            };

            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition(associationName, ConditionOperator.Equal, contactId);
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            filter.AddCondition("new_app_named", ConditionOperator.Equal, true);
            query.Criteria = filter;

            query.AddOrder("listname", OrderType.Ascending);

            return SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);
        }

        /// <summary>週數須為正整數，拒絕而不去掉期間條件；不任意縮短caller合法範圍。</summary>
        private static void RequireWeeks(int weeks)
        {
            if (weeks < 1) throw new ArgumentOutOfRangeException(nameof(weeks), "A positive week period is required.");
        }

        /// <summary>結構名稱與父正式GUID在I/O前驗證；條件仍保留原字串型別，不以格式合法冒充scope授權。</summary>
        private static void RequireParent(string parent, string idField, string id, string association, string child)
        {
            SdkDataQuery.RequireLogicalName(parent, nameof(parent));
            SdkDataQuery.RequireLogicalName(idField, nameof(idField));
            SdkDataQuery.RequireLogicalName(association, nameof(association));
            SdkDataQuery.RequireLogicalName(child, nameof(child));
            if (!Guid.TryParse(id, out var parsed)) throw new ArgumentException("A database identifier is required.", nameof(id));
            RequireId(parsed);
        }

        /// <summary>空GUID不能作正式父／聯絡人身份，也不轉為無篩選查詢；錯誤不回顯原資料。</summary>
        private static void RequireId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(id));
        }

        /// <summary>局部參照是單次同步操作owner；Dispose後拒絕新工作，不序列化不同租戶SDK或暗中中止已入場I/O。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(PresentRecordQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(PresentRecordQueryService));
        }

        /// <summary>標準可覆寫清理只冪等清借用SDK，已開始同步操作由host drain；不接管任何外部連線所有權。</summary>
        /// <param name="disposing">本類無自有unmanaged或背景資源；衍生型別可依標準模式擴充。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>停止新入口並清強參照，沒有終結器、logger、訂閱或隱藏委派要保留。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
