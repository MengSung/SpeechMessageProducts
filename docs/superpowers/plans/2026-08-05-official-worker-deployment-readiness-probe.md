# Official Worker Deployment Readiness Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans in inline mode. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local-only PowerShell preflight that determines whether approved CE 8.2 and CE 9.1 profile input can safely proceed to deployment generation without printing deployment identities, credential references, endpoints, or secrets.

**Architecture:** The probe reads a bounded UTF-8 manifest and, outside inventory mode, a separately supplied local profile-input JSON file. It confirms the two pinned Worker artifact records, validates only the allowed CE 8.2/9.1 profile shapes, compares the current execution identity to an explicit expected identity, and checks Credential Manager target presence without reading credential blobs. `-InventoryOnly` skips profile and credential checks so the operator can first verify local artifacts with one safe command. It never invokes the deployment generator, starts a Worker, starts Gateway, creates files, or sends a network request.

**Tech Stack:** Windows PowerShell 5.1, built-in .NET JSON/XML APIs, `cmdkey.exe`, existing standalone PowerShell assertion-test convention.

---

### Task 1: Create the failing readiness-probe test

**Files:**

- Create: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`
- Test: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`

- [x] **Step 1: Write a test fixture that owns two fake pinned Worker artifacts, one manifest, and a profile-input document.**

  The fixture writes only random temporary files. Its profile input contains a unique endpoint marker and credential-reference marker so the test can prove the probe never echoes either value.

- [x] **Step 2: Assert the missing script fails.**

  Run:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1
  ```

  Expected: failure containing `The deployment readiness probe is missing.`

- [x] **Step 3: Add assertions for the first implemented behavior.**

  The test invokes the probe with an unresolvable credential reference and verifies a JSON `no-go` result, a `credential-reference-unresolvable` reason, no endpoint marker, no credential-reference marker, no password/token/cookie fields, and no file creation beside either fake Worker.

### Task 2: Implement the local-only readiness probe

**Files:**

- Create: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Test: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`

- [x] **Step 1: Add comment-based help and strict process boundaries.**

  Parameters are `ManifestPath`, optional `ProfileInputPath`, `ExpectedExecutionIdentity`, `InventoryOnly`, and `Json`. The script uses `Set-StrictMode`, bounded UTF-8/no-BOM reads, and a single JSON output object. It contains no HTTP client, deployment-generator invocation, `New-Item`, `Set-Content`, `WriteAllText`, `Start-Process`, or credential-blob reader.

- [x] **Step 2: Add fail-closed manifest/profile parsing and pinned-artifact validation.**

  Require exactly `crm82` / `OfficialCrm82Worker` and `crm91` / `OfficialCrm91Worker` profile records, verify safe identifiers and non-placeholder GUIDs, require HTTPS organization and IFD home-realm values when applicable, then match package locks and SHA-256 values against real local executables.

- [x] **Step 3: Add identity and credential-target presence checks without reading secrets.**

  Require the caller to pass the intended execution identity. Compare it to the current Windows identity without serializing either value. For `WindowsCredentialReference`, invoke `cmdkey.exe /list`, compare target names in process memory, clear captured output, and report only `credential-reference-unresolvable` or `credential-target-present` state. `HostIdentity` requires no credential target but still requires execution-identity match.

- [x] **Step 4: Emit sanitized Go / No-Go evidence.**

  The result carries only schema version, `go` / `no-go`, CE version, profile alias, worker kind, and allowlisted reason codes. It never serializes paths, endpoints, organization IDs, credential references, current identity, secret values, command output, stack traces, or raw JSON input.

- [x] **Step 5: Run the focused test until it passes.**

  Run:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1
  ```

  Expected: `All official Worker deployment readiness probe tests passed.`

### Task 3: Perform text-format and scope checks

**Files:**

- Modify: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Modify: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`
- Modify: `docs/superpowers/plans/2026-08-05-official-worker-deployment-readiness-probe.md`

- [x] **Step 1: Verify UTF-8 without BOM, CRLF-only endings, and a final CRLF for the modified files.**

- [x] **Step 2: Run `git diff --check` and inspect the changed-file list.**

- [x] **Step 3: Do not commit.**

  The user explicitly prohibited commit, archive, and push for this workflow.
