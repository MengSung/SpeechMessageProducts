[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-2-continuation-local-slices

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 continuation Slice F-H local-only review

請只審查目前工作樹的 P7.2 continuation 本機契約與隔離邊界，不執行任何
CRM、CE、feature flag、產品流量、CE 8.2、Official Worker 或 ToolUtility
移除操作，也不要修改檔案。

已知狀態：Slice C 最新 fresh cycle 的唯一 ExecuteFixture 回報
`write-not-committed` no-go，exact cleanup 成功；Slice C CE 軌道 closed，不得重試。
Slice D、E、H 已有 local-only decision/plan builder；Slice F、G 正在補本機契約。
所有 D-H catalog definition 固定 `CeExecutorEnabled=false`、`ConsumerEnabled=false`，
Data8 executor 必須在 admission/lease/client 前回傳 `operation.not-supported`。

請檢查：
1. 是否存在任何將 Session、HttpContext、principal、CRM service、ToolUtility、
   credential、token、owner、profile 或 mutable state 帶入新 local-only contract 的路徑。
2. timeout、partial completion、ambiguous dispatch、duplicate 與 cleanup 不確定時，
   是否明確 no-replay、fail-closed 且沒有跨使用者 retention。
3. Slice F graph cleanup 是否能以 known keys 逆序處理；Slice G draft 是否是
   per-operation immutable/bounded；Slice H 是否維持 zero-active 不關聯、exactly-one
   精確關聯、duplicate/unavailable no-go。
4. 本機工作是否會繞過 P7.4 Gateway cutover 或 P7.5 ToolUtility removal gate。

輸出只要 Critical/Warning/Info，並指出可由本機修正的具體問題。不要輸出 CRM ID、
姓名、端點、帳密、token、原始回應或原始例外。


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
  PID: 41008
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-41008.log
