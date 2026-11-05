# CCG reviewer Task: integration-p1-bootstrap

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1 milestone 1 code review: smoke / P1-01 / P1-02
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
        internal int Calls { get; private set; }
        /// <summary>最後一頁之前回傳 paging cookie；最多十頁，由記憶體測試設定。</summary>
        internal int PageCount { get; set; } = 1;
        /// <summary>當前同步 SDK 呼叫數；finally 歸零作為 drain 證據。</summary>
        internal int ActiveCalls { get; private set; }

        /// <summary>為每個查詢建立新結果，IN 保留全部不同資料庫 ID；不使用顯示名稱合併。</summary>
        /// <param name="query">借用的 query，僅保留最後一個合成實例。</param><returns>此次呼叫專用 EntityCollection。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ActiveCalls++;
            try
            {
                Calls++;
                LastQuery = query;
                var name = query switch { QueryExpression expression => expression.EntityName, QueryByAttribute attributes => attributes.EntityName, _ => throw new NotSupportedException() };
                if (name == "invalid_entity_name") throw new InvalidOperationException("Synthetic entity rejection.");
                var condition = (query as QueryExpression)?.Criteria.Conditions.FirstOrDefault(item => item.Operator == ConditionOperator.In);
                var ids = condition is null ? new[] { ListId } : condition.Values.Cast<Guid>().ToArray();
                if (PageCount > 1) ids = Enumerable.Range(1, 100).Select(index => new Guid(index + (Calls * 100), 0, 0, new byte[8])).ToArray();
                var result = new EntityCollection(ids.Select(id => new Entity(name, id) { ["new_status"] = "Active", ["fullname"] = "synthetic-same-name" }).ToList());
                result.MoreRecords = Calls < PageCount;
                result.PagingCookie = result.MoreRecords ? "synthetic-page-" + Calls : null;
                return result;
            }
            finally { ActiveCalls--; }
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

## diff SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
```diff

```

## diff SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
```diff

```

## diff SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj
```diff

```

## diff SpeechMessageProducts.sln
```diff

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
