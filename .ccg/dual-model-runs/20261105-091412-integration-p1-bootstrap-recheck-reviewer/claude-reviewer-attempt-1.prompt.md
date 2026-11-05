ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-bootstrap-recheck

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1 milestone 1 code review: smoke / P1-01 / P1-02
Re-review this COMPLETE corrected self-contained packet. Prior Claude marked missing production/fixture/sln evidence Critical: original packet used git diff HEAD while the user/another process committed f5c69cde/82515aab at09:01, so diffs became empty. This packet uses fixed P0 base cb932c01 and supplies the full relevant code; evaluate the prior evidence finding explicitly. No claim the first Critical was code defect; the missing review evidence is now supplied. Added direct ProductTarget guard for unconsumed net48 production, thread-safe fixture counters, and clarified orphan dirs contain ONLY lock files. Original15 tests and coarse5sec/50MB budgets are retained, not weakened.
Review only this self-contained code packet. Do not use repository tools, edit files, run commands or load credentials. Other P1 code is still in progress and outside this milestone. Return Critical / High / Warning / Info findings with file/method and concrete failure scenario, or explicitly none. Review correctness, resource lifecycle, isolation, tests and R4 coverage, not merely style.
Requirements: smoke first query immediate, then 2 seconds, max180 seconds, cancellation and finally own task cleanup. P1-01 orphan removal requires no active source/project references, retain all15 formerly orphan performance cases and make runnable. ToolUtility.Tests added to solution. R4 actual each-target MSBuild conditions; dual products may reference dual net462/net10 products only, no host-only packages (ToolCore net10 Dataverse allowed); plugins/workflows net462 only, consuming net462 dual products; host-host forbidden; references downward; Deployment isolated; Framework restore assets must select net462. Test hosts net48 allowed. Pure XML fixtures and existing loaded TargetFramework tests prove rules. No network/CRM requests in tests. Existing duplicates baseline not to increase. Files must UTF8 noBOM CRLF Traditional Chinese docs. Evaluation uses trusted source projects only, per-process30s timeout, process and pipes drained/disposed. Model uses readonly collections synchronously, no external caller inputs.
Verification: deployment62/62, performance15/15, R4 XML behavior red9 failures then net10 22/22 plus net48 actual-product reflection1/1. Full-solution gate still pending before commit.

## SpeechMessage.Dynamics.Deployment.Tests/SmokePollingTests.cs
```
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 保護降低共用 D365 伺服器負載的兩秒輪詢契約；只使用假 SDK 與虛擬時鐘，不建立連線或等待真實時間。
/// 故障注入涵蓋延遲追蹤、完整三分鐘逾時、caller 取消及查詢失敗；各出口均要求刪除本次 task、timer 回到零。
/// </summary>
public sealed class SmokePollingTests
{
    /// <summary>第三次才讀到 trace，應只等待兩次各兩秒；立即成功時不建立任何 delay timer。</summary>
    /// <param name="poll">出現合成 trace 的輪詢次序。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Smoke_TraceArrives_UsesTwoSecondCadenceAndCleansUp(int poll)
    {
        using var f = new Fixture();
        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
        f.Service.TraceAvailableAfterPoll = poll;
        var start = f.Clock.GetUtcNow();

        Assert.True(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));

        Assert.Equal(poll, f.Service.TraceQueryCount);
        Assert.Equal(poll - 1, f.Clock.DelayCount);
        Assert.All(f.Clock.ObservedDelays, delay => Assert.Equal(TimeSpan.FromSeconds(2), delay));
        Assert.Equal(TimeSpan.FromSeconds((poll - 1) * 2), f.Clock.GetUtcNow() - start);
        AssertCleaned(f);
    }

    /// <summary>沒有 trace 時只輪詢九十次並恰於虛擬三分鐘停止，不能把輪詢頻率降低後意外放大總期限。</summary>
    [Fact]
    public async Task Smoke_NoTrace_StopsAtThreeMinutesWithNinetyPolls()
    {
        using var f = new Fixture();
        var start = f.Clock.GetUtcNow();
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.Equal(90, f.Service.TraceQueryCount);
        Assert.Equal(90, f.Clock.DelayCount);
        Assert.Equal(TimeSpan.FromMinutes(3), f.Clock.GetUtcNow() - start);
        Assert.All(f.Clock.ObservedDelays, delay => Assert.Equal(TimeSpan.FromSeconds(2), delay));
        AssertCleaned(f);
    }

    /// <summary>第一個延遲期間注入 caller 取消，必須保留 OperationCanceledException 而非誤當三分鐘逾時。</summary>
    [Fact]
    public async Task Smoke_CallerCancelsDuringDelay_ThrowsAndDeletesOwnTask()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Clock.OnDelay = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine.SmokeAsync("p0-ping-trace", cancellation.Token));
        Assert.Equal(1, f.Service.TraceQueryCount);
        AssertCleaned(f);
    }

    /// <summary>在第一次查詢注入真實失敗實例；核心往外拋供 CLI 統一通知，finally 仍清除唯一測試 task。</summary>
    [Fact]
    public async Task Smoke_QueryFails_PreservesOriginalFailureAndCleanup()
    {
        using var f = new Fixture();
        var failure = new InvalidOperationException("synthetic-query-failure");
        f.Service.RetrieveFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None)));
        AssertCleaned(f);
    }

    /// <summary>取消已發生或情境未核定時，在建立任何業務記錄或 timer 之前拒絕。</summary>
    [Fact]
    public async Task Smoke_PreflightRejects_DoesNotCreateTaskOrTimer()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine.SmokeAsync("p0-ping-trace", cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Engine.SmokeAsync("invalid-scenario", CancellationToken.None));
        Assert.Empty(f.Service.Writes);
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>以 SDK 寫入與 timer 計數確認所有出口確定性釋放，不以 GC 或固定等待推測完成。</summary>
    private static void AssertCleaned(Fixture fixture)
    {
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }
}

```

## ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs
```
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
    /// 只實作十五案例需要的 SDK 邊界；所有 query 都被案例另外檢查，未支援操作明確拒絕。
    /// 不保存回傳 Entity／結果集合，最多保留最後一個合成 query；生命週期與本測試實例相同，無連線或 handle。
    /// </summary>
    private sealed class QueryFixture : IOrganizationService
    {
        /// <summary>固定案例資料庫範圍，不由網路輸入建立。</summary>
        internal Guid ListId { get; } = new("07000000-0000-0000-0000-000000000001");
        /// <summary>最後一次合成查詢，供查詢契約斷言，不包含使用者資料。</summary>
        internal QueryBase? LastQuery { get; private set; }
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
                result.PagingCookie = result.MoreRecords ? "synthetic-page-" + call : null;
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

```

