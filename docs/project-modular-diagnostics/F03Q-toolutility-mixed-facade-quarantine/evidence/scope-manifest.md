# F03Q Scope Manifest

Status: COMPLETE
Mode: DIAGNOSIS_ONLY
Gate status: QUARANTINE

## Authoritative Ownership

The map defines F03Q as the ToolUtility mixed-facade quarantine:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:138`
  assigns files that simultaneously hold CRM and LINE state.
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:184-185`
  explicitly assigns `ToolUtility/Core/ToolUtilityFacade.cs`.
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:698`
  explicitly assigns
  `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs`.
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:815`
  states that F03Q has no stable contract.
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:880`
  limits F03Q to responsibility proof, split, handoff, or approved retirement.

## Primary Owner Files

1. `ToolUtility/Core/ToolUtilityFacade.cs`
   - Mutable CRM state: lines 53-56.
   - CRM service graph: lines 58-63 and 65-76.
   - LINE service state: line 64.
   - Mixed initialization: lines 137-158.
   - Connection mutation: lines 297-332.
   - LINE method: lines 526-529.
2. `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs`
   - Explicit F03Q test exception.
   - CRM facade tests: lines 29-89.
   - LINE facade test: lines 91-103.

## Map-Explicit Exceptions And Counter-Scope

- `ToolUtility/Core/ToolUtilityFacade.Metadata.cs` is a partial of the same
  class but contains only CRM metadata calls. The map exception names only
  `ToolUtilityFacade.cs`; therefore the metadata file remains F03A-owned.
- `ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs` is not the
  explicit F03Q test and falls through to F03A.
- `ToolUtility/LineMessaging/**`, `ToolUtility/PushUtility.cs`, and
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs` are F03B-owned.
- All other `ToolUtility/**` files default to F03A unless another explicit
  exception applies.
- `ToolUtility.Tests/ToolUtility.Tests.csproj` is F01D lifecycle ownership.

## Read-Only Dependencies

- F02:
  - `PowerPlatform.Dataverse.Client/**`
  - supplies CRM/Dataverse client behavior.
- F03A:
  - CRM services instantiated by `ToolUtilityFacade.InitializeServices`.
  - connection factory, query, CRUD, contact, list, metadata, attachment, and
    activity behavior.
- F03B:
  - `ILineMessageService` and `LineMessageService`.
  - legacy `ToolUtilityClass.Line.cs` and `PushUtility.cs` consumers.
- F04:
  - LINE HTTP/model contract used by F03B; F03Q does not own it.
- X04A:
  - runtime credentials and secret rotation.
- X01:
  - host DI and lifetime.
- F01D:
  - test container target framework and solution enrollment.

## Read-Only Consumers

- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:42` owns the facade
  field and constructs it at lines 87 and 99.
- CRM compatibility partials route broad method families through `_facade`.
- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:27-105` uses F03Q
  CRM methods to persist legacy LINE audit records.
- `ToolUtility/Factory/ToolUtilityFactory.cs:50-95` exposes one process-wide
  `ToolUtilityClass`.
- `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:32-35`
  registers the provider as singleton.
- ChurchReport controllers, services, models, and tools consume
  `IToolUtilityProvider` or `ToolUtilityFactory`; ownership remains with their
  B/X modules.
- `ToolUtility/PushUtility.cs:54-89` demonstrates LINE send paths that invoke
  legacy audit persistence before LINE transport.

## Tests And Gate

- F03Q has one explicit integration test file.
- The file passes logger first and `ICrmClient` second at lines 38, 61, 74, and
  97, while the only constructor requires `IOrganizationService` first at
  `ToolUtility/Core/ToolUtilityFacade.cs:83`.
- `MockCrmClientFactory.CreateMock` returns `Mock<ICrmClient>` at
  `ToolUtility.Tests/TestHelpers/MockCrmClientFactory.cs:30`.
- The test project targets `net8.0` and references a `net10.0` product project;
  the authoritative map records it outside the solution and gate-blocked.
- No restore/build/test command was run. The test issue is established by
  source-level type and ownership evidence.

## Quarantine Boundary

Allowed conclusions:

- prove mixed responsibilities;
- identify concrete security/lifetime/test risks;
- define owner-specific split contracts;
- hand off to F03A/F03B and supporting owners.

Prohibited conclusions:

- F03Q is a reusable shared layer;
- move all ToolUtility code in one change;
- optimize all facade methods together;
- modify product or test code during diagnosis;
- claim an executable green baseline.
