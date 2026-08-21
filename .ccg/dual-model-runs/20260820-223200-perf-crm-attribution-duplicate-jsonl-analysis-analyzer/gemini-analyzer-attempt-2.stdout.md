# 診斷與分析報告：CRM 歸因重複 JSONL 記錄問題

本報告針對 `ChurchReport` 系統中，Legacy Factory/Ambient 路徑下 `[Perf]` 效能計數器與 `dataverse-trace.jsonl` 的 `crmCount` 數據不一致（呈現約 1:2 偏差）的問題進行根本原因分析，並提出安全的最小化修正方案與回歸測試策略。

---

## 關鍵發現 (Findings)

### Critical
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **原因**：在 `AmbientGatewayOrganizationService` 的 8 個介面方法中，巢狀呼叫了 `CrmOperationTrace.Measure`。由於其委派的實際服務（在有 request 時為 `TimedOrganizationService` -> `GatewayOrganizationService`，無 request 時為 `GatewayOrganizationService`）內部已包含 `CrmOperationTrace.Measure`，這導致每次 CRM 操作都會觸發兩次 `crm.op` 事件，使 JSONL 中的 `crmCount` 翻倍，破壞了診斷數據的準確性。

### Warning
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **原因**：若為了消除重複計數而將 `AmbientGatewayOrganizationService` 改為直接解析 `IDataverseGateway`，將會繞過 `TimedOrganizationService` 裝飾器，導致 ChurchReport 的 `[Perf]` 效能計數器完全失效。必須維持解析 `IOrganizationService` 的設計以確保裝飾鏈完整。

