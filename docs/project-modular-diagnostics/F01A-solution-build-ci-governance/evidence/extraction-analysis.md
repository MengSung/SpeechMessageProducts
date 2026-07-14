# F01A Extraction Analysis

Status: COMPLETE
Module: F01A - Solution, Build, and CI Governance
Mode: DIAGNOSIS_ONLY

## Extraction Lens

For F01A, extraction means creating enforceable build and CI boundaries, not
moving product code. A valid boundary needs a provider/consumer contract,
deterministic project enrollment, an executable gate, and an owner-specific
rollback.

## Confirmed Finding: F01A-EXT-001

### The Declared ToolUtility Gate Cannot Establish A Green Baseline

Owning files and read-only evidence:

- `.github/workflows/toolutility-tests.yml:23-35` installs .NET 8 and restores,
  builds, and tests `ToolUtility.Tests/ToolUtility.Tests.csproj`.
- `ToolUtility.Tests/ToolUtility.Tests.csproj:4` targets `net8.0`.
- `ToolUtility.Tests/ToolUtility.Tests.csproj:37-40` references ToolUtility.
- `ToolUtility/ToolUtility.csproj:4` targets `net10.0`.
- `SpeechMessageProducts.sln:6-40` enrolls ToolUtility but not
  `ToolUtility.Tests`; `dotnet sln ... list` confirms the omission.

Contract:

- Input: a ToolUtility or ToolUtility.Tests change matching the workflow path
  filter.
- Gate: restore, Release build, xUnit test, coverage generation, upload, and
  threshold.
- Consumer: branch protection and maintainers deciding whether ToolUtility is
  safe to merge.

Failure:

A `net8.0` test project cannot reference a `net10.0`-only project as a compatible
target. The test-container project is also outside the solution's canonical
enrollment. The workflow therefore does not provide a solution-reproducible,
green ToolUtility gate.

Ownership and seam:

- F01A owns workflow selection and solution enrollment.
- F01D owns the test-container target framework and test SDK lifecycle.
- F03A/F03B own test content and ToolUtility product behavior.

Recommended boundary:

1. F01D establishes a compatible test target and clean green command.
2. F01A enrolls the repaired test container or records an explicit reason for
   a project-specific CI-only gate.
3. F01A makes the provider and consumer commands first-class required checks.

Rollback:

Revert only the solution enrollment/workflow commit; the F01D target change is
a separate owner commit.

## Confirmed Finding: F01A-EXT-002

### Tracked CI Does Not Enforce The Solution Or Provider/Consumer Matrix

Evidence:

- The solution enrolls 18 projects at `SpeechMessageProducts.sln:6-40`.
- `git ls-files .github/workflows/**` returns only
  `.github/workflows/toolutility-tests.yml`.
- Push filters at `.github/workflows/toolutility-tests.yml:4-8` and pull-request
  filters at lines 9-13 include only `ToolUtility/**` and
  `ToolUtility.Tests/**`.
- ToolUtility directly references Line.Messaging and Dataverse at
  `ToolUtility/ToolUtility.csproj:51-54`.
- Changes to `SpeechMessageProducts.sln`, `Line.Messaging/**`, or
  `PowerPlatform.Dataverse.Client/**` do not match the only workflow.

Contract gap:

- Provider changes in F02/F04 can alter ToolUtility's compile/runtime contract
  without scheduling the ToolUtility consumer test.
- The other enrolled projects and tests have no tracked solution-wide CI gate.
- Enrollment and build-matrix changes to the solution itself have no tracked CI
  validation.

Extraction value:

An owner-aware provider/consumer workflow matrix would allow each module to
change independently while still compiling its required consumers. This
directly implements the module map's section 9.4 contract and unlocks later
module optimization.

Recommended boundary:

- Add a minimal solution build gate for solution and root-governance changes.
- Add provider path triggers and explicit consumer commands rather than one
  ToolUtility-only workflow.
- Keep product test commands in their owning modules; F01A only schedules and
  composes them.

Rollback:

Each provider/consumer gate is a separate workflow commit that can be removed
without reverting product code.

## Confirmed Finding: F01A-EXT-003

### Canonical Project Alternatives Are Unmanaged And Divergent

Evidence:

- The solution selects `Line.Messaging/Line.Messaging.csproj` and
  `LineMessagingProcessor/LineMessagingProcessor.csproj` at
  `SpeechMessageProducts.sln:8-11`.
- `Line.Messaging/Line.Messaging_Net10.csproj:1-56` is content-identical to the
  selected `Line.Messaging.csproj` after normalizing the canonical file's
  UTF-8 BOM. Their raw bytes and SHA-256 values differ because only the
  canonical file begins with `EF BB BF`.
- `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj:13-25` includes
  package references and all new folders, but it omits the canonical project's
  configuration package set and project reference at
  `LineMessagingProcessor/LineMessagingProcessor.csproj:37-48`.
- The repository contains eight non-enrolled `.csproj` files and no root build
  registry, manifest, or CI guard that records retain/migrate/retire decisions.

Contract and consumers:

- Input: developer, script, package job, or external consumer chooses a
  `.csproj` path.
- Output: different compile graph and potentially different assembly contents
  for the same project/assembly name.
- Current solution consumers receive only the selected canonical definitions.
- No active consumer of the alternate definitions was proved.

Why confirmed:

The governance defect is not that an alternate build is known to run in
production. The confirmed defect is that F01A has multiple same-target project
entry points, including one divergent definition, without a recorded lifecycle
decision or enforcement mechanism.

Recommended boundary:

1. Record one canonical path and status for every non-enrolled project.
2. Have F04/F05A remove, migrate, or explicitly retain their alternate project
   files.
3. Add a read-only CI check that rejects unregistered `.csproj` additions or
   canonical drift.

Rollback:

The F01A registry/check can be reverted independently. Product-file removal or
migration remains separate owner work.

## Rejected Extraction Candidates

### Move GitHub Copilot Instructions Into F01B

Rejected as an F01A extraction issue. The map explicitly assigns
`.github/**` to F01A, while the user prohibits absorbing F01B agent workflow.
The correct action is a cross-module content handoff to F01B, not a unilateral
path move or ownership rewrite.

### Extract The Solution Configuration Matrix Immediately

Rejected pending consumer proof. Fifteen configuration/platform entries and 270
build mappings are present at `SpeechMessageProducts.sln:43-600`, but no active
consumer or timing evidence proves which legacy names can be removed.

### Enroll Every Visible Project

Rejected. Non-enrollment can be an intentional quarantine or retirement state.
F01A-EXT-003 requires explicit lifecycle decisions, not indiscriminate solution
growth.

## Cross-Module Handoffs

- F01D: compatible ToolUtility test-container target and green baseline.
- F02/F04: provider gate commands for ToolUtility consumers.
- F04/F05A: duplicate project lifecycle decisions.
- F08/X02Q/F02: explicit retain/retire decisions for other non-enrolled project
  families.
- F01B: semantic maintenance of GitHub Copilot instruction content.
