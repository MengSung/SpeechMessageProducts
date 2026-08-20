# UI 與系統架構審查報告：Perf CRM 歸因與 SessionVerbose 解耦 (Step 1 & Step 2)

本報告針對 `perf-crm-attribution-and-switch-decouple` 任務的 **Step 1** 與 **Step 2** 進行程式碼品質、系統架構、資源隔離性、安全性防線以及測試覆蓋率的深度審查。

---

## 1. 總體評估 (Summary)

本次重構成功解決了兩個核心系統診斷問題：
1. **CRM 歸因失效**：原本透過裝飾 `IToolUtilityProvider` 來包裝 `IOrganizationService` 的做法，因 `ToolUtilityClass` 在建構時已直接捕獲原始服務，導致後續透過 `_facade` 呼叫的 CRM 操作繞過了計時裝飾器。本次重構改為在 DI 容器註冊點直接裝飾 `IOrganizationService`，徹底解決了此問題。
2. **Session 診斷日誌污染**：原本 Session 診斷開關與一般 Trace 開關強耦合，導致開啟 Trace 時產生高達 88% 的 Session 雜訊日誌。本次重構引入了獨立的 `SessionVerbose` 開關，並在 Release 模式下建立了嚴格的 `fail-closed` 安全防線。

目前 Step 1 與 Step 2 的程式碼實作、DI 生命週期管理、Release 防線以及單元/整合測試均已完成，且品質優良。然而，**Step 4 的實測數據目前為缺失狀態（Missing Evidence）**，此部分必須在後續步驟中補齊，本審查絕不對未經實測的數據進行估算。

---

## 2. 審查發現分類 (Findings)

### 🔴 Critical (關鍵缺失)

#### 1. Step 4 實測證據缺失 (Missing Evidence)
* **檔案路徑**：`.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/implement.md` (第 50-75 行)
* **理由**：
  * 任務規範要求驗證 AC-1 與 AC-2 的實測數據（包括 `Trace.log` 總行數、`[Perf]` 行數、`crm{n=0,ms=0}` 行數、JSONL 中的 `crmCount` 與 `crmMs` 等）。
  * 目前 `implement.md` 中的 Step 4.9 與 Step 4.10 數據表格皆為空白，且未生成 `ChurchReport-Trace-Report.md`。
  * **決策**：此部分列為**缺失證據 (Missing Evidence)**，絕不進行估算。必須在執行 Step 4 實測後，將真實數據填入表格並生成報告。

---

### ⚠️ Warning (警告事項)

#### 1. Release 模式下的編譯期防線驗證
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
* **理由**：
  * `SessionDiagnosticsSwitch` 類別整個被 `#if DEBUG` 包裹，這確保了 Release 模式下該類別不會被編譯。
  * **建議**：請確保在 Release 建置時，所有調用 `SessionDiagnosticsSwitch` 的程式碼（例如 `WriteSessionDiagnostic` 等輔助方法）皆有使用 `[Conditional("DEBUG")]` 或 `#if DEBUG` 進行保護，避免 Release 模式下因找不到該類別而導致編譯失敗。

---

### ℹ️ Info (參考資訊)

#### 1. DI 生命週期與資源隔離性 (DI Lifetime & Isolation)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Startup.cs` (第 422-477 行)
* **理由**：
  * 重構後的 `IOrganizationService` 裝飾器註冊保留了原始的生命週期（`organizationServiceDescriptor.Lifetime`，通常為 `Scoped`）。
  * 裝飾器 `TimedOrganizationService` 與其內部的 `inner` 服務、`IHttpContextAccessor` 皆在同一個 Scope 內解析，避免了生命週期提升（Lifetime Elevation）至 `Singleton` 的風險。
  * 這確保了跨 Request 的連線租約（lease）與使用者/Session 狀態不會發生洩漏，資源隔離性設計非常嚴密。

#### 2. SessionVerbose 安全防線設計
* **檔案路徑**：`ToolUtility/Diagnostics/DiagnosticTraceOptions.cs` (第 140-180 行)
* **理由**：
  * 在 `FromConfiguration` 中，`SessionVerbose` 的最終值為 `allowEnabled && configuredSessionVerbose`。
  * 由於 Release 模式下組合根傳入的 `allowEnabled` 必定為 `false`，這確保了即使部署環境的 `appsettings.json` 誤設 `SessionVerbose: true`，系統在執行期也絕對不會啟用 Session 詳細診斷，達到了 `fail-closed` 的安全要求。

#### 3. 測試覆蓋率完整性
* **檔案路徑**：
  * `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs`
  * `ToolUtility.Dataverse.Tests/StartupOrganizationServiceProfilingTests.cs`
* **理由**：
  * `DiagnosticTraceOptionsTests.cs` 完整測試了 `SessionVerbose` 在各種組態配置下的行為（預設值、明確啟用、Release 邊界強制關閉等）。
  * 新增的 `StartupOrganizationServiceProfilingTests.cs` 是一個高質量的整合測試，模擬了 `Startup.ConfigureServices` 的 DI 容器建立過程，並驗證了在同一個 Scope 內解析的 `IOrganizationService` 確實為同一個 `TimedOrganizationService` 實例，且 `ToolUtilityClass` 捕獲的也是該裝飾實例。這證明了重構方案的正確性。

#### 4. 文件與編碼規範
* **檔案路徑**：所有修改的 `.cs` 檔案
* **理由**：
  * 所有修改的 `.cs` 檔案均在檔案頭部聲明了 `UTF-8 without BOM` 與 `CRLF` 的編碼要求，符合專案規範。
  * 提醒：後續執行 Step 3 時，請確保 `Analyze-ChurchReportTraces.ps1` 的 SHA-256 雜湊值與任務要求的 `C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B` 完全一致，且不包含 UTF-8 BOM。

---

## 3. 建議事項 (Suggestions)

1. **補齊實測數據**：請儘速執行 Step 4 的實測步驟，並將實際的日誌行數與 CRM 呼叫數據填入 `implement.md`，以解除 `Critical` 級別的證據缺失。
2. **編譯驗證**：請執行 `dotnet build -c Release`，確保 Release 模式下的編譯期防線（`#if DEBUG`）沒有引起任何編譯錯誤。

---

## 4. 評分表 (Scoring for Bugfix/Refactor Validation)

由於 Step 4 實測數據目前缺失，本評分僅針對 Step 1 & Step 2 的設計與程式碼品質進行評估。

```
VALIDATION REPORT
=================
User Experience: 18/20 - 成功解耦 Session 診斷，避免了日誌污染，提升了系統維護體驗。
Visual Consistency: N/A - 本次重構不涉及 UI 視覺呈現。
Accessibility: N/A - 本次重構不涉及 UI 可存取性。
Performance: 19/20 - 解決了 CRM 歸因失效問題，且避免了高頻 Session 日誌帶來的 I/O 效能損耗。
Browser Compatibility: N/A - 本次重構不涉及瀏覽器相容性。

TOTAL SCORE: 37/40 (僅評估後端與系統架構部分，滿分為 40 分)

ISSUES FOUND:
- [Critical] Step 4 實測證據目前為缺失狀態 (Missing Evidence)，待後續步驟補齊。

RECOMMENDATION: PASS (Step 1 & Step 2 設計與實作通過，待 Step 4 數據補齊後即可完成完整驗證)
```
