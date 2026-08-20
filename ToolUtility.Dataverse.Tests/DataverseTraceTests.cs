using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Dataverse;
using Moq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 Dataverse 結構化執行軌跡的隱私、非侵入性、檔案生命週期與端對端關聯契約。
/// 本類別停用 xUnit 平行化，因為 Trace 的已啟用實例是程序層級觀測器；測試仍以真實 pool 與
/// Gateway 的多執行緒工作負載驗證 request 間隔離，且所有輸出只寫入每個測試私有的暫存目錄。
/// </summary>
public sealed class DataverseTraceTests
{
    /// <summary>
    /// 保護關閉 trace 的零侵入契約。故障注入是直接呼叫最熱的 crm.op 觀測點；
    /// 決定性斷言是沒有建立輸出檔，且暖機後的一次呼叫不配置 managed 物件。
    /// </summary>
    [Fact]
    public void Disabled_trace_writes_nothing_and_allocates_nothing_on_hot_path()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var trace = new DataverseTrace(CreateOptions(directory, enabled: false));
            trace.CrmOperation("Execute");

            var before = GC.GetAllocatedBytesForCurrentThread();
            trace.CrmOperation("Execute");
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(before, after);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.jsonl"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 保護 JSONL schema 契約。以每種正式事件各寫一筆作為故障注入，
    /// 決定性斷言是每一行都可解析，且各事件具有分析器要求的共同與專屬欄位。
    /// </summary>
    [Fact]
    public void Enabled_trace_writes_parseable_records_with_required_schema_fields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("schema-trace", "schema-user", sessionId: null))
                {
                    trace.GatewayExecuteEnter(1);
                    trace.GatewayExecuteExit(1);
                    trace.PoolAcquireWait(3);
                    trace.PoolAcquire("l-schema-hit", "c-schema", "ChurchReport|Test|org.test|service", hit: true);
                    trace.PoolAcquire("l-schema-miss", "c-schema-new", "ChurchReport|Test|org.test|service", hit: false);
                    trace.PoolAcquireTimeout();
                    trace.PoolHealth("c-schema", result: true);
                    trace.PoolReturn("l-schema-hit", "c-schema", state: "healthy", callerIdAtReturn: "", heldMs: 4);
                    trace.PoolDispose("c-schema", stateAtDispose: "Idle", reason: "idle");
                    trace.PoolCleanup(idleBefore: 3, idleAfter: 2, minSize: 2, evicted: 1);
                    trace.PoolLockWait(12, "ensureMin.commit");
                    trace.GatewayConcurrent(2);
                    trace.GatewayScopeEnd(depthAtDispose: 0, leaseStillHeld: false);
                    trace.PoolSnapshot(new DataverseTrace.PoolSnapshotData
                    {
                        PoolKey = "ChurchReport|Test|org.test|service",
                        Idle = 2, Leased = 1, Alive = 3, Pending = 0,
                        Created = 5, Discarded = 2, TotalAcquires = 9, TotalReleases = 8,
                        Waiting = 0, AcquireTimeouts = 0, Faulted = 1, SubPools = 1
                    });
                    trace.CrmOperation("Execute");
                }
            }

            var records = ReadRecords(directory);
            var requirements = new Dictionary<string, string[]>
            {
                ["request.begin"] = ["ts", "ev", "traceId", "user"],
                ["request.end"] = ["ts", "ev", "traceId", "user", "durationMs"],
                ["gateway.execute.enter"] = ["ts", "ev", "traceId", "user", "depth"],
                ["gateway.execute.exit"] = ["ts", "ev", "traceId", "user", "depth"],
                ["pool.acquire.wait"] = ["ts", "ev", "traceId", "waitedMs"],
                ["pool.acquire.hit"] = ["ts", "ev", "traceId", "user", "leaseId", "clientId", "poolKey"],
                ["pool.acquire.miss"] = ["ts", "ev", "traceId", "user", "leaseId", "clientId", "poolKey"],
                ["pool.acquire.timeout"] = ["ts", "ev", "traceId"],
                ["pool.health"] = ["ts", "ev", "clientId", "result"],
                ["pool.return"] = ["ts", "ev", "traceId", "user", "leaseId", "clientId", "state", "callerIdAtReturn", "heldMs"],
                ["pool.dispose"] = ["ts", "ev", "clientId", "stateAtDispose", "reason"],
                ["pool.cleanup"] = ["ts", "ev", "idleBefore", "idleAfter", "minSize"],
                ["crm.op"] = ["ts", "ev", "traceId", "op", "leaseId"]
            };

            foreach (var (eventName, fields) in requirements)
            {
                var record = Assert.Single(records.Where(record => record.GetProperty("ev").GetString() == eventName));
                foreach (var field in fields)
                {
                    Assert.True(record.TryGetProperty(field, out var value), $"{eventName} 缺少 {field}");
                    Assert.NotEqual(JsonValueKind.Null, value.ValueKind);
                }
            }
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 保護 UTC timestamp 的字串可排序契約。依序寫入多個事件後，決定性斷言是 JSONL 中的 ts
    /// 字串本身已是遞增序列，外部分析器不必解析時區或依賴檔案系統時間戳。
    /// </summary>
    [Fact]
    public void Timestamp_strings_sort_in_event_emission_order()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("timestamp-trace", "timestamp-user", sessionId: null))
                {
                    trace.GatewayExecuteEnter(1);
                    trace.GatewayExecuteExit(1);
                    trace.CrmOperation("Execute");
                }
            }

            var timestamps = ReadRecords(directory)
                .Select(record => record.GetProperty("ts").GetString())
                .ToArray();
            Assert.Equal(timestamps.OrderBy(value => value, StringComparer.Ordinal), timestamps);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 保護使用者假名的隱私契約。相同識別來源在同一程序實例必須穩定，
    /// 但兩個獨立 Trace 實例使用不同隨機 salt，決定性斷言是輸出皆為 u_ 前綴且彼此不同。
    /// </summary>
    [Fact]
    public void User_pseudonym_is_stable_per_trace_and_changes_with_new_process_salt()
    {
        var firstDirectory = CreateTemporaryDirectory();
        var secondDirectory = CreateTemporaryDirectory();
        try
        {
            string first;
            string repeated;
            using (var trace = new DataverseTrace(CreateOptions(firstDirectory, enabled: true)))
            {
                first = trace.CreateUserPseudonym("member@example.test", sessionId: null);
                repeated = trace.CreateUserPseudonym("member@example.test", sessionId: null);
            }

            string second;
            using (var trace = new DataverseTrace(CreateOptions(secondDirectory, enabled: true)))
            {
                second = trace.CreateUserPseudonym("member@example.test", sessionId: null);
            }

            Assert.Equal(first, repeated);
            Assert.StartsWith("u_", first, StringComparison.Ordinal);
            Assert.Matches("^u_[0-9a-f]{8}$", first);
            Assert.NotEqual(first, second);
        }
        finally
        {
            DeleteTemporaryDirectory(firstDirectory);
            DeleteTemporaryDirectory(secondDirectory);
        }
    }

    /// <summary>
    /// 保護產品層只提供原始身分來源、而假名化仍由 ToolUtility 集中負責的契約。
    /// 測試依序注入已驗證名稱、只有 Session Id、以及兩者皆無的三種來源；決定性斷言是
    /// BeginRequest 輸出的 user 分別等於既有 HMAC helper 的結果，且三個假名互不相同。
    /// </summary>
    [Fact]
    public void Begin_request_falls_back_from_identity_to_session_then_anon()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            string expectedIdentity;
            string expectedSession;
            string expectedAnon;
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("identity-trace", "identity-user", sessionId: null)) { }
                using (trace.BeginRequest("session-trace", identityName: null, sessionId: "session-user")) { }
                using (trace.BeginRequest("anon-trace", identityName: null, sessionId: null)) { }

                expectedIdentity = trace.CreateUserPseudonym("identity-user", sessionId: null);
                expectedSession = trace.CreateUserPseudonym(identityName: null, sessionId: "session-user");
                expectedAnon = trace.CreateUserPseudonym(identityName: null, sessionId: null);
            }

            var users = ReadRecords(directory)
                .Where(record => record.GetProperty("ev").GetString() == "request.begin")
                .ToDictionary(record => record.GetProperty("traceId").GetString()!,
                    record => record.GetProperty("user").GetString()!);

            Assert.Equal(expectedIdentity, users["identity-trace"]);
            Assert.Equal(expectedSession, users["session-trace"]);
            Assert.Equal(expectedAnon, users["anon-trace"]);
            Assert.Equal(3, users.Values.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 保護檔案輪替的容量上限。以極小測試檔案上限製造連續輪替，
    /// 決定性斷言是產生多個檔案且保留總數永遠不超過設定上限。
    /// </summary>
    [Fact]
    public void Trace_rotates_files_and_keeps_only_configured_recent_files()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var options = CreateOptions(directory, enabled: true);
            options.MaxFileBytes = 256;
            options.MaxRetainedFiles = 2;
            using (var trace = new DataverseTrace(options))
            {
                using (trace.BeginRequest("rotation-trace", "rotation-user", sessionId: null))
                {
                    for (var index = 0; index < 12; index++)
                        trace.CrmOperation(new string('x', 128));
                }
            }

            var files = Directory.EnumerateFiles(directory, "*.jsonl").ToArray();
            Assert.Equal(2, files.Length);
            Assert.All(files, file => Assert.NotEmpty(File.ReadAllLines(file)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 保護滿載時不阻塞 request 的契約。以容量為二且延長背景 flush 的佇列製造滿載，
    /// 決定性斷言是呼叫端快速返回，且最終 JSONL 有帶累計 count 的 trace.dropped 事件。
    /// </summary>
    [Fact]
    public void Full_queue_drops_oldest_records_and_emits_trace_dropped_without_blocking_caller()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var options = CreateOptions(directory, enabled: true);
            options.QueueCapacity = 2;
            options.FlushInterval = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();
            using (var trace = new DataverseTrace(options))
            {
                using (trace.BeginRequest("drop-trace", "drop-user", sessionId: null))
                {
                    for (var index = 0; index < 2000; index++)
                        trace.CrmOperation("Execute");
                }
            }
            stopwatch.Stop();

            var dropped = Assert.Single(ReadRecords(directory)
                .Where(record => record.GetProperty("ev").GetString() == "trace.dropped"));
            Assert.True(dropped.GetProperty("count").GetInt64() > 0);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 以三個假名、二十個並行操作、一次巢狀 Execute 與一次服務例外驅動真實 Pool 與 Gateway。
    /// 此測試保護觀測接線本身：借還必須對稱、同 client 的租期不可重疊、巢狀操作不多借 lease、
    /// 例外必為 faulted return，且所有實際輸出事件都滿足其 schema 的必要欄位。
    /// </summary>
    [Fact]
    public async Task End_to_end_trace_proves_pool_lease_isolation_and_fault_eviction()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var options = CreateOptions(directory, enabled: true);
            options.QueueCapacity = 4096;
            options.FlushInterval = TimeSpan.FromMilliseconds(20);
            options.MaxFileBytes = 1024 * 1024;
            using (var trace = new DataverseTrace(options))
            using (var pool = new BoundedClientPool(
                       (_, _) => new TraceOrganizationService(),
                       _ => true,
                       new DataversePoolOptions
                       {
                           MinSize = 2,
                           MaxN = 4,
                           AcquireTimeout = TimeSpan.FromSeconds(5),
                           IdleTimeout = TimeSpan.FromMinutes(1),
                           HealthInterval = TimeSpan.FromMinutes(1)
                       }))
            {
                var manager = new TestManager(pool);
                var tasks = Enumerable.Range(0, 20).Select(index => Task.Run(() =>
                {
                    using var request = trace.BeginRequest($"trace-{index}", $"member-{index % 3}", sessionId: null);
                    using var gateway = new DataverseGateway(manager);
                    var service = new GatewayOrganizationService(gateway);

                    if (index == 0)
                    {
                        gateway.Execute(_ => service.Execute(new OrganizationRequest("nested")));
                    }
                    else if (index == 1)
                    {
                        Assert.Throws<CommunicationException>(() => service.Execute(new OrganizationRequest("throw")));
                    }
                    else
                    {
                        service.Execute(new OrganizationRequest("normal"));
                    }
                })).ToArray();

                await Task.WhenAll(tasks);
            }

            var records = ReadRecords(directory);
            var acquires = records
                .Select((record, index) => new { record, index })
                .Where(item => item.record.GetProperty("ev").GetString() is "pool.acquire.hit" or "pool.acquire.miss")
                .ToDictionary(
                    item => item.record.GetProperty("leaseId").GetString()!,
                    item => new LeaseInterval(
                        item.record.GetProperty("clientId").GetString()!,
                        item.index,
                        ReturnIndex: -1));
            var returns = records
                .Select((record, index) => new { record, index })
                .Where(item => item.record.GetProperty("ev").GetString() == "pool.return")
                .ToArray();

            Assert.Equal(acquires.Count, returns.Length);
            foreach (var returned in returns)
            {
                var leaseId = returned.record.GetProperty("leaseId").GetString()!;
                var acquired = acquires[leaseId];
                Assert.Equal(acquired.ClientId, returned.record.GetProperty("clientId").GetString());
                acquires[leaseId] = acquired with { ReturnIndex = returned.index };
            }

            foreach (var clientIntervals in acquires.Values.GroupBy(interval => interval.ClientId))
            {
                var ordered = clientIntervals.OrderBy(interval => interval.AcquireIndex).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                    Assert.True(ordered[index - 1].ReturnIndex < ordered[index].AcquireIndex,
                        $"{clientIntervals.Key} 的兩段 lease 發生重疊。");
            }

            Assert.Equal(1, acquires.Values.Count(interval => interval.ClientId != null &&
                records[interval.AcquireIndex].GetProperty("traceId").GetString() == "trace-0"));
            Assert.Contains(returns, record => record.record.GetProperty("state").GetString() == "faulted");
            Assert.All(records, AssertRecordHasRequiredFields);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 釘住建線失敗的可觀測性契約。修改前，建線失敗的 request 在稽核檔中只留下一筆
    /// pool.acquire.wait，既無 hit 也無 miss，最慢的那些 request 因此完全無法被解釋。
    /// 本測試以必定失敗的 client factory 重現該情境，並斷言失敗路徑會留下完整證據。
    /// </summary>
    [Fact]
    public void Failed_client_creation_emits_create_and_acquire_fail_events()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            using (trace.BeginRequest("create-fail-trace", "create-fail-user", sessionId: null))
            {
                // 外層包一般 Exception，模擬 CrmConnectionService 包裝底層傳輸錯誤的實際行為。
                using var pool = new BoundedClientPool(
                    (_, _) => throw new InvalidOperationException(
                        "建立 OnPremiseClient 連線時發生錯誤", new System.Net.WebException("503")),
                    _ => true,
                    new DataversePoolOptions { MinSize = 1, MaxN = 2 });

                Assert.Throws<InvalidOperationException>(() =>
                    pool.Acquire(new DataverseConnectionKey("P", "Test", "https://org.test/x", "svc")));
            }

            var records = ReadRecords(directory);
            Assert.All(records, AssertRecordHasRequiredFields);

            var begin = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "pool.create.begin"));
            Assert.Equal("ensureMin", begin.GetProperty("reason").GetString());

            var end = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "pool.create.end"));
            Assert.False(end.GetProperty("ok").GetBoolean());
            // 取最內層例外型別，外層包裝不得遮蔽真正的傳輸失敗原因。
            Assert.Equal("WebException", end.GetProperty("errKind").GetString());

            var fail = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "pool.acquire.fail"));
            Assert.Equal("ensureMin", fail.GetProperty("phase").GetString());
            Assert.Equal("WebException", fail.GetProperty("errKind").GetString());
            Assert.Equal("create-fail-trace", fail.GetProperty("traceId").GetString());

            // 稽核檔絕不可出現例外訊息：其中內嵌組織 URL 與服務帳號。
            Assert.DoesNotContain(records, r => r.ToString().Contains("OnPremiseClient", StringComparison.Ordinal));

            // 核心不變量：等待數必須能被結果完整解釋，不再有無法交代的 acquire。
            var waits = records.Count(r => r.GetProperty("ev").GetString() == "pool.acquire.wait");
            var results = records.Count(r => r.GetProperty("ev").GetString()
                is "pool.acquire.hit" or "pool.acquire.miss" or "pool.acquire.fail");
            Assert.Equal(waits, results);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 釘住 request.end 的聚合契約：一個 request 的時間歸屬與 N+1 徵兆必須是單筆可讀事件，
    /// 不需要下游自行重組數千筆 crm.op。
    /// </summary>
    /// <remarks>
    /// topEntity / topEntityCount 是本次觀測性強化的核心：實測有一個登入 request 發出 30 次 CRM
    /// 呼叫、耗時 5.7 秒，但當時無法分辨那是 30 張不同的表（複雜流程），還是同一張表被查了 30 次
    /// （應合併的 N+1）—— 這兩者的處置方式完全相反。
    /// </remarks>
    [Fact]
    public void Request_end_reports_crm_attribution_and_top_entity()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("agg-trace", "agg-user", sessionId: null))
                {
                    // 同一張表被查 3 次、另一張 1 次：典型 N+1 的形狀。
                    trace.CrmOperation("RetrieveMultiple", "contact", 100, ok: true, count: 5);
                    trace.CrmOperation("Retrieve", "contact", 50, ok: true, count: -1);
                    trace.CrmOperation("Retrieve", "contact", 50, ok: true, count: -1);
                    trace.CrmOperation("RetrieveMultiple", "account", 20, ok: true, count: 2);
                }
            }

            var records = ReadRecords(directory);
            Assert.All(records, AssertRecordHasRequiredFields);
            var end = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "request.end"));

            Assert.Equal(4, end.GetProperty("crmCount").GetInt32());
            Assert.Equal(220, end.GetProperty("crmMs").GetInt32());
            Assert.Equal("contact", end.GetProperty("topEntity").GetString());
            Assert.Equal(3, end.GetProperty("topEntityCount").GetInt32());
            Assert.Equal("2", end.GetProperty("distinctEntities").GetString());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 釘住租約洩漏偵測：request 結束時未歸還的租約數必須被回報，而不是等到連線池耗盡才發現。
    /// </summary>
    [Fact]
    public void Request_end_reports_outstanding_leases_when_a_lease_is_never_returned()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("leak-trace", "leak-user", sessionId: null))
                {
                    trace.PoolAcquire("l-1", "c-1", "P|Test|host|svc", hit: true);
                    trace.PoolReturn("l-1", "c-1", "healthy", string.Empty, 10);
                    // 第二條租約刻意不歸還，模擬控制流漏掉 Dispose 的情形。
                    trace.PoolAcquire("l-2", "c-2", "P|Test|host|svc", hit: true);
                }
            }

            var end = Assert.Single(ReadRecords(directory)
                .Where(r => r.GetProperty("ev").GetString() == "request.end"));
            Assert.Equal(2, end.GetProperty("leaseCount").GetInt32());
            Assert.Equal(1, end.GetProperty("leaseOutstanding").GetInt32());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 釘住平行 Gateway 偵測 —— 這是目前架構中唯一沒有防護的 Session Leakage 風險。
    /// </summary>
    /// <remarks>
    /// DataverseGateway 是 Scoped，但 _depth 與 _lease 無同步保護；產品程式碼有十餘處 Task.Run
    /// 會讓多執行緒共用同一個實例。本測試以 Barrier 強制兩條執行緒同時進入，斷言事件確實產生，
    /// 並確認 request.end 的 concurrentGateway 有累計 —— 讓這個風險從隱形變成可稽核。
    /// </remarks>
    [Fact]
    public async Task Concurrent_gateway_use_is_detected_and_reported()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("concurrent-trace", "concurrent-user", sessionId: null))
                {
                    var lease = new CountingLease(new Mock<IOrganizationService>(MockBehavior.Loose).Object);
                    using var gateway = new DataverseGateway(new SingleLeaseManager(lease));
                    var ready = new Barrier(2);

                    await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                    {
                        ready.SignalAndWait();
                        gateway.Execute(_ => Thread.Sleep(120));
                    })));
                }
            }

            var records = ReadRecords(directory);
            Assert.All(records, AssertRecordHasRequiredFields);

            var concurrent = records.Where(r => r.GetProperty("ev").GetString() == "gateway.concurrent").ToArray();
            Assert.NotEmpty(concurrent);
            Assert.All(concurrent, r => Assert.True(r.GetProperty("activeCalls").GetInt32() > 1));

            var end = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "request.end"));
            Assert.True(end.GetProperty("concurrentGateway").GetInt32() > 0);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>正常路徑不得產生平行告警，否則這個訊號會因為狼來了而失去價值。</summary>
    [Fact]
    public void Nested_gateway_calls_do_not_report_concurrency()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var trace = new DataverseTrace(CreateOptions(directory, enabled: true)))
            {
                using (trace.BeginRequest("nested-trace", "nested-user", sessionId: null))
                {
                    var lease = new CountingLease(new Mock<IOrganizationService>(MockBehavior.Loose).Object);
                    using var gateway = new DataverseGateway(new SingleLeaseManager(lease));
                    gateway.Execute(_ => gateway.Execute(__ => gateway.Execute(___ => { })));
                }
            }

            var records = ReadRecords(directory);
            Assert.DoesNotContain(records, r => r.GetProperty("ev").GetString() == "gateway.concurrent");

            // 巢狀深度仍必須被如實回報，否則無法分辨「沒有巢狀」與「沒有量測」。
            var end = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "request.end"));
            Assert.Equal(3, end.GetProperty("maxDepth").GetInt32());

            // scope 結束時不得仍持有租約：巢狀最外層的 finally 應已歸還。
            var scopeEnd = Assert.Single(records.Where(r => r.GetProperty("ev").GetString() == "gateway.scope.end"));
            Assert.False(scopeEnd.GetProperty("leaseStillHeld").GetBoolean());
            Assert.Equal(0, scopeEnd.GetProperty("depthAtDispose").GetInt32());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// 釘住資源快照契約 —— 這是整份稽核檔中唯一能支撐「記憶體是否洩漏」這個命題的證據來源。
    /// </summary>
    [Fact]
    public async Task Process_snapshot_is_emitted_periodically_with_resource_baseline()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var options = CreateOptions(directory, enabled: true);
            options.SnapshotInterval = TimeSpan.FromMilliseconds(1);
            using (var trace = new DataverseTrace(options))
            {
                using (trace.BeginRequest("snap-trace", "snap-user", sessionId: null))
                    trace.CrmOperation("Retrieve", "contact", 5, ok: true, count: -1);
                await Task.Delay(300);
            }

            var records = ReadRecords(directory);
            Assert.All(records, AssertRecordHasRequiredFields);

            var snapshots = records.Where(r => r.GetProperty("ev").GetString() == "proc.snapshot").ToArray();
            Assert.NotEmpty(snapshots);

            // 量測值必須是真實讀數而非佔位值，否則趨勢分析會建立在假資料上。
            Assert.All(snapshots, snapshot =>
            {
                Assert.True(snapshot.GetProperty("privateMb").GetInt64() > 0);
                Assert.True(snapshot.GetProperty("handles").GetInt32() > 0);
                Assert.True(snapshot.GetProperty("threads").GetInt32() > 0);
            });
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>只租一條 lease 的測試用 manager。</summary>
    private sealed class SingleLeaseManager : IDataverseConnectionManager
    {
        private readonly IClientLease _lease;
        internal SingleLeaseManager(IClientLease lease) => _lease = lease;
        public IClientLease Acquire(CancellationToken cancellationToken = default) => _lease;
        public DataversePoolMetrics GetMetrics() => new();
        public void Dispose() { }
    }

    /// <summary>記錄歸還次數的測試用 lease；Dispose 冪等以容許平行測試重複釋放。</summary>
    private sealed class CountingLease : IClientLease
    {
        internal CountingLease(IOrganizationService service) => Service = service;
        public IOrganizationService Service { get; }
        public void MarkFaulted() { }
        public void Dispose() { }
    }

    private static DataverseTraceOptions CreateOptions(string directory, bool enabled)
    {
        return new DataverseTraceOptions
        {
            Enabled = enabled,
            Path = Path.Combine(directory, "dataverse-trace.jsonl"),
            MaxFileBytes = 64 * 1024 * 1024,
            MaxRetainedFiles = 5,
            QueueCapacity = 1024,
            FlushInterval = TimeSpan.FromMilliseconds(50)
        };
    }

    private static List<JsonElement> ReadRecords(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.jsonl")
            .SelectMany(File.ReadLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private static void AssertRecordHasRequiredFields(JsonElement record)
    {
        var commonFields = new[] { "ts", "ev" };
        foreach (var field in commonFields)
            Assert.True(record.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null);

        var eventName = record.GetProperty("ev").GetString();
        var fields = eventName switch
        {
            "request.begin" => new[] { "traceId", "user" },
            "request.end" => new[]
            {
                "traceId", "user", "durationMs", "crmCount", "crmMs", "leaseCount",
                "leaseOutstanding", "maxDepth", "concurrentGateway", "topEntity",
                "topEntityCount", "distinctEntities"
            },
            "gateway.execute.enter" or "gateway.execute.exit" => new[] { "traceId", "user", "depth" },
            "pool.acquire.wait" => new[] { "traceId", "waitedMs" },
            "pool.acquire.hit" or "pool.acquire.miss" => new[] { "traceId", "user", "leaseId", "clientId", "poolKey" },
            "pool.acquire.timeout" => new[] { "traceId" },
            "pool.acquire.fail" => new[] { "traceId", "phase", "waitedMs", "totalMs", "errKind" },
            "pool.create.begin" => new[] { "traceId", "poolKey", "reason" },
            "pool.create.end" => new[] { "traceId", "clientId", "reason", "ms", "ok", "errKind" },
            "pool.health" => new[] { "clientId", "result" },
            "pool.return" => new[] { "traceId", "user", "leaseId", "clientId", "state", "callerIdAtReturn", "heldMs" },
            "pool.dispose" => new[] { "clientId", "stateAtDispose", "reason" },
            "pool.cleanup" => new[] { "idleBefore", "idleAfter", "minSize", "evicted" },
            "pool.lock.wait" => new[] { "traceId", "site", "waitedMs" },
            "gateway.concurrent" => new[] { "traceId", "user", "activeCalls" },
            "gateway.scope.end" => new[] { "traceId", "user", "depthAtDispose", "leaseStillHeld" },
            "pool.snapshot" => new[]
            {
                "poolKey", "idle", "leased", "alive", "pending", "created", "discarded",
                "totalAcquires", "totalReleases", "waiting", "acquireTimeouts", "faulted", "subPools"
            },
            "proc.snapshot" => new[]
            {
                "managedMb", "heapMb", "privateMb", "gen0", "gen1", "gen2",
                "handles", "threads", "poolThreads", "pendingWorkItems"
            },
            "crm.op" => new[] { "traceId", "op", "leaseId", "entity", "ms", "count", "ok" },
            "trace.dropped" => new[] { "count" },
            _ => throw new Xunit.Sdk.XunitException($"未知 trace event：{eventName}")
        };

        foreach (var field in fields)
            Assert.True(record.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null,
                $"{eventName} 缺少或空白欄位 {field}");
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataverse-trace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed record LeaseInterval(string ClientId, int AcquireIndex, int ReturnIndex);

    /// <summary>
    /// 提供真實 Gateway 使用的最小 Manager adapter；它只委派 pool 的 lease，
    /// 不自行建立或 Dispose client，維持測試與正式唯一資源所有權相同。
    /// </summary>
    private sealed class TestManager : IDataverseConnectionManager
    {
        private static readonly DataverseConnectionKey Key = new(
            "ChurchReport", "TraceTest", "https://trace.test/XRMServices/2011/Organization.svc", "service-account");
        private readonly IBoundedClientPool _pool;

        /// <summary>建立只轉接指定 pool 的測試 Manager，不持有 request 或使用者狀態。</summary>
        public TestManager(IBoundedClientPool pool) => _pool = pool;

        /// <summary>取得一條短命 lease，所有容量與故障淘汰行為仍由真實 pool 執行。</summary>
        public IClientLease Acquire(CancellationToken cancellationToken = default) => _pool.Acquire(Key, cancellationToken);

        /// <summary>回傳 pool metrics 快照，不建立或保存任何跨 request 資料。</summary>
        public DataversePoolMetrics GetMetrics() => _pool.GetMetrics();

        /// <summary>此 adapter 不擁有 pool，因此釋放本身不會釋放共享 client。</summary>
        public void Dispose() { }
    }

    /// <summary>
    /// 可並行使用的假 IOrganizationService。正常 Execute 短暫等待以放大 pool 排隊；
    /// 名稱為 throw 的 request 注入傳輸失敗等價例外，驗證 Gateway 會標記 lease Faulted。
    /// </summary>
    private sealed class TraceOrganizationService : IOrganizationService
    {
        /// <summary>關聯操作不保存資料，避免測試 double 保留任何使用者或 entity 狀態。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        /// <summary>建立操作回傳新的識別值，不影響 trace 的資源生命週期驗證。</summary>
        public Guid Create(Entity entity) => Guid.NewGuid();

        /// <summary>刪除操作不保存資料，讓每個測試操作保持獨立。</summary>
        public void Delete(string entityName, Guid id) { }

        /// <summary>解除關聯不保存資料，避免引入與 pool 租約無關的可變狀態。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        /// <summary>
        /// 執行測試 CRM 操作；throw 名稱會注入例外以驗證 faulted return，其他操作短暫等待
        /// 以製造並行排隊與同一 client 的重複借還情境。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (string.Equals(request.RequestName, "throw", StringComparison.Ordinal))
                // 必須是傳輸層例外：只有連線故障才會讓 lease 以 faulted 歸還並淘汰 client。
                throw new CommunicationException("Trace test fault.");
            Thread.Sleep(4);
            return new OrganizationResponse();
        }

        /// <summary>讀取操作回傳空實體，因本測試只驗證觀測與租約邊界。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => new();

        /// <summary>多筆讀取回傳空集合，避免假物件保存 CRM 資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => new();

        /// <summary>更新操作不保存資料，讓 test double 沒有跨 request 可變狀態。</summary>
        public void Update(Entity entity) { }
    }
}
