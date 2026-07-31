#Requires -Version 5.1
<#
.SYNOPSIS
  驗證 Dynamics 真機 smoke 工具不會以過時預設目標誤觸正式 CRM。

.DESCRIPTION
  此測試以新的 PowerShell 子程序執行工具，讓工具內的 exit code 不會中斷測試程序。
  它只驗證不啟用 live 時的說明，以及啟用 live 卻缺少明確目標時的 fail-closed 行為；
  不會對 Dynamics、ADFS、DNS、WinRM 或任何外部服務發送請求。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Contains {
    <#
    .SYNOPSIS
      確保子程序的合併輸出包含預期的安全提示。

    .DESCRIPTION
      這個唯一的斷言擁有測試失敗訊息，避免每個案例各自複製字串搜尋邏輯，
      並在輸出或工具契約被意外改變時提供可追溯的失敗原因。
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Actual,
        [Parameter(Mandatory)]
        [string]$Expected,
        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($Actual -notmatch [regex]::Escape($Expected)) {
        throw "$Context. Expected output to contain '$Expected'. Actual output: $Actual"
    }
}

$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $repositoryPath 'docs\scripts\Invoke-DynamicsLiveSmoke.ps1'

# 未啟用 live 時只能提供中立操作說明，不能透露或預選歷史 CRM host。
# StrictMode 不會預先建立這個 PowerShell 自動變數；先宣告它，才能把子程序的
# 真正結束碼與未執行子程序的測試基礎設施錯誤清楚區分。
$global:LASTEXITCODE = 0
$guidance = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -RepositoryPath $repositoryPath 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Dry-run guidance must exit 0; actual exit code was $LASTEXITCODE. Output: $guidance"
}

Assert-Contains -Actual $guidance -Expected '-WebApiRoot <https://.../api/data/v9.1/>' -Context 'Dry-run guidance must require an explicit target'
if ($guidance -match 'jesus\.speechmessage\.com\.tw') {
    throw "Dry-run guidance must not expose or select the obsolete jesus target. Output: $guidance"
}

# live 模式缺少目標時必須在任何遠端請求前失敗，避免誤打到舊部署。
$originalErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'SilentlyContinue'
$missingTargetCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' +
    $scriptPath + '" -EnableLive -RepositoryPath "' + $repositoryPath + '" >nul 2>&1'
$null = & cmd.exe /d /c $missingTargetCommand
$missingTargetExitCode = $LASTEXITCODE
$ErrorActionPreference = $originalErrorActionPreference
if ($missingTargetExitCode -eq 0) {
    throw 'Live mode without -WebApiRoot must fail closed.'
}

$source = Get-Content -LiteralPath $scriptPath -Raw
Assert-Contains -Actual $source -Expected '-WebApiRoot <https://.../api/data/v9.1/>' -Context 'Missing target error must identify the required parameter'
if ($source -match 'DYNAMICS_JESUS_') {
    throw 'SecretReference configuration must not retain a target-specific JESUS environment-variable default.'
}
Assert-Contains -Actual $source -Expected '[string]$UserNameSecretName' -Context 'SecretReference input must be explicit'
Assert-Contains -Actual $source -Expected '[string]$PasswordSecretName' -Context 'SecretReference input must be explicit'

# 啟用 live 時，整個測試組件內仍保留兩個「預設必須關閉」的保護測試；若不鎖定
# connector-owned WhoAmI 測試，即使 CRM 回應成功也會被那些保護測試誤判為失敗。
# 這個契約不觸碰任何外部服務，僅防止工具日後回退為未篩選的整組 test 執行。
Assert-Contains -Actual $source -Expected 'FullyQualifiedName~LiveDynamicsWebApiSmokeTests.WhoAmI_live_smoke_when_enabled' -Context 'Live smoke must execute only the explicit connector-owned WhoAmI test'

# 工具必須把為 dotnet 子程序設定的所有 smoke bridge 環境變數還原為呼叫前的 Process
# 值；互動式 PowerShell 連續驗證不同 profile 時，不能殘留 root、驗證模式或 secret
# reference 名稱。這個靜態契約要求顯式 snapshot 與 finally 還原，避免 exit/error
# 路徑繞過清理。
Assert-Contains -Actual $source -Expected '$originalSmokeEnvironment' -Context 'Live smoke must snapshot its process environment before setting bridge variables'
Assert-Contains -Actual $source -Expected '[Environment]::SetEnvironmentVariable' -Context 'Live smoke must restore bridge variables through the process environment API'
Write-Host 'Invoke-DynamicsLiveSmoke script contract passed.'
