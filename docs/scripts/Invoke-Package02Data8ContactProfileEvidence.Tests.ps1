<#
.SYNOPSIS
    驗證 P7.2 Slice B1/B2 handoff 與 descriptor initializer 的 fail-closed 契約。

.DESCRIPTION
    測試只使用暫存資料與 missing-repository 分支，不讀取 Credential blob、不啟動
    dotnet 子測試、不連線 CE、不修改 feature flag。決定性斷言包含：PowerShell 5.1
    語法、UTF-8/CRLF、sanitized JSON、既有 Slice A descriptor 的安全衍生、衝突拒絕、
    bounded child process 與 B1/B2 allowlist marker 的靜態邊界。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$handoffPath = Join-Path $PSScriptRoot 'Invoke-Package02Data8ContactProfileEvidence.ps1'
$initializerPath = Join-Path $PSScriptRoot 'Initialize-P72ContactProfileFixtureDescriptors.ps1'
$liveTestPath = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'ChurchReport.MemberInfo.Tests\LivePackage02Data8ContactProfileEvidenceTests.cs'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-profile-script-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-StrictTextFile {
    param([string] $Path)
    $bytes = $null
    try {
        Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) ('Required file is missing: ' + [IO.Path]::GetFileName($Path))
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True ($bytes.Length -gt 0) 'Checked file must not be empty.'
        Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Checked file must not contain UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True (-not [Regex]::IsMatch($text, '(?<!\r)\n')) 'Checked file must be CRLF-only.'
        Assert-True $text.EndsWith("`r`n", [StringComparison]::Ordinal) 'Checked file must end in CRLF.'
    }
    finally { if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) } }
}

function Write-StrictJsonFile {
    param([string] $Path, [object] $Value)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { [void][IO.Directory]::CreateDirectory($directory) }
    $json = $Value | ConvertTo-Json -Depth 6
    $text = ($json -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function Invoke-ScriptJson {
    param([string] $Path, [string[]] $Arguments)
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
    $jsonLines = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimStart().StartsWith('{') })
    Assert-True ($jsonLines.Count -eq 1) 'Script must emit exactly one sanitized JSON line.'
    return [pscustomobject]@{ ExitCode = $exitCode; JsonLine = [string]$jsonLines[0]; Value = ($jsonLines[0] | ConvertFrom-Json) }
}

function Import-ScriptFunction {
    param([string] $Path, [string] $FunctionName)
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Handoff script must have valid PowerShell syntax.'
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $FunctionName
    }, $true)
    Assert-True ($null -ne $functionAst) ('Required function is missing: ' + $FunctionName)
    $body = $functionAst.Body.Extent.Text.Trim()
    $body = $body.Substring(1, $body.Length - 2)
    Set-Item -Path ("Function:\global:{0}" -f $FunctionName) -Value ([ScriptBlock]::Create($body))
}

