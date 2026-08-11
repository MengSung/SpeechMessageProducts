ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p72-execute-child-diagnostic

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C ExecuteFixture diagnostic review

Review the current uncommitted changes only in:

- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`

Context: A controlled CE Slice C ExecuteFixture child can write strict, bounded
`no-go` evidence and then exit nonzero because its final xUnit assertion requires
`go`. The parent previously returned only `child-process-failed`, losing the
safe failure category needed to diagnose a future independent cycle.

Required behavior:

1. A nonzero child exit always remains `no-go / child-process-failed`; it must
   not become success, cleanup authority, CE evidence, or retry authority.
2. Only a parent-owned non-reparse temporary root, exact evidence filename,
   strict full evidence schema, and allowlisted `no-go` reason may add the one
   deidentified `diagnosticCategory` field.
3. `go`, malformed, unexpected-path, reparse, and any non-allowlisted evidence
   must expose no diagnostic category.
4. No credentials, endpoint, CRM IDs, raw child output, exceptions, feature
   flags, traffic, CE 8.2, Official Worker, or remote mutation may be added.
5. Preserve deterministic process stream and temporary-directory cleanup,
   current-user isolation, Windows PowerShell 5.1 compatibility, UTF-8 no BOM,
   CRLF, and task-scoped behavior.

Return a concise Critical / Warning / Info report with exact source evidence.


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
