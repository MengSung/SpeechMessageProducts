# F01D Security Analysis

Status: COMPLETE
Module: F01D - Shared Test Harness Governance
Mode: DIAGNOSIS_ONLY

## Method

The review traced test-container package/error policy, environment access,
shared mutable state, fixture lifetime, test-output content, and host project
references. Individual product assertions were not diagnosed.

Searches across both F01D-owned containers found:

- no `Environment.SetEnvironmentVariable` or environment-variable provider;
- no `UserSecrets` use;
- no xUnit class/collection fixture or assembly parallelization policy;
- no current `bin/**` output for either project;
- no mutable static test fixture state; the static matches were immutable
  constants or factory methods.

## Confirmed Finding: F01D-SEC-001

### The Shared ChurchReport Test Container Suppresses Every NU1605 Downgrade Error

Evidence:

- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:8`
  describes allowing package downgrade diagnostics.
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:9`
  sets `<NoWarn>NU1605</NoWarn>` for the entire project.
- The shared container references the web host and four extracted project
  families at lines 21-25, so the suppression applies across a broad dependency
  graph and all subject-owned tests compiled in the assembly.

Source/control/sink flow:

1. A direct or transitive package graph resolves a lower version than another
   dependency requires.
2. NuGet emits `NU1605`, normally an error-level downgrade signal.
3. Project-wide `NoWarn` suppresses the signal for every restore/build of the
   shared test container.
4. The resulting test result can be treated as dependency evidence without
   exposing the downgrade conflict.

Impact boundary:

- This finding does not prove a currently exploitable package or a specific
  vulnerable downgrade.
- It confirms loss of a dependency-integrity signal in the shared gate.
- The main host's package choices are read-only X01/X04B/product-owner
  dependencies; F01D owns only the blanket suppression in the test container.

Existing guards and counter-evidence:

- The suppression is limited to one test project, not the whole repository.
- Package versions are explicit in project files.
- No current test output was generated or inspected.
- These facts do not make a blanket downgrade suppression selective or
  auditable.

Recommended control:

- Remove the project-wide suppression after the underlying dependency graph is
  reconciled.
- If a temporary exception is unavoidable, scope it to the exact package edge
  through a documented owner-approved mechanism and add a gate that fails on
  any new downgrade.

## Rejected Security Candidates

### Test Fixtures Leak Secrets Or Environment State

Rejected. No shared fixture, environment mutation, user-secret provider, or
mutable static test state was found in the F01D-owned containers. Product tests
mostly construct in-memory configuration and fake tokens; their business
correctness belongs to their tested modules.

### Referencing The Web Host Copies Production Secrets Into Test Output

Rejected as confirmed. The host project lists `appsettings.json`,
`appsettings.Production.json`, and `web.config` as publish content at
`SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:29-31`,
but no test `bin/**` exists and the item uses `CopyToPublishDirectory`, not an
explicit test-output copy directive. Any committed secret value is an X04A
finding; output-copy behavior would require controlled runtime validation.

### Parallel Test Execution Currently Causes Cross-User Leakage

Rejected. No fixture/collection policy exists, but no shared mutable identity,
environment, or credential state was found in the test containers. Missing
parallelization policy is a future harness design concern, not a confirmed leak.

## Cross-Module Handoffs

- X04A: independently diagnose committed configuration/secret values.
- X01/X04B and package owners: reconcile any actual downgrade edge before
  F01D removes a temporary exception.
- Business test owners: preserve fake-token and in-memory configuration
  isolation in their own test cases.
