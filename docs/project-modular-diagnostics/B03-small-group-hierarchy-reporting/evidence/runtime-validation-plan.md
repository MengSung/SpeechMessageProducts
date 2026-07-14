# B03 Runtime Validation Plan

Status: VALIDATION_PLAN_ONLY
Module: B03
Mode: DIAGNOSIS_ONLY

No runtime validation was executed. The user prohibited restore/build/test,
package restore, codegen, formatting, migrations, generated files, bin/obj,
caches, lockfiles, and test outputs. The module map also marks B03 as
gate-blocked because it has no directly attributable executable test suite.

## B03-SEC-001 SaveIntegrate Mutation Boundary

Future validation after approval:

- Use a fake-auth/session test host and fake CRM service.
- POST `/SmallGroup/SaveIntegrate` without anti-forgery token and assert the
  mutation is rejected.
- POST with a session/contact that does not own the active list and assert CRM
  write calls are not made.
- Simulate background upload failure and assert the UI receives durable failure
  status rather than immediate success.
- Simulate concurrent saves for different active lists and assert no stale-list
  mutation.

## B03-SEC-002 SpiritLeader Lookup

Future validation after approval:

- Seed fake CRM lists and contacts for two users.
- Call `api/SpiritLeaderLookup/Get?id=<other-user-list>` under the first user's
  session.
- Assert denial or empty result before member/contact retrieval.
- Assert permitted-list lookup still returns expected qualified names.

## B03-PERF-001 Weekly Report CRM Call Shape

Future validation after approval:

- Wrap CRM/ToolUtility access with a fake or counting adapter.
- Run representative cases such as 1 list x 10 members, 10 lists x 20 members,
  and 50 lists x 30 members.
- Record retrieve/create/update/assign counts, elapsed time, thread-pool usage,
  and allocations.
- After batching, assert call counts scale by query group rather than by nested
  list/member loops.

## B03-EXT-001 Context Extraction

Future validation after approval:

- Create provider tests for a narrow B03 session-state interface and weekly-report
  service contract.
- Create consumer tests for B04/B06/B07/X02A paths that currently reach through
  `InMemoryDataContextSmallGroup`.
- Verify B03 route/view smoke with fakes.
- Keep compatibility properties until all consumers are migrated and covered.
