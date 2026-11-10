ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p3-09-live-deployment

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P3.Worktree

## Request
# P3-09 安裝後續作分析

使用者已親自安裝官方 .NET 10.0.14 Hosting Bundle 並刪除兩份暫存安裝檔。使用者紀錄及 Codex 透過 PowerShell Direct 的獨立唯讀複核均確認 ANCM v2、ASP.NET Core／.NET 10.0.14、CRM site 與 CRMAppPool Started、goodshepherdback HTTPS 回 HTTP 302、兩份安裝檔不存在。

請先讀本工作樹 P3-09 PRD、設計、04 P3-09 1～7 步與 00 §11；審慎檢查現有 CRM HTTPS `*:443:` binding 必須保留，新增測試 hostname 僅可用獨立 SNI binding、不可變更 CRM site/app pool/certificate；平台 SQL 只能使用隔離測試 DB。分析 LineWebhook net10 Host、已有設定／服務、正式 P3-06 三項 Warning（30 秒事件 timeout、appointment schema 額外教會欄位、annotation retry Upsert）如何以實機決定性證據核實；列出秘密不落地、LINE API 只用測試 Channel、清理及回復風險。

輸出 Critical / High / Warning / Info 分級的具體風險與有根據的建議，指出不應執行或必須先確認的任何步驟。不得建議修改 CRM site、既有 `*:443:` binding、CRMAppPool 或憑證；不得以離線測試冒充實機結果。不要讀取或要求輸出秘密。


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