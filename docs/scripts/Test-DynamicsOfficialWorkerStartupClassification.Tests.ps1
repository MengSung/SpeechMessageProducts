<#
.SYNOPSIS
驗證 P6 實際 Official Worker 啟動分類診斷的安全邊界。

.DESCRIPTION
此測試以不存在的測試擁有 executable 路徑呼叫診斷腳本，保護三項契約：
無效輸入不得建立 Worker、輸出僅能包含去識別化狀態、以及暫存資源必須由
診斷程序在結束時清理。測試不會讀取 Credential Manager、啟動實際 Worker，
或傳送任何 CE 要求。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Test-DynamicsOfficialWorkerStartupClassification.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'speechmessage-p6-startup-classification-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-StrictTextFile {
    param([string] $Path)

    $bytes = $null
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True ($bytes.Length -gt 0) 'Checked script must not be empty.'
        Assert-True (-not (
            $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Checked script must not contain a UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True (-not [Regex]::IsMatch($text, '(?<!\r)\n')) 'Checked script must not contain LF-only line endings.'
        Assert-True ($text.EndsWith("`r`n", [StringComparison]::Ordinal)) 'Checked script must end with a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

try {
    Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) 'Startup-classification diagnostic script is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    [void](New-Item -ItemType Directory -Path $fixtureRoot -Force)
    $missingExecutable = Join-Path $fixtureRoot 'missing-worker.exe'
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ProfileAlias', 'crm91',
        '-WorkerExecutablePath', $missingExecutable,
        '-WorkerKind', 'OfficialCrm91Worker',
        '-PackageLockId', 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60',
        '-ProfileGenerationId', 'crm91-test-generation-0001',
        '-Json'
    )
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $result = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    Assert-True ($exitCode -eq 1) 'An invalid Worker path must exit fail-closed.'
    $evidence = ($result -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
    Assert-True ($evidence.outcome -eq 'error') 'An invalid Worker path must report error.'
    Assert-True ($evidence.reason -eq 'worker-startup-input-invalid') 'An invalid Worker path must use the fixed sanitized reason.'
    Assert-True (-not $evidence.pipeConnected) 'Invalid input must not connect a named pipe.'
    Assert-True ($evidence.PSObject.Properties.Name -contains 'readyFrameObserved') 'Sanitized evidence must declare whether a READY frame was observed.'
    Assert-True ($evidence.PSObject.Properties.Name -contains 'workerExitCode') 'Sanitized evidence must declare the fixed Worker exit code when observed.'
    Assert-True ($null -eq $evidence.workerExitCode) 'Invalid input must not claim that a Worker exit code was observed.'
    Assert-True (-not $evidence.readyFrameObserved) 'Invalid input must not observe a READY frame.'
    Assert-True (-not $evidence.operationExecuted) 'Invalid input must not execute a CE operation.'
    Assert-True (-not $evidence.featureFlagChanged) 'Invalid input must not change a feature flag.'
    Assert-True (-not (Test-Path -LiteralPath $missingExecutable)) 'Test must not create a missing Worker executable.'

    $source = [IO.File]::ReadAllText($scriptPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'NamedPipeServerStream',
        'BeginWaitForConnection',
        'BeginRead',
        'ProcessStartInfo',
        'RedirectStandardOutput',
        'RedirectStandardError',
        'CopyToAsync([IO.Stream]::Null)',
        'sdk-client-not-ready',
        'identity-probe-not-ready',
        'sdk-authentication-failure',
        'sdk-secure-channel-failure',
        'sdk-transport-failure',
        'sdk-unclassified-failure',
        'sdk-diagnostic-unavailable',
        'sdk-initialization-failure',
        'workerExitCode',
        'worker-reported-ready',
        'WaitForExit',
        'Kill()'
    )) {
        Assert-True $source.Contains($fragment) 'Diagnostic script lacks a required startup or cleanup boundary.'
    }

    foreach ($forbidden in @(
        'Invoke-WebRequest',
        'Invoke-RestMethod',
        'Read-Host',
        'Write-Host',
        'Start-Process',
        'Get-StoredCredential',
        'ConvertTo-SecureString'
    )) {
        Assert-True (-not $source.Contains($forbidden)) 'Diagnostic script contains a forbidden interactive, secret, or arbitrary network path.'
    }

    'All Official Worker startup-classification tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
    $source = $null
    $result = $null
}
