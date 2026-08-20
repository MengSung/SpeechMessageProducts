# CCG reviewer Task: unified-trace-batch1-b1

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# B0 + B1 範圍審查

請審查目前工作樹中尚未提交的 B1 變更；先執行 `git diff --check` 與 `git diff`，再讀取下列檔案的完整內容：

- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- `SpeechMessageProducts.ChurchReport/Program.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
- `ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs`

任務背景：B1 先修正量測工具，避免高頻 Session Debug 輸出與 `AutoFlush=true` 污染 `/Home/ProcessLogin` 的效能量測。B2-B7 絕不可修改。

已定案、必須逐項驗證的設計：

1. 新增 `SessionDiagnosticsSwitch`，與既有 `ProfilingSwitch` 同模式：僅 `#if DEBUG`、`public static volatile bool Enabled = false;`；由 `Startup` 的受信任 `DiagnosticTraceOptions.Enabled` 指派；不可保存 request、Session、使用者、租戶、credential、token 或其他可變狀態。
2. `InMemoryDataContextSmallGroup.cs` 中原有的 51 個 `System.Diagnostics.Debug.WriteLine`（GetCurrentSessionId 21、GenerateCurrentRequestFingerprint 18、SetSessionDirtyFlag 11、InMemoryDataContext 1）必須全部改受開關保護；關閉開關只能抑制輸出副作用，不可改變任何 Session、指紋、key、dirty flag、例外或回傳行為。
3. `Program.cs` 的 `StreamWriter.AutoFlush` 與 `Trace.AutoFlush` 必須改為 false，且有請求結束、正常停止、未處理例外三種明確 flush 點。全域 Trace listener/stream 的唯一 owner 必須仍為 Program，資源釋放與 static event 訂閱必須有決定性清理，不得造成跨請求、跨使用者、跨租戶、記憶體或 handle 洩漏。
4. 新測試不得放寬既有斷言，且要能防止未受保護的新 Debug 寫入或漏掉 51 個既有呼叫。
5. 不得改 DataverseTrace 格式、PerfThresholds、DataverseGateway、BoundedClientPool 或 `D:\除錯追蹤` 任何原始 trace。
6. 所有修改過的 `.cs` 檔必須有完整、準確的繁體中文註解，解釋隔離邊界、生命週期、清理、效能與測試契約。

請只報告目前 diff 中可驗證的問題，使用：

- Critical：會破壞正確性、隔離、資源生命週期、安全或任務硬性禁止事項。
- Warning：可靠性、可維護性、測試有效性或範圍風險。
- Info：非阻擋建議。

對每項發現請提供檔案/行號、技術理由與具體修正建議；若無 Critical/Warning，請明確說明。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.