## tests-shared/ArchitectureTests/ArchitectureRules.cs
```
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Architecture.Tests;

/// <summary>一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。</summary>
/// <param name="Name">唯一專案名稱。</param><param name="Target">本節點目標。</param><param name="Targets">專案全部產品目標。</param><param name="References">此目標啟用的專案參考名稱。</param><param name="Packages">此目標啟用的直接套件名稱。</param><param name="IsTest">是否測試宿主。</param>
internal sealed record ArchitectureProject(string Name, string Target, IReadOnlyList<string> Targets, IReadOnlyList<string> References, IReadOnlyList<string> Packages, bool IsTest);

/// <summary>不含來源程式或秘密值的固定規則違規，供 CI 精確診斷參考邊界。</summary>
/// <param name="Rule">穩定規則代碼。</param><param name="Project">違規專案。</param><param name="Target">當次評估目標。</param><param name="Dependency">造成違規的參考或套件。</param>
internal sealed record ArchitectureViolation(string Rule, string Project, string Target, string Dependency);

/// <summary>純函式 R4 規則；每次驗證獨立建立結果，不保存跨測試或跨目標的可變靜態圖。</summary>
internal static class ArchitectureRules
{
    /// <summary>驗證完整評估圖；呼叫端擁有輸入，結果只含固定的架構診斷。</summary>
    /// <param name="projects">每目標的獨立節點。</param><returns>違規清單。</returns>
    internal static IReadOnlyList<ArchitectureViolation> Validate(IReadOnlyList<ArchitectureProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var result = new List<ArchitectureViolation>();
        var byName = projects.GroupBy(project => project.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var project in projects)
        {
            if (project.IsTest)
            {
                continue;
            }

            var layer = Layer(project.Name);
            if (layer < 0)
            {
                Add("UnknownProject", project.Name);
            }

            // 直接保護產品輸出，不依賴是否已有 Plugin／其他 core 消費這個專案。
            // net48 僅限上面已排除的測試宿主；不能藉孤立節點漏過沙箱 Framework 上限。
            if (project.Target != "net462" && project.Target != "net10.0")
            {
                Add("ProductTarget", project.Target);
            }

            var dual = project.Targets.Contains("net462", StringComparer.Ordinal) && project.Targets.Count > 1;
            var sandbox = IsSandbox(project.Name);
            if (sandbox && project.Target != "net462")
            {
                Add("SandboxTarget", project.Target);
            }

            foreach (var package in project.Packages)
            {
                var hostOnly = package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                    package.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                var exception = project.Name == "ToolUtility.Core" && project.Target == "net10.0" &&
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                if (hostOnly && (dual || sandbox) && !exception)
                {
                    Add("DualTargetPackage", package);
                }
            }

            foreach (var reference in project.References)
            {
                if (!byName.TryGetValue(reference, out var dependency))
                {
                    Add("MissingProject", reference);
                    continue;
                }

                var frameworkCore = dependency.Targets.Contains("net462", StringComparer.Ordinal) &&
                    dependency.Targets.Contains("net10.0", StringComparer.Ordinal);
                if (dual && !frameworkCore)
                {
                    Add("DualTargetReference", reference);
                }

                if (sandbox && (!frameworkCore || IsSandbox(reference)))
                {
                    Add("SandboxReference", reference);
                }

                if (IsHost(project.Name) && IsHost(reference))
                {
                    Add("HostReference", reference);
                }

                if (reference == "SpeechMessage.Dynamics.Deployment" ||
                    (project.Name == "SpeechMessage.Dynamics.Deployment" && IsSandbox(reference)))
                {
                    Add("DeploymentIsolation", reference);
                }

                if (layer >= 0 && Layer(reference) >= layer)
                {
                    Add("LayerDirection", reference);
                }
            }

            // 區域函式只在當次節點的同步驗證期間借用結果，不保存輸入圖或建立背景工作。
            void Add(string rule, string dependency) => result.Add(new ArchitectureViolation(rule, project.Name, project.Target, dependency));
        }

        return result.AsReadOnly();
    }

    /// <summary>檢查 NuGet 已選出的 Framework 相依，而不是只相信 csproj 宣告能提供 net462。</summary>
    /// <param name="target">已還原的單一 Framework target 節點。</param>
    /// <returns>沙箱不可接受的產品名稱，不回傳完整本機路徑。</returns>
    internal static IReadOnlyList<string> ValidateFrameworkAssets(JsonElement target)
    {
        var violations = new List<string>();
        foreach (var dependency in target.EnumerateObject())
        {
            if (dependency.Value.GetProperty("type").GetString() == "project" &&
                (!dependency.Value.TryGetProperty("framework", out var framework) ||
                 framework.GetString() != ".NETFramework,Version=v4.6.2"))
            {
                violations.Add(dependency.Name.Split('/')[0]);
            }
        }

        return violations.AsReadOnly();
    }

    /// <summary>部署端 Plugin 與 Workflow 都受同一個 v4.6.2 沙箱契約約束。</summary>
    private static bool IsSandbox(string name) => name is "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows";

    /// <summary>主機具有獨立啟動與授權邊界，不得彼此參考產品外殼。</summary>
    private static bool IsHost(string name) => name is "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook";

    /// <summary>
    /// 以實際 DAG 而非示意圖的位置標記層級；同層彼此也不可參考。未知專案 fail closed，
    /// 加入新產品時必須明確分類並補正反例，不能因名稱相似自動取得高層權限。
    /// </summary>
    private static int Layer(string name) => name switch
    {
        "Line.Messaging" or "PowerPlatform.Dataverse.Client" or "SpeechMessage.Payments" => 0,
        "SpeechMessage.Localization" or "LineMessagingProcessor.Core" => 10,
        "ToolUtility.Core" => 20,
        "SpeechMessage.Tenancy" or "SpeechMessageProducts.GoodShepherd.Core" => 30,
        "LineMessagingProcessor" or "SpeechMessage.Payments.Workflows" => 35,
        "ToolUtility" or "LineMessagingProcessor.Workflows" or "LineMessagingProcessor.RichMenus" => 40,
        "LineMessagingProcessor.AspNetCore" or "SpeechMessage.Payments.AspNetCore" or "SpeechMessage.Hosting" => 50,
        "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook" or "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" => 100,
        "SpeechMessage.Dynamics.Deployment" => 110,
        _ => -1
    };
}

```

## tests-shared/ArchitectureTests/ArchitectureProjectReader.cs
```
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 從實際方案逐 csproj、逐目標呼叫 MSBuild 的唯讀評估；避免自行猜測 Condition／Import 規則漏掉
/// Framework 專用參考。每個程序最長三十秒，輸入只來自受信任方案，程序、串流與 JSON 均有限持有。
/// </summary>
internal static class ArchitectureProjectReader
{
    /// <summary>定位當前工作樹的方案並評估每目標；不走父工作樹、不讀 appsettings 或連線資訊。</summary>
    /// <returns>只含相依名稱與目標的 detached 評估圖。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadSolution()
    {
        var root = FindRoot();
        var solution = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.sln"));
        var matches = Regex.Matches(solution, "(?m)^Project\\([^\\r\\n]+?= \"[^\"]+\", \"(?<path>[^\"]+\\.csproj)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var paths = matches.Select(match => Path.GetFullPath(Path.Combine(root, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))).ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidDataException("Solution contains no project entries.");
        }

        return ReadProjects(root, paths, verifyRestoredAssets: true);
    }

    /// <summary>評估呼叫者擁有的受信任 csproj 集合；同樣逐目標解析 Condition，不自行模擬 MSBuild。</summary>
    /// <param name="root">限制讀取範圍的絕對根目錄。</param><param name="paths">本次專案路徑。</param>
    /// <param name="verifyRestoredAssets">實際方案必須同時檢查 NuGet 已還原的 Framework 選擇；純 XML 範例不需要套件還原。</param>
    /// <returns>各目標的 detached 節點。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadProjects(string root, IReadOnlyList<string> paths, bool verifyRestoredAssets = false)
    {
        var projects = new List<ArchitectureProject>();
        foreach (var path in paths)
        {
            ValidateLocalPath(root, path);
            using var outer = Evaluate(root, path, null);
            var properties = outer.RootElement.GetProperty("Properties");
            var single = properties.GetProperty("TargetFramework").GetString();
            var multiple = properties.GetProperty("TargetFrameworks").GetString();
            var targets = (string.IsNullOrEmpty(multiple) ? single : multiple)?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (targets.Length == 0)
            {
                throw new InvalidDataException("Project has no evaluated target framework.");
            }

            foreach (var target in targets)
            {
                using var evaluated = Evaluate(root, path, target);
                var data = evaluated.RootElement;
                var items = data.GetProperty("Items");
                var references = items.GetProperty("ProjectReference").EnumerateArray()
                    .Select(item => Path.GetFileNameWithoutExtension(item.GetProperty("Identity").GetString())).ToArray();
                var packages = items.GetProperty("PackageReference").EnumerateArray()
                    .Select(item => item.GetProperty("Identity").GetString() ?? throw new InvalidDataException("Package identity is missing.")).ToArray();
                var test = string.Equals(data.GetProperty("Properties").GetProperty("IsTestProject").GetString(), "true", StringComparison.OrdinalIgnoreCase);
                if (verifyRestoredAssets && (target == "net462" || (test && target == "net48")))
                {
                    var assetsPath = data.GetProperty("Properties").GetProperty("ProjectAssetsFile").GetString();
                    if (string.IsNullOrEmpty(assetsPath)) throw new InvalidDataException("Restored assets path is missing.");
                    ValidateLocalPath(root, assetsPath);
                    using var stream = File.OpenRead(assetsPath);
                    using var assets = JsonDocument.Parse(stream);
                    var violations = ArchitectureRules.ValidateFrameworkAssets(assets.RootElement.GetProperty("targets").GetProperty(target));
                    if (violations.Count != 0) throw new InvalidDataException("Framework restore selected an incompatible product: " + string.Join(",", violations));
                }

                projects.Add(new ArchitectureProject(Path.GetFileNameWithoutExtension(path), target, targets, references!, packages, test));
            }
        }

        return projects.AsReadOnly();
    }

    /// <summary>只向上尋找目前 checkout 的方案；找到即停，不以另一個工作樹補來源。</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Current solution was not found.");
    }

    /// <summary>拒絕跨工作樹及 reparse point，確保測試只檢視本次方案宣告的普通檔案。</summary>
    private static void ValidateLocalPath(string root, string path)
    {
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Project path is outside the current worktree.");
        }

        for (FileSystemInfo? part = new FileInfo(path); part is not null && !string.Equals(part.FullName, root, StringComparison.OrdinalIgnoreCase); part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
        {
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Reparse point project paths are not allowed.");
            }
        }
    }

    /// <summary>
    /// 啟動不建置的 dotnet msbuild；並行 drain stdout／stderr 避免 pipe deadlock。失敗只回固定訊息，
    /// 不將工具輸出中的本機路徑或潛在設定展開到 assertion；逾時終止整個程序樹後等待退出再釋放。
    /// </summary>
    private static JsonDocument Evaluate(string root, string path, string? target)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "msbuild", path, "-nologo", "-p:Configuration=Release", "-getProperty:TargetFramework,TargetFrameworks,IsTestProject,ProjectAssetsFile", "-getItem:ProjectReference,PackageReference" })
        {
            start.ArgumentList.Add(argument);
        }

        if (target is not null)
        {
            start.ArgumentList.Add("-p:TargetFramework=" + target);
        }

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("Read-only architecture evaluation exceeded its deadline.");
            }

            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException("Read-only architecture evaluation failed.");
            }

            return JsonDocument.Parse(stdout.GetAwaiter().GetResult());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        }
    }
}

```

