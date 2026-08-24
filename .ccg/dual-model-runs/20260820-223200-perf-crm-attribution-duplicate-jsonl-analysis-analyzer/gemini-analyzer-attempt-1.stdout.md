# CCG 診斷與設計分析報告：CRM 歸因重複 JSONL 分析

本報告針對 ChurchReport 偵錯診斷中，`[Perf]` 效能計數器與 `dataverse-trace.jsonl` 的 `request.end.crmCount` / `crmMs` 數據不一致（呈現確定性的 2 倍偏差）進行根源分析，並提出安全的極簡修正方案與迴歸測試策略。

---

## 發現與分類 (Findings)

### 🔴 Critical
* **雙重測量根源 (Double Measurement Root Cause)**：`AmbientGatewayOrganizationService` 與 `GatewayOrganizationService` 兩者皆在其 8 個 `IOrganizationService` 介面方法中包裝了 `CrmOperationTrace.Measure`。當透過 Ambient 代理呼叫 CRM 時，會觸發巢狀的 `Measure` 呼叫，導致 `DataverseTrace.CrmOperation` 被執行兩次，使 `crmCount` 增加 2 並寫入兩筆 `crm.op` 事件；然而 `TimedOrganizationService` 裝飾器僅被呼叫一次，導致 `[Perf]` 僅記錄 1 次，產生 1:2 的數據偏差。
* **檔案路徑**：
  * `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
  * `ToolUtility/Dataverse/GatewayOrganizationService.cs`

### ⚠️ Warning
* **裝飾器繞過風險 (Decorator Bypass Risk)**：若為了修正重複測量而將 `AmbientGatewayOrganizationService` 改為直接解析 `IDataverseGateway`，將會繞過 `TimedOrganizationService` 裝飾器，導致 ChurchReport 的 `[Perf]` 效能計數器完全失效。必須維持解析 `IOrganizationService` 的設計以確保裝飾鏈完整。

### ℹ️ Info
* **背景 Fallback Scope 完整性**：在無 HTTP 請求的背景執行緒中，`AmbientGatewayOrganizationService` 會建立短壽命的 fallback scope。在此 scope 中解析出的 `IOrganizationService` 為 `GatewayOrganizationService`，其內部已包含 `CrmOperationTrace.Measure`。因此，移除 Ambient 代理的 Measure 呼叫後，背景呼叫的 trace 記錄依然完整且不會遺漏。

---

## 1. UX Analysis (使用者影響評估)

* **使用者體驗影響**：此問題為偵錯與診斷層面的數據不一致，不影響一般使用者的日常操作與功能正確性。
* **維運與診斷影響**：對於系統管理員與維運人員而言，此不一致會導致效能分析工具（如 `Analyze-ChurchReportTraces.ps1`）產生的報告出現偏差，誤導 CRM 呼叫次數的統計，增加效能瓶頸診斷的難度。
* **無障礙與行動端**：無直接影響。

---

## 2. Design Evaluation (設計系統評估)

* **一致性與模式**：設計系統要求診斷與效能監控數據必須具備確定性與一致性。`[Perf]` 效能計數器與 JSONL 追蹤日誌應描述相同的 CRM 呼叫次數。
* **職責分離 (Separation of Concerns)**：
  * `AmbientGatewayOrganizationService` 的職責是作為環境代理，解析當前 scope 的 `IOrganizationService` 並委派執行，不應負責具體的 CRM 操作測量。
  * `GatewayOrganizationService` 作為 Scoped `IOrganizationService` 的具體實作，負責與 `IDataverseGateway` 互動，是執行 CRM 操作的唯一終端，因此最適合擔任 `CrmOperationTrace.Measure` 的唯一觸發點。

---

## 3. Technical Considerations (技術考量與架構影響)

* **組件架構影響**：修正後，`AmbientGatewayOrganizationService` 將變為純粹的委派代理，不再依賴 `CrmOperationTrace`，簡化了類別依賴關係。
* **效能與記憶體**：減少了一層 `CrmOperationTrace.Measure` 的委派與 `Stopwatch` 建立，微幅提升了 legacy Factory 路徑的執行效率，並減少了垃圾回收（GC）的分配壓力。
* **測試考量**：需要確保現有的 DI 容器註冊測試（如 `StartupOrganizationServiceProfilingTests`）與生命週期測試依然通過，並新增專門的迴歸測試來驗證計數的一致性。

---

## 4. Options (替代方案評估)

### 方案 A：移除 `AmbientGatewayOrganizationService` 中的 `CrmOperationTrace.Measure`（推薦）
* **作法**：將 `AmbientGatewayOrganizationService` 的 8 個介面方法改為直接委派給 `service`，不再呼叫 `CrmOperationTrace.Measure`。
* **優點**：極簡修正，完全保留裝飾鏈，無 captive dependency 風險，且 `GatewayOrganizationService` 已有 Measure，追蹤覆蓋率 100% 不遺漏。
* **缺點**：無。

### 方案 B：在 `CrmOperationTrace` 中加入重入鎖定（Reentrancy Guard）
* **作法**：在 `CrmOperationTrace` 中使用 `AsyncLocal<bool>` 標記是否已在測量中，若已在測量中則不再重複記錄。
* **優點**：不需要修改 `AmbientGatewayOrganizationService` 的程式碼。
* **缺點**：增加了診斷元件的複雜度與執行開銷，且未從架構上解決職責重疊的問題。

---

## 5. Recommendation (建議方案與決策)

**首選方案 A**。此方案最為安全且符合極簡修正原則，能徹底解決巢狀呼叫的根源問題，同時維持了 trace 覆蓋率與裝飾鏈的有效性。

---

## 核心問題解答 (Requested Output)

### 1. 巢狀 `CrmOperationTrace.Measure` 呼叫是否確認為根源？
**是的，已確認。**
當透過 `AmbientGatewayOrganizationService` 呼叫 CRM 時，其呼叫鏈如下：
1. `AmbientGatewayOrganizationService.Retrieve` 呼叫 `CrmOperationTrace.Measure`（**第一次計數，crmCount + 1**）。
2. 在其 Action 內，解析並呼叫 `TimedOrganizationService.Retrieve`。
3. `TimedOrganizationService` 記錄 `[Perf]`（**Perf 計數 + 1**），並呼叫 `GatewayOrganizationService.Retrieve`。
4. `GatewayOrganizationService.Retrieve` 呼叫 `CrmOperationTrace.Measure`（**第二次計數，crmCount + 1**）。
5. 最終導致 `crmCount` 增加 2，但 `[Perf]` 僅增加 1。

### 2. 安全的極簡修正方案與追蹤覆蓋率保留原因
**修正方案**：
修改 `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`，將 8 個介面方法中的 `CrmOperationTrace.Measure` 移除，改為直接呼叫 `service` 的對應方法。

以 `Retrieve` 為例：
```csharp
// 修改前
public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    => Run(service => CrmOperationTrace.Measure(
        "Retrieve", entityName, () => service.Retrieve(entityName, id, columnSet)));

