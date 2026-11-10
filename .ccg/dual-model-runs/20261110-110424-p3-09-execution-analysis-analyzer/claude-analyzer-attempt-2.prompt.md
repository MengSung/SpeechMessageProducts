ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p3-09-execution-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P3.Worktree

## Request
任務：依 C:\Users\Administrator\.speechmessage\P3-09-codex-prompt.txt 完成 P3-09 與階段 3 收尾。

已知限制與核定事項：
- 目前工作樹為階段 3 P3 分支，P3-01～P3-08 已驗收。
- 使用者核定測試主機為 Hyper-V VM D365-9.1，VM 名稱 D365APP01，內部 IP 192.168.50.20；不得碰 D365DC01。
- 對外測試端點為 https://linewebhook-test.speechmessage.com.tw，Webhook URL 為 https://linewebhook-test.speechmessage.com.tw/api/line/webhook/1646814154。
- IIS 已有 goodshepherdback 與萬用憑證；新增獨立 IIS site/app pool，不能修改 CRM site/binding/app pool/certificate。
- 密碼只能從 C:\Users\Administrator\.speechmessage\test-vm-admin.clixml 以本機 DPAPI Import-Clixml 取得，不能輸出、記錄或複製。
- Channel secret/token 只能從本機 User Secrets 取得，不能落地或進 log/repo。
- 先修正三個既有紀錄檔的 CRLF，之後每個紀錄型提交先跑 §8.3。
- 必須依 04 檔 P3-09 第 1～7 步實機驗收；失敗或前置不足須如實記錄，不得宣稱完成。
- 實機操作需先 IIS appcmd backup；資料庫必須使用獨立測試資料庫，不得改 CRM DB；收尾殘留測試資料為 0。

請分別分析：
1. 安全且可回復的執行順序與前置驗證；
2. PowerShell Direct、IIS、SQL、DNS/TLS、.NET 10 hosting bundle 的風險與決定性驗證；
3. P3-09 第 1～7 步的證據與清理要求；
4. 哪些條件不足時必須停止並回報，而不是猜測或繼續；
5. 階段審查、推送與進度頁更新的收尾順序。

輸出格式：Critical / Warning / Info，附具體命令或驗證點；不得要求輸出任何秘密。


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