ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p72-slice-c-independent-cycle-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C 新獨立週期安全審視

請審視以下已獲使用者明確授權的受控 CE 9.1 測試週期，僅回覆安全與流程風險；不得建議擴大權限、重試失敗操作或修改週報。

## 已知事實

- 前一個 Slice C 的 `ExecuteFixture` 曾以 `child-process-failed` 結束，`safeToRetry=false`；該次週期已停止。
- 對該週期執行的 exact-ID cleanup 已完成，task-owned fresh fixture、descriptor 與 ledger 均已不存在。
- 目前使用者已明確授權一個全新、獨立週期：新 task-owned fixture、新 nonce、新 ledger；不可重試舊週期。
- 必須先執行僅含 WhoAmI、精確 Retrieve 與有界 RetrieveMultiple 的 `FreshPreflightProbe`。
- 只有 probe 為 `go`，才可建立新 fixture 並依 allowlist 執行一次 Slice C。任何 timeout、ambiguous、read-back 不符、cleanup 不確定或 no-go 均立即停止，且不可進入 Slice D-H。
- 週報絕不可被建立、選擇、修改、停用、刪除或修復：`zero-active` 是正常且不關聯週報的轉組出席紀錄；`exactly-one-active` 必須精確關聯並 read-back；`duplicate-active`／`unavailable` 必須 fail closed。
- 僅允許 mutation 作用於本週期的新 task-owned fixture，並要求 ledger、精確 ID、marker、allowlist、read-back 和 deterministic cleanup。不得掃描／自動挑選 CRM 使用者，或觸及舊、共享、正式及未知資料。

## 請檢查

1. 此序列在「先唯讀、後一次受控寫入、精確 read-back、清理」上是否缺少 release-blocking safety gate。
2. 是否有任何內容可能錯誤地把前一週期的 no-go 變成重試。
3. 只列出可由既有 fail-closed 合約驗證的 Critical／Warning；若沒有，明確寫無發現。



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
