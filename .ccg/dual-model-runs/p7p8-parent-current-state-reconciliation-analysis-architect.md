# CCG architect Task: p7p8-parent-current-state-reconciliation-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7/P8 parent current-state reconciliation analysis

請只讀檢查目前 P7/P8 parent 文件與封存 evidence 是否一致，並提出最小範圍、繁體中文的校正建議。

範圍：

- `.trellis/tasks/08-05-gateway-purpose-and-positioning/{prd.md,design.md,implement.md,roadmap-p5-p7.md,task.json}`
- `.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
- authoritative matrix、P7.5 prerequisite report、P7.2/P7.4 最新封存 child。

已知不可變事實：

1. P3-P6、P7.0-P7.3 已封存；P6 Official Worker live compatibility 仍 evidence-pending，但不阻擋 Data8-first local work。
2. 歷史 P7.2 Slice C 是 write-not-committed no-go 且 exact cleanup 完成，永久 non-replay。
3. `08-14-p72-governed-recurring-payment-return-write-family` 只有 local control-plane evidence；
   `CeDispatchAllowed=false`、`ProductConsumerAllowed=false`，不得升格為 CE／cutover。
4. P7.4 有多個 disabled local child；它們不會自動把 matrix legacy consumer row 改成 migrated。
5. P7.5 prerequisite report 為 deterministic no-go；P8 只能在 P7.5 immutable handoff 及外部部署條件就緒後建立。
6. 所有 checked-in feature gates 必須維持 false；此 task 不得建議 CE、流量、P7.5 removal、P8 deployment 或 matrix row rewrite。

輸出：Critical / Warning / Info。請只列可由現有 evidence 支持的 findings，並明確標示任何不應採用的推測。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.