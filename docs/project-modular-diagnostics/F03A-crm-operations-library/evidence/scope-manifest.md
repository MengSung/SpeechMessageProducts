# F03A Scope Manifest

Status: COMPLETE
Module: F03A - CRM Operations Library
Mode: DIAGNOSIS_ONLY
Authoritative map:
`docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Ownership Rule

F03A owns `ToolUtility/**` by default, including
`ToolUtility/ToolUtility.csproj` lifecycle, except the higher-priority F03B and
F03Q paths listed below. F03A also owns test-case content that tests F03A
subjects, while F01D owns `ToolUtility.Tests/ToolUtility.Tests.csproj`.

## Primary Owner Inventory

Read-only enumeration found 108 files under the F03A ToolUtility rule after the
four explicit source exclusions. The inventory includes executable source,
project metadata, historical in-project documentation, and five pre-existing
`obj/**` files; generated files were observed but never written.

| Path group | Files |
|---|---:|
| Root/project files | 4 |
| CRUD/query/contact/collection/list/attachment/activity operations | 29 |
| Attribute operations | 12 |
| Connection, adapters, interfaces, factories, DI | 21 |
| Remaining CRM operations, constants, diagnostics, extensions, utilities | 21 |
| F03A partials and allowed core metadata/contracts | 5 |
| In-project historical documentation | 16 |
| Total | 108 |

Owned test content:

- 18 `ToolUtility.Tests/**/*.cs` files after excluding F03B LINE tests and the
  F03Q facade integration test.
- `ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs`,
  which has no executable test project.

## Explicit Exclusions

- `ToolUtility/LineMessaging/**` - F03B.
- `ToolUtility/PushUtility.cs` - F03B.
- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs` - F03B.
- `ToolUtility/Core/ToolUtilityFacade.cs` - F03Q.
- `ToolUtility.Tests/LineMessaging/**` - F03B.
- `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs` - F03Q.
- `ToolUtility.Tests/ToolUtility.Tests.csproj` lifecycle - F01D.
- ChurchReport business rules, controllers, session, authorization, and UI -
  B01-B07/X owners.

## Read-Only Dependencies

- F02: Dataverse/on-premise CRM clients and SDK transport.
- F03Q: mixed facade used by current compatibility partials.
- F03B/F04: LINE project reference and LINE adapter, excluded from F03A logic.
- X04A: runtime secret configuration.
- X02A/X02C: cache infrastructure and future profiling.
- X01: host composition and DI.

## Consumers

The module map declares B01-B07 and X02A-X02C as CRM API consumers. Concrete
read-only traces include:

- Authentication and identity lookups in
  `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:610`
  and multiple `WebServiceConnector` classes.
- Donation contact query in
  `SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs:230`.
- DI registration in `SpeechMessageProducts.ChurchReport/Startup.cs:429`.

## Gate And Quarantine

- Gate: BLOCKED.
- `ToolUtility/ToolUtility.csproj:4` targets `net10.0`.
- `ToolUtility.Tests/ToolUtility.Tests.csproj:4` targets `net8.0` and references
  ToolUtility at line 39.
- The test project is not enrolled in the solution.
- `ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs`
  has no test project and leaves its service fields uninitialized at lines
  36-41.
- No restore, build, test, generation, or formatting was run.
- F03A is not quarantined, but optimization cannot begin until F01A/F01D repair
  the provider and consumer gates.

## Write Boundary

Allowed writes for this assignment:

- `docs/project-modular-diagnostics/F03A-crm-operations-library/**`
- newly generated `.ccg/dual-model-runs/**` artifacts with F03A prefix

All source, project, config, tests, maps, workflows, Trellis tasks, CCG tasks,
and other module workspaces are read-only.
