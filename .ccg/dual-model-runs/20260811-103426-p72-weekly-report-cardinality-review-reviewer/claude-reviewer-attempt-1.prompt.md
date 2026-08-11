ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p72-weekly-report-cardinality-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C weekly-report cardinality review

Review the current `git diff` for the P7.2 Slice C change only. Do not propose or
perform a CE call, data mutation, user search, owner selection, feature-flag
change, traffic change, credential change, or production rollout.

Required business contract:

- The relevant cardinality is the exact descriptor-bound transfer target list,
  active state, and fixed UTC Sunday — never all Sunday reports in the
  organization.
- `zero-active` is normal: the fixed list transfer may create exactly one
  present record with no weekly-report lookup, and read-back must prove that the
  lookup is absent.
- `exactly-one-active` is normal: create the present record with that exact
  method-local weekly-report lookup, then read it back exactly.
- `duplicate-active` and `unavailable` fail closed before the first CRM
  mutation. The code must not choose, create, alter, deactivate, merge, or
  repair a weekly report.
- Evidence may contain only the fixed categories `exactly-one-active`,
  `zero-active`, `duplicate-active`, and `unavailable`; the legacy merged
  category must be rejected.

Verify correctness, no-mutation behavior for duplicate data, bounded exact
queries, no caller-selected weekly-report identity, zero cross-request/profile
state retention, deterministic lease cleanup, PowerShell parser strictness,
Traditional Chinese documentation quality, and test coverage.

OUTPUT: Critical / Warning / Info findings only, each tied to a concrete file
and behavior. If there are no findings, say so explicitly.


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
