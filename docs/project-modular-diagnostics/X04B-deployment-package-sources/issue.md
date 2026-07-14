# X04B Deployment Package Sources Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X04B
Workspace: X04B-deployment-package-sources
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 60f11785574fa03c912c1c5ada047bb3397b5847a0998fc75adee2f738bdc2bb

Issue document SHA-256 for reviewed draft: 7F4049B93FA3F65EE034C380A251249CBD1D3D4C3E82A250A336472AD69B1DF6

## Executive Summary

X04B should first stabilize release reproducibility and artifact safety before optimizing deployment output. The highest-value issues are local-only package source drift, development launch metadata included as publish content, absence of an automated publish artifact audit, and many overlapping publish scripts without a canonical supported matrix. No literal secret value was confirmed in X04B-owned files, but the deployment package currently lacks an automated guard that would prove secrets, debug metadata, private paths, and overbroad contents are absent from release output.

## Ranked Confirmed Issues

### X04B-SEC-001 Local Package Source Drift Makes Restore Non-Reproducible

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 80
- Confirmed: true
- Evidence confidence: 18
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 13
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: X04B
- Cross-module: F01A for pipeline integration
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/NuGet.config:4`
  - `SpeechMessageProducts.ChurchReport/NuGet.config.bak:4`
- Evidence: Active NuGet config uses a DevExpress 19.1 absolute local package source; the backup file uses the same key with DevExpress 18.2. This creates private path exposure and stale package-source ambiguity.
- Control/data/lifetime flow: Restore reads package source config before build/publish; local machine state can decide whether restore succeeds and which package source path is used.
- Impact: Release builds are not reproducible across machines, and stale local package source files can silently reintroduce older DevExpress source paths.
- Why this is necessary: X04B owns package source governance and deployment reproducibility.
- Recommended action: Replace local-only source assumptions with a documented package source policy and remove or quarantine stale backup config from production guidance.
- Validation: Static NuGet config audit plus restore preflight using the approved package source policy.
- Rollback boundary: Keep the current config available until restore succeeds against the approved source in a controlled environment.
- Extraction contract: package-source input policy and restore preflight output.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

### X04B-SEC-002 Development Launch Settings Are Explicit Publish Content

- Category: Security
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 18
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 11
- Performance gain score: 3
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: XS
- Primary owner: X04B
- Cross-module: X04A for environment schema
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:55`
  - `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json:1-30`
- Evidence: The project includes `Properties/launchSettings.json` as content; that file contains Development profiles, HTTP localhost URLs, IIS Express settings, anonymous authentication, and `sslPort: 0`.
- Control/data/lifetime flow: Project content inclusion can place development metadata into publish output unless publish behavior excludes it later.
- Impact: Release artifacts can carry local/development metadata that is not a runtime secret but is unsafe packaged deployment guidance.
- Why this is necessary: Deployment packages should not include development launch profiles.
- Recommended action: Exclude launch settings from publish output or add an artifact audit that fails if launch settings appear in release packages.
- Validation: Publish manifest denylist check for `Properties/launchSettings.json`.
- Rollback boundary: Audit-only warning first; later removal must be validated against developer launch workflows.
- Extraction contract: artifact denylist and environment metadata policy.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

### X04B-SEC-003 Release Output Has No Automated Secret/Debug/Private-Path Artifact Audit

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 16
- Impact score: 21
- Likelihood/frequency score: 11
- Security urgency score: 14
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X04B
- Cross-module: X04A for secret-key taxonomy; X01 for deployment smoke
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat:108-149`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat:186-196`
  - `SpeechMessageProducts.ChurchReport/Tools/verify-release-noperf.ps1:4-17`
- Evidence: The official production script validates key files and prints manual checks for appsettings, web.config, Dynamics 365, Line Notify token, and CRM credentials. The release verifier only scans a Release DLL for `[Perf` text.
- Control/data/lifetime flow: Publish creates the deployable artifact; current checks do not scan artifact contents for forbidden files, secret-like values, debug symbols, local paths, or development metadata.
- Impact: A deployment package can pass current validation while still containing unsafe packaged assets or overbroad content.
- Why this is necessary: X04B owns deployment package safety; X04A owning secret values does not remove X04B's responsibility to prove the artifact does not leak them.
- Recommended action: Add a CI-safe artifact audit that scans output manifest, forbidden file patterns, secret-like keys, local absolute paths, debug symbols, and expected production config markers.
- Validation: Non-mutating audit against a known publish output folder, then integrate with canonical release script.
- Rollback boundary: Add audit-only reporting first; enforce failures after baseline is reviewed.
- Extraction contract: publish-output path in, artifact audit report out.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

