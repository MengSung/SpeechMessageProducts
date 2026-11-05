using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.CollectionOperations;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Performance;

/// <summary>
/// 將孤兒目錄中的十五個非同步查詢案例納入正式執行：以獨立假 SDK 保護查詢形狀、結果、I/O 次數、
/// 取消與有界結果持有。全部資料均為合成；不連 CRM、不以單元測試延遲宣稱實機效能。
/// 同步 SDK 的 Task 包裝不會憑空產生可取消的網路 I/O；取消案例明確驗證送出前拒絕而非依賴計時競賽。
/// </summary>
[Trait("Category", "Performance")]
public sealed class CollectionQueryServiceAsyncTests
{
    /// <summary>每案例獨享資料來源；只保留最後一個 query 與有限計數，不跨測試或使用者共用。</summary>
    private readonly QueryFixture source = new();
    /// <summary>待測物借用本案例的假服務，不建立連線、timer 或背景工作。</summary>
    private readonly ICollectionQueryService collectionService;

    /// <summary>完成原草稿遺漏的初始化；logger 僅為既有 API 的非空佔位，不會送通知或保留 request。</summary>
    public CollectionQueryServiceAsyncTests() => collectionService = new CollectionQueryService(new object(), source);

    /// <summary>固定欄位查詢須送出原值與啟用條件，回傳 detached 的唯一資料庫 ID。</summary>
    [Fact]
    public async Task RetrieveEntityCollectionByFieldAsync_ShouldReturnEntities()
    {
        var result = await collectionService.RetrieveEntityCollectionByFieldAsync("contact", "new_listid", source.ListId.ToString());
        Assert.Single(result.Entities);
        var query = Assert.IsType<QueryByAttribute>(source.LastQuery);
        Assert.Equal(new[] { "new_listid", "statecode" }, query.Attributes);
        Assert.Equal(source.ListId.ToString(), query.Values[0]);
        Assert.Equal(0, query.Values[1]);
        Assert.Equal(1, source.Calls);
    }

    /// <summary>單條件 QueryExpression 必須保留 caller 值並限制啟用狀態，不可變成全表掃描。</summary>
    [Fact]
    public async Task RetrieveEntityCollectionByConditionAsync_ShouldReturnEntities()
    {
        var result = await collectionService.RetrieveEntityCollectionByConditionAsync("contact", "new_listid", ConditionOperator.Equal, source.ListId);
        Assert.Single(result.Entities);
        var query = Assert.IsType<QueryExpression>(source.LastQuery);
        Assert.Contains(query.Criteria.Conditions, item => item.AttributeName == "new_listid" && Equals(source.ListId, item.Values[0]));
        Assert.Contains(query.Criteria.Conditions, item => item.AttributeName == "statecode" && Equals(0, item.Values[0]));
    }

    /// <summary>複合條件應全部以 AND 傳到 SDK，不能遺漏清單或狀態而擴大資料範圍。</summary>
    [Fact]
    public async Task RetrieveEntityCollectionByConditionsAsync_ShouldReturnEntities()
    {
        var result = await collectionService.RetrieveEntityCollectionByConditionsAsync("contact", new Dictionary<string, object> { ["new_listid"] = source.ListId, ["new_status"] = "Active" });
        Assert.Single(result.Entities);
        var query = Assert.IsType<QueryExpression>(source.LastQuery);
        Assert.Equal(LogicalOperator.And, query.Criteria.FilterOperator);
        Assert.Equal(3, query.Criteria.Conditions.Count);
    }

    /// <summary>週報日期採固定主日；斷言明確前兩月邊界與清單，不依賴執行日或程序時區。</summary>
    [Fact]
    public async Task QueryWeeklyReportBeforeTowMonthOfSundayAsync_ShouldReturnReports()
    {
        var sunday = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var result = await collectionService.QueryWeeklyReportBeforeTowMonthOfSundayAsync(sunday, source.ListId);
        Assert.Single(result.Entities);
        var query = Assert.IsType<QueryExpression>(source.LastQuery);
        Assert.Equal("new_group_present_weekly_report", query.EntityName);
        Assert.Contains(query.Criteria.Conditions, item => item.Operator == ConditionOperator.OnOrAfter && Equals(sunday.AddMonths(-2), item.Values[0]));
        Assert.Contains(query.Criteria.Conditions, item => item.Operator == ConditionOperator.OnOrBefore && Equals(sunday, item.Values[0]));
    }

