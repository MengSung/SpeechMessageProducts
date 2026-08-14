[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-current-state-rebaseline-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 current-state rebaseline final review

請審查以下 task-owned rebaseline 變更的 correctness、安全性與 evidence boundary。

本次變更：

- 新 child `08-14-p7-current-state-rebaseline` 以封存離線 analyzer 產生 current-source 70-row matrix，
  並新增僅限 task-owned 目錄的 wrapper、focused tests、summary、research 與 task records。
- matrix source SHA-256 已不同於 archived matrix；因此 parent 明確將 archived P7.5 report 降為歷史
  source/project/settings no-go snapshot，而 current matrix 自身保持 P7.5/P8 fail closed。
- parent PRD/design/implement/roadmap/task metadata 更新了 P7.4 00057 local-only evidence、00011/00012 action
  no-go、direct candidate audit=0 和下一個 authorization-boundary recovery prerequisite。

不可接受的結果：

- 將 registry/Data8/ProductClient/local-only/test 視為 consumer、CE、host、traffic、P7.5 或 P8 evidence。
- 重播 historical Slice C、執行 CE mutation、開 feature gate、切流、P7.5 removal 或 P8 deployment。
- 引入 session/profile/credential/resource retention、matrix output 越界或敏感輸出。
- 對 P7.5 current-source state 的不實宣稱，或遺漏 P8 immutable handoff／external deployment gate。

請輸出 Critical / Warning / Info，附精確檔案和理由。若無 issue，明確寫 no findings。不要建議 CE、feature、traffic、P7.5 removal 或 P8 deployment。


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
  PID: 55140
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-55140.log
