# Central Package Management

## 1. Scope / Trigger

Use this contract when adding or changing an actual program project or package.
It includes projects outside the solution and existing orphan projects. The user
approved excluding `docs/` reference snapshots and `.ccg/tasks/` task archives.

## 2. Signatures

```powershell
powershell.exe -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
powershell.exe -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1 -RepositoryPath <worktree-root>
dotnet restore SpeechMessageProducts.sln
dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605
```

## 3. Contracts

- `Directory.Packages.props` owns active package versions. Program projects use
  versionless `PackageReference` items; the guard checks attributes and children.
- All `Microsoft.Extensions.*` versions are `10.0.0`; Newtonsoft.Json is `13.0.4`.
  Other active versions stay unchanged unless a later plan explicitly changes them.
- Inherited CPM is explicitly false beneath the two snapshot roots. Their historic
  inline versions remain untouched. Normalize directory boundaries before testing
  prefixes so a project in `docs-like/` remains a program project.
- Parse XML nodes, not source lines. Commented ASP.NET Core 2.2 dependencies are
  disabled and must not become active central package entries.
- Preserve `PrivateAssets`, `IncludeAssets`, conditions, and unrelated warnings,
  including the existing `System.Text.Encoding.CodePages` `NoWarn="NU1510"`.
- No credentials or network access are needed for the guard. Git enumeration is
  bounded to 30 seconds and its process/redirected streams are disposed per run.

## 4. Validation & Error Matrix

| Condition | Result |
| --- | --- |
| Active PackageReference has Version attribute/child | Exit 1, InlineVersion |
| Active NoWarn element/attribute contains NU1605 | Exit 1, SuppressedNU1605 |
| Only XML comments contain these constructs | Ignore comments |
| A project is beneath either snapshot root | Exclude from guard; CPM false |
| No program projects or invalid project XML | Exit 1; no empty success |
| Legacy CP950 project comments | Read-only strict CP950 fallback; encoding remains owned by the encoding gate |
| NU1605/NU1608 during restore | Gate fails; never add NoWarn to make it pass |

## 5. Good / Base / Bad Cases

- Good: `ToolUtility.Tests` stays outside the solution during P0-03 but uses CPM.
- Base: a new tool project uses a versionless package reference already in the table.
- Bad: grepping XML comments invents active legacy dependencies, or a solution-only
  scan overlooks an actual project outside the solution.

## 6. Tests Required

Run the guard before and after migration and retain the real failure diagnostics.
Verify evaluated MSBuild properties: actual project CPM true, both snapshots false.
Restore without NU1605/NU1608, build without increasing baseline warnings, run the
complete Release suite and the Debug FileToolUtilityTracerTests contract.
Compare package metadata and excluded snapshot bytes to their pre-change values.

## 7. Wrong vs Correct

Wrong: `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />` in a
program project, or suppressing NU1605 after dependency changes.

Correct: `<PackageReference Include="Newtonsoft.Json" />`, with version `13.0.4`
in the central table, while leaving the two excluded snapshots unchanged.

## Reparse-point boundary (review correction 2026-11-04)

Validate the repository prefix and each existing path component before every
project, central-table or fixed security-reference XML read. Reject reparse
points, including junctions, with UnsafePath before decoding. Exclude other
worktrees/reference solutions and bin/obj at any depth; actual orphan projects
and docs-like prefixes remain checked. Test-PackageVersions.Tests.ps1 uses a
tracked path whose parent is replaced with a real junction and proves outside
content stays unchanged and unlocked. Run on PowerShell 5.1 and 7.
