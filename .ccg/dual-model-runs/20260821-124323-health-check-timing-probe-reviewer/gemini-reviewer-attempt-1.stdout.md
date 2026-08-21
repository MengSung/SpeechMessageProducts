# 健康檢查耗時觀測點審查報告

本報告針對以下三個 C# 檔案的變更進行審查：
- `ToolUtility/Dataverse/DataverseTrace.cs`
- `ToolUtility/Dataverse/BoundedClientPool.cs`
- `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`

---

## 1. 任務契約逐項核對

### 1.1 成功、false 回傳與委派拋例外三條路徑的 elapsed 是否正確
* **核對結果**：**正確**。
* **分析**：
  * 在 `BoundedClientPool.cs` 中，健康檢查委派的呼叫被包在 `try-catch` 區塊中：
    ```csharp
    var healthStartedTimestamp = traceEnabled ? Stopwatch.GetTimestamp() : 0;
    var healthy = false;
    try { healthy = _healthCheck(candidate.Service); } catch { healthy = false; }
    var healthElapsedMs = traceEnabled ? GetElapsedMilliseconds(healthStartedTimestamp) : 0;
    ```
  * **成功路徑**：`_healthCheck` 回傳 `true`，`healthy` 為 `true`，`healthElapsedMs` 正確計算，並記錄 `result: true`。
  * **false 回傳路徑**：`_healthCheck` 回傳 `false`，`healthy` 為 `false`，`healthElapsedMs` 正確計算，並記錄 `result: false`。
  * **拋出例外路徑**：`_healthCheck` 拋出例外被 `catch` 捕獲，`healthy` 設為 `false`，`healthElapsedMs` 依然會正確計算從開始到拋出例外之間的時間，並記錄 `result: false`。

### 1.2 trace disabled 是否零新增量測成本，且 trace schema 是否只加欄位
* **核對結果**：**正確**。
* **分析**：
  * 當 `traceEnabled` 為 `false` 時，`healthStartedTimestamp` 直接設為 `0`，且 `healthElapsedMs` 直接設為 `0`。完全沒有呼叫 `Stopwatch.GetTimestamp()` 或 `Stopwatch.GetElapsedTime()`，也沒有配置任何 TraceEntry 佇列項目，達到了零新增量測成本。
  * 在 `DataverseTrace.cs` 的 `WriteEventFields` 中，`pool.health` 事件僅新增了 `ms` 欄位：
    ```csharp
    case EventKind.PoolHealth:
        json.WriteString("clientId", entry.ClientId);
        json.WriteBoolean("result", entry.Result);
        json.WriteNumber("ms", entry.First);
        break;
    ```
    既有的 `clientId` 與 `result` 欄位完全沒有改變，符合 schema 僅新增欄位的契約。

### 1.3 是否引入 session、tenant、credential、CRM response 或資源生命週期風險
* **核對結果**：**無風險**。
* **分析**：
  * `PoolHealth` 事件僅記錄 `clientId`、`result` 與 `ms`，完全沒有記錄或傳遞任何 CRM 回應內容、使用者、身分、tenant 或認證資料。
  * 變更僅以 `Stopwatch` 包住既有的 `_healthCheck` 委派，沒有改變 pool 的行為、健康檢查時機、ensureMin、建線、資源生命週期或現有欄位語意。

### 1.4 測試是否真的驗證成功和失敗健康檢查均有正 `ms`，且避免測量精度造成非決定性
* **核對結果**：**正確**。
* **分析**：
  * 新增的測試 `Health_check_elapsed_is_recorded_for_success_and_failure` 中，健康檢查委派內刻意加入了 `Thread.Sleep(5);`，確保執行時間一定大於 0 毫秒，避免了因為系統時鐘精度問題導致 `ms` 為 0 的非決定性測試失敗。
  * 測試最後使用 `Assert.All(healthEvents, record => Assert.True(record.GetProperty("ms").GetInt64() > 0));` 斷言所有健康檢查事件的 `ms` 均為正數，且結果布林值仍分別保留成功與失敗語意。

### 1.5 註解是否準確且不過度宣稱
* **核對結果**：**正確**。
* **分析**：
  * 程式碼中的註解詳盡且準確地說明了設計意圖（如單調時鐘的使用、零量測成本、不保留敏感資料等），沒有任何過度宣稱。

---

## 2. 審查發現分類 (Critical / Warning / Info)

### Critical
* **無**。程式碼完全符合任務契約，沒有引入任何安全性、效能或資源生命週期風險。

### Warning
* **無**。

### Info
* **單調時鐘 API 選擇**：程式碼中使用了 .NET 7+ 引入的 `Stopwatch.GetElapsedTime` API，這在 `net10.0` 的專案中是完全安全且高效的，能有效避免時鐘回撥問題。
* **防禦性設計**：在 `DataverseTrace.PoolHealth` 中使用了 `Math.Max(0, elapsedMs)`，以及在 `BoundedClientPool.GetElapsedMilliseconds` 中對 `startedTimestamp == 0` 進行了防禦性檢查，這些設計都非常嚴謹。

---

## 3. 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 診斷日誌新增了精確的耗時欄位，有助於維運人員分析健康檢查效能。
Visual Consistency: 20/20 - JSONL schema 僅新增 ms 欄位，完全相容於既有的日誌分析器。
Accessibility: 20/20 - 無 UI 變更，後端診斷日誌語意清晰。
Performance: 20/20 - trace disabled 時達到零新增量測成本，且使用了高效的單調時鐘 API。
Browser Compatibility: 20/20 - 不適用於瀏覽器相容性，後端 .NET 10 環境完全相容。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無

RECOMMENDATION: PASS
```
