# Dynamics Cross-Process Capacity and Fencing Design

## Goal

Prove that two independently running .NET processes use the real LocalDB-backed
`SqlRuntimeHostSlotCoordinator` and `OrganizationAdmissionManager` as one
bounded Dynamics organization capacity domain.  The proof must fail closed on a
lost fence, worker crash, and coordinator outage, while leaving no child
process, SQL operation, lease, permit, timer, or test namespace behind.

This is a Phase 4 local-control-plane gate.  It does not claim an authenticated
CE 8.2/9.1 smoke result, change D365APP01, or enable Phase 5 or Phase 6.

## Chosen approach

Create a test-only console project named
`SpeechMessage.Dynamics.SqlCoordinatorTestWorker`.  A live xUnit parent test
starts two or three copies of that executable.  Each worker creates the actual
SQL coordinator and admission manager, but has its own process-local runtime,
semaphores, renewal task, cancellation sources, and permit ownership.

The parent and workers share only generated, non-secret identifiers:

| Value | Origin | Boundary |
| --- | --- | --- |
| Lease namespace | parent-generated GUID suffix | one test run; 128-character bounded |
| Canonical organization ID and invalid test URI | parent-generated | non-routable test identity |
| Worker host ID and protocol nonce | parent-generated | one child only |
| Capacity, TTL, fence and quarantine values | worker-owned constants | fixed and bounded |

The worker never accepts a SQL connection string, credential, token, endpoint,
or command-line configuration.  It reconstructs only the fixed opt-in LocalDB
target `(localdb)\\MSSQLLocalDB` / `SpeechMessageDynamicsControlPlane` with
integrated security.  The parent must separately have the existing explicit
live-test environment guard; it removes that environment variable and all
non-essential inherited variables before starting children.

## Worker protocol and lifecycle

The protocol is newline-delimited, fixed-format text with a maximum line length.
It has no JSON, generic commands, or reflected exception text.  Parent commands
are `ACQUIRE_HOST`, `ACQUIRE_WORK`, `BEGIN_DRAIN`, `RELEASE_WORK`,
`AWAIT_DRAIN`, `STOP`, and the test-only `OUTAGE_PROBE`.  Worker events are
nonce-bound fixed records such as `READY`, `HOST_READY`, `HOST_DENIED`,
`WORK_HELD`, `LEASE_LOST`, `DRAINED`, and `OUTAGE_CLEAN`.

Both stdout and stderr are drained concurrently with hard byte caps.  Every
reader, writer, cancellation registration, permit, manager, coordinator
operation, process handle, and protocol task has a deterministic `finally`/
`await using` owner.  A timeout first requests graceful drain; only then may the
parent kill the child process tree, wait for exit, and preserve the namespace
rather than erase evidence of an incomplete drain.

## Live assertions

1. **Shared capacity and graceful drain.**  With aggregate capacity two and a
   two-host maximum, two workers obtain one permit each.  A third worker remains
   NotReady and cannot obtain work.  A draining worker retains its host slot
   while its permit is active.  After release plus SQL quarantine, the third
   worker can acquire a replacement slot with a newer fencing token.
2. **Fence loss.**  A narrowly scoped, parameterized test fencer changes only
   the generated namespace's current SQL fencing row.  The affected worker's
   actual renewal loop observes the CAS failure, cancels its held permit's
   `LeaseLostToken`, becomes terminal NotReady, rejects later work, drains, and
   cannot be reused.  A replacement waits for the SQL TTL/quarantine sequence.
3. **Crash.**  A worker holding a slot is deliberately terminated without
   graceful release.  A different worker remains denied until SQL-server TTL
   expiry and quarantine have elapsed, then obtains a replacement lease.  The
   parent closes every process handle and deletes rows only after all surviving
   children exit.
4. **Coordinator outage.**  A child uses the real coordinator against the
   existing unreachable test endpoint.  Admission fails closed and reports zero
   active database operations before exit.

The test uses SQL Server time for lease persistence and does not claim proof of
host clock-skew behavior.  It also does not claim a full production Gateway or
Embedded workload smoke; those remain separate Phase 4 gates.

## Alternatives rejected

- Reusing the xUnit assembly as an executable mixes test discovery and worker
  hosting in one binary, broadens inherited test-host state, and weakens the
  process boundary.
- Starting actual Gateway and Embedded web hosts would add unrelated HTTP,
  authentication, configuration, and runtime concerns without a real target
  smoke environment.  The coordinator/admission layer is the precise local
  contract under test.
- A shared in-process helper cannot prove static, semaphore, timer, process,
  or connection-pool isolation.

## Verification

The new facts remain opt-in LocalDB tests.  They run only with the existing
explicit test environment variable, verify child exit and SQL operation
sentinels, and clean only their generated namespace in foreign-key order.
Focused live tests, the full Dynamics test project, Release build, and
`git diff --check` are required before this increment is committed.
