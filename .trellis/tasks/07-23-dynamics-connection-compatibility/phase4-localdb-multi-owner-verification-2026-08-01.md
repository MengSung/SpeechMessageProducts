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

## Executed verification

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

## What this does not prove

This is a real SQL durability and lifecycle check, but both owners run inside
one xUnit OS process. It does not replace the required true Gateway-plus-
Embedded multi-process test, coordinator-outage/lease-loss fault proof, or the
hosted-process soak and performance baseline. It also does not advance the
blocked CE 8.2/9.1 real-server smoke matrix.

`Package01FeeReadsEnabled` remains `false`; Phase 5 consumer migration and
Phase 6 SDK removal remain locked.
