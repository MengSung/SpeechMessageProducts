# P7.2 Slice C baseline-owner precondition analysis

## Scope

Review the current worktree and the active Trellis task
`.trellis/tasks/08-07-churchreport-write-action-function-migrations`.

Slice C uses the fixed `sunnyvalechback` / `crm91` / CE 9.1 / Data8 profile.
The current fresh-fixture provisioner returned the sanitized no-go category
`baseline-owner-unavailable`. Root-cause evidence is that the existing,
descriptor-bound, task-marked target leader is owned by the same active
`systemuser` as the Data8 `WhoAmI` subject. The provisioner correctly stops
before any ledger persistence or CRM mutation for this branch.

The operator explicitly authorizes one new independent Slice C cycle only if
the full precondition is proven:

1. an existing task-marked leader is proven to have an active `systemuser`
   owner that is not the Data8 `WhoAmI` user;
2. then run exactly one `ProvisionFreshFixture -> graph validation -> Slice C
   evidence -> CleanupFreshFixture` cycle;
3. otherwise return no-go without retrying or mutating CRM.

## Non-negotiable boundaries

- Do not automatically scan/select a substitute `systemuser`, accept a
  caller-provided owner, or weaken Assign to a self-assignment.
- Do not retry a prior no-go; do not run `ExecuteFixture`, flip feature flags,
  switch traffic/connector/profile, start CE 8.2, Official Worker, or
  Slices D-H.
- Keep descriptor, identity, credential, temporary-file, child-process, and
  Data8 lease state isolated and bounded. Never suggest leaking identifiers,
  credentials, endpoints, raw CRM payloads, or browser state into evidence.
- Treat the visible CE UI `0x80044150 SQL Server error` during a task-marker
  list search only as a UI-query failure, not as an owner-selection result.

## Requested output

Return a concise Critical / Warning / Info report that answers:

1. whether the existing code and proposed decision gate preserve the stated
   isolation, cleanup, and no-retry invariants;
2. the exact minimum authoritative evidence required before the fresh cycle;
3. whether any source change is justified before running the cycle;
4. any release-blocking defect that must stop the cycle.

Do not make repository or CE changes.
