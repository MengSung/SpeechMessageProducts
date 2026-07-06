ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: show-line-send-result-for-donations

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
請審查以下完整變更，重點檢查：
1. ATM/匯款奉獻是否會把 LINE 發送成功或失敗原因顯示給使用者。
2. 輸入奉獻是否會把 LINE 發送成功或失敗原因顯示給使用者。
3. ATM/匯款虛擬帳號結果資訊是否有可用的複製按鈕，且只在 ATM/匯款結果顯示。
4. 複製功能是否有 navigator.clipboard 與 fallback，並能回報成功或失敗。
5. LINE 發送失敗是否不會中斷奉獻/付款主流程。
6. 使用者可見錯誤原因是否足夠明確，且沒有洩漏敏感資訊。
7. 測試是否涵蓋成功、全部失敗、未綁定 LINE 的回歸案例。

請輸出 Critical / Warning / Info 分級審查報告。

```diff
System.Object[]
```

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