ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p71-dedication-booking-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
請審查目前工作樹中 P7.1 ORG-CALL-00041 的變更。範圍限於：
- payments.dedication.retrieve.by.contact 的 registry/Data8 executor/ProductClient typed DTO read；
- Phase-0 matrix/schema agreement；
- 對固定 QueryExpression、fail-closed input、response branch、A/B mutation isolation、lease/permit disposal 的測試。

嚴禁將本機測試視為 CE、consumer cutover、P7.5 或 P8 evidence。請根據 git diff 輸出 Critical / Warning / Info，特別檢查：跨使用者/profile 隔離、資源釋放、raw CRM Entity 洩漏、query 可控性、matrix drift、回歸風險及文件/編碼規範。

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
