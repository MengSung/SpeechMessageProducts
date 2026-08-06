# P6.2 Lenovo Operator Handoff

## Purpose

This handoff creates only the local, non-secret Official Worker profile input and then reruns the existing readiness probe. It does not start a Worker, contact D365, write ChurchReport data, enable a feature flag, or modify a Gateway overlay.

The expected execution identity is `LENOVO-LEGION\Administrator`. Perform every step below in a PowerShell window opened as that same Windows user.

## Before running PowerShell

Prepare these non-secret values for each approved read-only target. Do not paste any of them into chat except the final sanitized JSON output.

1. CE 8.2: approved read-only Organization base HTTPS URI, organization name, Organization ID, and IFD HTTPS home realm.
2. CE 9.1: the isolated `sunnyvalechback` Organization's base HTTPS URI, organization name, Organization ID, and IFD HTTPS home realm.
3. Two new, stable Credential Manager target names. Use only letters, digits, `.`, `-`, or `_`; for example, `speechmessage.crm82.p62` and `speechmessage.crm91.p62`.
4. Two non-secret profile generation IDs, also using only letters, digits, `.`, `-`, or `_`; for example, `crm82-p6-2-local-001` and `crm91-p6-2-local-001`.

For each Credential Manager target, open **Control Panel → Credential Manager → Windows Credentials → Add a generic credential**. Enter the target name and the dedicated IFD test account's username/password. Never paste the username or password into chat, a repository file, a Trellis artifact, or the commands below. The later readiness probe checks only whether the target name is resolvable; it never prints or reads the credential value.

Do not select a CE 8.2 target until its owner has explicitly approved it for P6 read-only validation. `sunnyvalechback` is approved only as the isolated CE 9.1 development target; its future test-member write authority belongs to P7.2 and is not used here.

## Step 1: Create the local profile input

Copy the following entire block into PowerShell. Each prompt asks for a non-secret value. The generated file is stored only under your current Windows user's `%LOCALAPPDATA%` directory and the script refuses to overwrite an existing file.

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
$manifest = "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json"

$crm82BaseUri = Read-Host 'CE 8.2 Organization base HTTPS URI'
$crm82Name = Read-Host 'CE 8.2 organization name'
$crm82OrganizationId = Read-Host 'CE 8.2 Organization ID (GUID)'
$crm82HomeRealm = Read-Host 'CE 8.2 IFD HTTPS home realm'
$crm82CredentialTarget = Read-Host 'CE 8.2 Credential Manager target name'
$crm82GenerationId = Read-Host 'CE 8.2 profile generation ID'

$crm91BaseUri = Read-Host 'CE 9.1 Organization base HTTPS URI'
$crm91Name = Read-Host 'CE 9.1 organization name'
$crm91OrganizationId = Read-Host 'CE 9.1 Organization ID (GUID)'
$crm91HomeRealm = Read-Host 'CE 9.1 IFD HTTPS home realm'
$crm91CredentialTarget = Read-Host 'CE 9.1 Credential Manager target name'
$crm91GenerationId = Read-Host 'CE 9.1 profile generation ID'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\New-DynamicsOfficialWorkerProfileInput.ps1" `
  -ManifestPath $manifest `
  -Crm82OrganizationBaseUri $crm82BaseUri `
  -Crm82OrganizationName $crm82Name `
  -Crm82ExpectedOrganizationId $crm82OrganizationId `
  -Crm82HomeRealm $crm82HomeRealm `
  -Crm82CredentialTarget $crm82CredentialTarget `
  -Crm82ProfileGenerationId $crm82GenerationId `
  -Crm91OrganizationBaseUri $crm91BaseUri `
  -Crm91OrganizationName $crm91Name `
  -Crm91ExpectedOrganizationId $crm91OrganizationId `
  -Crm91HomeRealm $crm91HomeRealm `
  -Crm91CredentialTarget $crm91CredentialTarget `
  -Crm91ProfileGenerationId $crm91GenerationId `
  -Json
```

Expected safe result:

```json
{"schemaVersion":1,"outcome":"written","profileCount":2}
```

If the outcome is `error`, paste only that short JSON line. Do not open, copy, attach, or paste `official-worker-profile-input.json`; it contains deployment metadata and stays local.

## Step 2: Run the sanitized readiness probe

Only after Step 1 reports `written`, copy this block into the same PowerShell window:

```powershell
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$profileInput = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.ps1" `
  -ManifestPath $manifest `
  -ProfileInputPath $profileInput `
  -ExpectedExecutionIdentity $identity `
  -Json
```

Paste only this second JSON result into chat. A result of `go` permits the next P6.2 offline deployment-material generation and controlled read-only evidence. A result of `no-go` is still useful: its `reasons` array tells us the next safe action without exposing credential values or D365 metadata.
