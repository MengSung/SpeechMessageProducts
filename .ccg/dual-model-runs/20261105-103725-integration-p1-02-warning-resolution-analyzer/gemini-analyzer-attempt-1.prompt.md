ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: integration-p1-02-warning-resolution

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1-02 Warning 根因分析

請唯讀檢查目前工作樹的實際程式與測試，不要修改檔案、執行 apply／部署、連線 D365，亦不得讀取或輸出任何憑證。

## 背景

P1-02 R4 架構守門及相關 P1-01 測試整合已實作，雙模型審查 run `20261105-091412-integration-p1-bootstrap-recheck-reviewer` 為完整雙模型結果（`dualModelComplete=true`、無 quota 降級）。Claude 留下兩項 Warning：

1. `tests-shared/ArchitectureTests/ArchitectureProjectReader.cs` 的 `Evaluate` 在逾時終止程序後呼叫無期限 `WaitForExit()`；`finally` 也可能無期限等待。輸出透過 `ReadToEndAsync` 排空。
2. `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs` 的 `QueryFixture.LastQuery` 在 `RetrieveMultiple` 並行呼叫時以一般欄位寫入。

Gemini 另將 `SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs` 的 `AdvancingClock` 標為 Warning：`CreateTimer` 同步快轉時間並呼叫 `OnDelay`、觸發已到期 timer，建議同步上下文。請檢視 fixture 的實際所有權、測試使用方式及呼叫執行緒，判斷它是否為可重現的併發風險或只是假設情境。

## 分析要求

- 分別說明根因、可重現條件、實際影響與風險等級；區分程式缺陷、測試 fixture 風險、以及僅為未來維護建議。
- 對子程序生命週期提出可實作的最小修正。必須同時考慮終止等待期限、stdout/stderr pipe 排空、任務觀察、有限輸出記憶體及 finally 清理；不得用無界等待或遺留背景讀取工作來滿足表面逾時。
- 對 `LastQuery` 建議最小的安全發布方式，並判斷是否需要以並行讀取斷言保護。
- 對 `AdvancingClock` 只依目前的測試呼叫圖判斷；若不存在跨執行緒呼叫，請明確說明理由，不要建議無必要地重寫測試時鐘。
- 對照正式 P1 計畫既有的「每個程序有界等待、資源確定性釋放」要求。只有在證據證明需求本身矛盾或不可行時，才建議改計畫；單純實作未落實規格不構成計畫缺陷。
- 提出先行測試、預期紅燈、最小修正與修後驗證。不得降低既有斷言或測試範圍。

## 輸出

依 `Critical / High / Warning / Info` 分級，提供檔案與方法、可重現條件、證據、修正及測試建議。若某 finding 不成立，明確標示並說明程式碼依據。


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