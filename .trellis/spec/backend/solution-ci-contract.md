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
