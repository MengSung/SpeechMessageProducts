# CCG architect Task: p74-memberinfo-relation-goal-read-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00033 source-only architecture analysis

## Scope

Review the proposed source-only local design no-go for
`ORG-CALL-00033` (`memberinfo.connection.retrieve.relation.goals`).

Authoritative sources:

- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `.trellis/spec/backend/cross-user-isolation-and-performance.md`
- `.trellis/spec/backend/member-info-tree-contract.md`

## Current source facts

- `SearchDistrictTree`, `LoadGroupMembers`, and `LoadUngroupedMembers` call
  `BatchRelationGoals` only after MemberInfo `GetAccess`/
  `CanViewContactsBatch` flows.
- `GetAccess` accepts Session `_MemberInfoAccess`; when absent, it reads shared
  `InMemoryContext` and writes the result to Session.
- Shepherd contact scope can invoke `EnsureShepherdListsLoaded`, which calls
  `SetupListManager` using saved credentials from shared legacy ListManager.
- `BatchRelationGoals` uses a fixed `connection` query, but feeds every page
  through unbounded `RetrieveAllEntities`; it catches all exceptions and emits
  formatted empty relation text, losing the difference between unavailable,
  timeout/partial, and genuinely empty results.

## Requested review

Return Critical / Warning / Info findings on whether it is safe to create an
independent DTO-only Data8/ProductClient capability now. Verify all of:

1. authorization input is truly server-derived, immutable, request-local, and
   valid for both Church and Shepherd paths;
2. no Session/InMemoryContext/ListManager/saved-credential/shared service is
   trusted as Gateway authority;
3. page/row/text/response-byte bounds and partial/fault semantics are sufficient;
4. no Church-only partial migration can be presented as consumer completion;
5. source-only no-go recovery conditions are precise.

Constraints: do not recommend CE actions, consumer/gate/traffic enablement,
P7.5 removal, P8, mutation, or fallback/retry. Output concise, sanitized,
evidence-based text only.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.