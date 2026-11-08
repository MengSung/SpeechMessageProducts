ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p203-required-resume-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
請以 reviewer 角色只讀審查 P2-03 已提交實作，勿修改任何檔案、勿啟動子代理、勿執行部署或網路測試。
本輪高優先執行設定要求 Gemini 與 Claude 雙模型審查；只審下列來源：
LineMessagingProcessor.Core/Security/ChannelTokenProtector.cs 與同目錄相關型別；SpeechMessage.Dynamics.Deployment/Channels；SpeechMessage.Dynamics.Deployment/DeploymentCli.cs、Program.cs；相應已提交離線測試與 .trellis/spec/backend/channel-token-contract.md。
檢查認證後解密、固定時間比較、秘密清除、有限輸入、白名單、組織隔離、穩定資料庫身分、SDK lease/lock/drain、真實Exception及敏感輸出。
不得讀取 User Secrets、任何 credentials、artifacts、evidence、原始 log、trace、環境變數內容或 repo 外的檔案。不審本輪未完成的 P2-07 stubs，不以已封存3.3的實機條件否定3.4的P2-03離線完成定義。
輸出 Critical/High/Medium/Low 分級、檔案行號、可重現原因；沒有問題寫無。不得將離線結果冒稱實機。


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