### Info
* **檔案路徑**：`ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
* **原因**：需要在此測試檔案中新增回歸測試，驗證透過 `AmbientGatewayOrganizationService` 呼叫時，`DataverseTrace` 的 `crm.op` 寫入次數與 `TimedOrganizationService` 的記錄次數完全一致（皆為 1 次）。

---

## 1. UX Analysis (使用者影響評估)

* **使用者體驗影響**：此問題為 Debug-only 的診斷修正，不影響一般使用者的日常操作與功能。
* **維運與開發體驗**：對系統管理員與開發人員而言，此數據偏差會導致效能分析工具（如 `Analyze-ChurchReportTraces.ps1`）產生的報告失真，誤導對 CRM 呼叫次數與延遲的評估，增加排查效能瓶頸的難度。
* **行動端與桌面端體驗**：無直接影響。

---

## 2. Design Evaluation (設計系統評估)

* **一致性與模式**：`AmbientGatewayOrganizationService` 的定位是環境代理（Ambient Proxy），其職責僅在於安全地解析當前 Scope 的服務並進行委派，不應負責具體的 CRM 操作測量。具體的測量職責應統一由 `GatewayOrganizationService`（負責 JSONL 追蹤）與 `TimedOrganizationService`（負責 `[Perf]` 效能計數）承擔。
* **組件重用性**：修正後，`AmbientGatewayOrganizationService` 將變為純粹的委派代理，不再依賴 `CrmOperationTrace`，簡化了類別依賴關係。

---

## 3. Technical Considerations (技術考量)

* **組件結構影響**：移除 `AmbientGatewayOrganizationService` 中的 `CrmOperationTrace.Measure` 後，呼叫鏈將簡化為：
  ```text
  AmbientGatewayOrganizationService (純委派)
    -> TimedOrganizationService (記錄 [Perf] 1 次)
       -> GatewayOrganizationService (呼叫 Measure 記錄 JSONL 1 次)
  ```
* **狀態管理與隔離性**：此修正完全在方法執行期進行委派，不持有任何狀態，因此完美保留了 legacy Factory 單例不捕獲 request scope、HttpContext、raw client、lease 或使用者狀態的隔離性約束。
* **效能與 Bundle Size**：減少了一次巢狀的 `Stopwatch` 測量與 `AsyncLocal` 讀取，微幅提升了執行期效能。

---

## 4. Options (方案評估)

### 方案 A：移除 `AmbientGatewayOrganizationService` 中的 `CrmOperationTrace.Measure`（推薦）
* **作法**：將 `AmbientGatewayOrganizationService` 的 8 個介面方法改為直接委派給 `service`，不再呼叫 `CrmOperationTrace.Measure`。
* **優點**：最安全、改動最小，且完全保留了 `TimedOrganizationService` 裝飾器與背景 Fallback Scope 的追蹤能力。
* **缺點**：無。

### 方案 B：在 `CrmOperationTrace.Measure` 中防止重入
* **作法**：在 `CrmOperationTrace` 中使用 `AsyncLocal<bool>` 標記是否已在測量中，若已在測量中則跳過計數。
* **優點**：不需要修改 `AmbientGatewayOrganizationService` 的程式碼。
* **缺點**：增加了額外的執行期開銷與複雜度，且治標不治本。

---

## 5. Recommendation (建議方案)

**採用方案 A**。此方案符合職責分離原則，能以最優雅、最安全的方式修正算術偏差，且完全符合所有既有的架構約束。

---

## 6. 核心問題解答

### 6.1 巢狀 `CrmOperationTrace.Measure` 呼叫是否為根本原因？
**確認是根本原因**。
當透過 `AmbientGatewayOrganizationService` 呼叫 CRM 時，其呼叫鏈如下：
1. `AmbientGatewayOrganizationService.Retrieve` 呼叫 `CrmOperationTrace.Measure`（**第一次計數，crmCount + 1**）。
2. 透過 `Run` 解析出當前 scope 的 `IOrganizationService`（即 `TimedOrganizationService`）。
3. `TimedOrganizationService` 執行 `Time` 記錄 `[Perf]`（**[Perf] 計數 + 1**）。
4. `TimedOrganizationService` 呼叫其 inner 服務，即 `GatewayOrganizationService`。
5. `GatewayOrganizationService.Retrieve` 再次呼叫 `CrmOperationTrace.Measure`（**第二次計數，crmCount + 1**）。
6. 最終呼叫 `IDataverseGateway` 執行操作。

這導致 `crmCount` 增加了 2，而 `[Perf]` 僅增加了 1，產生了 1:2 的算術偏差。

### 6.2 安全的最小修正及其為何能保留 trace 覆蓋範圍？
**修正方式**：
修改 `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`，將 8 個介面方法中的 `CrmOperationTrace.Measure` 移除，改為直接呼叫 `service` 的對應方法。例如：

```csharp
public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    => Run(service => service.Retrieve(entityName, id, columnSet));
```

**保留 trace 覆蓋範圍的理由**：
1. **有 HTTP 請求時**：解析出的 `service` 為 `TimedOrganizationService`，它會記錄 `[Perf]`，然後呼叫 `GatewayOrganizationService`，後者會呼叫 `CrmOperationTrace.Measure` 記錄 JSONL。兩者皆只記錄 1 次，比例為 1:1。
2. **無 HTTP 請求時（背景執行緒）**：`Run` 方法會建立短壽命的 fallback scope，解析出的 `service` 為 `GatewayOrganizationService`。它會直接呼叫 `GatewayOrganizationService`，後者內部依然會呼叫 `CrmOperationTrace.Measure` 記錄 JSONL。因此背景呼叫的 trace 記錄依然完整，不會遺漏。

### 6.3 精確的回歸測試策略（含 Fallback Scope 生命週期）
在 `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs` 中新增或擴充測試：

1. **裝飾鏈與計數一致性測試**：
   模擬有 HTTP 請求的環境，透過 `ToolUtilityFactory.GetInstance().m_Crm2011OrganizationService` 執行一次呼叫，驗證：
   * `RequestProfiler` 記錄的 `[Perf]` 呼叫次數為 1。
   * `DataverseTrace` 寫入的 `crm.op` 事件數量為 1。
   * 兩者數值完全一致。

2. **Fallback Scope 生命週期與計數測試**：
   模擬無 HTTP 請求的環境，呼叫 `m_Crm2011OrganizationService`，驗證：
   * `TrackingScopeFactory` 建立的 scope 數量為 1，且已被正確 `Dispose`（`DisposedCount` 為 1）。
   * `DataverseTrace` 寫入的 `crm.op` 事件數量為 1（由 `GatewayOrganizationService` 觸發）。

3. **連線池無洩漏測試**：
   執行 100 次跨 scope 呼叫，驗證 `IDataverseConnectionManager` 的連線池指標（Leased/Created）沒有異常增長，確保 fallback scope 釋放時連線已安全歸還。

### 6.4 觀察到的算術偏差是否有其他可能原因？
**沒有其他原因**。
在較小的直接路徑上（例如直接注入 `IOrganizationService`，繞過 legacy Factory/Ambient 代理），`[Perf]` 與 JSONL 的計數是完全吻合的（1/1, 2/2）。這證明了 `TimedOrganizationService` 與 `GatewayOrganizationService` 自身的計數邏輯是正確的。只有經過 `AmbientGatewayOrganizationService` 代理的路徑才會出現翻倍現象，這與程式碼中兩層 `Measure` 的巢狀結構完全吻合。
