#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 純 SDK 集合讀取器；每次建立操作局部候選，驗證正式 ID／表名後才回傳，不保存結果或 logger。
    /// host 必須先完成授權且以同一 scope 借入 SDK，並擁有連線逾時、並行政策、排空及 Dispose。
    /// </summary>
    /// <remarks>
    /// 同步 SDK 在 caller 執行緒上執行；Task 包裝不建立背景工作，也不宣稱能中斷已進入 SDK 的同步 I/O。
    /// caller 取消會在 I/O 前後及每個分頁邊界檢查；內部 timeout 保留真實例外，交最終 host 共用告警。
    /// 完整批次本地預算為 5000 輸入／輸出列、1000 頁，並非 D365 實測限制；超出直接失敗，不發布部分資料。
    /// 單頁 API 保留 MoreRecords／cookie，由 caller 續讀；可變 DTO 只屬本次操作，禁止當成共享快照。
    /// Dispose 停止新入口並清除借用 SDK；已取得局部參照的操作由 host drain，不處置借入 SDK。
    /// </remarks>
    public class CollectionQueryService : ICollectionQueryService, IDisposable
    {
        private const int MaximumRows = 5000;
        private const int MaximumPages = 1000;
        private const int MaximumCookieCharacters = 65536;
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>保留原必填 logger 建構契約但不保存它；SDK 僅借用至服務 Dispose 或 scope 結束。</summary>
        /// <param name="logger">舊來源相容參數，不保存、不反射、不處置。</param>
        /// <param name="organizationService">host 已授權且擁有的 SDK，不可跨使用者／租戶共用服務實例。</param>
        public CollectionQueryService(object logger, IOrganizationService organizationService)
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

        /// <summary>沿用 AllColumns、欄位值與 statecode=0，單頁結果保留分頁 metadata；不得把此值當授權。</summary>
        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue) =>
            Field(GetService(), entityName, fieldName, fieldValue);

        /// <summary>保留週報 Execute 訊息、主日前兩月閉區間及日期升冪；失敗固定訊息且保留真 InnerException。</summary>
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId) =>
            Weekly(GetService(), aSunday, aListEntityId);

        /// <summary>在 caller 執行緒完成相同單頁查詢；Task 只表達完成／故障／明確 caller 取消，不排程背景 I/O。</summary>
        public Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(string entityName, string fieldName, string fieldValue,
            CancellationToken cancellationToken = default) => ExecuteAsync(() =>
                Field(GetService(), entityName, fieldName, fieldValue), cancellationToken);

        /// <summary>保留單條件與 statecode=0 的 AND，驗完整單頁正式識別；值以 SDK 型別傳遞而非 XML 拼接。</summary>
        public Task<EntityCollection> RetrieveEntityCollectionByConditionAsync(string entityName, string fieldName,
            ConditionOperator conditionOperator, object value, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
        {
            var sdk = GetService();
            RequireName(fieldName, nameof(fieldName));
            var query = Query(entityName, null);
            query.Criteria.AddCondition(fieldName, conditionOperator, value);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return ValidatePage(sdk.RetrieveMultiple(query), entityName);
        }, cancellationToken);

        /// <summary>每個條件與啟用條件一併送出；輸入字典在同步操作期間須由 caller 獨占，不保存於服務。</summary>
        public Task<EntityCollection> RetrieveEntityCollectionByConditionsAsync(string entityName, Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default) => ExecuteAsync(() =>
        {
            var sdk = GetService();
            var query = Query(entityName, null);
#if NETFRAMEWORK
            if (conditions == null) throw new ArgumentNullException(nameof(conditions));
#else
            ArgumentNullException.ThrowIfNull(conditions);
#endif
            foreach (var condition in conditions)
            {
                RequireName(condition.Key, nameof(conditions));
                query.Criteria.AddCondition(condition.Key, ConditionOperator.Equal, condition.Value);
            }
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return ValidatePage(sdk.RetrieveMultiple(query), entityName);
        }, cancellationToken);

        /// <summary>與同步週報同一實作；caller 取消在 SDK 前後檢查，SDK 本身的 timeout 仍屬故障。</summary>
        public Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(DateTime aSunday, Guid aListEntityId,
            CancellationToken cancellationToken = default) => ExecuteAsync(() => Weekly(GetService(), aSunday, aListEntityId), cancellationToken);

        /// <summary>
        /// 相容 cookie 入口；官方 XML cookie 的 page 是上一頁，安全解析後加一，修正原固定第二頁缺陷。
        /// 空 cookie 代表第一頁；需要無 cookie 的後續頁時使用明確 pageNumber 多載，不在服務保存游標。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(string entityName, FilterExpression? filter = null,
            ColumnSet? columnSet = null, int pageSize = 100, string? pagingCookie = null, CancellationToken cancellationToken = default) =>
            ExecuteAsync(() => Page(GetService(), entityName, pagingCookie is null || pagingCookie.Length == 0 ? 1 : NextPage(pagingCookie),
                filter, columnSet, pageSize, pagingCookie), cancellationToken);

        /// <summary>
        /// 明確頁碼支援 SDK 沒有 cookie 的查詢；保留原介面不新增必要成員，避免破壞外部實作者。
        /// pageNumber／pageSize 有界；有 cookie 時必須與頁碼一致，filter 及 ColumnSet 在操作期間不可並行改動。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(string entityName, int pageNumber,
            FilterExpression? filter = null, ColumnSet? columnSet = null, int pageSize = 100, string? pagingCookie = null,
            CancellationToken cancellationToken = default) => ExecuteAsync(() =>
                Page(GetService(), entityName, pageNumber, filter, columnSet, pageSize, pagingCookie), cancellationToken);

        /// <summary>
        /// 保留啟用條件及 caller 指定主鍵欄位；有界擷取輸入，拒空 ID／重複輸入，不把空批次變成全表查詢。
        /// 所有分頁完成並驗證跨頁唯一及回傳 ID 在請求集合內，才發布候選；不存在或不可見的 ID 不補造資料。
        /// </summary>
        public Task<EntityCollection> RetrieveBatchByIdsAsync(string entityName, string idFieldName, IEnumerable<Guid> ids,
            ColumnSet? columnSet = null, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
        {
            var sdk = GetService();
            RequireName(idFieldName, nameof(idFieldName));
            var query = Query(entityName, columnSet);
            var requested = ReadIds(ids);
            if (requested.Count == 0) return new EntityCollection();
            query.Criteria.AddCondition(idFieldName, ConditionOperator.In, requested.Cast<object>().ToArray());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return ReadComplete(sdk, query, requested, cancellationToken);
        }, cancellationToken);

        /// <summary>
        /// 保留字典版本「沒有 statecode 過濾」的原差異；以 Add 而非索引覆寫保護同一消費集合唯一 ID。
        /// null／空輸入沿用空結果，但仍檢查服務狀態及 caller 取消；不同 ID 的同名資料完整保留。
        /// </summary>
        public Task<Dictionary<Guid, Entity>> RetrieveBatchByIdsAsync(string entityName, List<Guid>? entityIds,
            ColumnSet? columnSet = null, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
        {
            var sdk = GetService();
            var query = Query(entityName, columnSet);
            if (entityIds == null || entityIds.Count == 0) return new Dictionary<Guid, Entity>();
            var requested = ReadIds(entityIds);
            query.Criteria.AddCondition(entityName + "id", ConditionOperator.In, requested.Cast<object>().ToArray());
            query.PageInfo = new PagingInfo { Count = MaximumRows, PageNumber = 1 };
            var page = ReadComplete(sdk, query, requested, cancellationToken);
            var result = new Dictionary<Guid, Entity>(page.Entities.Count);
            foreach (var row in page.Entities) result.Add(row.Id, row);
            return result;
        }, cancellationToken);

        /// <summary>
        /// 保留字串 IN 與 statecode=0；本地輸入／輸出預算各 5000，結果先完整分頁驗證再回傳。
        /// null／空輸入沿用空集合，不執行全表掃描；任何 SDK 失敗原樣交回 host，不以空頁代替失敗。
        /// </summary>
        public Task<EntityCollection> RetrieveBatchByFieldValuesAsync(string entityName, string fieldName, List<string>? fieldValues,
            ColumnSet? columnSet = null, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
        {
            var sdk = GetService();
            var query = Query(entityName, columnSet);
            RequireName(fieldName, nameof(fieldName));
            if (fieldValues == null || fieldValues.Count == 0) return new EntityCollection();
            if (fieldValues.Count > MaximumRows) throw new ArgumentException("Query input exceeds the local row budget.", nameof(fieldValues));
            query.Criteria.AddCondition(fieldName, ConditionOperator.In, fieldValues.ToArray());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.PageInfo = new PagingInfo { Count = MaximumRows, PageNumber = 1 };
            return ReadComplete(sdk, query, null, cancellationToken);
        }, cancellationToken);

        /// <summary>單頁不隱藏 MoreRecords；建立的 query 與回傳值只屬此操作，沒有服務級快取。</summary>
        private static EntityCollection Field(IOrganizationService sdk, string name, string field, string value)
        {
            RequireName(name, nameof(name));
            RequireName(field, nameof(field));
            var query = new QueryByAttribute(name) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(field, "statecode");
            query.Values.AddRange(value, 0);
            return ValidatePage(sdk.RetrieveMultiple(query), name);
        }

        /// <summary>只包裝週報 SDK 故障，固定訊息不洩漏日期、ID 或例外本文；原物件保留於 InnerException。</summary>
        private static EntityCollection Weekly(IOrganizationService sdk, DateTime sunday, Guid listId)
        {
            if (listId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(listId));
            var query = Query("new_group_present_weekly_report", null);
            query.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.OnOrAfter, sunday.AddMonths(-2));
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.OnOrBefore, sunday);
            query.AddOrder("new_sunday_date", OrderType.Ascending);
            try
            {
                var response = sdk.Execute(new RetrieveMultipleRequest { Query = query }) as RetrieveMultipleResponse;
                return ValidatePage(response?.EntityCollection, query.EntityName);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("Weekly report query failed.", failure);
            }
        }

        /// <summary>有序單頁包含原始 continuation metadata；caller 持有 DTO，服務不保留 filter／cookie 或列。</summary>
        private static PagedResult<Entity> Page(IOrganizationService sdk, string name, int pageNumber,
            FilterExpression? filter, ColumnSet? columns, int size, string? cookie)
        {
            if (pageNumber < 1 || pageNumber > MaximumPages) throw new ArgumentOutOfRangeException(nameof(pageNumber));
            if (size < 1 || size > MaximumRows) throw new ArgumentOutOfRangeException(nameof(size));
            if (cookie is not null && cookie.Length != 0 && NextPage(cookie) != pageNumber)
                throw new ArgumentException("Paging cookie and page number disagree.", nameof(cookie));
            var query = Query(name, columns);
            if (filter != null) query.Criteria = filter;
            else query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder(name + "id", OrderType.Ascending);
            query.PageInfo = new PagingInfo { Count = size, PageNumber = pageNumber, PagingCookie = cookie };
            var page = ValidatePage(sdk.RetrieveMultiple(query), name);
            if (page.Entities.Count > size) throw new InvalidOperationException("The SDK exceeded the requested page size.");
            return new PagedResult<Entity>
            {
                Entities = new List<Entity>(page.Entities), TotalCount = page.TotalRecordCount,
                MoreRecords = page.MoreRecords, PagingCookie = page.PagingCookie
            };
        }

        /// <summary>
        /// 一般單頁保留原 golden；分頁時丟棄探測頁，以主鍵排序重讀第一頁，不混合兩種排序結果。
        /// cookie 可為 null，頁碼仍遞增；任一後頁故障、重複、空進度或超預算都拒絕整個候選，不隱藏重試。
        /// </summary>
        private static EntityCollection ReadComplete(IOrganizationService sdk, QueryExpression query, HashSet<Guid>? requested,
            CancellationToken cancellationToken)
        {
            var probe = ValidatePage(sdk.RetrieveMultiple(query), query.EntityName);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequested(probe, requested);
            if (!probe.MoreRecords) return probe;
            var result = new EntityCollection { EntityName = query.EntityName };
            var seen = new HashSet<Guid>();
            query.AddOrder(query.EntityName + "id", OrderType.Ascending);
            query.PageInfo = new PagingInfo { Count = MaximumRows, PageNumber = 1 };
            for (var number = 1; number <= MaximumPages; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                query.PageInfo.PageNumber = number;
                var page = ValidatePage(sdk.RetrieveMultiple(query), query.EntityName);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateRequested(page, requested);
                foreach (var row in page.Entities)
                {
                    if (!seen.Add(row.Id)) throw new InvalidOperationException("Query pages contain duplicate database identities.");
                    if (seen.Count > MaximumRows) throw new InvalidOperationException("Query output exceeds the local row budget.");
                    result.Entities.Add(row);
                }
                if (!page.MoreRecords) return result;
                if (seen.Count >= MaximumRows) throw new InvalidOperationException("Query output exceeds the local row budget.");
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            throw new InvalidOperationException("Query exceeds the local page budget.");
        }

        /// <summary>整頁含 metadata 必須對應根表；資料庫 ID 非空且唯一，不依名稱合併或偽造身份。</summary>
        private static EntityCollection ValidatePage(EntityCollection? page, string name)
        {
            if (page == null || page.Entities.Count > MaximumRows || (page.MoreRecords && page.Entities.Count == 0)
                || (!string.IsNullOrEmpty(page.EntityName) && !string.Equals(page.EntityName, name, StringComparison.Ordinal)))
                throw new InvalidOperationException("The SDK returned an invalid query page.");
            var seen = new HashSet<Guid>();
            foreach (var row in page.Entities)
                if (row == null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, name, StringComparison.Ordinal) || !seen.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return page;
        }

        /// <summary>只限制此次 ID 查詢的可回傳集合；不存在／不可見的記錄不補造，也不漏掉未請求列的診斷。</summary>
        private static void ValidateRequested(EntityCollection page, HashSet<Guid>? requested)
        {
            if (requested == null) return;
            foreach (var row in page.Entities)
                if (!requested.Contains(row.Id)) throw new InvalidOperationException("The SDK returned an unrequested database identity.");
        }

        /// <summary>逐項有界擷取輸入，最多前進 5001 次；拒空 ID 及重複，不保存來源 enumerable。</summary>
        private static HashSet<Guid> ReadIds(IEnumerable<Guid> ids)
        {
#if NETFRAMEWORK
            if (ids == null) throw new ArgumentNullException(nameof(ids));
#else
            ArgumentNullException.ThrowIfNull(ids);
#endif
            var result = new HashSet<Guid>();
            foreach (var id in ids)
                if (id == Guid.Empty || !result.Add(id) || result.Count > MaximumRows)
                    throw new ArgumentException("Invalid or excessive database identifiers.", nameof(ids));
            return result;
        }

        /// <summary>共用原 AND／AllColumns 建構器；結構名稱必須合法，不把名稱檢查當作權限驗證。</summary>
        private static QueryExpression Query(string name, ColumnSet? columns)
        {
            RequireName(name, nameof(name));
            return new QueryExpression(name) { ColumnSet = columns ?? new ColumnSet(true), Criteria = new FilterExpression(LogicalOperator.And) };
        }

        /// <summary>拒絕非 logical name 字元，訊息不回顯 caller 輸入；不建立 XML 或反射名稱。</summary>
        private static void RequireName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>
        /// 只解析有界官方 XML cookie 的 page；禁止 DTD／外部 resolver，完整讀取以拒絕尾隨 XML。
        /// reader／StringReader 只屬本次方法且 using 釋放；不將游標、資料列 ID 或原 cookie 寫入診斷。
        /// </summary>
        private static int NextPage(string cookie)
        {
            if (cookie.Length > MaximumCookieCharacters) throw new ArgumentException("Paging cookie exceeds the local size budget.", nameof(cookie));
            try
            {
                using var text = new StringReader(cookie);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumCookieCharacters
                });
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "cookie" || reader.NamespaceURI.Length != 0
                    || !int.TryParse(reader.GetAttribute("page"), NumberStyles.None, CultureInfo.InvariantCulture, out var previous)
                    || previous < 1 || previous >= MaximumPages)
                    throw new ArgumentException("Invalid paging cookie.", nameof(cookie));
                while (reader.Read()) { }
                return previous + 1;
            }
            catch (XmlException failure) { throw new ArgumentException("Invalid paging cookie.", nameof(cookie), failure); }
        }

        /// <summary>caller 取消在操作前後檢查；內部 OperationCanceledException 只有 caller 真已取消才分類為正常取消。</summary>
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

        /// <summary>取得單次局部 SDK 借用；Dispose 後停止新操作，不額外序列化所有租戶。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CollectionQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(CollectionQueryService));
        }

        /// <summary>冪等清除借用參照；不等待或中斷已開始的同步工作，host 須先排空再處置其 SDK owner。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
            GC.SuppressFinalize(this);
        }
    }
}