try {
    foreach ($path in @($PSCommandPath, $handoffPath, $initializerPath, $liveTestPath)) { Assert-StrictTextFile $path }
    foreach ($path in @($handoffPath, $initializerPath)) {
        $tokens = $null; $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        Assert-True ($errors.Count -eq 0) ('Windows PowerShell syntax is invalid: ' + [IO.Path]::GetFileName($path))
    }

    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    $sourcePath = Join-Path $fixtureRoot 'contact-basic-info-fixture.json'
    $destination = Join-Path $fixtureRoot 'derived'
    $contactId = [Guid]::NewGuid().ToString('D')
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Write-StrictJsonFile $sourcePath ([ordered]@{
        schemaVersion = 1; fixtureId = 'p7.2-contact-basic-info'; profileAlias = 'sunnyvalechback'; ceVersion = '9.1'; connector = 'Data8'; marker = 'p7.2-contact-basic-info'; contactId = $contactId; ownerIdentity = $identity
    })
    $initialized = Invoke-ScriptJson $initializerPath @('-SourceDescriptorPath', $sourcePath, '-DestinationDirectory', $destination)
    Assert-True ($initialized.ExitCode -eq 0 -and $initialized.Value.outcome -eq 'written' -and $initialized.Value.descriptorCount -eq 2) 'Initializer must derive exactly two descriptors.'
    $b1 = Get-Content -LiteralPath (Join-Path $destination 'contact-line-profile-fixture.json') -Raw | ConvertFrom-Json
    $b2 = Get-Content -LiteralPath (Join-Path $destination 'ungrouped-commitment-fixture.json') -Raw | ConvertFrom-Json
    Assert-True ($b1.contactId -ceq $contactId -and $b1.marker -ceq 'p7.2-contact-line-profile') 'B1 must reuse only the authorized contact identity.'
    Assert-True ($b2.marker -ceq 'p7.2-ungrouped-commitment' -and $null -eq $b2.PSObject.Properties['contactId']) 'B2 must remain read-only without arbitrary record identity.'
    $repeat = Invoke-ScriptJson $initializerPath @('-SourceDescriptorPath', $sourcePath, '-DestinationDirectory', $destination)
    Assert-True ($repeat.ExitCode -eq 0 -and $repeat.Value.outcome -eq 'written') 'Initializer must be idempotent when descriptors agree.'

    $missingRepository = Join-Path $fixtureRoot 'missing-repository'
    $preflight = Invoke-ScriptJson $handoffPath @('-RepositoryPath', $missingRepository, '-Json')
    Assert-True ($preflight.ExitCode -eq 1 -and $preflight.Value.outcome -eq 'error' -and $preflight.Value.reason -eq 'repository-invalid') 'Missing repository must fail before credential or operation access.'
    Assert-True (-not $preflight.Value.operationExecuted -and -not $preflight.Value.featureFlagChanged) 'Missing repository must execute no operation and change no flag.'
    foreach ($forbiddenValue in @($fixtureRoot, $contactId, 'speechmessage.crm91.p62')) { Assert-True (-not $preflight.JsonLine.Contains($forbiddenValue)) 'Sanitized output leaked path, GUID, or credential target.' }

    $source = [IO.File]::ReadAllText($handoffPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'speechmessage.crm91.p62', 'sunnyvalechback', 'WaitForExit(180000)',
        'P7_2_B1_EVIDENCE_JSON=', 'P7_2_B2_EVIDENCE_JSON=',
        'SPEECHMESSAGE_P7_2_B1_LIVE', 'SPEECHMESSAGE_P7_2_B2_LIVE',
        'contact-line-profile-fixture.json', 'ungrouped-commitment-fixture.json',
        'featureFlagChanged = $false', 'CredRead', 'CredFree', 'DtdProcessing',
        'Get-StrictB2EvidenceFile', 'P7_2_B2_EVIDENCE_PATH'
    )) { Assert-True $source.Contains($fragment) ('Handoff lacks required boundary: ' + $fragment) }
    foreach ($forbidden in @('Read-Host', 'Invoke-WebRequest', 'Invoke-RestMethod', 'Invoke-Expression', 'task.py start')) { Assert-True (-not $source.Contains($forbidden)) ('Handoff contains forbidden behavior: ' + $forbidden) }
    Assert-True (-not $source.Contains('New-HandoffResult (if (')) 'Windows PowerShell 5.1 must not receive an if statement as a parenthesized positional argument.'
    Assert-True (-not $source.Contains('$executionPairs = if (')) 'Windows PowerShell 5.1 must not collapse a single resumed execution pair into a hashtable.'

    $liveSource = [IO.File]::ReadAllText($liveTestPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        '[P72Data8B1LiveFact]', '[P72Data8B2LiveFact]',
        'P7_2_B1_EVIDENCE_JSON=', 'P7_2_B2_EVIDENCE_PATH', 'WriteB2EvidenceFile',
        'P72ContactProfileFixtureBridge.ExecuteAsync', 'P72UngroupedCommitmentFixtureBridge.ExecuteAsync',
        'DisposeRuntimeAsync', 'DisposeStore', 'DisposeLogger'
    )) { Assert-True $liveSource.Contains($fragment) ('Live lane lacks lifecycle or execution boundary: ' + $fragment) }

    Import-ScriptFunction $handoffPath 'Read-StrictJsonFile'
    Import-ScriptFunction $handoffPath 'Get-StrictB2EvidenceFile'
    $global:expectedB2OperationId = 'memberinfo.contact.count.ungrouped.commitment'
    $global:expectedProfileAlias = 'sunnyvalechback'
    $global:expectedDeploymentProfileAlias = 'crm91'
    $evidencePath = Join-Path $fixtureRoot 'strict-b2-evidence.json'
    Write-StrictJsonFile $evidencePath ([ordered]@{
        schemaVersion = 1; outcome = 'go'; reason = ''; operationId = $global:expectedB2OperationId;
        profileAlias = $global:expectedProfileAlias; deploymentProfileAlias = $global:expectedDeploymentProfileAlias;
        ceVersion = '9.1'; connector = 'Data8'; preflightOnly = $false; operationExecuted = $true;
        parityState = 'confirmed'; rowCount = 3; featureFlagChanged = $false
    })
    $parsedEvidence = Get-StrictB2EvidenceFile $evidencePath
    Assert-True ($parsedEvidence.outcome -eq 'go' -and $parsedEvidence.parityState -eq 'confirmed' -and $parsedEvidence.rowCount -eq 3) 'Strict B2 file parser must accept only the complete sanitized contract.'

    [ordered]@{ outcome = 'passed'; checks = 18 } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Force -Recurse }
    Remove-Variable -Name expectedB2OperationId, expectedProfileAlias, expectedDeploymentProfileAlias -Scope Global -ErrorAction SilentlyContinue
}
