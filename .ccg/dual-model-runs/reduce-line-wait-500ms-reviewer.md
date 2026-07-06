# CCG reviewer Task: reduce-line-wait-500ms

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.8.WorktreeFabelSecurityScan

## Request
請審查以下變更，重點檢查 correctness / async timeout behavior / LINE notification UX / test quality / regression risk。

需求：ATM/匯款與手動輸入奉獻的 LINE 發送結果最多只讓畫面等待 500ms；真正 LINE 發送可在背景繼續，不能讓使用者長時間卡住。

已執行本地驗證：
- dotnet test ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~DonationPaymentProcessorKeyInNotificationTests：8 passed
- dotnet build ChurchReport.csproj --no-restore -p:OutDir=<temp>：0 warnings / 0 errors
- UTF-8 no BOM + CRLF check：pass
- git diff --check：pass

請輸出 Critical / Warning / Info 分級審查報告；Critical 必須可重現且指向具體程式碼。

```diff
System.Object[]
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.