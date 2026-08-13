ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-dedication-booking-read-boundary-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 認獻單讀取 disabled boundary：實作審查

請審查目前 git diff 中 P7.4 ChurchReport 認獻單 typed-read boundary。

已實作：
- `DynamicsAccess:Package01DedicationBookingReadEnabled` sub-gate，依賴 `Package01FeeReadsEnabled`；所有 checked-in 設定為 false。
- gate=false 時不 bind options / resolve ProcessHost / create typed client。
- gate=true 時先驗證 deployment-owned ProfileAlias，即使注入 typed client 也不可略過。
- factory 支援 Embedded、DedicatedGateway、CentralGateway；Embedded RequestGuard allowlist 加入 `PaymentsDedicationRetrieveByContact`。
- `DonationBookingReadService` 僅使用 `IPackage01DedicationBookingReadClient` DTO、固定 workload、forwarded CancellationToken、完整 row validation、防禦性 immutable result。
- `DonationBookingReadModelAdapter` 先完成 read/mapping，才原子替換 request-local `DonationPaymentFormModel.DedicationBookingList`；沒有 retry、fallback、同步等待、CRM Entity 或 ToolUtility I/O。
- 保留同步 `DonationBookingService.FillBookingList` 原樣；新 async boundary 尚未接入流量。

限制：無 CE/feature enablement/traffic/P7.5/P8。請以 Critical / Warning / Info 審查：隔離、lifecycle、async/cancellation、partial publication、Gate/route、legacy 雙路、DTO parity、C# docs/encoding、測試缺口。

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