## tests-shared/ArchitectureTests/ArchitectureRuleTests.cs
```
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 以合成相依圖及實際方案保護 R4；每個反例只違反指定邊界，避免以其他建置失敗冒充架構拒絕。
/// 測試不讀任何秘密、不連資料庫；實際專案僅進行有界的 MSBuild 評估，子程序與串流由讀取器釋放。
/// </summary>
public sealed class ArchitectureRuleTests
{
    /// <summary>雙目標專案不能指向只有 net10 的產品，即使當次建置選到 net10 也不豁免整體雙目標契約。</summary>
    [Fact]
    public void DualTarget_ReferenceToNet10Only_IsRejected()
    {
        var graph = Graph(Product("SpeechMessage.Localization", "net462", "net462;net10.0", "ToolUtility"), Product("ToolUtility", "net10.0", "net10.0"));
        Assert.Contains(ArchitectureRules.Validate(graph), x => x.Rule == "DualTargetReference");
    }

    /// <summary>Extensions、ASP.NET 與 Dataverse 直接套件不得進入共用雙目標；以各套件正反例保護辨識。</summary>
    /// <param name="package">刻意注入的 host-only 套件。</param>
    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("Microsoft.AspNetCore.Http")]
    [InlineData("Microsoft.PowerPlatform.Dataverse.Client")]
    public void DualTarget_HostPackage_IsRejected(string package)
    {
        var project = Product("SpeechMessage.Localization", "net462", "net462;net10.0") with { Packages = new[] { package } };
        Assert.Contains(ArchitectureRules.Validate(Graph(project)), x => x.Rule == "DualTargetPackage");
        Assert.Empty(ArchitectureRules.Validate(Graph(project with { Packages = Array.Empty<string>() })));
    }

    /// <summary>ToolUtility.Core 的 Dataverse 例外只限 net10 SDK 型別來源，不能擴大為 net462 或任意 Extensions 豁免。</summary>
    [Fact]
    public void ToolCore_DataverseException_IsTargetSpecific()
    {
        var project = Product("ToolUtility.Core", "net10.0", "net462;net10.0") with { Packages = new[] { "Microsoft.PowerPlatform.Dataverse.Client" } };
        Assert.Empty(ArchitectureRules.Validate(Graph(project)));
        Assert.Contains(ArchitectureRules.Validate(Graph(project with { Target = "net462" })), x => x.Rule == "DualTargetPackage");
    }

    /// <summary>沙箱只引用可提供 net462 的雙目標產品；net48 宿主不等於合格部署產品。</summary>
    [Fact]
    public void Sandbox_ReferenceToNet48Product_IsRejected()
    {
        var plugin = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", "SpeechMessage.Localization");
        Assert.Contains(ArchitectureRules.Validate(Graph(plugin, Product("SpeechMessage.Localization", "net48", "net48;net10.0"))), x => x.Rule == "SandboxReference");
        Assert.Empty(ArchitectureRules.Validate(Graph(plugin, Product("SpeechMessage.Localization", "net462", "net462;net10.0"))));
    }

    /// <summary>Plugin 自身高於 v4.6.2 就必須拒絕，不得只檢查被參考專案。</summary>
    [Fact]
    public void Sandbox_HigherFramework_IsRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("SpeechMessage.Dynamics.Plugins", "net48", "net48"))), x => x.Rule == "SandboxTarget");
    }

    /// <summary>主機之間不共享 product assembly；故意讓 ChurchReport 指向另一主機應立即拒絕。</summary>
    [Fact]
    public void HostToHost_IsRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0", "SpeechMessageProducts.LineWebhook"), Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0"))), x => x.Rule == "HostReference");
    }

    /// <summary>低層反向依賴、工具被產品引用與未分類專案都不可靜默放行。</summary>
    [Fact]
    public void ReverseDependency_ToolReference_AndUnknownProject_AreRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("Line.Messaging", "net10.0", "net10.0", "LineMessagingProcessor"), Product("LineMessagingProcessor", "net10.0", "net10.0"))), x => x.Rule == "LayerDirection");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("ToolUtility", "net10.0", "net10.0", "SpeechMessage.Dynamics.Deployment"), Product("SpeechMessage.Dynamics.Deployment", "net10.0", "net10.0"))), x => x.Rule == "DeploymentIsolation");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("Unclassified.Product", "net10.0", "net10.0"))), x => x.Rule == "UnknownProject");
    }

    /// <summary>net10 共用層往雙目標核心為合法方向；層級圖不能因文件的視覺排列誤擋此情境。</summary>
    [Fact]
    public void Net10SharedLayer_ToDualCore_IsAllowed()
    {
        Assert.Empty(ArchitectureRules.Validate(Graph(Product("SpeechMessage.Tenancy", "net10.0", "net10.0", "SpeechMessage.Localization"), Product("SpeechMessage.Localization", "net462", "net462;net10.0"), Product("SpeechMessage.Localization", "net10.0", "net462;net10.0"))));
    }

    /// <summary>評估後每目標的參考各自檢查，net10 合法例外不能掩蓋 net462 的條件套件。</summary>
    [Fact]
    public void ConditionalReferences_AreCheckedForEachEvaluatedTarget()
    {
        var valid = Product("ToolUtility.Core", "net10.0", "net462;net10.0") with { Packages = new[] { "Microsoft.PowerPlatform.Dataverse.Client" } };
        var invalid = valid with { Target = "net462" };
        Assert.Contains(ArchitectureRules.Validate(Graph(valid, invalid)), x => x.Rule == "DualTargetPackage" && x.Target == "net462");
    }

    /// <summary>實際方案逐 csproj 與每目標評估；新增未知產品或違規參考須讓方案測試紅燈，而非只測合成字串。</summary>
    [Fact]
    public void Solution_AllEvaluatedTargets_ObeyR4()
    {
        var projects = ArchitectureProjectReader.ReadSolution();
        Assert.NotEmpty(projects);
        Assert.Empty(ArchitectureRules.Validate(projects));
    }

    /// <summary>建立單一不可變的合成評估節點，陣列僅屬此案例，不持有 request 或 SDK 型別。</summary>
    private static ArchitectureProject Product(string name, string target, string targets, params string[] references) =>
        new(name, target, targets.Split(';'), references, Array.Empty<string>(), false);

    /// <summary>將案例節點組成 operation-local 圖，不跨案例快取。</summary>
    private static ArchitectureProject[] Graph(params ArchitectureProject[] projects) => projects;
}

```

