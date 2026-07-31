#Requires -Version 5.1
<#
.SYNOPSIS
  以目前 Windows 身分執行明確指定目標的 Dynamics 真機 smoke 測試。

.DESCRIPTION
  此工具只負責把部署操作者明確提供的 Web API root 與驗證來源傳給既有的
  smoke 測試；它不擁有 CRM 端點、帳密、票證、Cookie 或使用者工作階段。
  未指定 -EnableLive 時不會產生任何外部流量；指定 -EnableLive 卻未提供
  -WebApiRoot 時會在任何 DNS、HTTPS 或 CRM 請求前 fail-closed。

  不做獨立的 HEAD 預檢，因為它可能與 connector 使用不同的驗證語意而把 401
  誤判為連線失敗。唯一具有判斷力的網路動作是後續以相同設定執行的 WhoAmI。

.PARAMETER EnableLive
  Required to actually hit CRM. Without this switch, the script only prints
  guidance and exits 0.

.PARAMETER WebApiRoot
  必填的 CE Web API root，例如 https://example.speechmessage.com.tw/api/data/v9.1/。
  工具不再內建任何歷史 CRM host，避免操作者未察覺地驗證錯誤部署。

.PARAMETER CeVersion
  9.1 or 8.2

.PARAMETER ContactId
  Optional GUID for fee date-range smoke.

.PARAMETER CredentialSource
  HostIdentity（預設）或 SecretReference。此工具不輸出 secret value。

.PARAMETER UserNameSecretName
  CredentialSource=SecretReference 時必填的使用者名稱 secret 環境變數名稱。
  這是引用名稱，不是帳號或 secret 值。

.PARAMETER PasswordSecretName
  CredentialSource=SecretReference 時必填的密碼 secret 環境變數名稱。
  這是引用名稱，不是密碼值；工具不會讀取或輸出該值。

.PARAMETER DomainSecretName
  CredentialSource=SecretReference 時選填的網域 secret 環境變數名稱。
  未提供時會清除這次程序先前可能遺留的 bridge 設定，避免 profile 間狀態殘留。

.PARAMETER NoRestore
  Pass --no-restore to dotnet test (useful when packages already restored).
