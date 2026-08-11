ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p72-seed-control-plane-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Seed Control-Plane Analysis

## Scope

Review the proposed fix for the P7.2 Slice C local fixture control-plane loop.
Do not suggest any Dynamics UI action, CRM user scan, feature-flag change,
traffic switch, CE 8.2 use, Official Worker use, or mutation of a non-task-owned
record.

## Observed root cause

Slice C cleanup correctly deletes the per-cycle fresh source/list descriptors
and ledger. The next `FreshPreflightProbe` incorrectly requires those deleted
descriptors, so a fresh cycle cannot start. The current local legacy `.bak`
candidate has one obsolete `targetOwnerId` field; it must never become owner
authority or a CE mutation target.

## Proposed contract

Create a persistent, current-Windows-user-bound, task-owned seed descriptor.
It holds only verified static list IDs, a task-marked baseline leader ID, UTC
Sunday key, and sanitized deployment metadata. A one-time bootstrap may use
the `.bak` only as a read-only migration candidate after strict local and CE
read-only proof. It ignores the obsolete owner field.

Each cycle is:

`seed -> read-only preflight -> new nonce + fresh fixture + fresh ledger ->
Slice C -> exact read-back -> cleanup`.

Cleanup retains the seed and deletes only the fresh descriptor pair, fresh
ledger, and task-owned fresh CRM entities. Provision may publish the fresh
descriptor pair only after successful fresh provision. Any no-go, timeout,
ambiguous result, read-back mismatch, or uncertain cleanup is non-retryable.

Weekly-report classification is: `zero-active` (go, no relation),
`exactly-one-active` (go, exact relation), `duplicate-active`/`unavailable`
(no-go). No weekly report is created or changed.

## Requested output

Return concise findings under Critical / Warning / Info focused on: schema
separation, migration safety, current-user isolation, CE read-only proof,
non-retry and cleanup behavior, test coverage, and any missing fail-closed
guard. Cite code-level integration points where inferable. Do not invent
credentials, identifiers, endpoints, or unsanctioned operations.


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