ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p7-2-slice-c-fresh-preflight-probe-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C FreshPreflightProbe review request

Review the current uncommitted P7.2 changes in this worktree, with emphasis on
the new `-FreshPreflightProbe` PowerShell parameter set, the strict
`Get-StrictFreshPreflightProbeEvidenceFile` parser, the C# fixed-category
evidence writer and probe, process environment snapshot/restore, timeout and
child-failure projections, and task-owned fresh-fixture mutation boundaries.

Requirements to verify:

- The probe uses only deployment-owned `crm91 + Data8 + CE 9.1` WhoAmI,
  exact-ID Retrieve and bounded RetrieveMultiple; it performs zero Create,
  Update, Assign, Delete, Associate, Disassociate, ledger/descriptor
  publication, feature-flag or traffic changes, and cleanup is deterministic.
- Every accepted evidence field is fixed and deidentified; CRM IDs, names,
  endpoints, credentials, tokens, cookies, raw responses, raw exceptions and
  baseline values cannot cross the child/parent boundary.
- `FreshPreflightProbe` is mutually exclusive with all mutation/reconcile/repair
  modes and always projects `operationExecuted=false`, `safeToRetry=false`.
- Later Create/Update/Assign/Delete/Associate/Disassociate are allowed only for
  a newly created task-owned fresh fixture after exact ledger/marker/allowlist,
  read-back and cleanup proof; stale, shared, unknown or caller-selected data is
  never mutable.
- Check cross-user isolation, resource ownership, timeout/ambiguous fail-closed
  behavior, UTF-8 no-BOM/CRLF/final-CRLF, tests and scope. Do not inspect or
  expose secrets or live CRM identifiers.

Return Critical/Warning/Info findings with file and line references. Do not
modify files.


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
