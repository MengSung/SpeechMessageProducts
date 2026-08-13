ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-legacy-admission-design-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 legacy admission boundary 設計審查

請審查既有工作樹中 P7.4 child 的規劃，不寫入檔案。

## 已確認事實

- ChurchReport `DonationFeeQueryService` 在 Package01 flag=false 時直接呼叫 legacy
  `ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml`；它最終對長壽命
  `IOrganizationService` 執行同步 `RetrieveMultiple`。
- ToolUtilityFactory 為 process-wide singleton；legacy CRM transport 不是 ChurchReport DI
  CrmConnectionPool，也沒有 IOrganizationAdmissionManager / durable coordinator seam。
- 現有 Dedicated/Embedded Data8 runtime 為 per-host in-memory admission，不能證明與 legacy
  ToolUtility 共用 aggregate capacity。
- 不能啟用 Package01FeeReadsEnabled、不能切流、不能 CE 寫入。舊 P7.2 CE cycle 永不重試。

## 擬議設計

建立 host-owned `LegacyToolUtilityDrainController`，僅管理受控 legacy ingress 的
stop/acquire/drain lease lifecycle，不持有 CRM、profile、endpoint、credential、request 或 response。
它不是 CRM pool、不替代 ToolUtilityFactory，且不能宣稱涵蓋所有 legacy call。另提供固定分類、
零 network/mutation 的 deployment validator 與 drain-first/non-overlap runbook。

功能旗標只能在 actual deployment owner 證明：所有 legacy ingress 已停止／drain 且 coverage 可證明，
Gateway/Data8 對相同 canonical Organization 使用同一 durable SQL plan/namespace/epoch/digest並 ready，
才可進行受控 smoke。否則維持 false/no-go。

## 請檢查

1. 此設計是否錯把 operation-level metering 當 organization-level safety？
2. 是否有 cross-user/profile/session/resource leakage 或 cancellation/drain race？
3. controller、validator、runbook 的最小測試契約有哪些遺漏？
4. 請只輸出 Critical / Warning / Info，且不要輸出任何 secret、endpoint、CRM ID 或原始例外。


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