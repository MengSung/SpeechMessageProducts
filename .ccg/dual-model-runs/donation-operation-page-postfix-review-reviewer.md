# CCG reviewer Task: donation-operation-page-postfix-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
最終修正後審查：請審查 codex/donation-operation-page 相對 d276ea270 的變更。確認上一輪指出的 DonationFeePaymentProcessor 硬編碼 LINE ID 與繞過 Exception.log 問題已移除；另檢查多類別奉獻頁、CRM 收費單付款金額/狀態同步、Callback 冪等與 Param1 fail-closed。只回報 Critical/Warning/Info 與檔案行號。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.