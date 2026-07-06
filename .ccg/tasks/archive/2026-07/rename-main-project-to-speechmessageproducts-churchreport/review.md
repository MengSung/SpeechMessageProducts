# Review: rename main project to SpeechMessageProducts.ChurchReport

## Scope
- Renamed the tracked main web project folder from `ChurchReport/` to `SpeechMessageProducts.ChurchReport/`.
- Renamed the main project file to `SpeechMessageProducts.ChurchReport.csproj`.
- Updated `SpeechMessageProducts.sln` to reference the renamed project path and display name.
- Updated `ChurchReport.MemberInfo.Tests` project reference to the renamed project.
- Removed stale untracked root `ChurchReport.sln` that still pointed to `ChurchReport\ChurchReport.csproj`.

## Verification
- `git ls-files -- ChurchReport` returned no tracked files under the old project folder.
- Recursive `.sln` / `.csproj` search found no stale `ChurchReport\ChurchReport.csproj` references.
- `SpeechMessageProducts.sln` points to `SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj`.
- Main project identity includes `<AssemblyName>SpeechMessageProducts.ChurchReport</AssemblyName>`.
- `<RootNamespace>ChurchReport</RootNamespace>` was intentionally preserved to avoid a risky namespace-wide rename in this phase.
- `dotnet build .\SpeechMessageProducts.sln --no-restore` passed with 0 warnings and 0 errors.

## External Review
- Dual-model external analysis/review was attempted through the project runner, but both providers were unavailable in this run.
- Local verification and build checks passed.
