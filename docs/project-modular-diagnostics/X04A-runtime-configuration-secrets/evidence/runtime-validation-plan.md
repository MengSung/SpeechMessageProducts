# X04A Runtime Validation Plan

## Scope

Validate X04A findings without modifying product code in this diagnostic task.

## Validation Targets

- X04A-SEC-001: committed runtime secrets and credentials.
- X04A-SEC-002: environment drift allows production to inherit unsafe base values.
- X04A-PERF-001: multiple ad hoc configuration builders bypass host configuration lifecycle.
- X04A-EXT-001: config validation and secret scanning can be extracted as a reusable module.

## Proposed Checks

1. Secret scan:
   - Scan only X04A owner files for key names matching secret patterns.
   - Report key path, file, line, and redacted value classification.
   - Fail when a non-empty committed literal appears under a secret key.

2. Effective production config validation:
   - Build effective configuration from `appsettings.json` plus `appsettings.Production.json` plus environment variables in a controlled smoke environment.
   - Assert Production has no sandbox/test defaults for payment mode, OAuth state, LINE Pay sandbox, or permissive security flags unless explicitly approved.

3. Placeholder validation:
   - Reject `your_store_key`, `your_store_iv`, `YOUR_*`, `random_state_string`, and empty production secret values.

4. Startup smoke:
   - Start host with injected fake-but-valid non-secret placeholders and secret references.
   - Assert startup fails on missing required secrets and does not print raw secret values.

5. Configuration lifecycle audit:
   - Search for `new ConfigurationBuilder`, `AddJsonFile("appsettings.json")`, and direct file-based config reads.
   - Require consumer-owned migration plans for the thirteen product runtime paths currently identified.
   - Replace only in a future optimization task after X04A gate is green.

## Required Evidence Before Optimization

- Secret scan output with zero committed raw secrets.
- Schema validation output for Development and Production.
- Host startup smoke command and result.
- Consumer smoke for X01 host startup and X04B deployment packaging.

## Rollback Boundary

Future optimization should be isolated to X04A owner files plus explicit consumer migration tasks. Secret rotation and deployment injection must be reversible independently from code changes.
