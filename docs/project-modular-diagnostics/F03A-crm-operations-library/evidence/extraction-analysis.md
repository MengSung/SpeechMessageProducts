# F03A Extraction Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Confirmed Candidate

### F03A-EXT-001 Establish A Typed CRM Operations Composition Boundary

F03A already contains cohesive service pairs:

- CRUD/query: `EntityOperations/**`, `QueryOperations/**`,
  `CollectionOperations/**`.
- Attribute access: `AttributeOperations/**`.
- Attachments: `AttachmentOperations/**`.
- Contacts: `ContactOperations/**`.
- Lists: `ListOperations/**`.
- Connection/client adapters: `ConnectionOperations/**`, `Adapters/**`,
  `Interfaces/**`.

The boundary is not currently consumable. DI at
`ServiceCollectionExtensions.cs:32-35` registers only
`IToolUtilityProvider`. `ToolUtilityProvider.cs:30-33` returns the static
factory instance, and `ToolUtilityFactory.cs:27-30`, `:50-95` owns global
construction. F03A compatibility partials delegate through the excluded F03Q
mixed facade:

- CRUD: `ToolUtilityClass.Entity.cs:28-42`, `:82-86`, `:198-199`.
- Query: `ToolUtilityClass.Query1.cs:27-123`.
- Attachment: `ToolUtilityClass.ActivityAttachment.cs:90-114`.
- List: `ToolUtilityClass.List.cs:29-173`.

The project remains physically coupled to F03B through
`ToolUtility.csproj:52`, while `ToolUtility.csproj:53` references the F02
Dataverse client.

## Proposed Contract

Inputs:

- CRM entity logical name and ID.
- Explicit projection/query specification.
- Narrow create/update command or SDK entity where compatibility requires it.
- Attachment stream/metadata with size and content policy.
- List/member IDs and batch options.
- Cancellation token for native async paths.

Outputs:

- Narrow entity/result DTOs or explicitly projected SDK entities.
- Operation result with partial-failure information.
- No authentication password field, hidden static state, or LINE dependency.

Dependencies:

- F03A typed services depend on an F02-owned CRM client abstraction.
- X04A supplies validated connection options.
- B01/B modules authorize and define use-case projections.
- F03Q depends on F03A as a compatibility adapter, not the reverse.

Test seam:

- Fake CRM client captures query shape and requested columns.
- Contract fixtures cover CRUD errors, paging, cancellation, batching,
  attachment limits, and authentication result minimization.
- Consumer compile/host tests follow the map matrix after gate repair.

Consumers:

- B01-B06C ChurchReport modules.
- X02A cache and X02C profiling integration.
- F03Q compatibility facade during migration.

## Migration Shape

1. Introduce typed registrations without removing existing APIs.
2. Move connection options/client creation behind explicit injected
   dependencies.
3. Route F03Q compatibility methods to typed F03A interfaces.
4. Migrate consumers by business owner and projection.
5. Split F03B build content/project only after compatibility gates exist.
6. Retire static factory paths after all consumers and host disposal are
   proved.

This avoids a circular dependency: F02 -> F03A -> consumers, with F03Q/F03B as
adapters or separate providers.

## Rejected Extraction Candidates

- Move every ToolUtility file into a new project immediately: rejected as a
  big-bang move without repaired gates.
- Treat F03Q facade as F03A-owned: rejected by explicit map exception.
- Extract attachment service alone: it is already cohesive; the missing work
  is composition, projection, and authorization contract.
- Extract connection pool as a new leaf: insufficient independent consumer and
  benefit evidence; keep it behind the F03A/F02 client boundary.
- Duplicate typed services into ChurchReport: rejected because F03A is the
  shared contract owner.

## Gate And Rollback

No implementation may start while the `net8.0` test container references the
`net10.0` library and remains outside the solution. Each future interface,
registration, facade adapter, and consumer migration must be independently
reversible and carry its provider/consumer checks.
