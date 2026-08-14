[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-memberinfo-smallgroup-tree-authorization-audit-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 MemberInfo 小組樹授權來源稽核：文件與安全審查

請審查目前尚未提交的 task/CCG/parent 路線文件變更。目標是記錄 ORG-CALL-00031/00032 的
source-only local design no-go，而非改產品程式。

必要結論：
- 現有 GetAccess 使用 Session/InMemoryContext；Shepherd scope 可透過保存 credential 載入 shared ListManager，
  因此不是在 cache/client/CRM I/O 前的 request-local server-derived scope。
- Church fixed descriptor query 不能替代 Shepherd scope；禁止 Church-only partial migration 宣稱完成。
- child 不得改 runtime、matrix、gate、CE、traffic、P7.5/P8。
- 必須將結果寫為僅停止這個 family，且下一個不相依 P7 family 可繼續。

請只輸出 Critical/Warning/Info，專注於：是否有錯誤的完成宣稱、放寬安全限制、漏掉恢復條件、parent/child
記錄不一致，或文件格式/範圍風險。不要建議執行 CE、開 gate 或建立 P8。


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
  PID: 43056
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-43056.log
