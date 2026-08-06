<#
.SYNOPSIS
驗證 P6 Credential Manager 使用者名稱比對工具的去識別化與 fail-closed 契約。

.DESCRIPTION
本測試建立一個不含任何真實端點、帳號或密碼的暫存輸入，並以不存在的 profile 路徑呼叫
Credential Manager 使用者名稱比對工具。它保護的契約是：工具在讀取 Credential Manager 前
先拒絕無效輸入、只輸出去識別化狀態、不建立 Gateway／Worker 或 CE 流量，且不把帳號、
credential target 或密碼寫入輸出。靜態檢查另保護 native Credential handle 必須由同一工具
在 finally 釋放，並禁止讀取 credential blob。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Test-DynamicsOfficialWorkerCredentialIdentity.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'speechmessage-p6-credential-identity-test-' + [Guid]::NewGuid().ToString('N'))

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
    Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        'Credential identity diagnostic script is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    [void](New-Item -ItemType Directory -Path $fixtureRoot -Force)
    $missingProfile = Join-Path $fixtureRoot 'missing-profile.json'
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ProfileInputPath', $missingProfile,
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

    Assert-True ($exitCode -eq 1) 'A missing profile input must fail before any credential read or prompt.'
    $evidence = ($result -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
    Assert-True ($evidence.schemaVersion -eq 1) 'Invalid input evidence must retain the fixed schema version.'
    Assert-True ($evidence.outcome -eq 'error') 'A missing profile input must report error.'
    Assert-True ($evidence.reason -eq 'profile-input-invalid') 'A missing profile input must use the fixed sanitized reason.'
    Assert-True (-not $evidence.operationExecuted) 'Credential identity validation must not execute a CE operation.'
    Assert-True (-not $evidence.featureFlagChanged) 'Credential identity validation must not change a feature flag.'
    Assert-True (-not ($evidence.PSObject.Properties.Name -match 'credential|target|user|account|password|secret')) `
        'Invalid input output must not disclose credential identity metadata.'

    # 此 fixture 的 target 與帳號皆為不存在的測試值。透過 redirected stdin 驗證工具真的
    # 編譯 native interop、讀取兩個 profile alias，並在 target 不存在時只回傳固定 no-go 狀態；
    # 不會接觸任何真實 Credential Manager target、密碼、Gateway、Worker 或 CE。
    $profileInputPath = Join-Path $fixtureRoot 'approved-profile-input.json'
    $profileFixture = [ordered]@{
        schemaVersion = 1
        profiles = @(
            [ordered]@{
                profileAlias = 'crm82'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = 'synthetic-crm82-credential'
                }
            },
            [ordered]@{
                profileAlias = 'crm91'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = 'synthetic-crm91-credential'
                }
            }
        )
    }
    [IO.File]::WriteAllText(
        $profileInputPath,
        ($profileFixture | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $scriptPath +
        '" -ProfileInputPath "' + $profileInputPath +
        '" -Crm82ExpectedUserName "synthetic.user82@example.test"' +
        ' -Crm91ExpectedUserName "SYNTHETIC\\user91" -Json'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    Assert-True ($null -ne $process) 'Credential identity diagnostic process must start for the synthetic fixture.'
    try {
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        [void]$process.WaitForExit(20000)
        Assert-True ($process.HasExited) 'Synthetic credential identity process must have a bounded exit.'
        Assert-True ($process.ExitCode -eq 2) 'Unavailable synthetic targets must return sanitized no-go.'
        Assert-True ([string]::IsNullOrEmpty($standardError)) 'Synthetic credential identity run must not emit diagnostic stderr.'
        $jsonLines = @($standardOutput -split "`r?`n" | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimStart().StartsWith('{')
        })
        Assert-True ($jsonLines.Count -eq 1) 'Synthetic credential identity run must emit exactly one result JSON line.'
        $syntheticEvidence = $jsonLines[0] | ConvertFrom-Json -ErrorAction Stop
        Assert-True ($syntheticEvidence.outcome -eq 'no-go') 'Unavailable synthetic targets must not report go.'
        Assert-True (@($syntheticEvidence.profiles).Count -eq 2) 'Synthetic credential identity run must report both fixed aliases.'
        Assert-True (@($syntheticEvidence.profiles | Where-Object {
            $_.credentialUserNameState -ne 'credential-identity-unreadable'
        }).Count -eq 0) 'Unavailable synthetic targets must not be reported as account mismatches.'
        Assert-True (-not $syntheticEvidence.operationExecuted) 'Synthetic credential identity run must not execute CE.'
        Assert-True (-not $syntheticEvidence.featureFlagChanged) 'Synthetic credential identity run must not change feature flags.'
        Assert-True (-not ($jsonLines[0] -match 'synthetic-crm82|synthetic-crm91|synthetic\.user82|SYNTHETIC')) `
            'Synthetic result JSON must not disclose fixture identity metadata.'
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
        $standardOutput = $null
        $standardError = $null
        $jsonLines = $null
        $syntheticEvidence = $null
    }

    $source = [IO.File]::ReadAllText($scriptPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'Read-Host',
        'CredRead',
        'CredFree',
        'MatchesCredentialUserName',
        'ConvertFrom-Json',
        'ConvertTo-Json',
        'profile-input-invalid',
        'matches-operator-provided-ifd-login',
        'does-not-match-operator-provided-ifd-login',
        'credential-identity-unreadable',
        'operationExecuted',
        'featureFlagChanged'
    )) {
        Assert-True $source.Contains($fragment) 'Credential identity diagnostic lacks a required contract boundary.'
    }

    foreach ($forbidden in @(
        'Invoke-WebRequest',
        'Invoke-RestMethod',
        'Start-Process',
        'NamedPipeServerStream',
        'CredentialBlob',
        'Marshal.Copy',
        'Write-Host',
        'Get-StoredCredential'
    )) {
        Assert-True (-not $source.Contains($forbidden)) 'Credential identity diagnostic contains a forbidden network, process, blob, or disclosure path.'
    }

    'All official Worker credential identity diagnostic tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }

    $source = $null
    $result = $null
}