## tests-shared/ArchitectureTests/ArchitectureXmlTests.cs
```
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 使用真實 csproj XML 的故意違規範例驗證 R4，不把合成模型當成 MSBuild 解析證據。
/// 範例不還原、不建置、不載入 DLL；每個案例擁有自己的暫存目錄，子程序退出後確定刪除。
/// </summary>
public sealed class ArchitectureXmlTests
{
    /// <summary>每條正式規則都有能被實際 XML 評估偵測的反例，移除違規參考後則必須通過。</summary>
    /// <param name="source">違規來源專案。</param><param name="targets">來源宣告目標。</param>
    /// <param name="dependency">參考專案。</param><param name="dependencyTargets">相依宣告目標。</param><param name="rule">必須攔截的規則。</param>
    [Theory]
    [InlineData("SpeechMessage.Localization", "net462;net10.0", "ToolUtility", "net10.0", "DualTargetReference")]
    [InlineData("SpeechMessage.Dynamics.Plugins", "net462", "SpeechMessage.Localization", "net48;net10.0", "SandboxReference")]
    [InlineData("Line.Messaging", "net10.0", "LineMessagingProcessor", "net10.0", "LayerDirection")]
    [InlineData("SpeechMessageProducts.ChurchReport", "net10.0", "SpeechMessageProducts.LineWebhook", "net10.0", "HostReference")]
    public void InvalidCsprojReference_IsRejected(string source, string targets, string dependency, string dependencyTargets, string rule)
    {
        using var fixture = new ProjectFixture();
        fixture.Add(source, Project(targets, $"<ProjectReference Include=\"{dependency}.csproj\" />"));
        fixture.Add(dependency, Project(dependencyTargets, string.Empty));
        var evaluated = fixture.Read();
        Assert.NotEmpty(evaluated);
        Assert.Contains(ArchitectureRules.Validate(evaluated), item => item.Rule == rule);
        fixture.Replace(source, Project(targets, string.Empty));
        var repaired = fixture.Read().Where(item => item.Name == source).ToArray();
        Assert.NotEmpty(repaired);
        Assert.Empty(ArchitectureRules.Validate(repaired));
    }

    /// <summary>條件式套件只在 net462 啟用仍必須拒絕；net10 例外不能掩蓋另一個 inner build。</summary>
    /// <param name="package">三種不可進入雙目標純核心的主機套件。</param>
    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("Microsoft.AspNetCore.Http")]
    [InlineData("Microsoft.PowerPlatform.Dataverse.Client")]
    public void InvalidCsprojConditionalPackage_IsRejected(string package)
    {
        using var fixture = new ProjectFixture();
        var reference = $"<PackageReference Include=\"{package}\" Condition=\"'$(TargetFramework)' == 'net462'\" />";
        fixture.Add("SpeechMessage.Localization", Project("net462;net10.0", reference));
        var nodes = fixture.Read();
        Assert.Equal(2, nodes.Count);
        Assert.Contains(ArchitectureRules.Validate(nodes), item => item.Rule == "DualTargetPackage" && item.Target == "net462");
        Assert.Empty(ArchitectureRules.Validate(nodes.Where(item => item.Target == "net10.0").ToArray()));
    }

    /// <summary>沙箱自身 net48 的 XML 不得冒充 net462 產品；測試宿主的豁免不會套到產品名稱。</summary>
    [Fact]
    public void InvalidCsprojSandboxTarget_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Add("SpeechMessage.Dynamics.Workflows", Project("net48", string.Empty));
        Assert.Contains(ArchitectureRules.Validate(fixture.Read()), item => item.Rule == "SandboxTarget");
        fixture.Replace("SpeechMessage.Dynamics.Workflows", Project("net462", string.Empty));
        Assert.Empty(ArchitectureRules.Validate(fixture.Read()));
    }

    /// <summary>沒有沙箱消費者的雙目標產品也不能把 net48 當產品目標，防止只靠被參考關係才攔截的死角。</summary>
    [Fact]
    public void InvalidCsprojUnreferencedProductTarget_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Add("SpeechMessage.Localization", Project("net48;net10.0", string.Empty));
        Assert.Contains(ArchitectureRules.Validate(fixture.Read()), item => item.Rule == "ProductTarget" && item.Target == "net48");
        fixture.Replace("SpeechMessage.Localization", Project("net462;net10.0", string.Empty));
        Assert.Empty(ArchitectureRules.Validate(fixture.Read()));
    }

    /// <summary>即使 csproj 宣告雙目標，NuGet 若實際選出 net48 產品也必須拒絕；正常 net462 與套件節點不誤擋。</summary>
    [Fact]
    public void RestoredAssets_Net48Product_IsRejected()
    {
        using var invalid = JsonDocument.Parse("{\"Core/1.0.0\":{\"type\":\"project\",\"framework\":\".NETFramework,Version=v4.8\"}}");
        Assert.Equal("Core", Assert.Single(ArchitectureRules.ValidateFrameworkAssets(invalid.RootElement)));
        using var valid = JsonDocument.Parse("{\"Core/1.0.0\":{\"type\":\"project\",\"framework\":\".NETFramework,Version=v4.6.2\"},\"Package/1.0\":{\"type\":\"package\"}}");
        Assert.Empty(ArchitectureRules.ValidateFrameworkAssets(valid.RootElement));
    }

    /// <summary>只組成受信任固定範例；Condition 由真正 MSBuild 解讀，沒有自製條件語言。</summary>
    private static string Project(string targets, string items) =>
        $"<Project><PropertyGroup><TargetFrameworks>{targets}</TargetFrameworks><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>";

    /// <summary>單一案例擁有其唯一暫存根目錄及固定清單，沒有 timer、網路、秘密或共享可變資源。</summary>
    private sealed class ProjectFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "speechmessage-r4-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> files = new();

        /// <summary>建構即建立本案例專屬目錄，所有後續檔案只能位於此目錄直接子層。</summary>
        internal ProjectFixture() => Directory.CreateDirectory(root);

        /// <summary>新增具明確專案名稱的 XML；不執行任意 target 或使用者字串。</summary>
        internal void Add(string name, string xml)
        {
            var path = Path.Combine(root, name + ".csproj");
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            files.Add(path);
        }

        /// <summary>只替換已由本案例建立的單一範例，讓同條規則同時有正反例。</summary>
        internal void Replace(string name, string xml) => File.WriteAllText(files.Single(path => Path.GetFileNameWithoutExtension(path) == name), xml, new UTF8Encoding(false));

        /// <summary>以完整工作範圍及顯式清單評估，不掃描系統其他專案。</summary>
        internal IReadOnlyList<ArchitectureProject> Read() => ArchitectureProjectReader.ReadProjects(root, files);

        /// <summary>MSBuild 只評估、不產生 obj；逐一刪除自己建立的檔後刪空目錄，不遞迴刪除任意路徑。</summary>
        public void Dispose()
        {
            foreach (var path in files) File.Delete(path);
            Directory.Delete(root);
        }
    }
}

```

