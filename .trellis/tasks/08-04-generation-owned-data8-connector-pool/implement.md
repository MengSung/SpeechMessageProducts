# P3 Data8 Connector Pool Implementation Plan

## Task 1: SDK-free contracts and project boundary

- [x] Add `ConnectorOperation`, `ConnectorOperationResult`, `IConnectorClient`, `IConnectorLease`, `IConnectorPool`, and `IConnectorRouter` under `SpeechMessage.Dynamics.Abstractions/Connectors`.
- [ ] Add `SpeechMessage.Dynamics.Connectors.Data8` targeting net10 and reference only approved host contracts plus the existing ControlPlane admission contracts.

## Task 2: Red tests

- [ ] Add tests for healthy return, fault eviction, cancellation/timeout permit release, drain rejection, cross-profile isolation, shared Organization admission, and idempotent disposal.
- [ ] Run the focused tests and observe the expected missing-type or behavior failures before implementing the pool.

## Task 3: Minimal pool implementation

- [ ] Implement immutable generation identity, bounded local slots, lazy client creation, and lease ownership.
- [ ] Reuse `IOrganizationAdmissionManager.AcquireAsync(DispatchEnvelope, CancellationToken)` and release the returned permit exactly once.
- [ ] Implement faulted lease eviction, drain, disposal aggregation, and rollback on factory failure.

## Task 4: Router and lifecycle tests

- [ ] Implement Data8-only routing with fail-closed rejection for Official Worker kinds.
- [ ] Add a repeated acquire/release soak test that asserts active clients, permits, and handles return to baseline.

## Validation

- [ ] `dotnet test SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Data8ConnectorPoolTests`
- [ ] `dotnet test SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj --no-restore`
- [ ] `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore`
- [ ] Verify modified C# files are UTF-8 without BOM, CRLF-only, and end with CRLF.
