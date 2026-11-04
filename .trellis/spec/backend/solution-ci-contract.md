# Solution CI Contract

## Scope

P0-11 runs on Windows with the SDK from global.json and full Git history.
All builds and tests are offline with respect to D365 and LINE. Hosted Actions
are not executed by local verification and must never be reported as such.

## Commands and evidence

The authoritative command sequence is .github/workflows/solution-ci.yml.
Run every applicable run block locally before committing a change to that
sequence. Preserve sanitized summaries in the active WP evidence directory;
raw coverage, build logs, temporary scripts and DLLs belong under ignored
artifacts only. Never commit TRX or signing keys.

## Coverage gate

Test-Coverage.ps1 -ReportPath <coverage.cobertura.xml> -Module <assembly>
requires exactly one ordinal matching package and a finite invariant decimal
line-rate in [0,1], at least 0.8 without rounding. Missing reports, missing or
duplicate modules, malformed XML and external entities fail closed. The XML
reader is bounded to 32 MiB characters, has no resolver, and is disposed before
returning. The tool does not load product assemblies or contact any service.

Collect each new module and each target framework separately into a fresh
directory; require exactly one report there. Do not select the best result from
multiple runs or let a net10 report satisfy net48. The complete solution test
run remains mandatory; targeted reruns provide isolated coverage for new code.
Include I18nCheck.Tests and PluginPackaging.Tests, which are outside the solution.
Add new modules to the explicit coverage matrix as they enter future WPs.

## Signing

Ordinary Release builds receive no key and do not package. The workflow reads
PLUGIN_SNK_BASE64 only in its optional preparation step. With no secret, it writes
an explicit skipped summary and does not execute the package target. Do not create
a GitHub secret or request an upload without separate user authorization.

A configured secret is decoded only into runner temp; clear the byte buffer,
never print its content, and always delete the fixed temporary key in a separate
if: always() step. Only the signed packaging step sets the process variable
SPEECHMESSAGE_PLUGIN_KEYFILE and explicitly opts into SpeechMessagePackagePlugin.
Only coverage XML is uploaded, never runner temp, DLLs or keys.

If `PLUGIN_SNK_BASE64` is configured in the future, it must be a GitHub
Environment secret restricted to protected branches and requiring approval.
Never place it in repository secrets: this workflow runs on pushes to all
branches, so a repository secret would be available to any branch workflow.
Whether to create that Environment secret remains a user decision.

Offline build contract samples use -UseEphemeralTestKey explicitly. This mode
creates a disposable RSA fixture in a unique temp directory, never reads the
formal key contents, restores the caller environment, and deletes the fixture.
Without the switch, P0-10's formal process-environment requirement remains intact.

## Tests

Keep coverage rejection cases and both signed/unsigned metadata samples.
Run Pester 3.4.0 for the existing encoding and duplicate tests, the relocated
Test-P002Settings.ps1, progress rendering, CCG summary semantics, analyzer probes,
central package guard, Debug tracer, I18n verification and plugin fixture tests.
Do not treat zero discovered tests or an arbitrary failure as a successful guard.

## Six Labors general license (user approved 2026-11-03)

SIXLABORS_LICENSE is a required, user-configured repository secret for this
workflow. This specific authorization is separate from the future Environment
restriction on the Plugin signing key. Pass it only through the preparation
step environment, never by interpolating its value into run script text.
Missing or whitespace content fails with a clear message before building.

The preparation block creates a unique speechmessage-sixlabors-<guid>.lic in
RUNNER_TEMP with CreateNew and UTF-8 without BOM. Dispose the writer and stream
before publishing only its path through GITHUB_ENV as SixLaborsLicenseFile.
Clear the preparation process secret in finally; do not print its content or
write it to summaries, GITHUB_ENV, or artifacts. A preparation/publication fault
removes the file it created and emits a fixed diagnostic without raw messages.

A separate if: always() block deletes the published file after success or
failure. Validate the direct runner directory and workflow-owned filename
before deleting; never follow an arbitrary environment path or delete another
file. Cleanup is idempotent and clears the local process path. Only coverage
XML is uploaded. Local tests execute these actual workflow blocks with synthetic
license text, including missing secret, publication failure and unowned-path
rejection. They do not read or copy the real local license.

Local product builds may use the user-level SixLaborsLicenseFile path only in
the current process, after Test-Path confirms existence. Do not commit that
path, inspect license contents, alter persistent environment settings, or claim
hosted Actions execution from local verification.

## Tracer execution evidence

The Debug tracer block writes a fresh tracer.trx, then Test-TrxExecution.ps1
requires positive executed count, all passed, no failed results and agreement
with actual result rows. Reject empty discovery even if dotnet exits zero. XML
is bounded to 4 MiB with DTD and external resolution disabled; dispose readers
on every path and never print raw XML or exception messages. Synthetic tests
execute the real workflow block with only dotnet replaced. Keep them active
even while the tracer step is temporarily disabled for a dependency source.

## Transitional DevExtreme dependency scope (user approved 2026-11-04)

SpeechMessageProducts.CI.slnf retains 23 of the solution's 26 projects. Exclude
SpeechMessageProducts.ChurchReport, ChurchReport.MemberInfo.Tests and
ToolUtility.Dataverse.Tests because they directly or transitively require
DevExtreme.AspNet.Core 23.1.5, whose approved package source is still pending.
Restore, build and Release test use this filter. The Debug tracer step keeps
its run block and positive-TRX guard but is temporarily disabled; synthetic
tracer checks stay active. The always-run scope summary derives the exclusions
from the filter and must state that this is not full solution verification.

Test-CiTransition.Tests.ps1 checks the real dependency closure and executes
actual command/summary blocks with only dotnet replaced. No unrelated project
may be silently omitted. Per-module coverage and offline JavaScript checks
remain enabled. Local 00 section 8 validation still uses the full solution,
including ChurchReport and Debug tracer.

The obsolete machine-specific DevExpress 19.1 source is removed from the
ChurchReport NuGet.config. It inherits user-level package sources. On a new
computer, install the licensed DevExpress 23.1 tools, locate their local
DevExtreme AspNetCore package folder, and add that folder to the user's NuGet
package sources (Visual Studio package-source settings or user-level NuGet
configuration). Keep the actual machine path outside the repository. Existing
machines may restore from their populated cache. Do not add nupkg files,
private source URLs or credentials to the repository, upgrade DevExtreme, or
try an unapproved remote feed.

After user approval of a DevExtreme source, restore the full solution commands
and tracer, then prove the complete hosted CI passes. Stage 0 cannot be closed
on this transitional CI alone without explicit user acceptance. Per the user's
hosted audit, Six Labors preparation/always-cleanup have succeeded, while past
runs stopped at Restore before builds/tests. Local checks do not establish a
subsequent hosted result.
