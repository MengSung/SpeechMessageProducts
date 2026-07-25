#Requires -Version 5.1
<#
.SYNOPSIS
  Run Dynamics live smoke tests under the current Windows identity.

.DESCRIPTION
  Use this from Visual Studio 2026 Developer PowerShell / your interactive
  account that can already open ChurchReport against jesus.

  Codex sandbox identity (codexsandboxoffline) cannot complete schannel TLS to
  jesus on this host. That is why agent-side live smoke failed earlier even
  though VS ChurchReport login works.

.PARAMETER EnableLive
  Required to actually hit CRM. Without this switch, the script only prints
  guidance and exits 0.

.PARAMETER WebApiRoot
  CE Web API root, e.g. https://jesus.speechmessage.com.tw/api/data/v9.1/

.PARAMETER CeVersion
  9.1 or 8.2

.PARAMETER ContactId
  Optional GUID for fee date-range smoke.

.PARAMETER CredentialSource
  HostIdentity (default) or SecretReference

.PARAMETER NoRestore
  Pass --no-restore to dotnet test (useful when packages already restored).
#>
[CmdletBinding()]
param(
    [switch]$EnableLive,
    [string]$WebApiRoot = "https://jesus.speechmessage.com.tw/api/data/v9.1/",
    [ValidateSet("9.1", "8.2")]
    [string]$CeVersion = "9.1",
    [string]$ContactId,
    [ValidateSet("HostIdentity", "SecretReference")]
    [string]$CredentialSource = "HostIdentity",
    [string]$ProfileAlias = "jesus-prod",
    [switch]$NoRestore,
    [string]$RepositoryPath
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) {
    Write-Host "[dynamics-live-smoke] $Message"
}

if (-not $RepositoryPath) {
    # Prefer current directory when already in worktree; otherwise climb from script.
    $candidate = (Get-Location).Path
    if (-not (Test-Path (Join-Path $candidate "SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj"))) {
        $candidate = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
        if (Test-Path (Join-Path $PSScriptRoot "..\..")) {
            $candidate = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
        }
    }
    $RepositoryPath = $candidate
}

$project = Join-Path $RepositoryPath "SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj"
if (-not (Test-Path -LiteralPath $project)) {
    throw "Smoke project not found: $project"
}

Write-Info "Identity: $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Info "Repository: $RepositoryPath"
Write-Info "Project: $project"
Write-Info "WebApiRoot: $WebApiRoot"
Write-Info "CeVersion: $CeVersion"
Write-Info "CredentialSource: $CredentialSource"

if (-not $EnableLive) {
    Write-Info "Live mode is OFF. Re-run with -EnableLive from the same account that can log into ChurchReport/jesus."
    Write-Info "Example:"
    Write-Info "  powershell -NoProfile -File .\docs\scripts\Invoke-DynamicsLiveSmoke.ps1 -EnableLive"
    exit 0
}

# Preflight HTTPS from THIS identity. Do not print response bodies that may contain secrets.
Write-Info "Preflight HTTPS HEAD to org host..."
try {
    $head = Invoke-WebRequest -Uri ($WebApiRoot.TrimEnd('/') + "/") -Method Head -TimeoutSec 30 -UseBasicParsing
    Write-Info "Preflight HTTP status: $([int]$head.StatusCode)"
} catch {
    $msg = $_.Exception.Message
    if ($_.Exception.InnerException) {
        $msg = "$msg | $($_.Exception.InnerException.Message)"
    }
    Write-Info "Preflight failed: $msg"
    Write-Info "If this fails here but ChurchReport login works, check you are in the same Windows account/session as VS2026."
    throw
}

$env:DYNAMICS_SMOKE_ENABLED = "1"
$env:DYNAMICS_SMOKE_WEBAPI_ROOT = $WebApiRoot
$env:DYNAMICS_SMOKE_CE_VERSION = $CeVersion
$env:DYNAMICS_SMOKE_CREDENTIAL_SOURCE = $CredentialSource
$env:DYNAMICS_SMOKE_PROFILE_ALIAS = $ProfileAlias

if ($CredentialSource -eq "SecretReference") {
    $env:DYNAMICS_SMOKE_USERNAME_SECRET = "DYNAMICS_JESUS_PROD_USERNAME"
    $env:DYNAMICS_SMOKE_PASSWORD_SECRET = "DYNAMICS_JESUS_PROD_PASSWORD"
    $env:DYNAMICS_SMOKE_DOMAIN_SECRET = "DYNAMICS_JESUS_PROD_DOMAIN"
    if (-not $env:DYNAMICS_JESUS_PROD_USERNAME -or -not $env:DYNAMICS_JESUS_PROD_PASSWORD) {
        throw "SecretReference requires env DYNAMICS_JESUS_PROD_USERNAME and DYNAMICS_JESUS_PROD_PASSWORD in this session. Do not commit them."
    }
}

if ($ContactId) {
    $env:DYNAMICS_SMOKE_CONTACT_ID = $ContactId
} else {
    Remove-Item Env:DYNAMICS_SMOKE_CONTACT_ID -ErrorAction SilentlyContinue
}

$dotnetArgs = @("test", $project, "--nologo")
if ($NoRestore) {
    $dotnetArgs += "--no-restore"
}

Write-Info "Running: dotnet $($dotnetArgs -join ' ')"
& dotnet @dotnetArgs
$exitCode = $LASTEXITCODE
Write-Info "dotnet test exit code: $exitCode"
exit $exitCode