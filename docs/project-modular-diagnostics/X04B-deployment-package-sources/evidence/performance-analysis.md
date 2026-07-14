# X04B Performance Analysis

Module: X04B
Mode: DIAGNOSIS_ONLY

## Publish Surface

The project disables default content items and then explicitly publishes broad content:

- `wwwroot/**`
- `Views/**`
- `Areas/**/Views`
- `appsettings.json`
- `appsettings.Production.json`
- `web.config`
- `bower.json`
- `Properties/launchSettings.json`

This is deterministic in the sense that the project owns its content list, but it is still broad. The package has no documented artifact budget or publish-output allowlist for X04B.

## Publish Script Sprawl

Observed publish scripts include:

- root-level Debug, WebMax, MaxThroughput, and official production scripts.
- DotNetPublish folder scripts for self-contained, debug, release, AOT, trimmed, single-file, ReadyToRun, and backup text variants.
- Several scripts publish to distinct `bin/Output-*` folders with overlapping settings.

Performance impact:

- Repeated publish variants encourage trial-and-error builds that waste restore/publish I/O.
- Self-contained, AOT, trimmed, single-file, and ReadyToRun flags are mixed across scripts without an authoritative matrix or measured tradeoff.
- Some scripts include `pause`, making them unsuitable for non-interactive CI or batch validation.
- Debug publish scripts can create large deployment-like output not useful for production release validation.

## Release Verification Gap

`Tools/verify-release-noperf.ps1` builds Release and scans the compiled DLL for `[Perf` text. This is a useful narrow check, but it is not a deployment package audit:

- It builds instead of validating an already-produced publish output.
- It does not enforce publish folder size, file count, duplicate assets, forbidden development files, private paths, or debug symbol leakage.
- Its `$proj` value is `ChurchReport\ChurchReport.csproj`, while the repository path evidence shows the project under `SpeechMessageProducts.ChurchReport`; the command shape may require a specific working directory or legacy path.

## Acceleration Opportunities

High-leverage automation:

- A single package validation module that accepts a publish output path and runs all release artifact checks.
- Batch validation across known publish scripts without interactive `pause`.
- A publish artifact audit that records file count, total size, duplicate file names/hashes, forbidden patterns, and required files.
- A canonical script matrix that separates supported production publish modes from experiments or historical samples.

## Performance Issue Candidates

Retained:

- X04B-PERF-001: consolidate publish script variants behind a canonical release matrix and remove interactive/non-production variants from production guidance.
- X04B-PERF-002: add publish artifact budget and duplicate-resource audit to reduce deployment package size and build/publish I/O.

Rejected:

- Claiming a measured runtime performance regression. Current evidence is static package/build analysis only; runtime measurement belongs in a later validation stage.