## wp-reports/P1-01-reference-audit.md
```
# P1-01 刪除前引用證據

範圍：目前工作樹 Git 追蹤檔；有效專案／方案及 C# 共 960 檔，不跨父工作樹。
檢查每個候選名稱及 Trace 專用型別；候選本身的自我參考不當作外部使用。

## Trace/
搜尋：Trace.csproj, Trace_Fixed.csproj, Trace_Net10.csproj, BSUStackTrace, BSUTextWriterTraceListener
結果：0 個外部參考。

## LinePayCSharp/
搜尋：LinePayCSharp
結果：0 個外部參考。

## Line.Messaging/Line.Messaging_Net10.csproj
搜尋：Line.Messaging_Net10.csproj
結果：0 個外部參考。

## LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
搜尋：LineMessagingProcessor_Net10.csproj
結果：0 個外部參考。

## SpeechMessage.Dynamics.Crm82Worker
搜尋：SpeechMessage.Dynamics.Crm82Worker
結果：0 個外部參考。

## SpeechMessage.Dynamics.Crm91Worker
搜尋：SpeechMessage.Dynamics.Crm91Worker
結果：0 個外部參考。

SpeechMessage.Dynamics.Crm82Worker：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm82Worker.Tests：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm91Worker：實際僅 packages.lock.json。
SpeechMessage.Dynamics.Crm91Worker.Tests：實際僅 packages.lock.json。

四個 worker 目錄在刪除前各只有鎖定檔，沒有 csproj／原始碼／方案項目；本次不是保留仍存活的 worker 專案，也沒有把其他 untracked 資料當成孤兒遞迴刪除。

待刪除追蹤檔：51
- Line.Messaging/Line.Messaging_Net10.csproj
- LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
- LinePayCSharp/LinePayCSharp.csproj
- LinePayCSharp/LinePayClient.cs
- LinePayCSharp/Models/AuthorizationInfo.cs
- LinePayCSharp/Models/AuthorizationResponse.cs
- LinePayCSharp/Models/Capture.cs
- LinePayCSharp/Models/CaptureInfo.cs
- LinePayCSharp/Models/CaptureResponse.cs
- LinePayCSharp/Models/Confirm.cs
- LinePayCSharp/Models/ConfirmInfo.cs
- LinePayCSharp/Models/ConfirmPayInfo.cs
- LinePayCSharp/Models/ConfirmResponse.cs
- LinePayCSharp/Models/ConfirmUrlType.cs
- LinePayCSharp/Models/CreditCardBrand.cs
- LinePayCSharp/Models/Currency.cs
- LinePayCSharp/Models/LanguageCode.cs
- LinePayCSharp/Models/PayInfo.cs
- LinePayCSharp/Models/PayMethod.cs
- LinePayCSharp/Models/PayStatus.cs
- LinePayCSharp/Models/PayType.cs
- LinePayCSharp/Models/PaymentInfo.cs
- LinePayCSharp/Models/PaymentResponse.cs
- LinePayCSharp/Models/PaymentUrl.cs
- LinePayCSharp/Models/PreApprovedPay.cs
- LinePayCSharp/Models/PreApprovedPayInfo.cs
- LinePayCSharp/Models/PreApprovedPayResponse.cs
- LinePayCSharp/Models/Refund.cs
- LinePayCSharp/Models/RefundInfo.cs
- LinePayCSharp/Models/RefundList.cs
- LinePayCSharp/Models/RefundResponse.cs
- LinePayCSharp/Models/Reserve.cs
- LinePayCSharp/Models/ReserveAddFriend.cs
- LinePayCSharp/Models/ReserveExtras.cs
- LinePayCSharp/Models/ReserveInfo.cs
- LinePayCSharp/Models/ReserveResponse.cs
- LinePayCSharp/Models/ResponseBase.cs
- LinePayCSharp/Models/TransactionType.cs
- SpeechMessage.Dynamics.Crm82Worker.Tests/packages.lock.json
- SpeechMessage.Dynamics.Crm82Worker/packages.lock.json
- SpeechMessage.Dynamics.Crm91Worker.Tests/packages.lock.json
- SpeechMessage.Dynamics.Crm91Worker/packages.lock.json
- Trace/AssemblyInfo.cs
- Trace/BSUStackTrace.cs
- Trace/BSUTextWriterTraceListener.cs
- Trace/Trace.csproj
- Trace/Trace.csproj.new
- Trace/Trace.xml
- Trace/Trace_Fixed.csproj
- Trace/Trace_Net10.csproj
- Trace/packages.config

基準：26 projects、879 sourcefiles、0 duplicate groups/new/stale；全方案1125pass，ToolUtility孤兒63pass。

```


## Actual diff from fixed P0: SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
```diff
diff --git a/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs b/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
index 1129916d8..486a98f08 100644
--- a/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
+++ b/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
@@ -296,6 +296,8 @@ internal sealed class DeploymentEngine
     /// <summary>
     /// 建立本次專用 task，有限等待對應 asyncoperation 與 Plugin trace；成功、斷言失敗、
     /// 逾時或 caller cancellation 都會在 finally 刪除該 task。只查詢此 task 關聯的記錄。
+    /// 首次立即查詢，後續每兩秒重試，總期限維持三分鐘；避免對與正式組織共用的地端主機施加高頻輪詢。
+    /// timeout／linked token 及延遲 timer 都由此操作擁有並確定性釋放，不保留連線或跨命令狀態。
     /// </summary>
     /// <param name="scenario">P0 核准的 trace 或 push 情境。</param>
     /// <param name="ct">呼叫端取消 token。</param>
@@ -330,7 +332,7 @@ internal sealed class DeploymentEngine
 
                 try
                 {
-                    await Task.Delay(TimeSpan.FromMilliseconds(100), clock, linked.Token).ConfigureAwait(false);
+                    await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token).ConfigureAwait(false);
                 }
                 catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
                 {

```

## Actual diff from fixed P0: SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
```diff
diff --git a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
index 084a4dbd6..48f21555d 100644
--- a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
+++ b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
@@ -684,7 +684,7 @@ public sealed class DeploymentContractTests
         using var f = new Fixture(); f.Service.Trace = null;
         Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
         Assert.True(f.Service.TraceQueryCount > 1);
-        Assert.Equal(2, f.Clock.DelayCount);
+        Assert.Equal(90, f.Clock.DelayCount);
         Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
         Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
         Assert.Equal(0, f.Clock.ActiveTimers);
@@ -1395,23 +1395,29 @@ internal sealed class AdvancingClock : TimeProvider
     public int ActiveTimers { get; private set; }
     /// <summary>記錄虛擬輪詢次數，避免 timeout 案例在第一次查詢前就被取消。</summary>
     public int DelayCount { get; private set; }
+    /// <summary>只保留本案例最多九十次輪詢的間隔；非共享集合，案例結束即釋放。</summary>
+    public List<TimeSpan> ObservedDelays { get; } = new();
+    /// <summary>在建立短延遲時注入正常 caller 取消；委派僅屬本 fixture，不跨測試持有。</summary>
+    public Action? OnDelay { get; set; }
     /// <summary>回傳固定起點加上假延遲。</summary><returns>UTC 時點。</returns>
     public override DateTimeOffset GetUtcNow() => now;
     /// <summary>單調時間使用同一假時間，不讀硬體計時器。</summary><returns>tick 數。</returns>
     public override long GetTimestamp() => now.Ticks;
     /// <summary>固定 tick 頻率配合單調時間。</summary>
     public override long TimestampFrequency => TimeSpan.TicksPerSecond;
-    /// <summary>長期限先排程；短輪詢等待快轉兩分鐘並執行所有已到期的一次性回呼。</summary>
+    /// <summary>長期限先排程；短輪詢等待按要求的實際間隔快轉並執行所有已到期的一次性回呼。</summary>
     /// <param name="callback">延遲的單次完成回呼。</param><param name="state">延遲 owner 的狀態。</param><param name="dueTime">模擬延遲。</param><param name="period">不得重複。</param><returns>由等待者釋放的 timer。</returns>
     public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
     {
         var timer = new OwnedTimer(this, callback, state, dueTime);
         timers.Add(timer);
         ActiveTimers++;
-        if (dueTime <= TimeSpan.FromSeconds(1))
+        if (dueTime <= TimeSpan.FromSeconds(2))
         {
             DelayCount++;
-            now += TimeSpan.FromMinutes(2);
+            ObservedDelays.Add(dueTime);
+            now += dueTime;
+            OnDelay?.Invoke();
             foreach (var scheduled in timers.ToArray())
             {
                 scheduled.FireIfDue(now);

```

## Actual diff from fixed P0: SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj
```diff
diff --git a/SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj b/SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj
index 75828301b..aa7235a7e 100644
--- a/SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj
+++ b/SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj
@@ -19,4 +19,8 @@
   <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
     <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
   </ItemGroup>
+  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
+    <!-- R4 原始碼共用；Framework 測試宿主不啟動 MSBuild 子程序。 -->
+    <Compile Include="../tests-shared/ArchitectureTests/*.cs" Link="ArchitectureTests/%(Filename)%(Extension)" />
+  </ItemGroup>
 </Project>

```

## Actual diff from fixed P0: SpeechMessageProducts.sln
```diff
diff --git a/SpeechMessageProducts.sln b/SpeechMessageProducts.sln
index 3dde51a4f..2665744d3 100644
--- a/SpeechMessageProducts.sln
+++ b/SpeechMessageProducts.sln
@@ -1,4 +1,4 @@
-
+﻿
 Microsoft Visual Studio Solution File, Format Version 12.00
 # Visual Studio Version 18
 VisualStudioVersion = 18.0.11205.157
@@ -55,6 +55,8 @@ Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "SpeechMessage.Dynamics.Depl
 EndProject
 Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "SpeechMessage.Dynamics.Deployment.Tests", "SpeechMessage.Dynamics.Deployment.Tests\SpeechMessage.Dynamics.Deployment.Tests.csproj", "{A0AAE470-0442-47A1-9524-CB880B137ECA}"
 EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ToolUtility.Tests", "ToolUtility.Tests\ToolUtility.Tests.csproj", "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}"
+EndProject
 Global
 	GlobalSection(SolutionConfigurationPlatforms) = preSolution
 		Debug_LearnCrm|Any CPU = Debug_LearnCrm|Any CPU
@@ -854,6 +856,36 @@ Global
 		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
 		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
 		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x64.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x86.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|Any CPU.Build.0 = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x64.ActiveCfg = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x64.Build.0 = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x86.ActiveCfg = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x86.Build.0 = Release|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
 	EndGlobalSection
 	GlobalSection(SolutionProperties) = preSolution
 		HideSolutionNode = FALSE

```

