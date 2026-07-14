# F01D Shared Test Harness Governance Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F01D
Workspace: F01D-shared-test-harness-governance
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Issue document SHA-256: d19bf05b41de20573b0bc39d0a6dea2f421ee111e2f9ce05161b2c26369a54fc

## Executive Summary

Four confirmed F01D issues survived source reopening. The ToolUtility provider
gate is structurally unusable because its test project targets `net8.0`,
references a `net10.0`-only provider, and is outside the solution. The shared
ChurchReport container suppresses all `NU1605` downgrade errors and combines
177 facts/theories from multiple owners behind a full web-host project graph.
ToolUtility also restores both Coverlet integrations while its documented and
CI contract uses only the collector.

No secret/environment leak, mutable shared fixture, or parallel identity leak
was confirmed. Individual business-test defects remain outside F01D.

## Ranked Confirmed Issues

### F01D-EXT-001 The ToolUtility Test Container Cannot Form An Executable Provider Gate

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 24
- Likelihood/frequency score: 15
- Security urgency score: 2
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01D
- Cross-module: F01A enrollment/CI; F03A/F03B test content
- Gate blocked: true
- Files:
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:4`
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:39`
  - `ToolUtility/ToolUtility.csproj:4`
  - `SpeechMessageProducts.sln:16`
  - `.github/workflows/toolutility-tests.yml:26`
- Evidence: The test project targets `net8.0` and references a provider that
  targets only `net10.0`. Existing `project.assets.json:2505-2506` corroborates
  this with `NU1201`. The project is absent from the solution, while the tracked
  workflow installs .NET 8 and selects it directly.
- Control/data/lifetime flow: ToolUtility change -> F01A workflow -> F01D test
  project restore/build -> incompatible provider reference -> no green test or
  coverage evidence.
- Impact: F03A/F03B lack an executable provider baseline and cannot satisfy the
  approved optimization gate. Solution validation also cannot represent the
  test-container lifecycle.
- Why this is necessary: F01D owns target/SDK lifecycle; F01A cannot schedule a
  trustworthy gate until the container is compatible.
- Recommended action: Align the test target with the supported ToolUtility
  target, establish a clean canonical command, then hand solution enrollment
  and workflow SDK changes to F01A.
- Validation: Clean-clone restore/build/test under the approved .NET 10 SDK,
  with no `NU1201`, followed by a green owner-aware CI gate.
- Rollback boundary: F01D target/package, F01A enrollment/workflow, and
  F03A/F03B test-content commits remain separate.
- Extraction contract: compatible test container -> ToolUtility provider ->
  F03A/F03B tests -> reproducible result -> F01A required check.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01D-PERF-001 Every ChurchReport Module Gate Uses One Host-Coupled Test Assembly

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 19
- Impact score: 18
- Likelihood/frequency score: 15
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: F01D
- Cross-module: F01A plus B01/B02/B05/B07/F09/X01/X05Q test owners
- Gate blocked: false
- Files:
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:21`
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:25`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:9`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:90`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:98`
- Evidence: One test project compiles 46 C# files containing 162 facts and 15
  theories for multiple owners. It references the full web host plus four
  extracted projects; the host references nine product projects and disables
  project parallelism. No stable trait/runsettings/module-gate contract exists.
- Control/data/lifetime flow: owner test request -> one shared test project ->
  complete host/project graph build -> one mixed-owner test assembly discovery
  -> filtered or full execution.
- Impact: Small owner gates cannot isolate compile/discovery from unrelated
  host and test failures, and they cannot have independent cache, lifecycle, or
  rollback. Exact time savings remain an implementation acceptance metric.
- Why this is necessary: The approved module program requires independently
  executable provider/consumer gates; a shared physical container is not an
  independent gate contract.
- Recommended action: Define reusable F01D test props/harness and empty focused
  project contracts. Actual subject-test migration must occur through separate
  B01/B02/B05/B07/F09/X01/X05Q owner tasks. Retain deliberate cross-module E2E
  coverage separately.
- Validation: Compare three clean restore/build/discovery samples and prove a
  focused gate reduces graph/time by at least 20% without losing consumer
  coverage.
- Rollback boundary: Add focused gates without deleting the shared container;
  migrate one owner at a time through separate commits.
- Extraction contract: shared F01D harness -> owner test project -> declared
  product references -> owner result; integration container remains separate.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01D-SEC-001 The Shared Test Container Suppresses Every Package Downgrade Error

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 20
- Impact score: 16
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: S
- Primary owner: F01D
- Cross-module: X01/X04B and package-edge owners
- Gate blocked: false
- Files:
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:8`
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:9`
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:21`
  - `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:25`
- Evidence: The project explicitly suppresses `NU1605` for the whole shared
  container, across the host and four direct project families.
- Control/data/lifetime flow: dependency downgrade -> NuGet `NU1605` -> blanket
  `NoWarn` -> shared test gate emits no downgrade failure.
