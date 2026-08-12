# CCG architect Task: p7-2-listmanager-operation-input-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 ListManager operation-local 輸入邊界分析

請審查下列本機已重現的設計：`ListManager` 是以 session key 快取的長生命週期物件；其
`SetupIntegrateData(string, IOrganizationService)` overload 雖將傳入 service 直接往下傳遞，
但仍從 instance 欄位取得帳號、密碼、登入型態與日期。該 overload 沒有正式產品呼叫端。

已存在的回歸測試要求它在任何 CRM I/O 前 fail closed，並驗證兩個 A/B marker service 均
未被保存或 Dispose。直接的 `DownloadIntegrateData` service-aware entry 則已用完整參數的
operation-local report 建立、只讀 SDK 查詢與 A/B interleave tests 驗證。

請判斷以下最小修正是否正確：保留 `ListManager` overload 以維持二進位相容性，但無條件
以 bounded `InvalidOperationException` 在讀取 session instance 欄位或進行 CRM I/O 前拒絕，
要求未來改用明確、不可變的 operation context；不得把 service 或 context 回寫到 instance、
Factory、ToolUtility、static、cache 或 AsyncLocal。

請只輸出 Critical / Warning / Info，並專注：跨使用者 isolation、service ownership、
fault/timeout cleanup、以及是否有更小且安全的替代方案。不得建議啟用 CE、feature flag、
流量切換、或修改正式／共享資料。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.