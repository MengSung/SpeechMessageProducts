ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-batch-b-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 Batch B 本機審查

請審查目前未提交的 git diff，範圍是 P7.4 Batch B 的 Package01 StorLesson read-only consumer cutover。

## 已授權且預期的變更

- Data8 `lesson` inner link 必須投影 `new_name`、`new_class_start_date`、`new_now_stage_name` 到純值 wire record，再映射至 ProductClient DTO。
- `MemberInfoController.LoadContactStorLessons` 與 `EquipmentController.LoadEquipmentStorLessons` 改用 request-cancellation-aware async typed projection。
- Package01 typed path 不可使用 `RetrieveEntity`、`EntityCollection` rehydration、sync-over-async、legacy fallback 或跨 request 可變狀態。
- SDK Entity / EntityCollection caller 必須保持 legacy-only，不能被誤標示為已遷移。
- null 開課日期必須維持既有 UI 的 `DateTime.MinValue`，不得受本機時區偏移成看似有效日期。

## 硬性限制

- 所有 feature gate 保持 false；不得 CE mutation、read-only CE、流量切換、P7.5、P8、push 或 PR。
- 不得擴張為 ToolUtility removal、generic CRM proxy、request-time fallback 或雙寫。
- 必須維持 server-owned profile／workload routing、A/B isolation、cancellation propagation、bounded resource ownership，以及 UTF-8 without BOM/CRLF。

## 審查輸出

以 Critical / Warning / Info 列出實際可驗證的問題；每項須說明檔案與精確原因。請避免將 deployment-owned configuration 誤判為 caller-controlled input，也不要要求啟用 feature gate 或執行 CE 作為本機審查條件。


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