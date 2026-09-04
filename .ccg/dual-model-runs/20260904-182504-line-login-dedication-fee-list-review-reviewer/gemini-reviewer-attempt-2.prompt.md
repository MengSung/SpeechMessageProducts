ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: line-login-dedication-fee-list-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.7.JsutComsumeClaude(SpeedUp).worktree

## Request
# LINE 奉獻收費清單最終審查

請審查目前 working tree 相對於 HEAD 的所有變更，重點如下：

1. 確認 LINE LIFF 登入到奉獻收費清單的資料流可正常運作：ID Token 驗證、LINE subject/channel/issuer/expiry、CRM contact、Session 綁定、成功/失敗轉址。
2. 確認奉獻收費清單與奉獻稽核頁不會因 null model/contact/Session 而當機，也不會跨使用者或跨 Session 顯示個資或奉獻清單。
3. 針對 managed/unmanaged memory、HttpClient、response/content、cache、timer、subscription、background task、controller/context ownership 檢查是否有 Memory Leakage 或 Resource Leakage。
4. 檢查最近補上的前端保護：後端 status != 1 時不轉址；瀏覽器 console 不得輸出 ID Token。
5. 檢查修改的 C# / cshtml 文件、測試與註解是否符合繁體中文、UTF-8 without BOM、CRLF、可維護性與效能要求。
6. 僅回報可由目前程式碼與測試證明的 Critical / Warning / Info；每一項附檔案與行號、原因、修正建議。若無 Critical，明確寫出「無 Critical」。

請勿自行修改檔案；只輸出審查報告。


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