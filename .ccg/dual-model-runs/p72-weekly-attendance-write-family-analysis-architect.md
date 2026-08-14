# CCG architect Task: p72-weekly-attendance-write-family-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 ORG-CALL-00063 weekly attendance write-family architect analysis

僅分析本機 source 與 task artifacts，不執行 CE、修改 gate、traffic 或資料，不輸出 credential、endpoint、CRM ID、名稱、原始例外或 raw response。

已知：
- 歷史 P7.2 Slice C `write-not-committed` no-go + exact cleanup，永久不可重試。
- ORG-CALL-00063 的唯一直接 production caller 是 PersonalQrCodeUtility；它先讀 meeting-statistics，再以 SDK Entity 執行 present-record create/update、meeting relationship、weekly-report recalculation，故不是 DTO-only read。
- SundayQrCodeUtility 也走相同 SigningMeetingStatistics write family；Package03 weekly DTO 僅含 ID/name/createdOn/Sunday。
- P7.5 readiness 仍 no-go；所有 feature gates false；P8 不得開始。

請輸出 Critical / Warning / Info：
1. 是否 source audit 正確區分 read contract 與 write-adjacent legacy graph；
2. 第一個最小 local-only write slice 的建議邊界與不可接受的 read-new/write-legacy 風險；
3. authorization、idempotency、ledger、exact read-back、cleanup、A/B isolation 與 no-replay 的缺口；
4. 是否存在可安全進入 CE preflight 的條件；若沒有，寫出精確 no-go。

不得把 local contract 或現有 Package03 read 稱為 CE、consumer、host、traffic、P7.5 或 P8 evidence。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.