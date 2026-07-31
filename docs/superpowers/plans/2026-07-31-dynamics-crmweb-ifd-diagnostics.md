# Dynamics CRMWeb IFD Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a deterministic, read-only D365APP01 diagnostic tool that captures evidence needed to distinguish a CRMWeb Claims/IFD URI-construction fault from a Gateway defect.

**Architecture:** A PowerShell 5.1 script accepts an explicit HTTPS Web API root and emits sanitized console objects. Its default path performs local URI validation, bounded ASP.NET 1309 event lookup, safe `Get-CrmSetting` projections, and relevant IIS/certificate inventory. The opt-in `-ProbeWhoAmI` path owns and disposes one HTTP request graph in `finally`.

**Tech Stack:** Windows PowerShell 5.1, local Dynamics deployment PowerShell cmdlets, IIS WebAdministration, and a static PowerShell contract test.

---

### Task 1: Establish a read-only script contract

**Files:**

- Create: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1`
- Create: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.ps1`

- [ ] Write a failing PowerShell 5.1 test. It must require explicit `[string]$WebApiRoot`, optional `[switch]$ProbeWhoAmI`, bounded `LookbackMinutes`, `Get-WinEvent`, `Get-CrmSetting`, and `Get-WebBinding`. It must reject file writes, PSSession/remoting, WSMan/DNS/IIS/CRM mutation, credential/token inputs, and an HTTP client graph lacking `finally` disposal.

- [ ] Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1` and observe failure because the implementation file does not exist.

- [ ] Implement the smallest PowerShell 5.1 script that meets the static contract. Require an absolute HTTPS `/api/data/v8.2/` or `/api/data/v9.1/` root. The default path never contacts CRM. `-ProbeWhoAmI` uses host identity plus `UseProxy = $false`, returns only response metadata, and disposes request, response, client, and handler in `finally`.

- [ ] Re-run the test and expect `Get-DynamicsCrmWebIfdDiagnostics script contract passed.`

### Task 2: Verify no-probe behavior and record the handoff

**Files:**

- Modify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1`
- Modify: `docs/scripts/Get-DynamicsCrmWebIfdDiagnostics.ps1`
- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-live-ce91-2026-07-31.md`

- [ ] Add a test that executes the script with an explicit root and no `-ProbeWhoAmI`; it must report `ProbeOutcome = 'not-requested'`, without requiring D365 cmdlets on the development workstation.

- [ ] Make missing `Get-CrmSetting` or `WebAdministration` a labeled diagnostic status, never a fallback configuration action.

- [ ] Run the static test and `git diff --check`; expect the contract to pass with no whitespace errors.

- [ ] Add the exact D365APP01 invocation and the scope boundary to the Phase 4 evidence. It must state that the script never runs `Set-CrmSetting`, writes CRM data, changes IIS/DNS/ADFS, accepts passwords/tokens, or persists output.
