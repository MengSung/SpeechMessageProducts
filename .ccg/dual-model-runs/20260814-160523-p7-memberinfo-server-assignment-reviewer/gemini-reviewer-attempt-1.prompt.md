ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-memberinfo-server-assignment

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 MemberInfo server-owned assignment evidence review

Review the current uncommitted P7.4 child implementation for:

- fixed `memberinfo.authorization.assignment.resolve.by.subject` operation;
- Data8 fixed read / bounded 512-list evidence path;
- typed ProductClient projection and DI;
- ChurchReport `MemberInfoServerAssignmentEvidenceSource` adapter;
- request-local A/B subject/profile isolation, cancellation, failure handling, and resource ownership;
- matrix/registry consistency.

The capability is **local-only**. It must not change a controller, feature gate, CE traffic/mutation, ToolUtility removal, P7.5, or P8.

Inspect the full working-tree diff and the untracked P7.4 source/test files. Report only verified Critical, Warning, and Info findings, with file/line evidence. Treat any session, cross-user, cross-profile, credential, CRM SDK boundary, mutable collection, retry/fallback, unbounded query, or resource-lifecycle risk as Critical. Do not recommend out-of-scope cutover work.


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