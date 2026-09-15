# CCG reviewer Task: donation-operation-page-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
請審查 codex/donation-operation-page 相對 d276ea270 的所有變更，以及目前未提交變更。重點：奉獻操作頁 Lines[i].Category/Amount/Others、最多7列、定期定額單列、多類別 ATM 預設封鎖、信用卡 Callback Param1 多 Guid fail-closed、CRM 每張 new_fee 的 new_fee_really_paid/new_pay_status 同步與冪等、Session isolation、例外告警順序、資源生命週期、UTF-8/CRLF，以及是否含來源教會硬編碼資料。請依 Critical/Warning/Info 回報具體檔案行號。任何會造成跨使用者資料、部分入帳、重複入帳、錯誤付款狀態、秘密外洩或資源洩漏的問題列為 Critical。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.