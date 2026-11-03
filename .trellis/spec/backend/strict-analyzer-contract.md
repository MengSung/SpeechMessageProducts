# Strict Analyzer Contract

## 1. Scope / Trigger

New projects opt in with `SpeechMessageStrictCode=true` in their project body.
Legacy projects keep their existing settings. The user approved placing the
conditional configuration in `Directory.Build.targets`, because props evaluate
before the project-body opt-in exists.

## 2. Signatures

```powershell
powershell.exe -NoProfile -File tools/RepoQuality/Test-AnalyzerProbe.ps1
```

Optional parameters: `-RepositoryPath <worktree-root>` and
`-EvidenceDirectory <diagnostic-directory>`. Exit 0 means every contract passed;
the intentionally invalid probe builds themselves must fail.

## 3. Contracts

- Strict projects enable NET analyzers, Recommended analysis, nullable, latest
  language version, and warnings as errors in Directory.Build.targets.
- Explicitly default an empty AnalysisLevel to latest inside the strict group.
  The SDK otherwise leaves this empty for net48, so AnalysisMode=Recommended
  alone never loads that framework's Recommended rules. This was reproduced and
  repaired on 2026-11-03: before repair net48 naming violations compiled, while
  net10 rejected them. Check nonempty EffectiveAnalysisLevel for both targets
  and real net48 CA1707/CA1716 diagnostics, never merely the property label.
- The user approved one permanent naming exception on 2026-11-03: only projects
  with SpeechMessageStrictCode=true, IsTestProject=true, and an ordinal .Tests
  project-name suffix receive NoWarn CA1707. Directory.Build.targets is the only
  configuration source for that exception. Product projects and every other
  diagnostic remain unchanged; do not repeat the setting in test csproj files.
- Every strict csproj must set both SpeechMessageStrictCode=true and
  GenerateDocumentationFile=true in its project body. The SDK derives
  DocumentationFile and XML output copying before Directory.Build.targets is
  imported; setting only the flag there gives a false success. Never set SDK
  private properties such as _DocumentationFileProduced. The
  ValidateSpeechMessageDocumentation target fails with SMQ0001 before
  CoreCompile when a strict project has an empty DocumentationFile.
- `build/StrictCode.globalconfig` actually enables CA1307, CA1309, CA1310 and
  RS0030. WarningsAsErrors alone does not enable a disabled analyzer rule.
- BannedApiAnalyzers is a private development dependency. Root BannedSymbols
  blocks host-local time and process-default culture state. Plugin/Workflow
  project names additionally load `build/Plugin/BannedSymbols.txt` for GDI.
- Use property documentation IDs for CultureInfo default-culture properties.
  Accessor method IDs did not match real assignments in version 5.6.0. The
  property ban intentionally rejects both reads and writes of global defaults.
- IsExternalInit and NotNullWhenAttribute are linked only for net48. Their
  source files are shared; they introduce no state or runtime dependency.
- Probe projects never execute. Per-run plugin fixtures are deleted after their
  build process drains; the cleanup target must remain beneath the owned root.

## 4. Validation & Error Matrix

| Condition | Expected result |
| --- | --- |
| Default DateTime.Now probe, net10.0 or net48 | Only RS0030 errors |
| Today / ToLocalTime / TimeZoneInfo.Local / default culture assignment | RS0030 |
| Culture-sensitive comparison | CA1309 error |
| IndexOf without explicit comparison | CA1310 error |
| Ordinal Contains without explicit comparison overload | CA1307 error |
| Plugin namespace probe uses System.Drawing | RS0030 |
| Strict opt-in supplied only in project body | All strict properties enabled |
| Existing ToolUtility project without opt-in | Original settings preserved |
| Public member intentionally missing XML comments | CS1591 error |
| Strict project missing the project-body documentation setting | SMQ0001 before compilation |
| Documented positive build after deleting stale output XML | Successful build; output bin XML contains the documented type |
| Strict product has a public method with underscores | Only CA1707 errors |
| Strict test with IsTestProject=true and .Tests name uses underscores | Successful build |
| IsTestProject=true but project name has no .Tests suffix | Only CA1707 errors |
| .Tests suffix but IsTestProject=false | Only CA1707 errors |
| net48 strict product uses underscores | Only CA1707 errors |
| net48 public interface member is named Get | Only CA1716 errors |
| net48 or net10.0 effective analysis level | Both AnalysisLevel and EffectiveAnalysisLevel are nonempty |
| Restore, framework or unexpected compiler failure | Fail the guard; never count it as the expected rejection |

## 5. Good / Base / Bad Cases

- Good: the guard returns success because its intentional compile failures have
  the exact required diagnostics, and legacy property checks pass.
- Base: a new project without forbidden APIs builds under the strict settings.
- Bad: setting StrictCode on the command line masks a broken import order, or
  accepting any build failure mistakes missing net48 assets for analyzer proof.

## 6. Tests Required

Run Test-AnalyzerProbe's 24 checks, retaining all previous 20 checks and adding
two net48 naming probes plus the two effective-analysis-level checks.
Check the actual nonempty DocumentationFile and
the newly emitted XML in the output directory, rather than trusting only the
GenerateDocumentationFile flag. Preserve the initial missing-analyzer red
evidence and the final summary, then run the complete Release solution build,
Release tests, Debug FileToolUtilityTracerTests, and the package-version guard.
The probe is deliberately outside the solution and is not production code.

## 7. Wrong vs Correct

Wrong: a condition in Directory.Build.props reads a value first assigned later
in the csproj, or a net10-only project is built with a net48 override and its
NETSDK1005 failure is accepted as proof of the banned API rule.

Correct: evaluate the opt-in in Directory.Build.targets while setting the XML
documentation flag earlier in each csproj body. Declare both probe frameworks
and build each explicitly. Reject unexpected diagnostic codes and verify both
missing-comment rejection and a fresh successful XML output.
