# X04B Runtime Validation Plan

Module: X04B
Mode: DIAGNOSIS_ONLY

## Validation Goal

Validate X04B deployment/package-source findings without changing product code or producing untracked build artifacts during diagnosis. Optimization remains blocked until explicit approval and repeatable green gates exist.

## Static Validation

Package source audit:

- Parse `SpeechMessageProducts.ChurchReport/NuGet.config` and `NuGet.config.bak`.
- Report absolute local package source paths.
- Verify whether active and backup sources disagree.
- Confirm no credentials exist in package source XML.

Publish script audit:

- Enumerate `DotNetPublish-*.bat` and `DotNetPublish/**`.
- Classify scripts as official production, debug, exploratory, backup/text, or unsupported.
- Record output directories and publish flags.
- Flag interactive `pause` usage and non-CI-safe scripts.

Artifact inclusion audit:

- Parse `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`.
- Verify publish content includes and excludes.
- Flag `Properties/launchSettings.json` as development metadata packaged by project content.

## Controlled Publish Validation

Run only after optimization approval:

1. Execute package restore with a controlled NuGet source policy.
2. Run the canonical production publish command in a disposable output directory.
3. Generate a publish manifest with file count, total bytes, hashes, and extensions.
4. Fail if forbidden files appear: launch settings, debug symbols when disabled, local path metadata, backup files, secrets, cache folders, or non-production config.
5. Run deployment smoke owned jointly by X04B and X01.

## Measurement Criteria

- Package source audit returns no stale local-only source drift.
- Publish output has an approved file allowlist and total size budget.
- Duplicate hash report has no unexplained duplicate static assets.
- Secret-like pattern scan reports no credential values in publish output.
- Deployment smoke proves host startup with production environment settings.

## Rollback Boundary

Audit-only tools can be removed without changing publish behavior. Script consolidation must keep the current official production script callable until replacement scripts have one green release cycle.

## Current Validation State

The current diagnosis used static read-only inspection only. No restore, build, publish, or test command was run because the worker scope forbids generated, bin, obj, cache, lockfile, or product-code writes.
