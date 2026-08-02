# Phase 4 LocalDB multi-owner capacity verification — 2026-08-01

## Scope

This evidence verifies the durable-control-plane portion of Phase 4 without
contacting a Dynamics server. It is limited to the fixed Development LocalDB
instance and the dedicated `SpeechMessageDynamicsControlPlane` database. It
does not connect to a CRM database, use a CRM credential, issue an HTTP request,
or enable consumer traffic.

## Controlled environment

- Provisioner target: `(localdb)\MSSQLLocalDB` only.
- Database target: `SpeechMessageDynamicsControlPlane` only.
- Authentication: the current Windows user's integrated authentication.
- Provisioning result: schema verified; the optional drained-epoch recovery was
  not requested and removed zero rows.
- Test process: the opt-in SQL connection setting existed only for the test
  process and was cleared afterwards.

## Earlier same-process verification

```text
dotnet test SpeechMessage.Dynamics.Tests --filter SqlRuntimeHostSlotCoordinatorTests --no-restore --nologo

Passed: 16
Failed: 0
Skipped: 0
Duration: 3 seconds
```

The added `Live_sql_multi_owner_managers_share_durable_capacity_and_drain`
case constructs two separate `SqlRuntimeHostSlotCoordinator` instances and two
separate `OrganizationAdmissionManager` instances against one generated durable
lease namespace. It proves all of the following:

1. Both independent owners become Ready only by obtaining the two durable host
   slots in that shared namespace.
2. A third host slot is rejected.
3. Exactly two aggregate work permits are admitted; additional work is rejected
   rather than queued or allowed to exceed the organization budget.
4. Every acquired permit and manager is awaited during cleanup, only the
   generated test namespace is removed, and both coordinator operation counters
   return to zero.

## Cross-process extension — verified 2026-08-01

The separate opt-in cross-process suite launches only the already-built,
test-only worker executable. The parent and every child use a generated
non-secret namespace and a fixed protocol; the child environment is explicitly
scrubbed of the live test selector and Dynamics/CRM/credential-shaped variables.
No CRM endpoint, CRM credential, browser session, token, or request data crosses
this boundary.

```text
dotnet test SpeechMessage.Dynamics.Tests --filter CrossProcessSqlRuntimeHostSlotCoordinatorTests --no-restore

Passed: 6
Failed: 0
Skipped: 0
Duration: 48 seconds
```

The six facts prove the following bounded behaviours against the real LocalDB
durable coordinator:

1. The parent accepts only nonce-bound, fixed-format worker protocol events.
2. A separately running worker returns the required nonce-bound `READY` event.
3. Independent OS workers share aggregate host/work capacity; a graceful drain
   retains its slot until its held permit releases and quarantine completes.
4. A parameterized, generated-namespace fencing mutation makes a real lease
   renewal fail; the worker emits `LEASE_LOST` and rejects later work.
5. A forcibly terminated host cannot be replaced until the durable lease TTL
   expires and the quarantine period completes.
6. A fixed local coordinator outage leaves its operation counter at zero,
   drains/releases the original durable slot, and makes later host/work
   admission fail closed.

The test finally paths use a fresh bounded cleanup token for their own generated
namespace. After the fresh run, no test worker or Dynamics test host process
remained and the invoking process no longer had the opt-in SQL test environment
variable. The full Dynamics test project also passed 313/313 tests, and the
Release solution build completed with zero warnings and zero errors.

## What this still does not prove

This is now a real SQL durability and lifecycle check across independent OS
processes. It does not replace a true Gateway-plus-Embedded deployment proof,
the hosted-process soak/performance baseline, or the blocked CE 8.2/9.1
real-server smoke matrix.

`Package01FeeReadsEnabled` remains `false`; Phase 5 consumer migration and
Phase 6 SDK removal remain locked.