// 修改後
public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    => Run(service => service.Retrieve(entityName, id, columnSet));
```

**保留追蹤覆蓋率的原因**：
* **有 Request 時**：解析出 `TimedOrganizationService` -> `GatewayOrganizationService`。最終呼叫會到達 `GatewayOrganizationService`，其內部仍保有 `CrmOperationTrace.Measure`，因此會被正確記錄 1 次。
* **無 Request 時 (Fallback Scope)**：解析出 `GatewayOrganizationService`。呼叫直接到達 `GatewayOrganizationService`，其內部仍保有 `CrmOperationTrace.Measure`，因此也會被正確記錄 1 次。
* 追蹤覆蓋率達到 100%，且完全消除了重複記錄。

### 3. 確切的迴歸測試策略 (Regression Test Strategy)

在 `ToolUtility.Dataverse.Tests\ToolUtilityFactoryAmbientGatewayTests.cs` 中新增以下測試：

```csharp
#if DEBUG
[Fact]
public void Ambient_service_produces_exactly_one_crm_op_and_matches_perf_count()
{
    ResetFactory();
    var directory = Path.Combine(Environment.CurrentDirectory, "TestLogs_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
            ["CrmConnection:Username"] = "service-user",
            ["CrmConnection:Password"] = "test-secret",
            ["Dataverse:Pool:MinSize"] = "1",
            ["Dataverse:Pool:MaxN"] = "1",
            ["Dataverse:Pool:AcquireTimeout"] = "00:00:02",
            ["Dataverse:Pool:IdleTimeout"] = "00:05:00",
            ["Dataverse:Pool:HealthInterval"] = "00:05:00"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton(CreateConnectionService(new List<IOrganizationService>()));

    var diagnosticOptions = DiagnosticTraceOptions.Create(directory, enabled: true);
    var startup = new ChurchReport.Startup(configuration, diagnosticOptions);
    startup.ConfigureServices(services);

    using var provider = services.BuildServiceProvider();
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var trace = provider.GetRequiredService<IToolUtilityTracer>() as DataverseTrace;

    Assert.NotNull(trace);

    try
    {
        using var requestScope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        var profiler = new ChurchReport.Diagnostics.Profiling.RequestProfiler();
        context.Items[ChurchReport.Diagnostics.Profiling.RequestProfiler.ItemsKey] = profiler;
        httpContextAccessor.HttpContext = context;

        ToolUtilityFactory.SetConfiguration(configuration);
        ToolUtilityFactory.SetTracer(trace);
        
        var ambient = new AmbientGatewayOrganizationService(
            () => httpContextAccessor.HttpContext?.RequestServices,
            provider.GetRequiredService<IServiceScopeFactory>());
        ToolUtilityFactory.SetAmbientService(ambient);

        // 執行一次 Ambient 呼叫
        using (trace.BeginRequest("test-trace-id", "test-user", sessionId: null))
        {
            ToolUtilityFactory.GetInstance()
                .m_Crm2011OrganizationService
                .Retrieve("account", Guid.NewGuid(), new ColumnSet("name"));
        }

        // 驗證 1：[Perf] 效能計數器記錄次數為 1
        var summary = profiler.BuildSummaryLine("test-op", totalMs: 1);
        Assert.Contains("crm{n=1,", summary, StringComparison.Ordinal);

        // 強制寫入日誌
        trace.Dispose();

        // 驗證 2：JSONL 檔案中的 crm.op 事件數量剛好為 1，且 request.end 的 crmCount 為 1
        var logFile = Directory.EnumerateFiles(directory, "*.jsonl").FirstOrDefault();
        Assert.NotNull(logFile);
        
        var lines = File.ReadAllLines(logFile);
        var crmOpCount = lines.Count(line => line.Contains("\"ev\":\"crm.op\""));
        Assert.Equal(1, crmOpCount);

        var endLine = lines.FirstOrDefault(line => line.Contains("\"ev\":\"request.end\""));
        Assert.NotNull(endLine);
        Assert.Contains("\"crmCount\":1", endLine);
    }
    finally
    {
        httpContextAccessor.HttpContext = null;
        ResetFactory();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
#endif
```

**Fallback Scope 生命週期覆蓋**：
* 既有的 `Factory_singleton_resolves_current_gateway_and_releases_ambient_scopes_without_pool_growth` 測試已完整覆蓋了無 HTTP 請求時，建立短壽命 fallback scope 並在操作結束後立即釋放（`DisposedCount` 遞增）的生命週期驗證。

### 4. 觀察到的算術偏差是否有其他可能原因？
**沒有其他原因。**
* 經審查，`TimedOrganizationService` 僅在 ChurchReport 的 DI 容器中被註冊為 `IOrganizationService` 的唯一裝飾器，並無重複包裝。
* `DataverseGateway` 與 `BoundedClientPool` 僅負責連線管理與租約控制，其內部並無呼叫 `CrmOperationTrace.Measure`。
* 因此，`AmbientGatewayOrganizationService` 與 `GatewayOrganizationService` 的巢狀 `Measure` 呼叫是導致此 2 倍算術偏差的唯一且確切的原因。
