<#
.SYNOPSIS
驗證 P6 本機 Gateway startup operator bridge 的安全契約。

.DESCRIPTION
本測試不啟動 Gateway、Worker、SQL 或 CE。它只檢查 bridge 的 UTF-8／CRLF、bounded
參數、固定 local profile selectors、sanitized outcome、精確 process cleanup 與禁止
回顯內容。真正的 startup smoke test 由操作者在 Lenovo Legion 執行 bridge；bridge
只輸出最後的去識別化 JSON。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Test-DynamicsOfficialWorkerP6LocalStartup.ps1'

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
        Assert-True ($bytes.Length -gt 0) 'Startup bridge is empty.'
        Assert-True (-not (
            $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) `
            'Startup bridge contains a UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True (-not [Regex]::IsMatch($text, '(?<!\r)\n')) `
            'Startup bridge contains LF-only line endings.'
        Assert-True ($text.EndsWith("`r`n", [StringComparison]::Ordinal)) `
            'Startup bridge lacks a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

try {
    Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        'Startup bridge is missing.'
    Assert-StrictTextFile -Path $scriptPath
    $source = [IO.File]::ReadAllText(
        $scriptPath,
        [Text.UTF8Encoding]::new($false, $true))

    foreach ($fragment in @(
        'Start-Process',
        'RedirectStandardOutput',
        'RedirectStandardError',
        'ReadBoundedDiagnosticText',
        'Get-GatewayStartupFailureClassification',
        'WaitForExit',
        'gateway-startup-failed-before-ready',
        'worker-ready-frame-not-published',
        'gateway-startup-unclassified',
        'failureClassification',
        'listenerReleased',
        'crm82',
        'crm91',
        'runtime.health.whoami',
        'runtime.pool.validate.connection',
        'Remove-Item -LiteralPath $temporaryRoot -Recurse -Force'
    )) {
        Assert-True $source.Contains($fragment) `
            'Startup bridge is missing a required lifecycle or selector boundary.'
    }

    foreach ($forbidden in @(
        'Invoke-WebRequest',
        'Invoke-RestMethod',
        'Read-Host',
        'CredentialManager',
        'password',
        'token',
        'cookie',
        'connection string',
        'Write-Host'
    )) {
        Assert-True (-not $source.Contains($forbidden)) `
            'Startup bridge contains an interactive, secret, or unbounded output path.'
    }

    'All official Worker P6 local startup bridge tests passed.'
}
finally {
    $source = $null
}
