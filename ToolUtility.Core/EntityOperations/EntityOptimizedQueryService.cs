#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.CollectionOperations;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 欄位選取、條件、完整批次與單頁查詢的來源相容入口；共用既有 Core Query／Collection 唯一驗證實作。
    /// 服務僅借用 host 已授權 scope 的 SDK，不保存 logger、查詢、Entity、cookie、使用者或 credential。
    /// </summary>
    /// <remarks>
    /// 同步 SDK 仍在 caller 執行緒上執行；Task 包裝不建立 Task.Run、timer、queue 或 cancellation registration。
    /// caller 取消在 SDK 前後檢查，不能中斷正在執行的同步 I/O；host 擁有傳輸 timeout、並行上限與 drain。
    /// 批次完整讀取沿用 Collection 的 5000 列／1000 頁本地預算；單頁保留 metadata，由 caller 接續。
    /// Dispose 停止新入口並清借用參照；已取得局部 SDK 的操作可完成，host 必須先 drain 才 Dispose SDK。
    /// 故障保留真實 Exception 交最終 host 共用告警；不在 Core 記錄個資、原訊息或以空資料偽裝成功。
    /// </remarks>
    public class EntityOptimizedQueryService : IDisposable
    {
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>保留舊 logger 必填簽章但不持有或反射它；SDK 只借用至服務 Dispose 或 host scope 結束。</summary>
        /// <param name="logger">相容診斷參數，不保存、不處置；效能及錯誤輸出由 host 擁有。</param>
        /// <param name="organizationService">host 已授權的 SDK，不得跨使用者／租戶共用此服務實例。</param>
        public EntityOptimizedQueryService(object logger, IOrganizationService organizationService)
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

        /// <summary>沿用指定欄位／缺省 AllColumns；委派唯一單筆驗證以核對正式 ID、根表與非 null 回應。</summary>
        public Entity RetrieveEntity(string entityName, Guid entityId, params string[]? columns)
        {
            using var query = new EntityQueryService(organizationService: GetService());
            return query.RetrieveEntity(entityName, entityId, Columns(columns));
        }

        /// <summary>同一單筆查詢的完成 Task；操作前後檢查 caller 取消，不保留結果或增加背景排程。</summary>
        public Task<Entity> RetrieveEntityAsync(string entityName, Guid entityId,
            CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => RetrieveEntity(entityName, entityId, columns), cancellationToken);

        /// <summary>
        /// 保留無 statecode 的字典批次及 null／空輸入語義；完整有界分頁後才回傳，不覆寫重複正式 ID。
        /// 相同顯示名但不同 ID 全部保留；缺失／不可見 ID 不補造，SDK 失敗不隱藏重試。
        /// </summary>
        public Dictionary<Guid, Entity> RetrieveBatch(string entityName, List<Guid>? entityIds, params string[]? columns) =>
            Batch(GetService(), entityName, entityIds, columns, default).GetAwaiter().GetResult();

        /// <summary>與同步批次共用完整候選及預算；caller 取消在每頁邊界檢查，後頁失敗不回部分字典。</summary>
        public Task<Dictionary<Guid, Entity>> RetrieveBatchAsync(string entityName, List<Guid>? entityIds,
            CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => Batch(GetService(), entityName, entityIds, columns, cancellationToken).GetAwaiter().GetResult(), cancellationToken);

        /// <summary>
        /// 保留 AllColumns／top 與原直接 statecode 條件；沒有直接 statecode 時，以局部 AND 包原 filter 加啟用條件。
        /// 修正原先修改 caller filter 及把啟用條件塞進 OR 擴張範圍；輸入樹借讀、執行中 caller 不可並行修改。
        /// top 本地預算 1..5000；完整驗證單頁後回傳，MoreRecords 保留、不宣稱取得整表。
        /// </summary>
        public EntityCollection RetrieveByCondition(string entityName, FilterExpression? filter, int topCount = 1000,
            params string[]? columns)
        {
            var sdk = GetService();
            if (topCount < 1 || topCount > 5000) throw new ArgumentOutOfRangeException(nameof(topCount));
            var criteria = filter;
            if (filter == null || !filter.Conditions.Any(condition =>
                string.Equals(condition.AttributeName, "statecode", StringComparison.OrdinalIgnoreCase)))
            {
                criteria = new FilterExpression(LogicalOperator.And);
                if (filter != null) criteria.Filters.Add(filter);
                criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            }
            var expression = new QueryExpression(entityName)
            {
                ColumnSet = Columns(columns), TopCount = topCount, Criteria = criteria
            };
            using var query = new EntityQueryService(organizationService: sdk);
            var page = query.RetrieveMultiple(expression);
            if (page.Entities.Count > topCount)
                throw new InvalidOperationException("The SDK exceeded the requested row budget.");
            return page;
        }

        /// <summary>同一局部條件查詢的完成 Task；內部 timeout 保持故障，不因例外自帶 token 而誤認 caller 取消。</summary>
        public Task<EntityCollection> RetrieveByConditionAsync(string entityName, FilterExpression? filter, int topCount = 1000,
            CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => RetrieveByCondition(entityName, filter, topCount, columns), cancellationToken);

        /// <summary>以 SDK 等值條件傳入值而非拼接 XML；欄位值不是授權，條件樹只活至此次操作結束。</summary>
        public EntityCollection RetrieveByFieldValue(string entityName, string fieldName, object fieldValue,
            int topCount = 100, params string[]? columns)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentException("A field name is required.", nameof(fieldName));
            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition(fieldName, ConditionOperator.Equal, fieldValue);
            return RetrieveByCondition(entityName, filter, topCount, columns);
        }

        /// <summary>保留欄位查詢預設 top=100；明確 caller 取消前不 I/O，SDK 完成後取消也不發布成功候選。</summary>
        public Task<EntityCollection> RetrieveByFieldValueAsync(string entityName, string fieldName, object fieldValue,
            int topCount = 100, CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => RetrieveByFieldValue(entityName, fieldName, fieldValue, topCount, columns), cancellationToken);

        /// <summary>
        /// 相容 cookie 入口，安全解析官方 cookie 的上一頁頁碼後加一，不再固定第二頁；無 cookie 是第一頁。
        /// 保留原缺省「無 statecode 過濾」差異，單頁以正式主鍵排序；不跨授權 scope 保存游標。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedAsync(string entityName, FilterExpression? filter = null,
            int pageSize = 100, string? pagingCookie = null, CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => Page(GetService(), entityName, null, filter, pageSize, pagingCookie, columns, cancellationToken), cancellationToken);

        /// <summary>
        /// 明確頁碼多載支援沒有 cookie 的後續頁；不新增既有介面必要成員。
        /// 頁數、頁長、cookie 及候選完整性共用 Collection 守門，caller 擁有此次回傳 DTO 及後續續頁。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedAsync(string entityName, int pageNumber, FilterExpression? filter = null,
            int pageSize = 100, string? pagingCookie = null, CancellationToken cancellationToken = default, params string[]? columns) =>
            ExecuteAsync(() => Page(GetService(), entityName, pageNumber, filter, pageSize, pagingCookie, columns, cancellationToken), cancellationToken);

        /// <summary>局部 adapter 由本次方法擁有且 using 釋放；不複製另一套 ID／分頁驗證或保存來源清單。</summary>
        private static async Task<Dictionary<Guid, Entity>> Batch(IOrganizationService sdk, string name, List<Guid>? ids,
            string[]? columns, CancellationToken cancellationToken)
        {
            using var query = new CollectionQueryService(new object(), sdk);
            return await query.RetrieveBatchByIdsAsync(name, ids, Columns(columns), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>局部 adapter 維持舊空 filter 差異，單頁結果清單移轉給此 DTO；服務不保留結果。</summary>
        private static PagedResult<Entity> Page(IOrganizationService sdk, string name, int? number, FilterExpression? filter,
            int size, string? cookie, string[]? columns, CancellationToken cancellationToken)
        {
            using var query = new CollectionQueryService(new object(), sdk);
            filter ??= new FilterExpression(LogicalOperator.And);
            var page = (number.HasValue
                ? query.RetrievePagedEntitiesAsync(name, number.Value, filter, Columns(columns), size, cookie, cancellationToken)
                : query.RetrievePagedEntitiesAsync(name, filter, Columns(columns), size, cookie, cancellationToken)).GetAwaiter().GetResult();
            return new PagedResult<Entity>
            {
                Entities = page.Entities, MoreRecords = page.MoreRecords,
                PagingCookie = page.PagingCookie, TotalCount = page.TotalCount
            };
        }

        /// <summary>每次新建欄位容器；caller 的陣列只在同步建構時借讀，不把 caller 陣列存入服務狀態。</summary>
        private static ColumnSet Columns(string[]? columns) => columns != null && columns.Length > 0
            ? new ColumnSet(columns) : new ColumnSet(true);

        /// <summary>同步 SDK 不以 Task.Run 偽裝非同步；只有明確 caller token 已取消才分類為正常取消。</summary>
        private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
            try
            {
                var result = operation();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Task.FromCanceled<T>(cancellationToken); }
            catch (Exception failure) { return Task.FromException<T>(failure); }
        }

        /// <summary>取得單次操作借用，Dispose 後拒絕新入口；不用全域鎖序列化不同使用者的查詢。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(EntityOptimizedQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(EntityOptimizedQueryService));
        }

        /// <summary>保留可覆寫的舊 Dispose hook；冪等清除借用參照，既有同步操作由 host 排空，不處置 SDK。</summary>
        /// <param name="disposing">沿用標準 Dispose 模式；本服務沒有自有 unmanaged 或背景資源。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>停止新入口、清除 SDK 參照並抑制終結器；不接管 host 連線所有權。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>保留舊 namespace／setter 的操作自有分頁 DTO；不能當 Session 或共享快照。</summary>
    public class PagedResult<T>
    {
        /// <summary>本次頁面獨占的可變清單；caller 修改後不能宣稱仍是已驗共享資料。</summary>
        public List<T> Entities { get; set; } = new List<T>();
        /// <summary>SDK 的原始總數，未知值不以頁面筆數代替。</summary>
        public int TotalCount { get; set; }
        /// <summary>SDK 的原始續頁旗標；true 表示不能把此頁冒充完整集合。</summary>
        public bool MoreRecords { get; set; }
        /// <summary>只活於同一已驗證 scope 的原始 cookie，可為 null；服務不保存。</summary>
        public string? PagingCookie { get; set; }
    }
}
