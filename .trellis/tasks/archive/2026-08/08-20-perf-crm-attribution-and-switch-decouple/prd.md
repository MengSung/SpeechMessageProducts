# PRD：修復 Perf CRM 歸因與 Session 診斷開關耦合

## 背景

`統一 Trace 兩層保護與三檔綜合分析` 任務的 B1 批次已提交（`de4c1710`），
但後續稽核發現兩個缺陷，使該批次的目標實際未達成。兩者都以既有 trace 實證，
不需要重新收集資料即可確認。

## 問題 1：`[Perf]` 的 CRM 歸因恆為零（核心缺陷）

### 觀測事實

`D:\除錯追蹤\Trace.log`（2026-08-20，12,115 行、6 次應用程式啟動）：

- `[Perf]` 行共 **84** 行
- 其中 `crm{n=0,ms=0}` 共 **84** 行
- `crm{n=` 後面接非零數字的行數：**0**

同期 `ChurchReport-Trace-Report.md`（11:42 產生，涵蓋 11:03:09–11:04:43）顯示，
同一時間窗的 `dataverse-trace.jsonl` 記錄了 **175 次 `crm.op`、累計 9,463 毫秒**。

亦即：JSONL 那條量測路徑正常運作，`[Perf]` 這條完全沒有。

### 影響

`RequestProfiler.Gap` 定義為 `_actionMs - CrmMs`。當 `CrmMs` 恆為 0，
`Gap` 恆等於 `_actionMs`，於是 `[Perf-Gap]` 對每個超過門檻的請求都會印出同一句：

```
(未歸因:可能 gateway 代理路徑或非 CRM 運算)
```

這不是診斷結果，而是一個常數。它已經實際造成誤判：
一次耗時 8,654ms 的登入（真因是 `pool.create` 冷啟動後拋 `WebException`）
被這行字導向「gateway 代理路徑」的錯誤方向。

### 根因

`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:94-98`：

```csharp
m_Crm2011OrganizationService = organizationService;
_facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
```

`TimedToolUtilityProvider.GetToolUtility()` 在事後把 `tu.m_Crm2011OrganizationService`
換成 `TimedOrganizationService` 裝飾器，但 `_facade` 在建構時已經抓走**未裝飾的原始參考**。

`ToolUtility/ToolUtilityPartials/` 內的使用次數統計：

| 路徑 | 次數 |
|---|---:|
| `_facade.` | 158 |
| `m_Crm2011OrganizationService.` | 1 |

裝飾器被安裝在 159 個呼叫點中只用到 1 次的欄位上。
`RequestProfiler.RecordCrmCall` 因此幾乎永不觸發。

`ToolUtilityFacade` 另持有可變的 `_organizationService` 欄位與多個
`Lazy<I...Service>` 子服務，子服務各自在首次求值時捕獲當時的 service 參考。
因此**事後改寫欄位無法回溯修正已捕獲的子服務**，修正必須發生在 DI 解析點。

## 問題 2：Session 診斷開關與主 Trace 旗標耦合

`SpeechMessageProducts.ChurchReport/Startup.cs:158`：

```csharp
ChurchReport.Diagnostics.SessionDiagnosticsSwitch.Enabled = _diagnosticTraceOptions.Enabled;
```

`_diagnosticTraceOptions.Enabled` 來自 `DiagnosticsTrace:Enabled`，
而 `appsettings.Development.json:3` 為 `true`——同一個旗標同時決定
「是否寫 Trace.log」與「是否輸出 51 行 Session 逐步診斷」。

結果是一個無解的耦合：**要有 Trace.log 就必然有噪音；要沒噪音就沒有 Trace.log。**
B1 宣稱的「預設完全停用」只在診斷整體關閉時成立，而那時沒有 log 需要降噪。

### 實證

`Debug.WriteLine` 在本專案（net10.0）確實流入 `Trace.Listeners` → Trace.log。
Trace.log 12,115 行中，Session 診斷佔：

| 標籤 | 行數 |
|---|---:|
| `[GetCurrentSessionId]` | 6,230 |
| `[GenerateCurrentRequestFingerprint]` | 4,311 |
| `[InMemoryDataContext]` | 100 |
| `[SetSessionDirtyFlag]` | 32 |
| **合計** | **10,673（88%）** |

13:08:15 重啟後的 147 行區段中仍有 14 / 9 / 4 筆，證實開關在實際環境是開著的。

## 目標

1. 讓 `[Perf]` 的 `crm{n,ms}` 真實反映該請求的 CRM 呼叫次數與耗時。
2. 讓 Session 逐步診斷能在 Trace.log 開啟的前提下獨立關閉。

## 非目標（明確排除，不得順手改）

- 不修改 `ensureMin` 在登入路徑同步等待的行為（另案處理）。
- 不處理 `CHURCH_REPORT_TRACE.TXT` 從未產生的問題（待釐清）。
- 不調整連線池淘汰、租約或健康檢查邏輯。
- 不修改 `Analyze-ChurchReportTraces.ps1`（B0 已定版，SHA-256 必須維持
  `C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B`，且必須保留 UTF-8 BOM——
  該檔含 185 行繁體中文，移除 BOM 會使 Windows PowerShell 5.1 以 cp950 解碼而全毀）。
- 不擴大修正既有的 `CS1572`（`Line.Messaging/LineMessagingClient.cs:840`）等範圍外警告。

## 驗收條件

### AC-1：CRM 歸因正確（必須以實跑 trace 證明）

重新收集 trace 後，於新的 Trace.log 中：

- 至少有一行 `[Perf]` 的 `crm{n=` 大於 0。
- 對任一有 CRM 活動的請求，`[Perf]` 的 `crm.n` 與同一 `traceId` 在
  `dataverse-trace.jsonl` `request.end` 的 `crmCount` **相等**；
  `crm.ms` 與 `crmMs` 差距在 ±10% 或 ±20ms 內（取較寬者）。
- `[Perf-Gap]` 不再對每個慢請求無條件出現。

### AC-2：Session 噪音可獨立關閉

- 在 `DiagnosticsTrace:Enabled = true`（Trace.log 正常產生）且新開關為預設值時，
  新 Trace.log 中 `[GetCurrentSessionId]`、`[GenerateCurrentRequestFingerprint]`、
  `[SetSessionDirtyFlag]`、`[InMemoryDataContext]` 四個標籤的行數合計為 **0**。
- 將新開關明確設為 `true` 後重跑，四個標籤重新出現（證明只是關閉、未刪除診斷能力）。

### AC-3：不回歸

- Debug build 與 Release build 皆 0 error。
- `ToolUtility.Dataverse.Tests` 與 `ToolUtility.Tests` 全數通過，且測試數不低於現有基準
  （58 / 63）。
- Release 組態不得包含任何新增診斷旗標（維持 `#if DEBUG` 編譯防線）。

### AC-4：量測方法正確

收集 trace 前必須將既有 `D:\除錯追蹤\Trace.log` 與 `dataverse-trace.jsonl`
**改名移走**（非清空——兩者皆為 Append 模式）。
所有行數比較必須以**單次應用程式啟動區段**為單位，不得使用整檔行數
（現有 Trace.log 混雜 6 次啟動，整檔統計無意義）。