    /// <summary>第一頁必須有固定頁數及大小；fake 回一列，確認結果不多於要求且不提前取下一頁。</summary>
    [Fact]
    public async Task RetrievePagedEntitiesAsync_FirstPage_ShouldReturnPagedResult()
    {
        var result = await collectionService.RetrievePagedEntitiesAsync("contact", pageSize: 50);
        Assert.Single(result.Entities);
        var query = Assert.IsType<QueryExpression>(source.LastQuery);
        Assert.Equal(50, query.PageInfo.Count);
        Assert.Equal(1, query.PageInfo.PageNumber);
        Assert.Null(query.PageInfo.PagingCookie);
        Assert.Equal(1, source.Calls);
    }

    /// <summary>caller 的明確過濾式必須完整傳遞；此合成列只作回傳轉換驗證，不冒充 SDK 篩選測試。</summary>
    [Fact]
    public async Task RetrievePagedEntitiesAsync_WithFilter_ShouldReturnFilteredResults()
    {
        var filter = new FilterExpression(LogicalOperator.And);
        filter.AddCondition("new_status", ConditionOperator.Equal, "Active");
        var result = await collectionService.RetrievePagedEntitiesAsync("contact", filter: filter, pageSize: 100);
        Assert.Equal("Active", Assert.Single(result.Entities).GetAttributeValue<string>("new_status"));
        var query = Assert.IsType<QueryExpression>(source.LastQuery);
        Assert.Same(filter, query.Criteria);
        Assert.Equal(100, query.PageInfo.Count);
    }

    /// <summary>十個資料庫 ID 以一次 IN 查詢送出；每個不同 ID 均完整保留，不按相同顯示名稱去重。</summary>
    [Fact]
    public async Task RetrieveBatchByIdsAsync_ShouldReturnAllMatchingEntities()
    {
        var ids = Enumerable.Range(1, 10).Select(index => new Guid(index, 0, 0, new byte[8])).ToList();
        var result = await collectionService.RetrieveBatchByIdsAsync("contact", "contactid", ids);
        Assert.Equal(ids, result.Entities.Select(row => row.Id));
        Assert.Equal(1, source.Calls);
        Assert.Equal(10, Assert.IsType<QueryExpression>(source.LastQuery).Criteria.Conditions[0].Values.Count);
    }

