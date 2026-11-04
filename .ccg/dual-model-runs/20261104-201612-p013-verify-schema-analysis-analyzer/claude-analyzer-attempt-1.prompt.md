ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p013-verify-schema-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# P0-13 verify 查詢欄位故障：離線根因分析
使用者指定 ClaudeOnly。只讀產品與測試，不修改任何檔案，不連 D365/LINE/伺服器。不讀 appsettings、User Secrets、artifacts/d365、export、金鑰或授權。回報不得含機密或個資。
已知：net462 Plugin apply 成功；使用者隨後 verify 回報 0x80041103（QueryBuilderNoAttribute）及 exit 1。官方欄位 sdkmessageprocessingstepsecureconfigid，程式卻用 secureconfigid。trace 清單 secureConfig 為空，首次建立前 plan 不查步驟，故可建立成功後才在 verify 報錯。
一手依據：https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/sdkmessageprocessingstep#sdkmessageprocessingstepsecureconfigid 及 https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes （QueryBuilderNoAttribute）。
範圍：SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs、DeploymentCli.cs，SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs，.trellis/spec/backend/deployment-contract.md。
請獨立追查：verify 進入 FindStep 及 fingerprint/ReadSteps 的呼叫；錯誤欄位全部讀寫/secure presence/export/config 切換路徑；替身無欄位驗證的假綠燈。建議最小修正與測試。維持不讀 secureconfig 值、不比較秘密、不改 Plugin/ToolUtility/框架、不放寬守門。新回歸應跑真實 CLI verify、plan/apply/export/切換/停用流程，替身依獨立固定 schema 拒絕未知欄位。
OUTPUT: 根因、最小修正、必要測試、Critical/High/Medium 風險。勿擴大本次範圍。


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