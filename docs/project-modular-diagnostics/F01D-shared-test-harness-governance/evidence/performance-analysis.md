# F01D Performance Analysis

Status: COMPLETE
Module: F01D - Shared Test Harness Governance
Mode: DIAGNOSIS_ONLY

## Cost Model

F01D performance concerns are test restore, project-graph build, test assembly
discovery, coverage instrumentation, fixture lifetime, and the ability to run
focused provider/consumer gates. No product request-path performance claim is
made.

## Confirmed Finding: F01D-PERF-001

### Every ChurchReport Module Gate Uses One Host-Coupled Test Assembly

Evidence:

- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:21`
  references the complete ChurchReport web host.
- Lines 22-25 also directly reference workflow, payment host/workflow, and
  RichMenu projects.
- The host itself references nine product projects at
  `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:90-98`.
- The host sets `<BuildInParallel>false</BuildInParallel>` at line 9.
- Static inventory found 46 C# files with 162 facts and 15 theories in one test
  assembly across security, member, payment, LINE, and host/platform owners.
- Neither F01D-owned project contains traits, runsettings, collection
  definitions, or another stable module-gate classification contract.

Cost flow:

1. A module owner selects `ChurchReport.MemberInfo.Tests.csproj`.
2. The SDK compiles the entire test project and resolves the full host project
   graph before tests can run.
3. The test adapter loads/discovers one assembly containing all owner groups.
4. Name filtering can reduce execution, but it does not create a smaller
   project graph or independently versioned provider gate.

Impact:

- Small B01/B02/B05/B07/F09/X01 checks are coupled to the same broad compile
  and discovery unit.
- A host or unrelated test compile failure can block every subject owner.
- The structure prevents owner-scoped gate caching, lifecycle, and rollback.
- Exact wall-clock savings require later measurement; the broad graph and
  shared compilation unit are statically confirmed.

Recommended action:

- Define a reusable F01D test-harness contract for test SDK, assertions,
  fake/environment isolation, and result layout.
- Move subject-owned tests into focused owner test projects or equivalent
  independently buildable gate modules.
- Keep cross-module E2E tests in a deliberate integration container.
- Coordinate project enrollment and consumer scheduling with F01A.

## Confirmed Finding: F01D-PERF-002

### ToolUtility Restores Two Coverlet Integrations But Uses Only The Collector Contract

Evidence:

- `ToolUtility.Tests/ToolUtility.Tests.csproj:27-30` references
  `coverlet.collector`.
- Lines 31-34 also reference `coverlet.msbuild`.
- The tracked CI invokes only `--collect:"XPlat Code Coverage"` at
  `.github/workflows/toolutility-tests.yml:35`.
- The project README documents the same collector invocation at
  `ToolUtility.Tests/README.md:59`.
- No `CollectCoverage`, `CoverletOutput`, or `/p:CollectCoverage=true` consumer
  was found.

Cost flow:

1. Restore resolves and records both packages.
2. Build evaluates both packages' build assets.
3. The actual coverage command selects the collector path only.

Impact:

- The extra package adds deterministic restore/build graph work and a second,
  undocumented coverage mechanism.
- The cost is small and no double instrumentation is claimed.

Recommended action:

- Retain the collector used by CI and remove `coverlet.msbuild`, unless an
  explicit owner and command for the MSBuild integration is documented.

## Rejected Performance Candidates

### Test SDK 17.8.0 Is Proven Incompatible With net10.0

Rejected as confirmed. All eight visible test projects use the same test SDK
and xUnit versions, but this diagnosis cannot run restore/build/test. Age alone
does not prove failed discovery. A controlled clean-clone baseline should
decide whether an SDK upgrade is required.

### Missing Fixture Reuse Causes Expensive Reinitialization

Rejected. No shared fixtures exist, but test cases predominantly use small
in-memory fakes. No repeated external setup, database, network server, or
measured initialization cost was found in F01D-owned harness code.

### SanityTest Is A Material Runtime Cost

Rejected. `ChurchReport.MemberInfo.Tests/SanityTest.cs:21-24` contains one
constant assertion. Its cost is negligible and it provides a minimal
discovery/assertion smoke signal.
