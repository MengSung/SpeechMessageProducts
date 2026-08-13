ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-dedication-booking-read-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 認獻單讀取 disabled boundary：架構分析

請審查以下計畫是否滿足 ChurchReport 到 Dynamics ProductClient 的安全遷移邊界。

範圍：將已存在的 `IPackage01DedicationBookingReadClient` 接成新的 async、DTO-only
ChurchReport service。新增 `Package01DedicationBookingReadEnabled` sub-gate，必須依賴
`Package01FeeReadsEnabled`。gate=false 時不得 bind options、解析 host、建立 client/pool/handler
或 outbound I/O。gate=true 時 ProfileAlias 必須取自 deployment config，且在 injected client 或
host resolution 前驗證非空。

現有 `DonationBookingService.FillBookingList` 是同步 legacy path，使用 FetchXML + N+1
`RetrieveEntity`；計畫不修改它，也禁止 `.Result` / `.GetAwaiter().GetResult()`。
新 adapter 必須先完成 typed query/DTO validation/local mapping，再單一替換 request-local
`DonationPaymentFormModel.DedicationBookingList`；fault/cancellation/invalid row 不得部分發布。

約束：無 CE mutation、無 feature enablement、無 traffic、無 P7.5/P8。使用 fixed workload、
server-authorized contact ID、ProfileAlias deployment-owned；不可保存 HttpContext/Session/Entity/
DTO/client/lease/cache/timer 或 caller routing state。所有新的 C# 必須完整繁中 XML docs、UTF-8 no BOM、CRLF。

請輸出 Critical / Warning / Info。特別檢查：跨使用者/Profile 隔離、lifecycle ownership、
cancellation、partial publication、設定 gate 漏洞、legacy 雙路徑/N+1 風險，以及 P7.5/P8 gate violation。


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