## DeploymentEngine.cs source lines 18-70
```cs
/// <summary>
/// 單一 Dataverse 服務租約上的佈署操作核心。物件不跨命令共用；它不保存 user／tenant 狀態、
/// 已解析的秘密或外部 client。計畫與匯出只包含非敏感資料，所有檔案 writer 都由呼叫期間擁有。
/// </summary>
internal sealed class DeploymentEngine
{
    private const string AllowedOrganization = "goodshepherdback";
    private const string AssemblyLogicalName = "pluginassembly";
    private const string TypeLogicalName = "plugintype";
    private const string StepLogicalName = "sdkmessageprocessingstep";
    /// <summary>
    /// 步驟安全設定查閱的官方邏輯名稱；不可縮寫成 secureconfigid，否則已註冊步驟的查詢會被拒絕。
    /// 所有讀寫、存在性比對、指紋與匯出共用此欄位；只持有本次查閱 ID，不讀取安全設定內容。
    /// </summary>
    private const string StepSecureConfigurationLookup = "sdkmessageprocessingstepsecureconfigid";
    private const string TraceLogicalName = "plugintracelog";
    private const string AsyncOperationLogicalName = "asyncoperation";
    private const string TaskLogicalName = "task";
    private const long MaximumExportBytes = 100L * 1024 * 1024;
    private static readonly Regex SecretPlaceholderRegex =
        new(@"\$\{secret:([A-Za-z0-9_.-]{1,100})\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IOrganizationService service;
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly Func<string, string> secret;

    /// <summary>
    /// 組合一次性的服務、輸出根、時鐘與秘密來源。秘密委派只在實際建立或更新步驟設定時呼叫。
    /// </summary>
    /// <param name="service">本命令唯一的 Dataverse 服務。</param>
    /// <param name="directory">受信任的本機 plan／export 輸出根。</param>
    /// <param name="clock">UTC 與 smoke timeout 使用的可控時鐘。</param>
    /// <param name="secret">依佔位名稱取得 User Secrets 值的委派。</param>
    /// <exception cref="ArgumentNullException">必要依賴為 null。</exception>
    /// <exception cref="ArgumentException">輸出目錄為空白。</exception>
    public DeploymentEngine(IOrganizationService service, string directory, TimeProvider clock, Func<string, string> secret)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.secret = secret ?? throw new ArgumentNullException(nameof(secret));
    }

    /// <summary>
    /// 將清單、組件 PE 與組織中的受管理 metadata 作離線式比對，建立沒有憑證值的 plan。
    /// 此方法只讀取組件、類型、訊息、篩選器與步驟，不讀取業務記錄，也不解析秘密。
    /// </summary>
    /// <param name="manifest">經過格式與佔位驗證的註冊清單。</param>
    /// <param name="image">已以 MetadataReader 驗證的 Plugin 組件內容。</param>
    /// <param name="organization">目前唯一允許的開發測試組織。</param>
```

## DeploymentEngine.cs source lines 296-359
```cs
    /// <summary>
    /// 建立本次專用 task，有限等待對應 asyncoperation 與 Plugin trace；成功、斷言失敗、
    /// 逾時或 caller cancellation 都會在 finally 刪除該 task。只查詢此 task 關聯的記錄。
    /// 首次立即查詢，後續每兩秒重試，總期限維持三分鐘；避免對與正式組織共用的地端主機施加高頻輪詢。
    /// timeout／linked token 及延遲 timer 都由此操作擁有並確定性釋放，不保留連線或跨命令狀態。
    /// </summary>
    /// <param name="scenario">P0 核准的 trace 或 push 情境。</param>
    /// <param name="ct">呼叫端取消 token。</param>
    /// <returns>追蹤內容符合固定情境時為 true；未到達或不符合時為 false。</returns>
    /// <exception cref="OperationCanceledException">呼叫端主動取消等待。</exception>
    public async Task<bool> SmokeAsync(string scenario, CancellationToken ct)
    {
        if (scenario is not ("p0-ping-trace" or "p0-ping-push"))
        {
            throw new ArgumentException("Smoke 情境不在 P0 白名單中。", nameof(scenario));
        }

        ct.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var runId = Guid.NewGuid();
        var taskId = service.Create(new Entity(TaskLogicalName)
        {
            ["subject"] = $"[smoke] P0-{runId:N}",
            ["description"] = "佈署工具自動化追蹤驗證"
        });

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var trace = ReadSmokeTrace(taskId);
                if (trace is not null)
                {
                    return IsExpectedSmokeTrace(trace, scenario);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    return false;
                }
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }
        finally
        {
            service.Delete(TaskLogicalName, taskId);
        }
    }

    /// <summary>只查受管理組件與清單中的穩定步驟身份，建立操作候選；不解析秘密、不寫入資料，完成後才發布計畫。</summary>
    private Inspection Inspect(RegistrationManifest manifest, AssemblyImage image)
    {
        var assembly = FindAssembly(manifest.AssemblyName);
        var operations = new List<DeploymentOperation>();
        IReadOnlyList<Entity> pluginTypes = Array.Empty<Entity>();

        if (assembly is null)
```

## DeploymentEngine.cs source lines 785-834
```cs

    /// <summary>
    /// 只依 smoke task 找到的 async operation correlation id 查 Plugin trace。
    /// 尚無對應記錄時回傳 null，讓外層依有限輪詢期限繼續等待。
    /// </summary>
    /// <param name="taskId">本次建立且最後必定刪除的 task ID。</param>
    /// <returns>已出現的非空追蹤文字；等待中尚未出現時為 null。</returns>
    private string? ReadSmokeTrace(Guid taskId)
    {
        var asyncOperations = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(AsyncOperationLogicalName)
        {
            ColumnSet = new ColumnSet("correlationid"),
            Criteria = Equal("regardingobjectid", taskId)
        }).Entities, "smoke 非同步作業");
        foreach (var operation in asyncOperations)
        {
            var correlationId = operation.GetAttributeValue<Guid>("correlationid");
            var traces = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(TraceLogicalName)
            {
                ColumnSet = new ColumnSet("messageblock"),
                Criteria = Equal("correlationid", correlationId)
            }).Entities, "smoke Plugin 追蹤");
            var trace = traces
                .Select(row => row.GetAttributeValue<string>("messageblock"))
                .FirstOrDefault(message => !string.IsNullOrEmpty(message));
            if (trace is not null)
            {
                return trace;
            }
        }

        return null;
    }

    /// <summary>只以固定成功標記判定本次 smoke；不輸出或保存追蹤原文，推播另須具備成功狀態。</summary>
    private static bool IsExpectedSmokeTrace(string trace, string scenario)
    {
        if (trace.Length == 0 ||
            !trace.Contains("P0-PING OK", StringComparison.Ordinal) ||
            !trace.Contains("zh-TW=", StringComparison.Ordinal) ||
            !trace.Contains("qps-ploc=", StringComparison.Ordinal))
        {
            return false;
        }

        return scenario == "p0-ping-trace" ||
            (trace.Contains("Outcome=Accepted", StringComparison.Ordinal) &&
             trace.Contains("Status=200", StringComparison.Ordinal));
    }

```