- Impact: A package-integrity conflict can be absent from test evidence. No
  specific vulnerable package or exploit is claimed.
- Why this is necessary: Shared test evidence must preserve dependency safety
  signals; broad suppression makes every future downgrade indistinguishable
  from an approved exception.
- Recommended action: Reconcile the dependency graph and remove the blanket
  suppression; if temporary, scope and document the exact package edge with an
  expiry and negative gate.
- Validation: Clean restore/build with no unexplained `NU1605`, plus a fixture
  proving a new downgrade fails.
- Rollback boundary: Remove only the test-project suppression after package
  owners resolve the edge; product package changes remain separate.
- Extraction contract: package graph -> downgrade validation -> shared test
  result -> owner-approved exception registry.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01D-PERF-002 ToolUtility Restores An Unused Second Coverage Integration

- Category: Performance
- Severity: Low
- Priority: P3
- Priority score: 47
- Confirmed: true
- Evidence confidence: 20
- Impact score: 5
- Likelihood/frequency score: 12
- Security urgency score: 0
- Performance gain score: 2
- Loop leverage score: 3
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: F01D
- Cross-module: F01A coverage workflow
- Gate blocked: false
- Files:
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:27`
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:31`
  - `.github/workflows/toolutility-tests.yml:35`
  - `ToolUtility.Tests/README.md:59`
- Evidence: The test project references both `coverlet.collector` and
  `coverlet.msbuild`, while tracked commands use only the collector's
  `XPlat Code Coverage` contract. No MSBuild coverage property or consumer was
  found.
- Control/data/lifetime flow: every restore/build -> resolve/evaluate two
  coverage packages -> collector-only test command.
- Impact: Small deterministic package/build-asset overhead and an ambiguous
  second coverage path. Double instrumentation is not claimed.
- Why this is necessary: A shared gate should expose one documented coverage
  mechanism unless a second consumer is explicit.
- Recommended action: Remove `coverlet.msbuild` or document and test its exact
  consumer.
- Validation: Collector coverage output remains equivalent after removal.
- Rollback boundary: One package-reference change.
- Extraction contract: test execution -> collector -> one deterministic result
  layout -> F01A report/upload.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

## Runtime Validation Pending

None. Future acceptance measurements are documented in
`evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- Secret/environment leakage from shared fixtures: rejected; no fixtures,
  environment mutation, user-secrets provider, or mutable shared state found.
- Host configuration copied to test output: rejected as confirmed; content is
  publish-only and no test output exists. X04A owns committed secret values.
- Test SDK 17.8.0 is incompatible with net10.0: rejected pending a controlled
  clean discovery baseline.
- Centralize all test project package versions immediately: rejected as a
  current defect because all eight visible test projects are version-aligned;
  retain as a reusable harness candidate with owner-specific adoption.
- Tautological sanity test: rejected as a defect; it is a minimal
  discovery/assertion smoke, not a product behavior gate.
- Product-specific test helpers are F01D fixtures: rejected by tested-subject
  ownership.

## Cross-Module Handoffs

1. F01A: enroll or explicitly classify the repaired ToolUtility test project,
   select the compatible SDK, and schedule provider/consumer gates.
2. F03A/F03B: own ToolUtility test behavior after F01D repairs the container.
3. X01/X04B and package owners: resolve actual ChurchReport dependency
   downgrade edges.
4. B01/B02/B05/B07/F09/X01/X05Q: migrate subject tests only through separate
   owner tasks if focused gate projects are approved.
5. F04-F09 test-project owners: opt into future shared test props without
   transferring individual test ownership.

## Final CCG Approval

Substantive issue verdict: `APPROVED_DEGRADED`.

- Submitted Round 1 SHA-256:
  `3988C0BE91F6ABF0DE0BA1458E76B3D02DB1E4B67357230036113ACBB12E3199`.
- Run ID: `20260710-194722-f01d-issue-review-r1-reviewer`.
- Summary:
  `.ccg/dual-model-runs/20260710-194722-f01d-issue-review-r1-reviewer/summary.json`.
- Claude reopened every original source, returned KEEP for all four issues,
  restored no rejected candidate, reported no write side effects, and returned
  module verdict `APPROVE`.
- Gemini produced no usable output because provider quota/billing returned
  HTTP 403 insufficient balance.
- The summary has `degradedFallback=true`, `fallbackAccepted=true`,
  `quotaBlocked=true`, `completedBackends=["claude"]`, and
  `failedBackends=["gemini"]`.
- The completed backend reported no unresolved Critical or Warning.
- A non-blocking Info suggestion clarified that F01D provides harness/project
  contracts while each subject owner migrates its own test files. No issue
  evidence, score, severity, verdict, or ownership changed.
- Retained: 4. Deleted: 0. Runtime pending: 0. Cross-module handoff groups: 5.
