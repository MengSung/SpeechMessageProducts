# CCG reviewer Task: p207-worker-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
Active task: .trellis/tasks/11-07-p2-07-outbound

請唯讀審查本輪 P2-07 變更，不修改檔案、不建置、不連 D365／LINE、不讀取任何 secrets。

範圍（只審查這些新增／修改檔案）：
- SpeechMessage.Hosting/Outbound/OutboundTaskWorker.cs
- SpeechMessage.Hosting.Tests/Outbound/OutboundTaskWorkerTests.cs
- ToolUtility/Outbound/D365OutboundWorkStoreAdapter.cs
- SpeechMessage.Hosting/SpeechMessage.Hosting.csproj（確認未破壞架構相依）
- .trellis/tasks/11-07-p2-07-outbound/wp-reports/P2-07.md
- .trellis/tasks/11-07-p2-07-outbound/implement.md
- .trellis/tasks/11-07-p2-07-outbound/task.json

契約：每 5 秒、每批最多 50；RowVersion CAS；stale Processing 超過 10 分鐘可重認領；429／5xx／timeout 只按 1／2／4／8／16 分鐘重試，五次後 Failed 並交既有共用 ExceptionReporting；停止只停新輪詢，當批最多 30 秒排空，scope／task 有界且確定性清理；不跨教會／使用者／tenant 保存可變狀態；Hosting 不得反向參考 ToolUtility.Core，D365 Core→Hosting 只能由 net10 ToolUtility adapter 轉接。

請檢查：正確性、CAS 與 stale 邊界、取消／停機 drain、未觀察 Task、scope／SDK 所有權、跨教會隔離、重試計數與 error code、例外通知保密性、架構相依、測試是否真實保護契約、繁中檔首說明與可維護性。輸出 Critical／High／Medium／Low，若無問題明確寫「無」。不要把目前未完成的乾淨複本守門誤判為已完成。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.