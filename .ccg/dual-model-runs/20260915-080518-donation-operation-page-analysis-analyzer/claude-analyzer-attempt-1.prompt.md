ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: donation-operation-page-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# 分析任務

請唯讀分析目前 repository 的 `SpeechMessageProducts.ChurchReport` 奉獻頁、DonationPaymentFormModel、DonationPaymentManager、DedicationController、PaymentReturnController、DonationFeePaymentProcessor 與 SpeechMessage.Payments 整合。

目標是將參考頁的多類別奉獻操作體驗移植至現有專案，同時保留最多七列、定期定額單列、信用卡與 ATM、Session isolation、Callback 冪等，以及成功付款後 CRM 收費單實收金額與付款狀態同步。

請輸出：現有資料流、缺少的契約、最小修改檔案、主要安全/隔離/資源生命週期風險、建議測試。不得輸出 appsettings 的任何秘密值。


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