### X04B-PERF-001 Publish Script Sprawl Wastes Build/Publish IO And Hides Canonical Release Path

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 64
- Confirmed: true
- Evidence confidence: 17
- Impact score: 15
- Likelihood/frequency score: 12
- Security urgency score: 4
- Performance gain score: 9
- Loop leverage score: 5
- Ease/reversibility score: 2
- Effort: M
- Primary owner: X04B
- Cross-module: F01A for release command registration
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-Debug.bat:1-3`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat:71-91`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish/DotNetPublish-SelfContained-Release-AOT-SingleFile.bat:1-10`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish/DotNetPublish-PublishReadyToRun-備份.txt:1-20`
- Evidence: Multiple scripts publish Debug, self-contained, AOT, trimmed, single-file, ReadyToRun, WebMax, MaxThroughput, official production, and backup/text variants into different `bin/Output-*` directories. Several scripts include `pause`.
- Control/data/lifetime flow: Operators can run different scripts with overlapping flags and distinct output paths; each variant incurs restore/build/publish I/O.
- Impact: Slower release iteration, harder diagnosis of package size, and higher chance of validating one output while deploying another.
- Why this is necessary: A canonical release path is required before package optimization can be measured.
- Recommended action: Document a supported publish matrix, classify exploratory scripts, and route validation through one non-interactive release command.
- Validation: Script inventory check plus one canonical command selected for artifact audit.
- Rollback boundary: Do not delete scripts until users and release history are mapped.
- Extraction contract: publish script inventory and canonical command metadata.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

### X04B-PERF-002 Publish Package Lacks Size, Duplicate, And Overbroad Content Budget

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 15
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 6
- Performance gain score: 9
- Loop leverage score: 5
- Ease/reversibility score: 2
- Effort: M
- Primary owner: X04B
- Cross-module: X03 for shared static assets
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:29`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:54`
- Evidence: Publish content includes broad `wwwroot/**`, `Views/**`, appsettings files, web.config, bower metadata, and launch settings, but no X04B-owned artifact budget or duplicate-resource audit was found.
- Control/data/lifetime flow: Project content is copied into publish output; without a manifest budget, output growth and duplicate resources are not detected at release time.
- Impact: Larger deployment packages, more publish I/O, and slower artifact transfer or deployment.
- Why this is necessary: Package size and overbroad content are X04B deployment concerns even when asset source ownership belongs to X03.
- Recommended action: Generate a publish manifest with total bytes, file count, extension breakdown, duplicate hashes, and denylisted development files.
- Validation: Artifact manifest audit against a controlled publish output.
- Rollback boundary: Audit-only threshold reporting before enforcing hard budgets.
- Extraction contract: publish manifest schema and budget thresholds.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

### X04B-EXT-001 Deployment Validation Should Be Extracted Into A Reusable Audit Module

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 58
- Confirmed: true
- Evidence confidence: 15
- Impact score: 13
- Likelihood/frequency score: 9
- Security urgency score: 6
- Performance gain score: 5
- Loop leverage score: 8
- Ease/reversibility score: 2
- Effort: M
- Primary owner: X04B
- Cross-module: F01A, X04A, X01, X03
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Tools/verify-release-noperf.ps1:4-17`
  - `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat:102-149`
- Evidence: Current validation is split between a production batch file and a narrow no-perf PowerShell check; neither exposes a reusable artifact audit contract.
- Control/data/lifetime flow: Release verification depends on script-specific procedural checks instead of a reusable validation module with stable inputs and outputs.
- Impact: CCG, CI, and operators cannot batch-validate publish artifacts consistently.
- Why this is necessary: A reusable audit module is the acceleration foundation for X04B package/build validation.
- Recommended action: Create an audit-only tool contract before changing scripts: package source audit, script matrix audit, publish manifest audit, forbidden artifact scan, and deployment smoke handoff.
- Validation: Fixture-based static tests for audit parser plus one non-mutating audit run against a publish output.
- Rollback boundary: Audit module can be removed without changing publish behavior.
- Extraction contract: repository root and publish output path in; structured audit report and exit code out.
- CCG round history:
  - Round 1: CCG runner submitted through self-healing entrypoint; result fields are tracked in `review-log.md`.

## Runtime Validation

Runtime/package validation is required before optimization. The proposed plan is in `evidence/runtime-validation-plan.md`. No restore, build, publish, or test command was run during this diagnosis because the worker scope forbids generated, bin, obj, cache, lockfile, or product-code writes.

## Deleted Or Rejected Candidates

- Literal committed secret in X04B-owned files: rejected. Targeted inspection found secret-related checklist labels but no actual token, password, or API key value in the X04B package-source files.
- Measured runtime performance regression: rejected. Current evidence supports static package/build I/O risks, not measured runtime latency or CPU regression.
- Moving runtime configuration ownership into X04B: rejected. X04A remains owner of runtime configuration and secrets.

## Cross-Module Handoffs

- F01A: integrate canonical release command and package audit in build/release governance.
- X04A: provide secret-key taxonomy and production environment schema used by the artifact audit.
- X01: provide host startup and deployment smoke after package output exists.
- X03: provide static asset ownership and budget inputs for duplicate/large asset investigation.

## Final CCG Approval

Final status: DEGRADED_REVIEW_PENDING

CCG review was attempted through the self-healing runner, but no backend produced usable output. Gemini was blocked by provider quota/billing and Claude was blocked by session limit. The diagnostic issue set remains ready for re-review when at least one backend is available.
