<#
.SYNOPSIS
執行 P6 Official Worker connection-evidence wrapper 的隔離測試。

.DESCRIPTION
完整 fixture、allowlist、dry-run、sanitized-failure 與 deterministic cleanup 驗證由
Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1 統一持有，避免第二套假 Worker
artifact fixture 漂移。本 entry point 僅在 child PowerShell 執行該完整測試，並固定
輸出成功／失敗；它不啟動 Gateway、Worker 或 CE，也不讀取 Credential Manager。
child process 的輸出不被重播，因此任何 fixture 路徑或例外細節不會被擴散到呼叫端。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$compatibilityTests = Join-Path $PSScriptRoot 'Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1'
if (-not (Test-Path -LiteralPath $compatibilityTests -PathType Leaf)) {
    throw 'Official worker compatibility tests are unavailable.'
}

$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $compatibilityTests 2>&1)
    $exitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
    $output = $null
}

if ($exitCode -ne 0) {
    throw 'Official worker P6 evidence tests failed.'
}

'All official Worker P6 evidence harness tests passed.'
