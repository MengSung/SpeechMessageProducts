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
    /// 六個N:1／N:N純SDK單頁入口；完整保留原link方向、條件值型別、欄位與排序，不按顯示名去重。
    /// 結構名稱及正式GUID在I/O前驗證，但不是權限；host必須先驗使用者／教會／組織及所需資料scope。
    /// </summary>
    /// <remarks>
    /// 僅借用SDK，不持有logger、credential、查詢或結果，不設cache／timer／queue／背景工作。
    /// 每頁本地最多5000列，先核預算及全部根表/穩定ID再發布；MoreRecords及cookie原樣，不能冒稱完整全表。
    /// 同步I/O逾時與並行界線屬host；例外原樣向最終共用告警傳遞，本核心不吞日誌錯誤或記錄原資料。
    /// Dispose停止新入口並清SDK參照，已入場操作可drain，host先排空才Dispose原連線。
    /// </remarks>
    public class RelationshipQueryService : IRelationshipQueryService, IDisposable
    {
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>保持原logger必填建構簽章，只驗null不保存／反射；SDK所有權仍屬原授權host scope。</summary>
        /// <param name="logger">來源相容的host診斷參數，不藉此保留request或credential。</param>
        /// <param name="organizationService">已授權的SDK，不能跨使用者／租戶singleton共用此服務。</param>
        public RelationshipQueryService(object logger, IOrganizationService organizationService)
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

        /// <summary>按父正式ID查N:1子頁；啟用條件留在原link filter而不是暗中加在根表，父ID保留原字串型別。</summary>
        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            var sdk = GetService();
            return Read(sdk, ParentQuery(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName));
        }

        /// <summary>與N:1相同link條件，僅依原契約新增根表listname升序；名稱只排序，不用作身份或合併鍵。</summary>
        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            var sdk = GetService();
            var query = ParentQuery(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            query.AddOrder("listname", OrderType.Ascending);
            return Read(sdk, query);
        }

        /// <summary>保留contact兩欄及account內關聯一欄/alias；此舊示例沒有父篩選，host必須已授權整個SDK可見範圍。</summary>
        public EntityCollection RetrieveManyToOneWithLinkEntity()
        {
            var sdk = GetService();
            var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("fullname", "address1_city") };
            query.LinkEntities.Add(new LinkEntity
            {
                LinkFromEntityName = "contact", LinkFromAttributeName = "parentcustomerid", LinkToEntityName = "account",
                LinkToAttributeName = "accountid", JoinOperator = JoinOperator.Inner,
                Columns = new ColumnSet("accountnumber"), EntityAlias = "aliasAccount"
            });
            return Read(sdk, query);
        }

        /// <summary>
        /// 保留原週報link filter的Equal及日期字串型別／CurrentCulture格式，不把抽取混成日期語義改寫。
        /// 此入口不轉換時區或修改ambient culture；教會時鐘整合應由後續已核定P1-06明確處理。
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var sdk = GetService();
            var query = ParentQuery(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            query.LinkEntities[0].LinkCriteria.AddCondition("new_sunday_date", ConditionOperator.Equal,
                sunday.ToString(CultureInfo.CurrentCulture));
            query.AddOrder("new_sunday_date", OrderType.Descending);
            return Read(sdk, query);
        }

        /// <summary>動態N:N入口維持根表bool／啟用與link的Guid條件；七個結構名稱先驗證，不將編碼當合法metadata。</summary>
        public EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue)
        {
            var sdk = GetService();
            RequireNames(conditionAttributeName, entityNameToSearch, linkFromEntityName, linkFromAttributeName,
                linkToEntityName, linkToAttributeName, attributeName);
            RequireId(entityIdValue);
            var query = new QueryExpression(entityNameToSearch) { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition(conditionAttributeName, ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var link = new LinkEntity
            {
                LinkFromEntityName = linkFromEntityName, LinkFromAttributeName = linkFromAttributeName,
                LinkToEntityName = linkToEntityName, LinkToAttributeName = linkToAttributeName
            };
            link.LinkCriteria.AddCondition(attributeName, ConditionOperator.Equal, entityIdValue);
            query.LinkEntities.Add(link);
            return Read(sdk, query);
        }

        /// <summary>聯絡人名單是同一N:N入口的固定metadata組合，不維護第二套查詢或結果cache；非空GUID不等於已授權。</summary>
        public EntityCollection QueryListOfContactManyToMany(Guid contactId) =>
            QueryManyToMany("new_app_named", "list", "list", "listid", "listmember", "listid", "entityid", contactId);

        /// <summary>建立操作局部父關聯樹；原parent ID字串保留，GUID只驗合法非空，不改成另一種條件型別。</summary>
        private static QueryExpression ParentQuery(string parent, string idField, string id, string association, string child)
        {
            RequireNames(parent, idField, association, child);
            if (!Guid.TryParse(id, out var parsed)) throw new ArgumentException("A database identifier is required.", nameof(id));
            RequireId(parsed);
            var query = new QueryExpression(child) { ColumnSet = new ColumnSet(true) };
            var link = new LinkEntity
            {
                LinkFromEntityName = child, LinkFromAttributeName = association,
                LinkToEntityName = parent, LinkToAttributeName = idField
            };
            link.LinkCriteria.AddCondition(idField, ConditionOperator.Equal, id);
            link.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.LinkEntities.Add(link);
            return query;
        }

        /// <summary>metadata只允許共享SDK名稱規則；有限params陣列只活於本次呼叫，不保存輸入或作授權。</summary>
        private static void RequireNames(params string[] names)
        {
            foreach (var name in names) SdkDataQuery.RequireLogicalName(name, nameof(names));
        }

        /// <summary>空GUID不能作正式父／聯絡人身份，也不轉為無篩選查詢；錯誤不回顯原資料。</summary>
        private static void RequireId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(id));
        }

        /// <summary>不改原SDK入口/QueryExpression/PageInfo，借用共用整頁驗證與5000列預算，不發布部分或重複身份。</summary>
        private static EntityCollection Read(IOrganizationService sdk, QueryExpression query) =>
            SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, 5000);

        /// <summary>局部參照是單次同步操作owner；Dispose後拒絕新工作，不序列化不同租戶SDK或暗中中止已入場I/O。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(RelationshipQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(RelationshipQueryService));
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
