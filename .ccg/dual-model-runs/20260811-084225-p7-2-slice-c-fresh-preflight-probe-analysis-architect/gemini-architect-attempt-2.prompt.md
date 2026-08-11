ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-2-slice-c-fresh-preflight-probe-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C fresh preflight diagnostic analysis

## Context

One explicitly authorized `-ProvisionFreshFixture -ReplaceStaleDescriptor -Json`
cycle stopped with the sanitized child category `fixture-precondition-failed`.
It did not run any Slice C capability. Existing UI evidence indicates the
task-marked leader is owned by an enabled, non-Data8 test user, and the
task-marked weekly report is active, points to the task-marked transfer target,
and has the expected Sunday date. The current provision result intentionally
hides which pre-mutation proof failed.

## Requested bounded capability

Design a new opt-in, **read-only** fresh preflight probe in the existing
`Invoke-Package02Data8ListManagementEvidence.ps1` flow. It must use only the
deployment-owned `crm91` / Data8 / CE 9.1 profile and the existing local
descriptor. It must never issue Create, Update, Delete, Assign, Execute,
Associate, Disassociate, feature-flag, traffic, cleanup, descriptor-publication,
or ledger-persistence actions.

It must safely classify (without exposing CRM IDs, record names, endpoint,
credential, token, cookie, raw CRM response, raw exception, or baseline value):

1. request/descriptor shape;
2. aggregate task-owned/static validity of the five operational lists;
3. task-marked leader provenance, owner logical kind, active state, and whether
   it differs from the verified Data8 WhoAmI user;
4. fixed transfer-target weekly-report cardinality and active/Sunday-date proof;
5. a final `go` only when every precondition is proven.

The parent contract must be strict UTF-8 no-BOM, CRLF-only/final-CRLF evidence,
with a bounded schema and a fixed deidentified vocabulary. Any transport or
projection uncertainty must fail closed. Tests must prove zero mutation calls
for every outcome and no cross-user/profile state retention.

## Required response

Give a concise design review covering:

- safest integration point and parameter-set boundary;
- recommended sanitized evidence schema and allowed values;
- abuse/leakage and lifecycle risks;
- concrete test cases and likely affected files;
- whether an existing repair/reconcile mode can be reused (and why/why not).


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
