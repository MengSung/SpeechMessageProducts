# CCG reviewer Task: file-purpose-documentation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀審查本次逐檔文件及永久守門變更。需求及計畫在.ccg/tasks/file-purpose-documentation/；與c3e9db15相比主要是1200多檔繁中檔頭、generator與6個.g.cs輸出註解、文件守門Python及8案例、CI入口、AGENTS/spec、JSON伴隨說明與docs/檔案用途與註解索引.md。全方案Release4371/4371；I18n工具170/170；JS16/16；Roslyn996檔token/directive差異0；文件守門1247檔零缺漏。請重點審查checker是否確實涵蓋未來新來源、偽檔頭/encoding/link邊界、JSON身分、generator可重生、註解是否錯述執行事實及是否意外修改行為。不得修改來源，不派子代理，輸出Critical/Warning/Info。既有P1主對話繼續原實作，此處不得標記P1已完成。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.