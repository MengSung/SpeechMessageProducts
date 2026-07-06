ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: show-line-send-result-for-donations

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
請重新審查以下完整變更，這次重點確認前次發現已修正：
1. ATM/匯款複製按鈕是否在 payWay 為「虛擬帳號」或「ATM轉帳/匯款」時會顯示。
2. 複製按鈕色彩對比與鍵盤 focus 是否符合基本可用性。
3. ATM/匯款與輸入奉獻是否會顯示 LINE 發送成功或失敗原因。
4. LINE 發送失敗是否不會中斷奉獻/付款主流程。
5. 測試是否覆蓋成功、全部失敗、未綁定 LINE 的回歸案例。

請只輸出 Critical / Warning / Info 分級審查報告，若沒有 Critical 請明確寫「Critical: 無」。

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