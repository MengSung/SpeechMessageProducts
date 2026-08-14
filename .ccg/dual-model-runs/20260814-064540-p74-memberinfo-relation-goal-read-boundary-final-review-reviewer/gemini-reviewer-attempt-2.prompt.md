ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-memberinfo-relation-goal-read-boundary-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00033 source-only final review

Review only the task-owned documents and the parent task metadata changed for
the `ORG-CALL-00033` relation-goal source-only local design no-go.

## Requirements

- Do not permit a Church-only partial migration: every current consumer derives
  authorization through `GetAccess` / `CanViewContactsBatch`; Shepherd may use
  saved-credential `ListManager` loading.
- No Session, `InMemoryContext`, `ListManager`, ToolUtility, browser locator,
  caller profile/connector/credential/query, or old `allowedIds` may be
  described as a valid Gateway authorization authority.
- Confirm the no-go includes the unbounded `RetrieveAllEntities(connection)`
  paging and catch-all error-to-empty formatting issues.
- Confirm no production/runtime/CE/gate/traffic/matrix/P7.5/P8 change is
  presented as complete.
- Confirm recovery conditions require a new immutable server-derived MemberInfo
  authorization boundary before relation-goal registry/Data8/ProductClient work.

## Files under review

- `.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/`
- `.ccg/tasks/p74-memberinfo-relation-goal-read-boundary/`
- `.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`

Return only concise Critical / Warning / Info findings. Do not request or
recommend CE actions, feature enablement, traffic change, fallback/retry,
P7.5 removal, or P8 deployment.


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