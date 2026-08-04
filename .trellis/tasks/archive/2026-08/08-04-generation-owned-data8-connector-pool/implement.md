# P3 Data8 Connector Pool Implementation Plan

## Task 1: SDK-free contracts and project boundary

- [x] Add `ConnectorOperation`, `ConnectorOperationResult`, `IConnectorClient`, `IConnectorLease`, `IConnectorPool`, and `IConnectorRouter` under `SpeechMessage.Dynamics.Abstractions/Connectors`.
- [x] Add `SpeechMessage.Dynamics.Connectors.Data8` targeting net10 and reference only approved host contracts plus the existing ControlPlane admission contracts.

## Task 2: Red tests

- [x] Add tests for healthy return, fault eviction, cancellation/timeout permit release, drain rejection, cross-profile isolation, shared Organization admission, and idempotent disposal.
- [x] Run the focused tests and observe the expected missing-type or behavior failures before implementing the pool.

## Task 3: Minimal pool implementation

- [x] Implement immutable generation identity, bounded local slots, lazy client creation, and lease ownership.
- [x] Reuse `IOrganizationAdmissionManager.AcquireAsync(DispatchEnvelope, CancellationToken)` and release the returned permit exactly once.
- [x] Implement faulted lease eviction, drain, disposal aggregation, and rollback on factory failure.

## Task 4: Router and lifecycle tests

- [x] Implement Data8-only routing with fail-closed rejection for Official Worker kinds.
- [x] Add a repeated acquire/release soak test that asserts active clients, permits, and handles return to baseline.

## Validation

- [x] `dotnet test SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Data8ConnectorPoolTests` — 12 passed, 0 failed.
- [x] `dotnet test SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj --no-restore` — P3 tests passed; full suite recorded 430 passed, 1 failed, 7 skipped. The only failure is the pre-existing, timing-sensitive `OfficialWorkerSoakAndPerformanceTests.WorkerSoak_repeated_package01_recycle_returns_all_owners_to_zero_without_unbounded_trends` private-bytes trend check; it is outside P3 and a direct rerun passed. No threshold was changed.
- [x] `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore` — 0 warnings, 0 errors.
- [x] Verify modified C# files are UTF-8 without BOM, CRLF-only, and end with CRLF.
