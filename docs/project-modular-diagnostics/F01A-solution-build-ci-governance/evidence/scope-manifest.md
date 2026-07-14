# F01A Scope Manifest

Status: COMPLETE
Module: F01A - Solution, Build, and CI Governance
Mode: DIAGNOSIS_ONLY
Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
Branch: `1.0.0.1.EvenVersion`
Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Ownership Rule

The module map makes F01A the unique Primary Owner for the solution, root build
and Git rules, CI, project enrollment, build matrix, and canonical-project
decisions. Product project files are read-only evidence. F01A does not own the
product code or project-file fixes needed after a governance decision.

## Exact Owned Files

`git ls-files` returned the following complete F01A set:

| Path | Diagnostic use |
|---|---|
| `SpeechMessageProducts.sln` | Solution enrollment, canonical project selection, configurations, and build mappings |
| `.editorconfig` | Root text-format policy |
| `.gitattributes` | Git text and line-ending policy |
| `.gitignore` | Git exclusion and generated-output policy |
| `.github/ASP.NET.Core.NET.10.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-architecture.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-concurrency.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-dynamics365.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-efcore.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-instructions.md` | GitHub Copilot entry instruction; content handoff boundary with F01B |
| `.github/copilot-performance.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-pr-review.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/copilot-security.md` | GitHub-hosted Copilot guidance; content handoff boundary with F01B |
| `.github/workflows/toolutility-tests.yml` | The only tracked GitHub Actions workflow |

No tracked root `Directory.Build.*`, `Directory.Packages.*`, `global.json`,
root `NuGet.config`, `.props`, or `.targets` file exists. Therefore there is no
additional otherwise-unowned root build metadata in the current tree.

## Read-Only Enrollment And Canonical Evidence

The repository has 26 visible `.csproj` files. `dotnet sln
SpeechMessageProducts.sln list` reported 18 enrolled projects. The eight
non-enrolled project definitions are:

- `Line.Messaging/Line.Messaging_Net10.csproj`
- `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj`
- `LinePayCSharp/LinePayCSharp.csproj`
- `PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj`
- `ToolUtility.Tests/ToolUtility.Tests.csproj`
- `Trace/Trace.csproj`
- `Trace/Trace_Fixed.csproj`
- `Trace/Trace_Net10.csproj`

The following project files were reopened only to determine enrollment, target
matrix, direct project edges, or canonical-project divergence:

- `ToolUtility.Tests/ToolUtility.Tests.csproj:1-48`
- `ToolUtility/ToolUtility.csproj:1-61`
- `Line.Messaging/Line.Messaging.csproj:1-56`
- `Line.Messaging/Line.Messaging_Net10.csproj:1-56`
- `LineMessagingProcessor/LineMessagingProcessor.csproj:1-50`
- `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj:1-28`
- all remaining `.csproj` files through an XML-only target-framework and
  `ProjectReference` inventory

Binary `.snk` files were inspected only for tracked-file classification. The
inspection was limited to file length, the first 16 bytes, and SHA-256; private
key bodies were not printed.

## Explicit Exclusions

- F01B: `.agents/**`, `.ccg/**`, `.claude/**`, `.codex/**`, `.gemini/**`,
  `.opencode/**`, `.serena/**`, `.trellis/**`, and agent-workflow policy.
- F01C: root `README.md`, `AGENTS.md`, `docs/**`, `tools/**`, `scratch/**`,
  `openspec/**`, images, and history artifacts.
- F01D: shared test project lifecycle, test SDK versions, target frameworks,
  fixtures, and sanity gates.
- X04B: package-source trust, publish scripts, launch settings, and deployment.
- X04A: runtime configuration and secret injection.
- Product modules: all `.cs`, `.csproj`, package, runtime, test-content, and
  business behavior changes outside F01A.
- Other diagnostic workspaces and the active `.trellis`/`.ccg` task records.

## Dependencies And Consumers

F01A has no runtime dependency. It consumes project metadata and provides
governance to every project owner.

| Direction | Modules | Contract |
|---|---|---|
| Read-only dependency | F01D | Test-container enrollment and executable target framework |
| Read-only dependency | F02, F04, F05A, F08, X02Q | Canonical project and retained/retired project decisions |
| Read-only dependency | X04B | Trusted package source and deployment validation |
| Read-only dependency | F01B | Copilot instruction content and generated CCG artifact policy |
| Consumer | All F/B/X leaves | Solution enrollment, build visibility, and CI gate scheduling |
| Consumer | Contributors and branch protection | Required checks, reproducible build commands, and artifact evidence |

## Existing Tests And Gates

Static/read-only checks completed:

- `dotnet sln SpeechMessageProducts.sln list`: parsed successfully; 18 projects.
- XML parse of all 26 `.csproj` files: completed; target frameworks and direct
  references inventoried.
- `git ls-files .github/workflows/**`: one tracked workflow.
- `git ls-files *.snk` and `git check-ignore --no-index`: three tracked,
  non-ignored `.snk` files.
- Solution parse: 15 solution configuration/platform entries and 270
  `Build.0` mappings for 18 projects.
- `dotnet --version`: `10.0.301`.

No restore, build, or test command was run because those commands create or
update `obj`, `bin`, coverage, or tool artifacts outside this agent's write
scope. The corresponding clean-clone commands are specified in
`runtime-validation-plan.md`.

## Gate And Quarantine State

- Diagnostic gate: `READY`.
- Optimization authorization: not granted.
- F01A quarantine: false.
- Current repository CI baseline: not trustworthy; see F01A-EXT-001 and
  F01A-EXT-002.

## Cross-Module Handoffs

1. F01D: align `ToolUtility.Tests` with the `net10.0` tested project and define
   its test-container lifecycle.
2. F02/F04: supply provider gates when F01A adds dependency-triggered consumer
   CI; F04 also decides the duplicate `Line.Messaging` project lifecycle.
3. F05A: decide whether `LineMessagingProcessor_Net10.csproj` is retained,
   migrated, or removed.
4. F08/X02Q/F02: classify and rotate/retire tracked strong-name key material in
   their owned project families after F01A establishes repository prevention.
5. X04B: define trusted NuGet sources and provenance for executable CI tools.
6. F01B: own the semantic accuracy of `.github/copilot-*.md`,
   `.github/ASP.NET.Core.NET.10.md`, and `.github/copilot-instructions.md`, and
   decide retention/redaction rules for `.ccg/dual-model-runs/**`.

## Manifest Self-Check

- Every F01A-owned tracked path is listed.
- Product files are read-only evidence only.
- F01B/F01C/F01D/X04B responsibilities were not absorbed.
- No quarantine or product-code issue was invented.
