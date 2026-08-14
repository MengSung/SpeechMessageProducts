ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p71-list-member-count-source-no-go-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 ORG-CALL-00047 source-only no-go architecture analysis

Review the task-record changes only; do not modify code, invoke CE, request credentials, enable gates, change traffic, or recommend P7.5/P8 work.

## Sources and confirmed facts

- `ORG-CALL-00047` is `list.members.count.by.listid` in the authoritative matrix.
- `DownloadListManager.GetListManager` obtains lists inside a login/mutable workflow and calls `GetSmallGroupMemberNumber` for weekly-report totals/charts.
- The method can fall back to a shared ToolUtility CRM service when no operation-scoped service is supplied.
- Static lists query `listmember`; dynamic lists read CRM `list.query` and execute the stored FetchXML via `FetchExpression`.
- The task concludes that a direct typed migration is a fail-closed local design no-go because listId alone is not server authorization and stored dynamic FetchXML is not a server-owned named template.

## Review questions

1. Is the no-go technically justified under cross-user/profile isolation and request-local authorization requirements?
2. Does the task correctly forbid a static-only partial migration and raw CRM query/object bridges?
3. Are the listed recovery conditions sufficient as a minimum before a future dedicated child could be planned?
4. Identify only Critical or Warning defects in the task records; do not expand scope.

OUTPUT: Critical / Warning / Info findings, with exact file references. State explicitly if no Critical/Warning issue is found.


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