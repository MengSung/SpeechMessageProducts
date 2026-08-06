# P6.2 Lenovo Operator Handoff

## Purpose

This handoff creates only the local, non-secret Official Worker profile input and then reruns the existing readiness probe. It does not start a Worker, contact D365, write ChurchReport data, enable a feature flag, or modify a Gateway overlay.

The expected execution identity is `LENOVO-LEGION\Administrator`. Perform every step below in a PowerShell window opened as that same Windows user.

## Before running PowerShell

Prepare these non-secret values for each approved read-only target. Do not paste any of them into chat except the final sanitized JSON output.

1. CE 8.2: approved read-only canonical Organization HTTPS root URI, organization name, Organization ID, and IFD HTTPS home realm.
2. CE 9.1: the isolated `sunnyvalechback` Organization's canonical HTTPS root URI, organization name, Organization ID, and IFD HTTPS home realm.
3. Two stable Credential Manager target names. Use only letters, digits, `.`, `-`, or `_`; the
   current local profile uses `speechmessage.crm82.p62` and `speechmessage.crm91.p62`.
4. Two non-secret profile generation IDs, also using only letters, digits, `.`, `-`, or `_`; for example, `crm82-p6-2-local-001` and `crm91-p6-2-local-001`.

For each Credential Manager target, open **Control Panel → Credential Manager → Windows Credentials**.
If the target already exists, expand it and choose **Edit**; add a generic credential only when it
does not exist. Enter the target name and the dedicated IFD test account's username/password. The
username must be exactly the form accepted by that environment's IFD login page (for example,
`DOMAIN\\name` or a UPN only when that is the working interactive-login form). Never paste the
username or password into chat, a repository file, a Trellis artifact, or the commands below. The
later readiness probe checks only whether the target name is resolvable; it never validates a
password or reads the credential value.

The Organization base URI is the canonical IFD host root, exactly `https://host-name/`: it includes the final `/`, has no organization path, query, fragment, user information, or non-default port spelling. Enter the organization separately in the organization-name prompt. For example, `https://crm.example.test/organization/` is invalid here even when that is a browser navigation URL.

Do not select a CE 8.2 target until its owner has explicitly approved it for P6 read-only validation. `sunnyvalechback` is approved only as the isolated CE 9.1 development target; its future test-member write authority belongs to P7.2 and is not used here.

## Step 1: Create the local profile input

Copy the following entire block into PowerShell. Each prompt asks for a non-secret value. The generated file is stored only under your current Windows user's `%LOCALAPPDATA%` directory and the script refuses to overwrite an existing file.

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
$manifest = "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json"

$crm82BaseUri = Read-Host 'CE 8.2 canonical Organization HTTPS root URI (https://host-name/)'
$crm82Name = Read-Host 'CE 8.2 organization name'
$crm82OrganizationId = Read-Host 'CE 8.2 Organization ID (GUID)'
$crm82HomeRealm = Read-Host 'CE 8.2 IFD HTTPS home realm'
$crm82CredentialTarget = Read-Host 'CE 8.2 Credential Manager target name'
$crm82GenerationId = Read-Host 'CE 8.2 profile generation ID'

$crm91BaseUri = Read-Host 'CE 9.1 canonical Organization HTTPS root URI (https://host-name/)'
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

If the outcome is `error`, paste only that short JSON line. Do not open, copy, attach, or paste `official-worker-profile-input.json`; it contains deployment metadata and stays local. When an
existing local profile is replaced, the script retains a recoverable local backup rather than
writing the prior profile to this repository.

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

## Step 3: Run the sanitized local Gateway startup bridge

Use this only after the readiness result is `go` and the deployment material has been
provisioned. Run it as the same `LENOVO-LEGION\Administrator` user that owns both
Credential Manager targets. The bridge starts only the published local Gateway with the
fixed `crm82`／`crm91` profile selectors and the two P6 read-only operation IDs; it does not
send a CE request or start ChurchReport. It captures child logs in a temporary directory,
does not print them, stops only the process it started, and prints one sanitized JSON result.

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
$gatewayDir = Join-Path $root 'artifacts\dynamics-workers-p6.2\gateway-host'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Test-DynamicsOfficialWorkerP6LocalStartup.ps1" `
  -GatewayExecutablePath "$gatewayDir\SpeechMessage.Dynamics.Gateway.exe" `
  -GatewayContentRootPath $gatewayDir `
  -GatewayEndpoint 'https://localhost:7244/' `
  -StartupTimeoutSeconds 20 `
  -Json
```

Paste only the final JSON line. Expected meanings:

- `outcome: "started"`: the Gateway survived the bounded startup window and released its
  listener after the bridge stopped it; continue with the P6 allowlisted read-only evidence.
- `outcome: "no-go"` with `reason: "gateway-startup-failed-before-ready"`: do not retry
  blindly. Recheck the two target credentials and approved IFD home realms under this same
  Windows user, then regenerate the profile input only if an externally confirmed fact changed.
- `outcome: "error"`: the local bridge input or process setup is invalid; paste only that
  sanitized JSON and do not paste its temporary logs.

## If Step 3 reports `gateway-startup-failed-before-ready`

Do not change the canonical root URI, regenerate the local profile, or rerun the bridge unchanged.
The current canonical root URIs have already been recorded successfully. On **Lenovo Legion**, while
signed in as `LENOVO-LEGION\Administrator`, refresh only these two existing Windows Credentials:

1. In **Windows Credentials → Generic Credentials**, find `speechmessage.crm82.p62`, choose
   **Edit**, and enter the exact account and password that successfully sign in through the CE 8.2
   IFD login for `https://jesus.speechmessage.com.tw/`.
2. Find `speechmessage.crm91.p62`, choose **Edit**, and enter the exact account and password that
   successfully sign in through the CE 9.1 IFD login for
   `https://sunnyvalechback.speechmessage.com.tw/`.
3. Select **Save** for both entries. Do not create a duplicate target, disclose a password, or
   change the target-name spelling.
4. Rerun the Step 3 block exactly once. Paste only its final sanitized JSON output. A new
   `outcome: "started"` permits the P6 allowlisted read-only matrix; the same `no-go` result means
   the next required fact is CE/ADFS-side IFD account authorization, not another local retry.
