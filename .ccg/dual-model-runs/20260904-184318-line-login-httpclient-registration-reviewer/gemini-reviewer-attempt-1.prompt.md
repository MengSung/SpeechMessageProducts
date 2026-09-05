ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: line-login-httpclient-registration

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.7.JsutComsumeClaude(SpeedUp).worktree

## Request
# 審查請求：LINE 奉獻收費清單登入的 HttpClient 生命週期

請審查目前 working tree 相對於 HEAD 的所有變更，特別關注：

1. `Startup.cs` 新增的 `LineLoginApi` named HttpClient 是否正確、有界 timeout，且與 `IHttpClientFactory` 使用方式一致。
2. LINE LIFF ID Token 驗證與奉獻收費清單流程是否可能造成跨使用者/跨 Session/跨租戶資料洩漏。
3. 是否存在 managed/unmanaged memory、socket、connection、stream、timer、task、cancellation 或其他 resource leakage。
4. `DedicationController`、`DedicationAuditController`、`HomeController`、付款模型/服務與相關 Razor/測試變更是否有正確性或回歸風險。
5. 是否有可證明的效能問題或安全的加速機會；不要提出沒有測量或會犧牲隔離/清理的投機性重構。

請先閱讀實際 `git diff` 與相關呼叫路徑，再輸出：

- Critical：必須修正才能交付的問題
- Warning：建議修正或需明確限制的風險
- Info：觀察與可選優化

每一項請附檔案、行號或可定位的程式片段，以及判斷依據。若沒有問題，請明確寫出驗證過的條件；不要把不存在的行為當成事實。


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