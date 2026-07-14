# F01D Extraction Analysis

Status: COMPLETE
Module: F01D - Shared Test Harness Governance
Mode: DIAGNOSIS_ONLY

## Extraction Lens

F01D extraction means producing independently executable test gates and a small
reusable harness contract. It does not transfer ownership of business
assertions to F01D.

## Confirmed Finding: F01D-EXT-001

### The ToolUtility Test Container Cannot Form An Executable Provider Gate

Owning and dependency evidence:

- `ToolUtility.Tests/ToolUtility.Tests.csproj:4` targets `net8.0`.
- `ToolUtility.Tests/ToolUtility.Tests.csproj:39` references
  `ToolUtility/ToolUtility.csproj`.
- `ToolUtility/ToolUtility.csproj:4` targets only `net10.0`.
- Existing generated corroboration records `NU1201` at
  `ToolUtility.Tests/obj/project.assets.json:2505-2506`.
- `SpeechMessageProducts.sln:6-40` does not enroll ToolUtility.Tests.
- F01A's tracked workflow installs `.NET 8` and selects this project at
  `.github/workflows/toolutility-tests.yml:26-35`.

Contract:

- Input: F03A/F03B ToolUtility source or test change.
- Provider gate: restore, compile, xUnit execution, and coverage.
- Output: a green, reproducible result consumed by CI and later optimization.
- Current failure: the test target cannot consume the `net10.0`-only provider,
  and solution-based validation cannot see the test project.

Ownership:

- F01D owns the target framework, test SDK/package lifecycle, and clean
  provider-gate command.
- F01A owns solution enrollment, installed SDK, workflow scheduling, and
  required-check policy.
- F03A/F03B own test cases and product behavior.

Recommended boundary:

1. F01D aligns the test target with the supported ToolUtility target and
   records one canonical command.
2. F01A either enrolls the compatible test project or records an explicit
   CI-only lifecycle decision, then updates the SDK/workflow.
3. F03A/F03B validate their owned tests without changing the harness contract.

Rollback:

- F01D target/package change, F01A enrollment/workflow change, and product-test
  changes remain separate owner commits.

## Reusable Harness Candidate

The repository has eight test projects with the same
`Microsoft.NET.Test.Sdk 17.8.0`, `xunit 2.6.6`,
`xunit.runner.visualstudio 2.5.6`, and `FluentAssertions 6.12.0` declarations,
but no central test props or runsettings.

A future F01D harness can define:

- test SDK/framework compatibility policy;
- common analyzer/runner/private-assets settings;
- deterministic result and coverage layout;
- environment/secret isolation helpers;
- module trait/gate naming;
- shared fixture disposal and parallelization rules.

This is not retained as a separate confirmed issue because current package
versions are consistent. Consumer test project files remain owned by F04-F09
and other product modules, so adoption must be separate handoffs rather than an
F01D bulk rewrite.

## Rejected Extraction Candidates

### Move All Test Helpers Into F01D

Rejected. ToolUtility `TestHelpers/**`, RichMenu `Support/**`, and payment/LINE
fakes encode subject-specific types and follow the tested module.

### Make SanityTest A Product Behavior Suite

Rejected. The F01D-owned sanity file is correctly small. Product smoke and DI
validation should live in owner-scoped or integration gates, not accumulate in
the shared sanity file.

### Absorb Individual ChurchReport Tests Into F01D

Rejected by the map. Test ownership follows B01/B02/B05/B07/F09/X01/X05Q
subjects even when the `.cs` files compile in an F01D-governed container.

## Cross-Module Handoffs

1. F01A: ToolUtility test project enrollment, .NET SDK workflow selection, and
   provider/consumer scheduling.
2. F03A/F03B: rerun and own ToolUtility product tests after the container gate
   is compatible.
3. B01/B02/B05/B07/F09/X01/X05Q: move subject tests only through separately
   approved owner tasks if focused projects are created.
4. F04-F09 test-project lifecycle owners: opt into any future reusable F01D
   test props through owner-specific migrations.
