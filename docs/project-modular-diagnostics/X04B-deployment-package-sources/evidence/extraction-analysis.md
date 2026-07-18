# X04B Extraction Analysis

Module: X04B
Mode: DIAGNOSIS_ONLY

## Cohesive Responsibility

X04B is cohesive when it is treated as deployment/package-source governance rather than application runtime configuration:

- owns package source definitions and restore reproducibility;
- owns publish command surfaces and release output validation;
- owns deployment smoke/audit scripts;
- delegates runtime secret schema and values to X04A;
- delegates solution/build topology to F01A.

## Current Boundary Shape

The boundary currently mixes several maturity levels:

- active package-source config (`NuGet.config`);
- stale package-source backup (`NuGet.config.bak`);
- one official production publish script;
- many exploratory publish variants under `DotNetPublish/**`;
- a narrow release verification script;
- development launch settings included as project content.

The files are physically grouped enough to diagnose as X04B, but the operational contract is not yet extracted into a single reusable validation module.

## Proposed Extraction Contract

Inputs:

- repository root or project root;
- expected publish output path;
- expected environment name;
- expected package source policy;
- artifact allowlist/denylist configuration.

Outputs:

- package source audit result;
- publish output manifest with size/file-count/hash summary;
- forbidden artifact finding list;
- required artifact presence list;
- deployment smoke preflight result.

Dependencies:

- F01A build/solution command contracts;
- X04A runtime configuration and secret-key denylist;
- X03 shared asset inventory for duplicate/static asset budget checks;
- X01 host startup/deployment smoke command.

Test seam:

- Static file fixture tests for NuGet config, launch settings, script matrix, and publish output manifests.
- Non-mutating audit mode for existing publish folders.
- CI-compatible exit codes for artifact audit failures.

Rollback boundary:

- The first optimization should introduce audit-only tooling without changing publish output.
- Script consolidation should happen only after current script users are mapped and the official production path is validated.

## Cross-Module Handoffs

- X04A: owns secret key taxonomy and environment schema that X04B artifact audit should check for leakage.
- F01A: owns build pipeline integration once X04B provides a stable audit command.
- X01: owns host startup/deployment smoke after package artifact is produced.
- X03: owns static asset source hygiene; X04B owns packaged asset size/duplication reporting.

## Extraction Issue Candidates

Retained:

- X04B-EXT-001: extract a reusable deployment/package audit module with clear inputs and CI-safe outputs.

Rejected:

- Moving business configuration into X04B. Runtime configuration ownership remains X04A.
