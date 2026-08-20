# 分析任務：Perf CRM 歸因與 Session 診斷開關解耦

請以架構與 ASP.NET Core DI／資源生命週期審查者身分，分析下列已核定任務在實作前的完整性。不要修改任何檔案。

## 必讀契約

- `.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/prd.md`
- `.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/design.md`
- `.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/implement.md`
- `AGENTS.md`

## 目前問題與既定設計

1. `TimedToolUtilityProvider` 在 `ToolUtilityClass` 與 `ToolUtilityFacade` 已捕獲原始 `IOrganizationService` 後才替換欄位，致 `[Perf] crm{n=0,ms=0}` 恆為零。
   核定修法是在 ChurchReport 的 Debug DI 組合根中，在 `AddToolUtility()` 後保留原 descriptor lifetime，將解析出的 `IOrganizationService` 包裝為 `TimedOrganizationService`；移除無效的 `TimedToolUtilityProvider`。
2. `SessionDiagnosticsSwitch.Enabled` 錯誤跟隨 `DiagnosticsTrace:Enabled`。核定修法是加入預設 false 的 `DiagnosticsTrace:SessionVerbose`，以 `allowEnabled && configuredSessionVerbose` 收斂，且仍保留 `#if DEBUG` 與 `[Conditional("DEBUG")]` 防線。

## 特別不變量

- 不得改動 `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`。
- `IOrganizationService` 必須維持 scoped；不得把 inner、request、Session、使用者、tenant、credential 或連線狀態升為跨 request 共享狀態。
- `TimedOrganizationService.Inner` 必須保留，供連線池解包歸還真正連線。
- 最終 AC-1/AC-2 必須以單次實跑、正常關閉後的 Trace.log 與 JSONL 交叉比對，不可以測試替代。

## 輸出格式

列出：
1. Critical（會阻止依設計安全實作或驗收的問題）
2. Warning（應在實作或測試時特別核對的問題）
3. 建議的最小可驗證測試形狀
4. 對 DI descriptor 置換、scope、釋放鏈與實跑 trace 的具體檢查清單

每一項都要引用具體檔案／型別／行為。若核定設計已正確，請明確說明原因。
