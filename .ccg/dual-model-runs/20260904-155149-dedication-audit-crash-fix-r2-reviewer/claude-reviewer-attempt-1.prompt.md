ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dedication-audit-crash-fix-r2

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.7.JsutComsumeClaude(SpeedUp).worktree

## Request
# Review request: Dedication audit crash fix

請審查目前工作樹相對於 HEAD 的所有未提交變更，重點檢查：

1. `DedicationAuditController.DedicationFeeAuditViewWeb` 從 Layout 導覽進入時，是否仍可能因 request-scoped `DonationPaymentManager.m_Contact` 為 null 而當機。
2. `BuildAuditWebFormModel` 的 null-safe fallback 是否正確、是否會把上一位使用者的姓名、手機、奉獻編號、身分證、後六碼、奉獻清單、同名清單或總額帶到目前 request。
3. manager 表單模型為 null 時是否會在後續 AJAX/Grid 路徑再次當機。
4. 本次測試是否真實保護了上述行為，是否存在 tautological 或脆弱反射測試。
5. 所有未提交變更是否符合 Session isolation、Memory/Resource lifecycle、繁體中文文件、UTF-8 無 BOM、CRLF 與效能要求。

請以 Critical/Warning/Info 分級，指出檔案與行號，並提出可直接修正的建議。不要修改檔案。


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