    /// <summary>一千個 ID 仍只有一次有界 SDK 查詢；保留原五秒粗略預算作輔助，但主要斷言是 I/O 次數及完整結果。</summary>
    [Fact]
    public async Task RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently()
    {
        var ids = Enumerable.Range(1, 1000).Select(index => new Guid(index, 0, 0, new byte[8])).ToList();
        var watch = Stopwatch.StartNew();
        var result = await collectionService.RetrieveBatchByIdsAsync("contact", "contactid", ids);
        watch.Stop();
        Assert.Equal(ids, result.Entities.Select(row => row.Id));
        Assert.Equal(1, source.Calls);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>已取消 caller 不得送出任何同步 I/O，也不得建立尚待完成的背景 task。</summary>
    [Fact]
    public async Task RetrieveEntityCollectionByFieldAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collectionService.RetrieveEntityCollectionByFieldAsync("contact", "new_listid", source.ListId.ToString(), cancellation.Token));
        Assert.Equal(0, source.Calls);
    }

    /// <summary>分頁取消以送出前的確定取消注入，不用 CancelAfter 與同步 SDK 競賽造成偶然綠燈。</summary>
    [Fact]
    public async Task RetrievePagedEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collectionService.RetrievePagedEntitiesAsync("contact", pageSize: 1000, cancellationToken: cancellation.Token));
        Assert.Equal(0, source.Calls);
    }

    /// <summary>同步與非同步包裝必須有相同資料 ID 且每次僅一筆 I/O；不宣稱假 SDK 的毫秒比值是效能提升。</summary>
    [Fact]
    public async Task CompareAsyncVsSyncPerformance()
    {
        var sync = collectionService.RetrieveEntityCollectionByField("contact", "new_listid", source.ListId.ToString());
        var result = await collectionService.RetrieveEntityCollectionByFieldAsync("contact", "new_listid", source.ListId.ToString());
        Assert.Equal(sync.Entities.Select(row => row.Id), result.Entities.Select(row => row.Id));
        Assert.NotSame(sync, result);
        Assert.Equal(2, source.Calls);
    }

    /// <summary>十次 caller 各持 detached 結果，完成後沒有活躍 SDK 呼叫；修改一份集合不影響其他結果。</summary>
    [Fact]
    public async Task ConcurrentAsyncQueries_ShouldNotBlock()
    {
        var tasks = Enumerable.Range(0, 10).Select(_ => collectionService.RetrieveEntityCollectionByFieldAsync("contact", "new_listid", source.ListId.ToString())).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, source.Calls);
        Assert.All(tasks, task => Assert.True(task.IsCompletedSuccessfully));
        results[0].Entities.Clear();
        Assert.All(results.Skip(1), result => Assert.Single(result.Entities));
        Assert.Equal(0, source.ActiveCalls);
    }

    /// <summary>連續十頁每次僅一百列；處理後清空結果且來源不保存結果，保留原五十 MB 粗略預算並驗證無活躍工作。</summary>
    [Fact]
    public async Task PagedQuery_MemoryUsage_ShouldBeLow()
    {
        source.PageCount = 10;
        var before = GC.GetTotalMemory(true);
        string? cookie = null;
        for (var page = 0; page < 10; page++)
        {
            var result = await collectionService.RetrievePagedEntitiesAsync("contact", pageSize: 100, pagingCookie: cookie);
            Assert.Equal(100, result.Entities.Count);
            cookie = result.PagingCookie;
            result.Entities.Clear();
        }
        Assert.Null(cookie);
        Assert.Equal(10, source.Calls);
        Assert.Equal(0, source.ActiveCalls);
        Assert.True(GC.GetTotalMemory(true) - before < 50L * 1024 * 1024);
    }

    /// <summary>假 SDK 拒絕無效 entity，核心必須傳遞相同失敗，而非吞掉回傳空集合。</summary>
    [Fact]
    public async Task RetrieveEntityCollectionByFieldAsync_WithInvalidEntityName_ShouldThrowException()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => collectionService.RetrieveEntityCollectionByFieldAsync("invalid_entity_name", "fieldname", "value"));
        Assert.Equal("Synthetic entity rejection.", exception.Message);
        Assert.Equal(0, source.ActiveCalls);
    }

    /// <summary>空 ID 集合無正式資料，回傳空集合而非製造暫存身份；不以姓名或內容建立假 key。</summary>
    [Fact]
    public async Task RetrieveBatchByIdsAsync_WithEmptyIdList_ShouldReturnEmptyCollection()
    {
        var result = await collectionService.RetrieveBatchByIdsAsync("contact", "contactid", new List<Guid>());
        Assert.Empty(result.Entities);
        Assert.Equal(0, source.ActiveCalls);
    }

    /// <summary>
    /// 保護查詢 fixture 的並行發布契約；同時注入 64 個不同的合成查詢，
    /// 斷言最後觀察值一定是其中一個完整已發布 query，且所有呼叫均已排空。
    /// </summary>
    [Fact]
    public async Task QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference()
    {
        var requests = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
            {
                var query = new QueryExpression("synthetic_" + index);
                var result = source.RetrieveMultiple(query);
                Assert.Single(result.Entities);
                return query;
            }))
            .ToArray();

        var completedQueries = await Task.WhenAll(requests);
        var lastQuery = source.LastQuery;

        Assert.NotNull(lastQuery);
        Assert.Contains(completedQueries, query => ReferenceEquals(query, lastQuery));
        Assert.Equal(completedQueries.Length, source.Calls);
        Assert.Equal(0, source.ActiveCalls);
    }

    /// <summary>
    /// 只實作十五案例需要的 SDK 邊界；所有 query 都被案例另外檢查，未支援操作明確拒絕。
    /// 不保存回傳 Entity／結果集合，最多保留最後一個合成 query；生命週期與本測試實例相同，無連線或 handle。
    /// </summary>
    private sealed class QueryFixture : IOrganizationService
    {
        /// <summary>固定案例資料庫範圍，不由網路輸入建立。</summary>
        internal Guid ListId { get; } = new("07000000-0000-0000-0000-000000000001");
        /// <summary>最後一次合成查詢，供查詢契約斷言，不包含使用者資料。</summary>
        internal QueryBase? LastQuery
        {
            get => Volatile.Read(ref lastQuery);
            private set => Volatile.Write(ref lastQuery, value);
        }
        private QueryBase? lastQuery;
        /// <summary>每案例有限 I/O 總數。</summary>
        internal int Calls => Volatile.Read(ref calls);
        private int calls;
        /// <summary>最後一頁之前回傳 paging cookie；最多十頁，由記憶體測試設定。</summary>
        internal int PageCount { get; set; } = 1;
        /// <summary>當前同步 SDK 呼叫數；finally 歸零作為 drain 證據。</summary>
        internal int ActiveCalls => Volatile.Read(ref activeCalls);
        private int activeCalls;

        /// <summary>為每個查詢建立新結果，IN 保留全部不同資料庫 ID；不使用顯示名稱合併。</summary>
        /// <param name="query">借用的 query，僅保留最後一個合成實例。</param><returns>此次呼叫專用 EntityCollection。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Interlocked.Increment(ref activeCalls);
            try
            {
                var call = Interlocked.Increment(ref calls);
                LastQuery = query;
                var name = query switch { QueryExpression expression => expression.EntityName, QueryByAttribute attributes => attributes.EntityName, _ => throw new NotSupportedException() };
                if (name == "invalid_entity_name") throw new InvalidOperationException("Synthetic entity rejection.");
                var condition = (query as QueryExpression)?.Criteria.Conditions.FirstOrDefault(item => item.Operator == ConditionOperator.In);
                var ids = condition is null ? new[] { ListId } : condition.Values.Cast<Guid>().ToArray();
                if (PageCount > 1) ids = Enumerable.Range(1, 100).Select(index => new Guid(index + (call * 100), 0, 0, new byte[8])).ToArray();
                var result = new EntityCollection(ids.Select(id => new Entity(name, id) { ["new_status"] = "Active", ["fullname"] = "synthetic-same-name" }).ToList());
                result.MoreRecords = call < PageCount;
                // 使用 SDK 官方 cookie 的 page 屬性，讓第三頁以後能驗證真正遞增的頁碼。
                // 不以任意字串讓舊版固定第二頁缺陷通過；fixture 只保存最後一個合成 query。
                if (PageCount > 1) Assert.Equal(call, Assert.IsType<QueryExpression>(query).PageInfo.PageNumber);
                result.PagingCookie = result.MoreRecords ? "<cookie page=\"" + call + "\" />" : null;
                return result;
            }
            finally { Interlocked.Decrement(ref activeCalls); }
        }

        /// <summary>只支援週報 RetrieveMultiple 訊息；回傳該次獨立的 SDK response。</summary>
        /// <param name="request">合成訊息。</param><returns>具正式結果鍵的回應。</returns>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var query = Assert.IsType<RetrieveMultipleRequest>(request).Query;
            return new RetrieveMultipleResponse { Results = { ["EntityCollection"] = RetrieveMultiple(query) } };
        }
        /// <summary>查詢測試不建立記錄；誤呼叫必須失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>查詢測試不更新記錄；誤呼叫必須失敗。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>查詢測試不刪除記錄；誤呼叫必須失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>此組案例不走單筆 Retrieve，不能讓未測試路徑假成功。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>查詢測試不變更關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>查詢測試不變更關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
