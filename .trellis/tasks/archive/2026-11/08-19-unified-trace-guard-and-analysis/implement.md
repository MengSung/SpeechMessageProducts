# 統一 Trace 兩層保護與三檔綜合分析 Implementation Plan

> **For agentic workers:** This plan is executed inline in the current Codex session. Steps use checkbox syntax for tracking.

**Goal:** 讓三種 ChurchReport Trace 共用一個設定入口、由 Release 編譯防線強制停用，並提供可分析三檔且產生完整報告的 PowerShell 工具。

**Architecture:** `DiagnosticTraceOptions` 是程序級統一設定模型；Debug 組態可由設定啟用，Release 組態建立 disabled instance。Program 保留唯一 `Trace.log` listener；ToolUtility legacy tracer 改為私有 writer，避免全域 listener 互相污染。PowerShell 以串流方式掃描三檔，使用 bounded aggregation 產出 Markdown。

**Tech Stack:** .NET 10、ASP.NET Core、Microsoft.Extensions.Configuration/DI、System.Diagnostics、PowerShell 7/Windows PowerShell 5.1。

---

### Task 1: 建立設定模型與失敗測試

**Files:**
- Create: `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`
- Create: `ToolUtility/Diagnostics/NullToolUtilityTracer.cs`
- Modify: `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`
- Modify: `ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs`

- [ ] **Step 1:** 加入測試：停用 options 具備固定三檔路徑；Null tracer 不建立檔案；停用 File tracer 不改變 `Trace.Listeners`。
- [ ] **Step 2:** 執行 `dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore --filter "FullyQualifiedName~DiagnosticTrace|FullyQualifiedName~FileToolUtilityTracer"`，確認因型別/建構式尚未存在而 RED。
- [ ] **Step 3:** 實作設定解析/驗證/固定檔名與 no-op tracer；不在建構式建立目錄或檔案。
- [ ] **Step 4:** 讓既有 File tracer 接受統一 options，同時保留測試用 path 建構式；重跑同一測試確認 GREEN。

### Task 2: 接上三種 Trace 的集中設定與 Release 防線

**Files:**
- Modify: `ToolUtility/Dataverse/DataverseTrace.cs`
- Modify: `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `ToolUtility/Diagnostics/FileToolUtilityTracer.cs`
- Modify: `ToolUtility/Diagnostics/TraceLogger.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Program.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Startup.cs`
- Modify: `SpeechMessageProducts.ChurchReport/appsettings.json`
- Modify: `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- Modify: `SpeechMessageProducts.ChurchReport/appsettings.Production.json`

- [ ] **Step 1:** 讓 `DataverseTraceOptions` 從 `DiagnosticTraceOptions` 取得 Enabled/JSONL path。
- [ ] **Step 2:** `AddToolUtility()` 預設使用 Null tracer；Debug enabled 才建立 File tracer，Release 或 disabled 永不建立檔案 tracer。
- [ ] **Step 3:** legacy tracer 改成私有 writer，禁止加入 `Trace.Listeners`；寫入與 Dispose 冪等、確定 flush/釋放；Release direct construction no-op。
- [ ] **Step 4:** Program/Startup 刪除重複初始化；只在 Debug + options.Enabled 初始化 `Trace.log`；provider 與 profiling 同一 options；Release 強制 disabled。
- [ ] **Step 5:** Development 只保留 `DiagnosticsTrace.Enabled/Directory`；移除 `Dataverse:Trace`、`EnableTrace`、`Profiling:Enabled` 的啟停責任。
- [ ] **Step 6:** 執行既有 ToolUtility/Dataverse 測試與 Debug build。

### Task 3: 建立三檔綜合 PowerShell 分析器

**Files:**
- Create: `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/診斷程式執行說明.txt`

- [ ] **Step 1:** 建立三檔 fixture，含 request/lease、Perf/N+1/gap、legacy error；先確認功能未完成時執行失敗。
- [ ] **Step 2:** 實作 `-TraceDirectory` 與三個 path override；FileShare.ReadWrite/Delete；明確 UTF-8/Big5 decoder；逐行讀取。
- [ ] **Step 3:** 實作 Dataverse JSONL 聚合、配對、pool/timeout/fault/dispose/cleanup、假名與敏感掃描；配對集合超限計數並 WARN。
- [ ] **Step 4:** 實作 Trace.log 效能聚合：`[Perf]`、`[Perf-N+1]`、`[Perf-Gap]`、`[Perf-Startup]`、error/warning、endpoint Top N。
- [ ] **Step 5:** 實作 legacy 聚合：時間範圍、行數、錯誤/例外、常見前綴與敏感模式；不把原文敏感值寫入報告。
- [ ] **Step 6:** 產生完整 Markdown、跨檔關聯、限制與 exit code；以 fixture 與真實 Trace 執行，確認唯讀。

### Task 4: Release/Debug 驗證與資源稽核

- [ ] **Step 1:** Debug disabled 確認三檔不建立；Debug enabled 確認三檔集中到同一目錄且停止後 handle 釋放。
- [ ] **Step 2:** Release publish + 外部 `DiagnosticsTrace__Enabled=true` smoke，確認三檔沒有新增；掃描 Release 產物。
- [ ] **Step 3:** 執行 ToolUtility、ToolUtility.Dataverse、ChurchReport 受影響測試與 Debug/Release build。
- [ ] **Step 4:** 對所有修改檔案做 UTF-8 no BOM、CRLF、最終 CRLF byte-level 檢查。

### Task 5: 審查、規格沉澱與交付

- [ ] **Step 1:** 讀取完整 diff，確認未覆寫既有未提交工作。
- [ ] **Step 2:** 透過 `Start-CcgDualModelRun.ps1 -Role reviewer` 執行 Gemini/Claude；份額不足時記錄單模型參考結果，不誤報雙模型完成。
- [ ] **Step 3:** 驗證每個 Critical/Warning against actual code；Critical 修正後再審查。
- [ ] **Step 4:** 更新 backend code-spec，記錄 unified options、Release fail-closed、唯一 global listener 與分析器契約。
- [ ] **Step 5:** 提交前提供 commit plan，不自行提交未經使用者確認的 commit。

## Rollback points

- 啟動失敗時先將 Debug `DiagnosticsTrace:Enabled=false`；不刪除既有 trace。
- legacy 格式回歸時回退格式改動，但保留集中 options、Release no-op 與不加入 global listener。
- 分析器遇到非預期編碼時分析封存副本，不修改原始 Trace。