#>
[CmdletBinding()]
param(
    [switch]$EnableLive,
    [string]$WebApiRoot,
    [ValidateSet("9.1", "8.2")]
    [string]$CeVersion = "9.1",
    [string]$ContactId,
    [ValidateSet("HostIdentity", "SecretReference")]
    [string]$CredentialSource = "HostIdentity",
    [string]$ProfileAlias,
    [string]$UserNameSecretName,
    [string]$PasswordSecretName,
    [string]$DomainSecretName,
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
if (-not [string]::IsNullOrWhiteSpace($WebApiRoot)) {
    Write-Info "WebApiRoot: $WebApiRoot"
}
Write-Info "CeVersion: $CeVersion"
Write-Info "CredentialSource: $CredentialSource"

if (-not $EnableLive) {
    Write-Info "Live mode is OFF. CRM was not contacted."
    Write-Info "Explicitly provide -WebApiRoot <https://.../api/data/v9.1/> when enabling live mode."
    Write-Info "Example:"
    Write-Info "  powershell -NoProfile -File .\docs\scripts\Invoke-DynamicsLiveSmoke.ps1 -EnableLive -WebApiRoot <https://.../api/data/v9.1/>"
    exit 0
}

if ([string]::IsNullOrWhiteSpace($WebApiRoot)) {
    throw "Live mode requires an explicit -WebApiRoot <https://.../api/data/v9.1/>. No CRM request was made."
}

if ($ContactId -and [string]::IsNullOrWhiteSpace($ProfileAlias)) {
    throw "Fee smoke requires an explicit -ProfileAlias. No CRM request was made."
}

# 由真正 connector 以同一組 root、驗證模式與 timeout 執行 WhoAmI，避免把與實際
# 連線語意不同的匿名 HEAD 結果當作 health 或 authentication 結論。
Write-Info "Running connector-owned WhoAmI smoke; no separate HEAD preflight is used."

$smokeEnvironmentNames = @(
    "DYNAMICS_SMOKE_ENABLED",
    "DYNAMICS_SMOKE_WEBAPI_ROOT",
    "DYNAMICS_SMOKE_CE_VERSION",
    "DYNAMICS_SMOKE_CREDENTIAL_SOURCE",
    "DYNAMICS_SMOKE_PROFILE_ALIAS",
    "DYNAMICS_SMOKE_USERNAME_SECRET",
    "DYNAMICS_SMOKE_PASSWORD_SECRET",
    "DYNAMICS_SMOKE_DOMAIN_SECRET",
    "DYNAMICS_SMOKE_CONTACT_ID"
)
$originalSmokeEnvironment = @{}
foreach ($smokeEnvironmentName in $smokeEnvironmentNames) {
    # 只記住 bridge 變數的原始 Process 值；不讀取 secret reference 所指向的任何值。
    $originalSmokeEnvironment[$smokeEnvironmentName] = [Environment]::GetEnvironmentVariable(
        $smokeEnvironmentName,
        [EnvironmentVariableTarget]::Process)
}

# bridge 變數只屬於即將建立的 dotnet 子程序。finally 必須還原呼叫端既有的 Process
# 環境，避免下一個 profile 取得 root、驗證模式、contactId 或 secret reference 名稱。
$exitCode = 1
try {

    $env:DYNAMICS_SMOKE_ENABLED = "1"
    $env:DYNAMICS_SMOKE_WEBAPI_ROOT = $WebApiRoot
    $env:DYNAMICS_SMOKE_CE_VERSION = $CeVersion
    $env:DYNAMICS_SMOKE_CREDENTIAL_SOURCE = $CredentialSource
    if ([string]::IsNullOrWhiteSpace($ProfileAlias)) {
        Remove-Item Env:DYNAMICS_SMOKE_PROFILE_ALIAS -ErrorAction SilentlyContinue
    } else {
        $env:DYNAMICS_SMOKE_PROFILE_ALIAS = $ProfileAlias
    }

    if ($CredentialSource -eq "SecretReference") {
        if ([string]::IsNullOrWhiteSpace($UserNameSecretName) -or [string]::IsNullOrWhiteSpace($PasswordSecretName)) {
            throw "SecretReference requires explicit -UserNameSecretName and -PasswordSecretName. No CRM request was made."
        }

        # 只允許標準環境變數名稱，防止參數被解釋為路徑、其他 provider 或不受控的
        # variable expression；所有檢查都只讀取「是否存在」，不接觸或輸出 secret 值。
        $secretReferenceNames = @($UserNameSecretName, $PasswordSecretName)
        if (-not [string]::IsNullOrWhiteSpace($DomainSecretName)) {
            $secretReferenceNames += $DomainSecretName
        }

        foreach ($secretReferenceName in $secretReferenceNames) {
            if ($secretReferenceName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
                throw "SecretReference names must use environment-variable syntax. No CRM request was made."
            }

            if (-not (Test-Path -LiteralPath ("Env:" + $secretReferenceName))) {
                throw "A required SecretReference environment variable is unavailable in this process. No CRM request was made."
            }
        }

        $env:DYNAMICS_SMOKE_USERNAME_SECRET = $UserNameSecretName
        $env:DYNAMICS_SMOKE_PASSWORD_SECRET = $PasswordSecretName
        if ([string]::IsNullOrWhiteSpace($DomainSecretName)) {
            Remove-Item Env:DYNAMICS_SMOKE_DOMAIN_SECRET -ErrorAction SilentlyContinue
        } else {
            $env:DYNAMICS_SMOKE_DOMAIN_SECRET = $DomainSecretName
        }
    } else {
        # 同一互動式 PowerShell 可能連續執行多個 profile。HostIdentity 分支必須清掉
        # 先前 SecretReference 產生的 bridge，確保 connector 不會跨次帶入 credential state。
        Remove-Item Env:DYNAMICS_SMOKE_USERNAME_SECRET -ErrorAction SilentlyContinue
        Remove-Item Env:DYNAMICS_SMOKE_PASSWORD_SECRET -ErrorAction SilentlyContinue
        Remove-Item Env:DYNAMICS_SMOKE_DOMAIN_SECRET -ErrorAction SilentlyContinue
    }

    if ($ContactId) {
        $env:DYNAMICS_SMOKE_CONTACT_ID = $ContactId
    } else {
        Remove-Item Env:DYNAMICS_SMOKE_CONTACT_ID -ErrorAction SilentlyContinue
    }

    # 啟用 live 後，測試組件內的「預設關閉」保護測試必須繼續保留給 CI；若整組執行，
    # 它們會正確偵測到 DYNAMICS_SMOKE_ENABLED=1，卻讓真正的 connector WhoAmI 成功結果
    # 被誤判為失敗。此工具的唯一真機契約是下列明確、唯讀且 connector-owned 的 WhoAmI。
    $dotnetArgs = @(
        "test",
        $project,
        "--nologo",
        "--filter",
        "FullyQualifiedName~LiveDynamicsWebApiSmokeTests.WhoAmI_live_smoke_when_enabled"
    )
    if ($NoRestore) {
        $dotnetArgs += "--no-restore"
    }

    Write-Info "Running: dotnet $($dotnetArgs -join ' ')"
    & dotnet @dotnetArgs
    $exitCode = $LASTEXITCODE
    Write-Info "dotnet test exit code: $exitCode"
    }
finally {
    foreach ($smokeEnvironmentName in $smokeEnvironmentNames) {
        [Environment]::SetEnvironmentVariable(
            $smokeEnvironmentName,
            $originalSmokeEnvironment[$smokeEnvironmentName],
            [EnvironmentVariableTarget]::Process)
    }
}

exit $exitCode
