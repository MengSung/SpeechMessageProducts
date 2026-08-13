ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-batch-c-inventory-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 Batch C caller-shape inventory（唯讀分析）

請僅分析目前 repository，不修改任何檔案、設定、CE 或 feature flag。目標是在既有 P7.4 active task
中，為下列三個已具 Data8 executor／typed ProductClient／CE 9.1 Embedded read evidence、但 consumer
仍 `not-migrated` 的能力判斷下一個可安全實作的本機 read-only sub-batch：

- `ORG-CALL-00005` / `fee.dedication.retrieve.by.contact`
- `ORG-CALL-00064` / `fees.retrieve.by.dedication.period`
- `ORG-CALL-00066` / `fees.editor.load.by.disciplelesson`

已知限制：

- `Package01FeeReadsEnabled` 必須保持 disabled；禁止 CE request/mutation、traffic switch、P7.5/P8、push、PR。
- 不能把 ProductClient API 已存在誤當 consumer migrated。
- 禁止 request-time fallback、dual-write、SDK Entity/EntityCollection rehydration、sync-over-async、static
  request state、caller-controlled profile/endpoint/connector/owner。
- 若 caller 還有 EntityCollection 依賴、與 write 交錯、或 response contract 不是 ProductClient DTO
  可直接表示，必須維持 temporary-legacy，而非推薦硬切。
- consumer path 必須保留 cancellation、A/B request/profile isolation、bounded resource ownership 與
  deterministic rollback。任何實機 enablement 仍需 aggregate-capacity 或 verified drain-first evidence。

請輸出繁體中文，並逐 operation 提供：

1. 實際 ChurchReport caller 檔案／方法及其 response shape；
2. 是否只讀、是否相鄰 write、是否依賴 SDK Entity/EntityCollection；
3. 可否在 P7.4 以 DTO-only、disabled-by-default local sub-batch 遷移；
4. 若可行，最小檔案與測試邊界；若不可行，精確 temporary-legacy/P7.5 blocker；
5. 不得提出啟用 flag 或執行 CE 作為解決方式。


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