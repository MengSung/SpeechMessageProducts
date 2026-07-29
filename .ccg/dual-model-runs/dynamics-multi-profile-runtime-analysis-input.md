# Dynamics crm82/crm91 multi-profile runtime analysis

Analyze the next implementation milestone against:

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`

Current state:

- The Gateway/WebApi DI graph is single-profile through one `DynamicsWebApiOptions`.
- `DynamicsHttpTransport`, `DynamicsWebApiClient`, `AdfsOAuthTokenProvider`,
  `ControlledOperationExecutor`, and `OrganizationAdmissionManager` are
  singleton/scoped around that one options instance.
- Capacity keys, admission plans, runtime-host leases, deterministic shutdown,
  and several isolation/soak tests already exist and must be reused, not
  rewritten.
- ProductClient now pins a request to its deployment-configured alias and uses
  bounded response streaming.
- The next milestone must support immutable `crm82` and `crm91` runtime
  generations, alias routing, validated replacement publication,
  active-plus-one-draining ownership, deterministic drain/disposal, and shared
  organization admission when profiles target the same physical organization.
- No product traffic is enabled. Embedded remains deferred. Data8 does not enter
  this in-process runtime and remains a future isolated worker.

Inspect at least:

- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/CapacityKeys.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionPlan.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionManager.cs`
- `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs`
- `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`
- `SpeechMessage.Dynamics.Gateway/Program.cs`
- `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`

Produce an implementation-oriented architecture analysis with:

1. Exact new types/interfaces and their ownership responsibilities.
2. Exact existing files that should be minimally modified.
3. A safe compatibility strategy so current single-profile tests and host
   startup keep working while the multi-profile manager is introduced.
4. The immutable generation key fields and where secrets/version fingerprints
   may and may not appear.
5. How aliases map to profiles without a request supplying a physical endpoint,
   credential, version, or transport.
6. How two aliases/generations share one admission manager when their validated
   canonical physical-organization key is equal, without sharing credentials,
   tokens, clients, handlers, metadata, retries, or mutable session state.
7. Atomic replacement steps, maximum active-plus-one-draining enforcement,
   rapid-update coalescing/rejection, in-flight lease ownership, and disposal
   order.
8. Tests that must be written first and their expected RED cause.
9. File ownership decomposition suitable for parallel `ccg-implement` agents
   with no overlapping writable files.
10. Critical lifecycle, race, memory-retention, session-isolation, and
    performance risks in the current code that the plan must explicitly avoid.

Keep the milestone incremental and executable. Do not design the CE 8.2 Data8
worker, WinRM provisioning, ChurchReport migration, or Phase 6 deletion here.
Return Critical / Warning / Recommendation sections and a proposed task order.