## FULL Fixture/MemoryOrganization/AdvancingClock source (synthetic only)
```cs
internal sealed class Fixture : IDisposable
{
    /// <summary>唯一核定的組織邊界。</summary>
    public const string Organization = "goodshepherdback";
    /// <summary>只供機密掃描與故障注入的假值。</summary>
    public const string SecretValue = "SYNTHETIC-SECRET-NOT-A-CREDENTIAL";
    /// <summary>唯一測試根，不從外部輸入組成清理目標。</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-deployment-" + Guid.NewGuid().ToString("N"));
    /// <summary>不含業務資料的記憶體組織。</summary>
    public MemoryOrganization Service { get; } = new();
    /// <summary>不依賴牆上時鐘的有限等待來源。</summary>
    public AdvancingClock Clock { get; } = new();
    /// <summary>固定清單候選，不含實際 token 或 userId。</summary>
    public RegistrationManifest Manifest { get; }
    /// <summary>以小型合成內容保護內容雜湊與還原，不作正式部署。</summary>
    public AssemblyImage Image { get; } = new("SpeechMessage.Dynamics.Plugins", "1.0.0.0", "0123456789abcdef", "AQID", Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), new[] { "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin" });
    /// <summary>本案例待測核心，使用相同假服務。</summary>
    public DeploymentEngine Engine { get; }
    /// <summary>證明唯讀計畫沒有解析秘密的計數。</summary>
    public int SecretReads { get; private set; }
    /// <summary>建立各案例隔離資源；推播也只使用佔位與假 secret。</summary>
    /// <param name="push">是否測試推播設定佔位。</param><param name="enabled">是否明確啟用測試步驟。</param>
    public Fixture(bool push = false, bool enabled = false)
    {
        Directory.CreateDirectory(Root);
        var step = new StepRegistration {
            UnsecureConfig = push ? "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"${secret:TestLineUserId}\"}" : "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            SecureConfig = push ? "${secret:TestChannelToken}" : "",
            EnabledByOrganization = enabled ? new Dictionary<string, bool>(StringComparer.Ordinal) { [Organization] = true } : new Dictionary<string, bool>(StringComparer.Ordinal)
        };
        Manifest = new RegistrationManifest { Steps = new[] { step } };
        Engine = new DeploymentEngine(Service, Root, Clock, _ => { SecretReads++; return SecretValue; });
        Service.Seed(new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Create" });
        Service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid()) { ["primaryobjecttypecode"] = "task", ["sdkmessageid"] = Service.Rows[0].ToEntityReference() });
    }
    /// <summary>對目前假組織規劃，測試無網路副作用。</summary>
    /// <returns>含固定時鐘的計畫。</returns>
    public DeploymentPlan Plan() => Engine.Plan(Manifest, Image, Organization);
    /// <summary>注入既有組件，以版本或公鑰衝突測試拒絕。</summary>
    /// <param name="version">合成版本。</param><param name="token">合成公鑰身份。</param>
    public void SeedAssembly(string version = "1.0.0.0", string token = "0123456789abcdef") => Service.Seed(new Entity("pluginassembly", Guid.NewGuid()) {
        ["name"] = Image.Name, ["version"] = version, ["publickeytoken"] = token, ["content"] = Image.Content,
        ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0)
    });
    /// <summary>離線產生沒有金鑰的最小 net462 樣本，僅供 MetadataReader 測試。</summary>
    /// <param name="extra">是否注入未註冊型別。</param><returns>不載入執行的 PE 路徑。</returns>
    public string Emit(bool extra = false)
    {
        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.6.2\")] " +
            "namespace Microsoft.Xrm.Sdk { public interface IPlugin { } } namespace SpeechMessage.Dynamics.Plugins { public abstract class PluginShellBase : Microsoft.Xrm.Sdk.IPlugin { } } " +
            "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { public sealed class DiagnosticsPingPlugin : SpeechMessage.Dynamics.Plugins.PluginShellBase { } " +
            (extra ? "public sealed class UnregisteredPlugin : Microsoft.Xrm.Sdk.IPlugin { }" : "") + " }";
        var compilation = CSharpCompilation.Create(Image.Name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(Root, "sample.dll"); var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(x => x.ToString())));
        return path;
    }
    /// <summary>待測程式已釋放 reader、stream、timer 後立即刪除案例自己的唯一根。</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>無外部資源的 SDK 假組織，僅模擬 P0 中繼資料與測試 task；不把它當作產品實作。</summary>
internal sealed class MemoryOrganization : IOrganizationService, IDisposable
{
    /// <summary>
    /// 官方 sdkmessageprocessingstep schema 的本包使用範圍；獨立於產品常數維護，不能接受 secureconfigid 別名。
    /// 此唯讀用途的私有固定陣列不含案例資料；未知欄位在查詢或寫入前拒絕，避免寬鬆替身掩蓋伺服器錯誤。
    /// </summary>
    private static readonly string[] StepAttributes =
    {
        "sdkmessageprocessingstepid", "name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
        "stage", "mode", "rank", "filteringattributes", "configuration",
        "sdkmessageprocessingstepsecureconfigid", "statecode", "statuscode"
    };

    /// <summary>案例自己的候選列，不跨 fixture 共用。</summary>
    public List<Entity> Rows { get; } = new();
    /// <summary>用於驗證寫入先後順序的固定事件，不包含欄位值。</summary>
    public List<string> Writes { get; } = new();
    /// <summary>寫入前檢查 export 的故障注入點。</summary>
    public Action? BeforeWrite { get; set; }
    /// <summary>只供 smoke 對應記錄的合成追蹤內容；null 表示尚無回應。</summary>
    public string? Trace { get; set; }
    /// <summary>設定第幾次 plugintracelog 查詢才可看見合成追蹤。</summary>
    public int TraceAvailableAfterPoll { get; set; } = 1;
    /// <summary>記錄 smoke 追蹤查詢次數，證明案例真的輪詢而非立即失敗。</summary>
    public int TraceQueryCount { get; private set; }
    /// <summary>故障注入：查詢時回傳同一例外，供 CLI 驗證遮罩與租約清理。</summary>
    public Exception? RetrieveFailure { get; set; }
    /// <summary>smoke task 與其關聯記錄建立後觸發 caller 取消的測試掛鉤。</summary>
    public Action? OnTaskCreated { get; set; }
    /// <summary>假 client 的 deterministic release 計數；用於確認連線租約只釋放一次。</summary>
    public int DisposeCount { get; private set; }
    /// <summary>只在安排階段注入列，不計入產品 SDK 寫入。</summary>
    /// <param name="entity">只屬本案例的假列。</param>
    public void Seed(Entity entity) => Rows.Add(entity);
    /// <summary>模擬資料庫生成 ID，並記錄唯一身份；task 帶動自己專用的非同步作業。</summary>
    /// <param name="entity">候選列。</param><returns>資料庫身份。</returns>
    public Guid Create(Entity entity)
    {
        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
        Record("Create:" + entity.LogicalName);
        var copy = Copy(entity); if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid(); Rows.Add(copy);
        if (entity.LogicalName == "task")
        {
            var correlation = Guid.NewGuid();
            Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["regardingobjectid"] = copy.ToEntityReference(), ["correlationid"] = correlation });
            if (Trace is not null)
            {
                Seed(new Entity("plugintracelog", Guid.NewGuid()) { ["correlationid"] = correlation, ["typename"] = new StepRegistration().Type, ["messageblock"] = Trace });
            }

            OnTaskCreated?.Invoke();
        }
        return copy.Id;
    }
    /// <summary>只更新同一實體的資料庫身份，不按顯示名稱合併。</summary>
    /// <param name="entity">包含 ID 的更新。</param>
    public void Update(Entity entity)
    {
        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
        Record("Update:" + entity.LogicalName); var row = Rows.Single(x => x.LogicalName == entity.LogicalName && x.Id == entity.Id);
        foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
    }
    /// <summary>刪除精確身份，用以驗證 smoke finally 的所有出口。</summary>
    /// <param name="entityName">固定邏輯名。</param><param name="id">本案例的資料庫 ID。</param>
    public void Delete(string entityName, Guid id) { Record("Delete:" + entityName); Rows.RemoveAll(x => x.LogicalName == entityName && x.Id == id); }
    /// <summary>以 detached 副本回傳，避免測試以共用可變參考掩蓋發布缺陷。</summary>
    /// <param name="entityName">邏輯名。</param><param name="id">資料庫 ID。</param><param name="columnSet">要求欄位。</param><returns>獨立副本。</returns>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        ValidateStepAttributes(entityName, columnSet.Columns);
        return Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
    }
    /// <summary>只支援測試實際需要的 QueryExpression，未支援格式直接失敗。</summary>
    /// <param name="query">含隔離條件的結構查詢。</param><returns>獨立結果列集合。</returns>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        if (RetrieveFailure is not null) throw RetrieveFailure;
        var expression = Assert.IsType<QueryExpression>(query);
        ValidateStepAttributes(expression.EntityName, expression.ColumnSet.Columns);
        ValidateStepFilter(expression.EntityName, expression.Criteria);
        if (expression.EntityName == "plugintracelog")
        {
            TraceQueryCount++;
            if (TraceQueryCount < TraceAvailableAfterPoll)
            {
                return new EntityCollection();
            }
        }

        var rows = Rows.Where(x => x.LogicalName == expression.EntityName && Matches(x, expression.Criteria)).Select(Copy).ToList();
        return new EntityCollection(rows);
    }

    /// <summary>
    /// 在結果列是否存在之前驗證步驟欄位，重現 Dataverse 的 QueryBuilderNoAttribute 邊界。
    /// 只回傳固定型別、數字碼與合成說明，不含輸入欄位值；非步驟實體保持既有模擬範圍。
    /// </summary>
    /// <param name="entityName">本次 SDK 操作的實體邏輯名稱。</param>
    /// <param name="attributes">查詢或寫入要求的欄位名稱，不讀取對應內容。</param>
    private static void ValidateStepAttributes(string entityName, IEnumerable<string> attributes)
    {
        if (entityName == "sdkmessageprocessingstep" &&
            attributes.Any(attribute => !StepAttributes.Contains(attribute, StringComparer.Ordinal)))
        {
            throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80041103),
                Message = "Synthetic metadata attribute was not found."
            });
        }
    }

    /// <summary>遞迴檢查 AND／OR 條件中的步驟欄位，防止只修正 ColumnSet 卻把錯字移到條件後假通過。</summary>
    /// <param name="entityName">查詢實體的邏輯名稱。</param>
    /// <param name="filter">由本次產品呼叫建立、不跨案例保留的條件樹。</param>
    private static void ValidateStepFilter(string entityName, FilterExpression filter)
    {
        ValidateStepAttributes(entityName, filter.Conditions.Select(condition => condition.AttributeName));
        foreach (var child in filter.Filters)
        {
            ValidateStepFilter(entityName, child);
        }
    }
    /// <summary>模擬狀態切換；未定義訊息拒絕，避免假的成功。</summary>
    /// <param name="request">SDK 訊息。</param><returns>合成回應。</returns>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request.RequestName == "SetState") {
            var reference = (EntityReference)request["EntityMoniker"];
            Update(new Entity(reference.LogicalName, reference.Id) { ["statecode"] = request["State"], ["statuscode"] = request["Status"] });
            return new OrganizationResponse();
        }
        throw new NotSupportedException("Unsupported synthetic request.");
    }
    /// <summary>本包不使用關聯操作，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>本包不使用解除關聯，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>假服務沒有 OS handle；計數只用來斷言單次連線租約確實執行 release。</summary>
    public void Dispose() => DisposeCount++;
    /// <summary>只記固定事件，不記秘密或業務資料。</summary>
    /// <param name="name">編譯期種類。</param>
    private void Record(string name) { BeforeWrite?.Invoke(); Writes.Add(name); }
    /// <summary>建立欄位層級獨立候選，不把可變 SDK 選項或參照交回呼叫者。</summary>
    /// <param name="entity">來源列。</param><returns>detached 副本。</returns>
    private static Entity Copy(Entity entity)
    {
        var copy = new Entity(entity.LogicalName, entity.Id);
        foreach (var pair in entity.Attributes) copy[pair.Key] = pair.Value switch { EntityReference r => new EntityReference(r.LogicalName, r.Id), OptionSetValue o => new OptionSetValue(o.Value), _ => pair.Value };
        return copy;
    }

    /// <summary>只依精確欄位及 ID 判斷 AND／OR，未知比較不可假通過。</summary>
    /// <param name="row">候選列。</param><param name="filter">查詢條件。</param><returns>是否符合。</returns>
    private static bool Matches(Entity row, FilterExpression filter)
    {
        var values = filter.Conditions.Select(c => {
            object? actual = row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == row.LogicalName + "id" ? row.Id : null;
            if (actual is EntityReference reference) actual = reference.Id;
            if (actual is OptionSetValue option) actual = option.Value;
            return c.Operator switch { ConditionOperator.Equal => Equals(actual, c.Values[0]), ConditionOperator.In => c.Values.Contains(actual), _ => throw new NotSupportedException() };
        }).Concat(filter.Filters.Select(x => Matches(row, x)));
        return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
    }
}

/// <summary>
/// 單案例隔離的 UTC／單調假時鐘。短輪詢等待會快轉虛擬時間，長逾時計時器則保留至期限；
/// 因此測試能在不等待三分鐘的情況下驗證真正輪詢、逾時取消及 timer 釋放。
/// </summary>
internal sealed class AdvancingClock : TimeProvider
{
    /// <summary>只供本 fixture 的 UTC 時點。</summary>
    private DateTimeOffset now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    /// <summary>由本案例建立且尚未釋放的 timer；集合不跨 fixture 共用。</summary>
    private readonly List<OwnedTimer> timers = new();
    /// <summary>確認所有 delay 的取消登記與 timer 都釋放。</summary>
    public int ActiveTimers { get; private set; }
    /// <summary>記錄虛擬輪詢次數，避免 timeout 案例在第一次查詢前就被取消。</summary>
    public int DelayCount { get; private set; }
    /// <summary>只保留本案例最多九十次輪詢的間隔；非共享集合，案例結束即釋放。</summary>
    public List<TimeSpan> ObservedDelays { get; } = new();
    /// <summary>在建立短延遲時注入正常 caller 取消；委派僅屬本 fixture，不跨測試持有。</summary>
    public Action? OnDelay { get; set; }
    /// <summary>回傳固定起點加上假延遲。</summary><returns>UTC 時點。</returns>
    public override DateTimeOffset GetUtcNow() => now;
    /// <summary>單調時間使用同一假時間，不讀硬體計時器。</summary><returns>tick 數。</returns>
    public override long GetTimestamp() => now.Ticks;
    /// <summary>固定 tick 頻率配合單調時間。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <summary>長期限先排程；短輪詢等待按要求的實際間隔快轉並執行所有已到期的一次性回呼。</summary>
    /// <param name="callback">延遲的單次完成回呼。</param><param name="state">延遲 owner 的狀態。</param><param name="dueTime">模擬延遲。</param><param name="period">不得重複。</param><returns>由等待者釋放的 timer。</returns>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new OwnedTimer(this, callback, state, dueTime);
        timers.Add(timer);
        ActiveTimers++;
        if (dueTime <= TimeSpan.FromSeconds(2))
        {
            DelayCount++;
            ObservedDelays.Add(dueTime);
            now += dueTime;
            OnDelay?.Invoke();
            foreach (var scheduled in timers.ToArray())
            {
                scheduled.FireIfDue(now);
            }
        }

        return timer;
    }

    /// <summary>從案例 owner 移除已釋放 timer，讓生命週期斷言精確回到基線。</summary>
    /// <param name="timer">被釋放的單次 timer。</param>
    private void Release(OwnedTimer timer)
    {
        if (timers.Remove(timer))
        {
            ActiveTimers--;
        }
    }

    /// <summary>單次假 timer 的唯一 owner，釋放後計數回到基線。</summary>
    private sealed class OwnedTimer(AdvancingClock owner) : ITimer
    {
        /// <summary>期限到達時交由 BCL 延遲／取消來源的 callback。</summary>
        private TimerCallback? callback;
        /// <summary>只在 timer 活躍期間保留的 callback 狀態。</summary>
        private object? state;
        /// <summary>虛擬到期時間；觸發後設為 MaxValue，避免重複回呼。</summary>
        private DateTimeOffset dueAt;
        /// <summary>防止同步／非同步重複 Dispose 減少兩次計數。</summary>
        private bool disposed;
        /// <summary>建立一次性計時器；無限期限不會觸發 callback。</summary>
        /// <param name="timerOwner">本案例的唯一 timer 集合 owner。</param>
        /// <param name="timerCallback">期限到達時呼叫的委派。</param>
        /// <param name="timerState">傳回 callback 的狀態。</param>
        /// <param name="timerDueTime">虛擬到期時間。</param>
        public OwnedTimer(AdvancingClock timerOwner, TimerCallback timerCallback, object? timerState, TimeSpan timerDueTime) : this(timerOwner)
        {
            callback = timerCallback;
            state = timerState;
            dueAt = timerDueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : timerOwner.GetUtcNow() + timerDueTime;
        }

        /// <summary>虛擬時間到達期限時執行一次回呼；timer 仍由原呼叫端負責 Dispose。</summary>
        /// <param name="utcNow">目前的虛擬 UTC 時點。</param>
        public void FireIfDue(DateTimeOffset utcNow)
        {
            if (disposed || dueAt > utcNow || callback is null)
            {
                return;
            }

            dueAt = DateTimeOffset.MaxValue;
            callback(state);
        }

        /// <summary>測試只使用單次延遲，不接受改期。</summary>
        /// <param name="dueTime">不使用。</param><param name="period">不使用。</param><returns>不支援改期。</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        /// <summary>解除 timer owner，只釋放一次。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callback = null;
            state = null;
            owner.Release(this);
        }
        /// <summary>非同步釋放與同步使用相同路徑。</summary><returns>已完成。</returns>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.