<#
.SYNOPSIS
驗證 P7.1 Data8 真機唯讀 evidence handoff 的輸入、去識別化輸出與文字格式契約。

.DESCRIPTION
測試只以不存在的暫存 worktree 呼叫 handoff，因此驗證 repository-invalid 必須在讀取 Windows
Credential Manager、啟動 dotnet test 或連線 D365 前失敗關閉；測試本身不提示密碼、啟動
Gateway 或修改 ChurchReport feature flag。它保護的契約是：無效路徑先失敗關閉、只輸出固定
JSON 分類，且任何 operator 傳入的 GUID／period 或絕對路徑都不會進入輸出。靜態檢查也保護
script 必須使用 CredRead/CredFree 的有界 native handle 生命週期，且不得改寫設定或使用
Official Worker。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Invoke-Package01Data8ReadEvidence.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-1-handoff-test-' + [Guid]::NewGuid().ToString('N'))

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
        Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Checked script must not contain a UTF-8 BOM.'
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
    Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) 'P7.1 Data8 evidence handoff script is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    [void](New-Item -ItemType Directory -Path $fixtureRoot -Force)
    $missingRepository = Join-Path $fixtureRoot 'missing-repository'
    $fixtureContactId = '11111111-1111-1111-1111-111111111111'
    $fixtureBookingId = '22222222-2222-2222-2222-222222222222'
    $fixtureLessonId = '33333333-3333-3333-3333-333333333333'
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-RepositoryPath', $missingRepository,
        '-ContactId', $fixtureContactId,
        '-DedicationBookingId', $fixtureBookingId,
        '-DiscipleLessonId', $fixtureLessonId,
        '-StartDate', '2026-01-01T00:00:00Z',
        '-EndDate', '2026-01-31T23:59:59Z',
        '-PaidPeriod', '202601',
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

    Assert-True ($exitCode -eq 1) 'Missing repository must fail before dotnet test or a CE operation.'
    $jsonLines = @($result | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimStart().StartsWith('{')
    })
    Assert-True ($jsonLines.Count -eq 1) 'Invalid input must emit exactly one JSON line.'
    $evidence = $jsonLines[0] | ConvertFrom-Json -ErrorAction Stop
    Assert-True ($evidence.schemaVersion -eq 1) 'Invalid input evidence must retain schema version 1.'
    Assert-True ($evidence.outcome -eq 'error') 'Missing repository must report error.'
    Assert-True ($evidence.reason -eq 'repository-invalid') 'Missing repository must use a fixed sanitized reason.'
    Assert-True (-not $evidence.operationExecuted) 'Missing repository must not execute a CE operation.'
    Assert-True (-not $evidence.featureFlagChanged) 'Handoff must never change the feature flag.'
    foreach ($marker in @($fixtureRoot, $fixtureContactId, $fixtureBookingId, $fixtureLessonId, '202601')) {
        Assert-True (-not $jsonLines[0].Contains($marker)) 'Sanitized invalid-input JSON must not disclose operator data.'
    }

    $source = [IO.File]::ReadAllText($scriptPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'CredRead',
        'CredFree',
        'public static class CredentialReader',
        'speechmessage.crm91.p62',
        'SPEECHMESSAGE_P7_1_LIVE',
        'P7_1_CONTACT_ID',
        'P7_1_DEDICATION_BOOKING_ID',
        'P7_1_DISCIPLE_LESSON_ID',
        'P7_1_START_DATE',
        'P7_1_END_DATE',
        'P7_1_PAID_PERIOD',
        'P7_1_EVIDENCE_JSON=',
        'operationExecuted',
        'featureFlagChanged',
        'repository-invalid',
        'evidence-result-unavailable'
    )) {
        Assert-True $source.Contains($fragment) 'P7.1 handoff lacks a required contract boundary.'
    }

    foreach ($forbidden in @(
        'Read-Host',
        'OfficialWorker',
        'Invoke-WebRequest',
        'Invoke-RestMethod',
        'Invoke-Expression',
        'Package01FeeReadsEnabled = $true'
    )) {
        Assert-True (-not $source.Contains($forbidden)) ('P7.1 handoff must not contain forbidden behavior: ' + $forbidden)
    }

    # 環境變數屬於呼叫端 process；即使 repository 或 fixture 在最早期失敗，finally 也只能還原快照，
    # 不能把呼叫端原有的 CRM_PASSWORD 或 P7.1 opt-in state 清空。此來源順序斷言直接保護 exit 導致
    # PowerShell 無法在同一 process 以一般 integration test 觀察環境的限制。
    $snapshotIndex = $source.IndexOf('foreach ($name in $inputEnvironmentNames)')
    $repositoryValidationIndex = $source.IndexOf('$resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)')
    Assert-True ($snapshotIndex -ge 0 -and $snapshotIndex -lt $repositoryValidationIndex) 'Environment snapshot must precede every validation early-exit path.'

    # 暫存 TRX 可能被防毒或索引器短暫鎖住；清除失敗不能阻斷憑證、fixture 與 process environment 的後續
    # deterministic cleanup。只接受將 Remove-Item 包在同一個明確 try/catch 的模式，避免 ErrorActionPreference=Stop
    # 使 finally 提早中止。
    $temporaryCleanupIndex = $source.IndexOf('Remove-Item -LiteralPath $temporaryDirectory')
    $cleanupTryIndex = $source.LastIndexOf('try {', $temporaryCleanupIndex, [StringComparison]::Ordinal)
    $cleanupCatchIndex = $source.IndexOf('catch {', $temporaryCleanupIndex, [StringComparison]::Ordinal)
    Assert-True ($temporaryCleanupIndex -ge 0 -and $cleanupTryIndex -ge 0 -and $cleanupCatchIndex -gt $temporaryCleanupIndex) 'Temporary-directory cleanup must not interrupt later finally cleanup.'

    [pscustomobject]@{
        outcome = 'passed'
        checks = 6
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Force -Recurse
    }
}
