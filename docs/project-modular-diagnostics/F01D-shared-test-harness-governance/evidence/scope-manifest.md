# F01D Scope Manifest

Status: COMPLETE
Module: F01D - Shared Test Harness Governance
Mode: DIAGNOSIS_ONLY
Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
Branch: `1.0.0.1.EvenVersion`
Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Ownership Rule

The authoritative map assigns F01D the lifecycle of ChurchReport shared test
projects, test SDK and target-framework governance, shared fixtures/harness,
and `SanityTest.cs`. Individual test cases follow the subject they verify and
are read-only dependency evidence. Solution enrollment and CI scheduling remain
F01A responsibilities.

## Exact Owned Files

| Path | F01D responsibility |
|---|---|
| `ToolUtility.Tests/ToolUtility.Tests.csproj` | Test-container target, SDK/packages, coverage integration, and project-reference lifecycle |
| `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` | Shared ChurchReport test-container target, SDK/packages, references, and warnings |
| `ChurchReport.MemberInfo.Tests/SanityTest.cs` | Minimal test-runner sanity gate |

No current F01D-owned shared fixture, xUnit collection definition, assembly
parallelization policy, `.runsettings`, `Directory.Build.*`,
`Directory.Packages.*`, `global.json`, or `xunit.runner.json` file exists.

The following helper files are not F01D-owned fixtures because they are
ToolUtility/CRM-specific test content:

- `ToolUtility.Tests/TestHelpers/MockCrmClientFactory.cs`
- `ToolUtility.Tests/TestHelpers/MockLoggerFactory.cs`
- `ToolUtility.Tests/TestHelpers/TestEntityFactory.cs`

They remain read-only F03A evidence. RichMenu `Support/**`, payment fakes, LINE
handlers, and other test-local helpers likewise follow their tested subjects.

## Read-Only Dependencies

| Path | Owner | Diagnostic use |
|---|---|---|
| `ToolUtility/ToolUtility.csproj:4` | F03A | Tested project targets `net10.0` |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:3` | X01 | Shared ChurchReport test container references a `net10.0` host |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:9` | X01 | Host disables MSBuild project parallelism |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:90` | X01/F04 | Host project graph begins with LINE SDK |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:98` | X01/F07 | Host graph includes RichMenu |
| `SpeechMessageProducts.sln:16` | F01A | ChurchReport shared test project is enrolled |
| `SpeechMessageProducts.sln:6-40` | F01A | ToolUtility test project is absent from the enrolled project block |
| `.github/workflows/toolutility-tests.yml:26` | F01A | CI installs only .NET 8 for the ToolUtility test gate |
| `.github/workflows/toolutility-tests.yml:35` | F01A | CI invokes Coverlet collector coverage |
| `ToolUtility.Tests/obj/project.assets.json:2505` | generated corroboration only | Existing restore artifact records `NU1201` incompatibility |

Generated `obj/**` is not an owned source and was not modified. It is cited
only as corroboration of the source-declared target mismatch.

## Test Content And Consumers

Read-only inventory:

- `ToolUtility.Tests`: 20 C# files, 62 `[Fact]`, 2 `[Theory]`.
- `ChurchReport.MemberInfo.Tests`: 46 C# files, 162 `[Fact]`, 15 `[Theory]`.
- ChurchReport test content spans security, member access, payments, LINE
  shared workflows, and host/platform checks.
- The map assigns those test cases to B01, B02, B05, B07, F09, X01, or X05Q;
  only `SanityTest.cs` belongs to F01D.

Consumers of F01D governance:

- F03A/F03B use the ToolUtility test container as their provider gate.
- B01/B02/B05/B07/F09/X01/X05Q place subject-owned tests in the ChurchReport
  shared container.
- F01A consumes the project lifecycle decision for solution enrollment and CI.
- Contributors and branch protection consume stable test commands and results.

## Gate State

- F01D diagnostic gate: `READY`.
- Quarantine: false.
- `ToolUtility.Tests` provider gate: `BLOCKED`.
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:4` targets `net8.0`.
  - `ToolUtility.Tests/ToolUtility.Tests.csproj:39` references ToolUtility.
  - `ToolUtility/ToolUtility.csproj:4` targets only `net10.0`.
  - The project is not enrolled in `SpeechMessageProducts.sln`.
- `ChurchReport.MemberInfo.Tests` enrollment: present.
- ChurchReport runtime baseline: not executed in this diagnosis because
  restore/build/test would write generated files outside the permitted scope.

## Explicit Exclusions

- Individual business/product test behavior and assertions.
- Solution enrollment, workflow triggers, and CI branch-protection policy.
- Product `.csproj`, runtime configuration, source, controllers, services, and
  libraries.
- `ChurchReport.Tests/PerformanceTests/CollectionQueryServiceAsyncTests.cs`,
  which belongs to F03A and has no test project.
- Other diagnostic workspaces, parent task files, existing CCG artifacts, and
  all generated output.

## Manifest Self-Check

- All three F01D-owned tracked files are listed.
- Shared-fixture absence was checked rather than inferred from naming.
- Product test cases were used only as dependency and scale evidence.
- F01A solution/CI concerns are handoffs, not absorbed findings.
- Nested agent count: 0.
