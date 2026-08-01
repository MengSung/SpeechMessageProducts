# CRM IFD External-Domain Diagnostic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the local CRMWeb IFD diagnostic from treating a scheme-bearing `IfdSettings.ExternalDomain` value as a green match for the documented bare-host IFD input contract.

**Architecture:** The diagnostic remains read-only and redacted. A bare hostname remains a match. Any absolute URI keeps its safe normalized-host metadata but is reported as `absolute-uri-requires-supported-review` with `ContainsScheme=true` and `MatchesExpectedContract=false`; this blocks automatic green status without claiming that the server is misconfigured or changing D365APP01. Generic host/domain projections add a shape-only `ContainsScheme` flag.

**Tech Stack:** Windows PowerShell 5.1, Dynamics Deployment PowerShell read-only projection, repository script contract test, Markdown specification/evidence.

---

### Task 1: Define the failing contract test

**Files:**
- Modify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1:110-130,229-265`

- [ ] **Step 1: Change the HTTPS-root fixture to express the required failure**

```powershell
if (-not $uriExternalDomainEvidence.ContainsScheme -or
    $uriExternalDomainEvidence.MatchesExpectedContract -or
    $uriExternalDomainEvidence.Representation -ne 'absolute-uri-requires-supported-review') {
    throw 'A scheme-bearing IFD ExternalDomain value must require supported review, not pass automatically.'
}
```

- [ ] **Step 2: Run the script contract test and observe the expected RED failure**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1
```

Expected: failure because the current helper calls the representation `absolute-https-root-uri` and returns `MatchesExpectedContract=true`.

### Task 2: Make the smallest read-only implementation change

**Files:**
- Modify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.ps1:425-612`

- [ ] **Step 1: Add only shape-safe state**

```powershell
ContainsScheme = $true
Representation = 'absolute-uri-requires-supported-review'
MatchesExpectedContract = $false
```

Apply those results only to the absolute-URI branch. Preserve normalized-host and unsafe-URI-shape evidence, never serialize the raw DWS value, and do not add a setting writer, network request, cookie, credential, proxy, or remote session.

- [ ] **Step 2: Add `ContainsScheme` to generic host/domain-like projections**

```powershell
ContainsScheme = $(if (-not $isUriLike) { $rawValue -match '(?i)^[a-z][a-z0-9+.-]*:' } else { $null })
```

- [ ] **Step 3: Run the contract test and observe GREEN**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1
```

Expected: exit code `0`.

### Task 3: Align the documented diagnostic contract

**Files:**
- Modify: `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:1116-1178`
- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-deployment-context-evidence-2026-08-01.md:100-129`

- [ ] **Step 1: Replace the old equivalence statement**

State that Microsoft documents a bare external-domain hostname as the IFD input. A DWS result containing a scheme is a redacted, fail-closed review signal: it is not an automatic configuration failure and it is not an authorization to run `Set-CrmSetting`, `iisreset`, or an infrastructure workaround.

- [ ] **Step 2: Record the evidence boundary**

State that `[uri]::IsWellFormedUriString(..., Absolute)=True` proves only an absolute URI representation. It does not establish the CRMWeb root cause. A server change needs an independently verified, supported cause.

### Task 4: Verify scope and safety

**Files:**
- Verify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.ps1`
- Verify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1`
- Verify: `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- Verify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-deployment-context-evidence-2026-08-01.md`

- [ ] **Step 1: Run the focused contract test**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1
```

- [ ] **Step 2: Run static scope and formatting checks**

```powershell
git diff --check
rg -n '(?im)^\s*(Set-CrmSetting|iisreset|New-PSSession|Invoke-Command)\b' docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.ps1
```

Expected: `git diff --check` is silent; the `rg` command produces no output. No D365APP01 request or mutation is made.
