# F05B Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Current Responsibility Shape

F05B is physically small but compositionally broad. Its project references:

- F04 SDK;
- F05A processor;
- F06 notification/reply workflows;
- F07 RichMenu engine.

`AddLineMessagingProcessor` then registers all four layers in one call
(`LineMessagingProcessor.AspNetCore.csproj:14-17`,
`LineMessagingProcessorServiceCollectionExtensions.cs:53-68`).

The adapter therefore owns five distinct concerns:

1. credential and endpoint options;
2. `IHttpClientFactory` transport construction;
3. concrete F05A processor construction;
4. F06 notification/reply workflow selection;
5. F07 cache/state/policy/orchestrator/provisioning registration.

This is retained as F05B-EXT-001 because the host cannot opt into or replace
these capabilities independently through one stable public seam.

## Override And Idempotency Contract

Core path:

- `AddTransient` is used for client, processor, notification, and reply;
- calling the extension twice appends duplicate descriptors;
- direct resolution follows last-registration-wins;
- `IEnumerable<T>` exposes all descriptors;
- pre-registered custom services are not preserved.

RichMenu path:

- most services use `TryAdd*`;
- policy uses `TryAddEnumerable`;
- trigger options do not use the options pipeline;
- a later configured call removes all earlier trigger-options instances and
  installs a new singleton
  (`LineMessagingProcessorServiceCollectionExtensions.cs:88-106`).

The two halves therefore expose inconsistent extension semantics.

The subject test demonstrates the absence of a first-class override seam by
calling `RemoveAll<ILineRichMenuProcessor>` and then installing a fake
(`LineMessagingProcessorServiceCollectionExtensionsTests.cs:74-77`).

## Clean Seam Target

A clean F05B boundary should separate:

### Transport And Processor

Input:

- validated credential/endpoint options;
- `IHttpClientFactory` or typed-client builder;
- narrow F05A capability interfaces.

Output:

- one scoped transport/processor lease;
- explicit compatibility concrete registration during migration.

### Notification And Reply

Input:

- narrow send/reply capability.

Output:

- F06 interfaces only.

### RichMenu Basics

Input:

- narrow RichMenu transport capability;
- cache/state implementations;
- additive trigger/policy options.

Output:

- F07 workflow/orchestrator interfaces.

### Product Provisioning

Input:

- product `ILineRichMenuCatalog`.

Output:

- provisioning workflow registration without re-registering unrelated
  notification/reply services.

## Dependency Direction

Current:

```text
F05B -> concrete F04
F05B -> concrete F05A
F05B -> concrete F06
F05B -> concrete F07
X01 -> one F05B bundle
```

Target:

```text
F04 capability registration
  -> F05A capability adapters
     -> optional F06 workflow registration
     -> optional F07 workflow registration
        -> optional product catalog registration
X01 composes only required capabilities
```

F05B may remain the convenience package, but the convenience extension should
delegate to granular, idempotent extensions rather than being the only public
composition contract.

## Test Seam Review

Current subject tests verify type resolution but not public registration
contracts:

- no repeated-call test;
- no pre-registered custom service test;
- no additive options test;
- no independent capability test;
- no descriptor/lifetime identity test;
- no invalid-options startup test;
- fake replacement requires implementation-aware `RemoveAll`.

Recommended contract tests should inspect descriptors and resolve services
through controlled scopes without invoking LINE.

## Cross-Module Ownership

- F04 owns request serialization, HTTP behavior, and low-level endpoint
  normalization.
- F05A owns narrow processor capability contracts and mutable compatibility
  cleanup.
- F06 owns notification/reply result logic.
- F07 owns RichMenu cache/state behavior and workflow contracts.
- F05B owns registration names, options validation, lifetimes, override policy,
  and composition convenience.
- X01 owns which capabilities the product host enables.

## Rejected Extraction Candidates

- Move F06/F07 logic into F05B: rejected; F05B should register, not own logic.
- Make F05A processor singleton now: rejected; public mutable fields make that
  unsafe.
- Treat the test project package versions as an F05B extraction defect:
  rejected; shared test governance belongs to F01D.
- Move ChurchReport catalog into F05B: rejected; product catalog remains B07.
