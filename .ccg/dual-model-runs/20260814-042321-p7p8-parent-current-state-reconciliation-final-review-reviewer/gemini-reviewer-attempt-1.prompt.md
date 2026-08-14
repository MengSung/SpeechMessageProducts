ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7p8-parent-current-state-reconciliation-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7/P8 parent current-state reconciliation final review

請審查目前未提交的 P7/P8 parent 文件校正，僅檢查正確性、證據強度、P7.2 non-replay、
P7.5/P8 gate 與範圍漂移。

已驗證的事實：

- authoritative matrix：70 rows、70 temporary-legacy、67 consumer-not-migrated。
- P7.5 prerequisite report：`readiness.state=no-go`。
- 歷史 P7.2 Slice C：write-not-committed no-go + exact cleanup；不可重播。
- 新 P7.2 payment control plane：`CeDispatchAllowed=false`、`ProductConsumerAllowed=false`；local-only。
- P7.4 有 15 個封存 local child；它們不自動升級 matrix consumer／CE／host／traffic evidence。
- ORG-CALL-00066 已有封存 disabled DTO-only fee-editor boundary，不能重做或接回 FeeList/SaveBatch。

限制：

- 本 task 不得修改 C#、appsettings、matrix、CE、fixture、traffic、P7.5 removal 或 P8 deployment。
- 不得建議將 disabled local contract 宣稱為 consumer cutover／CE／host／traffic 完成。
- P7.5/P8 必須繼續 fail closed。

輸出：Critical / Warning / Info，附精確檔案與理由；若無問題，明確寫 no findings。


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