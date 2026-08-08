<#
.SYNOPSIS
驗證 P7.2 contact basic-info fixture preflight 的 fail-closed、去識別化與格式契約。

.DESCRIPTION
測試只使用不存在或暫存的 repository／fixture，不啟動 dotnet、不讀取 Credential
blob、不登入 D365、不執行 CE operation、不修改 feature flag。它保護的契約是：
環境快照早於所有 early exit、matrix/profile/fixture 失敗使用固定分類、輸出不洩漏
GUID／路徑／credential target、以及新增腳本遵守 UTF-8 no-BOM／CRLF-only/final CRLF。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Invoke-Package02Data8ContactBasicInfoWriteEvidence.ps1'
$liveTestPath = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'ChurchReport.MemberInfo.Tests\LivePackage02Data8ContactBasicInfoWriteEvidenceTests.cs'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-preflight-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
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
        Assert-True $text.EndsWith("`r`n", [StringComparison]::Ordinal) 'Checked script must end with a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    }
}

function Write-StrictTextFile {
    param([string] $Path, [string] $Text)

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $normalized = ($Text -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

function Write-StrictJsonFile {
    param([string] $Path, [object] $Value)
    Write-StrictTextFile -Path $Path -Text ($Value | ConvertTo-Json -Depth 8)
}

function Import-ScriptFunction {
    param(
        [string] $Path,
        [string] $FunctionName
    )

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Production handoff must be valid PowerShell syntax.'
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $FunctionName
    }, $true)
    Assert-True ($null -ne $functionAst) ('Required script function is missing: ' + $FunctionName)
    $bodyText = $functionAst.Body.Extent.Text.Trim()
    $bodyText = $bodyText.Substring(1, $bodyText.Length - 2)
    Set-Item -Path ("Function:\global:{0}" -f $FunctionName) -Value ([ScriptBlock]::Create($bodyText))
}

function New-TestRepository {
    param([string] $Root)

    $matrixSource = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $matrixTarget = Join-Path $Root '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    Write-StrictTextFile -Path $matrixTarget -Text ([IO.File]::ReadAllText($matrixSource, [Text.UTF8Encoding]::new($false, $true)))
    Write-StrictTextFile -Path (Join-Path $Root 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj') -Text '<Project Sdk="Microsoft.NET.Sdk"></Project>'
    Write-StrictTextFile -Path (Join-Path $Root 'SpeechMessageProducts.ChurchReport\appsettings.json') -Text @'
{
  "CrmConnection": {
    "OrganizationCatalog": {
      "sunnyvalechback": { "CeVersion": "9.1", "ServiceUri": "https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc" }
    }
  }
}
'@
    Write-TestDevelopmentConfiguration -Root $Root -Mode 'Embedded'
}

function Write-TestDevelopmentConfiguration {
    param(
        [string] $Root,
        [string] $Mode,
        [bool] $Package02Enabled = $false
    )

    $package02Value = if ($Package02Enabled) { 'true' } else { 'false' }

    Write-StrictTextFile -Path (Join-Path $Root 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') -Text @"
{
  "DynamicsAccess": {
    "Package01FeeReadsEnabled": false,
    "Package02ContactBasicInfoUpdatesEnabled": $package02Value,
    "ConnectionMode": "$Mode",
    "ProfileAlias": "sunnyvalechback"
  }
}
"@
}

function Write-TestProfileInput {
    param([string] $Path, [string] $CredentialTarget)

    Write-StrictJsonFile -Path $Path -Value ([ordered]@{
        schemaVersion = 1
        profiles = @(
            [ordered]@{ profileAlias = 'crm82' },
            [ordered]@{
                profileAlias = 'crm91'
                workerKind = 'OfficialCrm91Worker'
                authentication = 'Ifd'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = $CredentialTarget
                }
            }
        )
    })
}

function Write-TestFixtureDescriptor {
    param(
        [string] $Path,
        [string] $ContactId,
        [string] $Marker = 'p7.2-contact-basic-info',
        [string] $CeVersion = '9.1',
        [string] $Connector = 'Data8'
    )

    Write-StrictJsonFile -Path $Path -Value ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-contact-basic-info'
        profileAlias = 'sunnyvalechback'
        ceVersion = $CeVersion
        connector = $Connector
        marker = $Marker
        contactId = $ContactId
        ownerIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    })
}

function Invoke-Preflight {
    param(
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $FixturePath,
        [string] $CommandPath = $scriptPath,
        [switch] $ExecuteFixture
    )
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $CommandPath,
        '-RepositoryPath', $RepositoryPath,
        '-ProfileInputPath', $ProfilePath,
        '-FixtureDescriptorPath', $FixturePath,
        '-Json'
    )
    if ($ExecuteFixture) {
        $arguments += '-ExecuteFixture'
    }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $jsonLines = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimStart().StartsWith('{') })
    Assert-True ($jsonLines.Count -eq 1) 'Preflight must emit exactly one JSON line.'
    return [pscustomobject]@{ ExitCode = $exitCode; JsonLine = [string]$jsonLines[0]; Evidence = ($jsonLines[0] | ConvertFrom-Json) }
}

try {
    Assert-True (Test-Path -LiteralPath $scriptPath -PathType Leaf) 'P7.2 preflight script is missing.'
    Assert-True (Test-Path -LiteralPath $liveTestPath -PathType Leaf) 'P7.2 opt-in live evidence test is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath
    Assert-StrictTextFile -Path $liveTestPath

    [void](New-Item -ItemType Directory -Path $fixtureRoot -Force)
    $missingRepository = Join-Path $fixtureRoot 'missing-repository'
    $profilePath = Join-Path $fixtureRoot 'official-worker-profile-input.json'
    $fixturePath = Join-Path $fixtureRoot 'contact-basic-info-fixture.json'
    $result = Invoke-Preflight -RepositoryPath $missingRepository -ProfilePath $profilePath -FixturePath $fixturePath

    Assert-True ($result.ExitCode -eq 1) 'Missing repository must fail before profile or credential access.'
    Assert-True ($result.Evidence.schemaVersion -eq 1) 'Schema version must be 1.'
    Assert-True ($result.Evidence.outcome -eq 'error') 'Missing repository must report error.'
    Assert-True ($result.Evidence.reason -eq 'repository-invalid') 'Missing repository reason must be sanitized.'
    Assert-True (-not $result.Evidence.operationExecuted) 'Preflight must never execute an operation.'
    Assert-True (-not $result.Evidence.featureFlagChanged) 'Preflight must never change a feature flag.'
    foreach ($forbiddenValue in @($fixtureRoot, '11111111-1111-1111-1111-111111111111', 'speechmessage.crm91.p62')) {
        Assert-True (-not $result.JsonLine.Contains($forbiddenValue)) 'Sanitized output leaked operator or credential data.'
    }

    $executeWithMissingRepository = Invoke-Preflight `
        -RepositoryPath $missingRepository `
        -ProfilePath $profilePath `
        -FixturePath $fixturePath `
        -ExecuteFixture
    Assert-True ($executeWithMissingRepository.ExitCode -eq 1) 'Explicit execution must still validate the repository before credential or process access.'
    Assert-True ($executeWithMissingRepository.Evidence.reason -eq 'repository-invalid') 'Explicit execution must retain the sanitized repository failure.'
    Assert-True (-not $executeWithMissingRepository.Evidence.preflightOnly) 'The result must record explicit execution intent without claiming an operation ran.'
    Assert-True (-not $executeWithMissingRepository.Evidence.operationExecuted) 'Repository failure must occur before any live operation.'

    $testRepository = Join-Path $fixtureRoot 'repository'
    $fixtureContactId = '11111111-1111-1111-1111-111111111111'
    New-TestRepository -Root $testRepository
    Write-TestProfileInput -Path $profilePath -CredentialTarget 'speechmessage.crm91.p62'
    Write-TestFixtureDescriptor -Path $fixturePath -ContactId $fixtureContactId

    Write-TestDevelopmentConfiguration -Root $testRepository -Mode 'DedicatedGateway'
    $configMismatch = Invoke-Preflight -RepositoryPath $testRepository -ProfilePath $profilePath -FixturePath $fixturePath
    Assert-True ($configMismatch.ExitCode -eq 2 -and $configMismatch.Evidence.reason -eq 'churchreport-config-invalid') 'Wrong ChurchReport mode must fail before credential access.'
    Write-TestDevelopmentConfiguration -Root $testRepository -Mode 'Embedded'

    Write-TestDevelopmentConfiguration -Root $testRepository -Mode 'Embedded' -Package02Enabled $true
    $enabledConsumerMismatch = Invoke-Preflight -RepositoryPath $testRepository -ProfilePath $profilePath -FixturePath $fixturePath
    Assert-True ($enabledConsumerMismatch.ExitCode -eq 2 -and $enabledConsumerMismatch.Evidence.reason -eq 'churchreport-config-invalid') 'Enabled Package02 consumer traffic must fail before credential access.'
    Assert-True (-not $enabledConsumerMismatch.Evidence.featureFlagChanged) 'Preflight must report without changing the enabled flag.'
    Write-TestDevelopmentConfiguration -Root $testRepository -Mode 'Embedded'

    Write-TestProfileInput -Path $profilePath -CredentialTarget 'wrong.target'
    $profileMismatch = Invoke-Preflight -RepositoryPath $testRepository -ProfilePath $profilePath -FixturePath $fixturePath
    Assert-True ($profileMismatch.ExitCode -eq 2 -and $profileMismatch.Evidence.reason -eq 'profile-input-invalid') 'Wrong crm91 credential reference must fail closed.'
    Write-TestProfileInput -Path $profilePath -CredentialTarget 'speechmessage.crm91.p62'

    foreach ($invalidFixture in @(
        @{ Marker = 'wrong-marker'; CeVersion = '9.1'; Connector = 'Data8' },
        @{ Marker = 'p7.2-contact-basic-info'; CeVersion = '8.2'; Connector = 'Data8' },
        @{ Marker = 'p7.2-contact-basic-info'; CeVersion = '9.1'; Connector = 'OfficialCrm91Worker' }
    )) {
        Write-TestFixtureDescriptor -Path $fixturePath -ContactId $fixtureContactId -Marker $invalidFixture.Marker -CeVersion $invalidFixture.CeVersion -Connector $invalidFixture.Connector
        $fixtureMismatch = Invoke-Preflight -RepositoryPath $testRepository -ProfilePath $profilePath -FixturePath $fixturePath
        Assert-True ($fixtureMismatch.ExitCode -eq 2 -and $fixtureMismatch.Evidence.reason -eq 'fixture-input-invalid') 'Marker, CE, and connector mismatches must fail before credential access.'
        Assert-True (-not $fixtureMismatch.JsonLine.Contains($fixtureContactId)) 'Fixture mismatch output must not disclose the contact GUID.'
    }

    Write-TestFixtureDescriptor -Path $fixturePath -ContactId $fixtureContactId
    $missingCredentialTarget = 'speechmessage.p72.missing.' + [Guid]::NewGuid().ToString('N')
    $missingCredentialScript = Join-Path $fixtureRoot 'Invoke-Package02Data8ContactBasicInfoWriteEvidence.missing-credential.ps1'
    $productionSource = [IO.File]::ReadAllText($scriptPath, [Text.UTF8Encoding]::new($false, $true))
    Write-StrictTextFile -Path $missingCredentialScript -Text $productionSource.Replace('speechmessage.crm91.p62', $missingCredentialTarget)
    Write-TestProfileInput -Path $profilePath -CredentialTarget $missingCredentialTarget
    $credentialMissing = Invoke-Preflight -RepositoryPath $testRepository -ProfilePath $profilePath -FixturePath $fixturePath -CommandPath $missingCredentialScript
    Assert-True ($credentialMissing.ExitCode -eq 2 -and $credentialMissing.Evidence.reason -eq 'credential-unavailable') 'Missing Generic Credential must fail closed.'
    foreach ($forbiddenValue in @($fixtureRoot, $fixtureContactId, $missingCredentialTarget)) {
        Assert-True (-not $credentialMissing.JsonLine.Contains($forbiddenValue)) 'Credential failure output leaked path, GUID, or target data.'
    }

    $source = [IO.File]::ReadAllText($scriptPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'CredRead', 'CredFree', 'speechmessage.crm91.p62',
        'profile-input-required', 'credential-unavailable', 'fixture-input-required',
        'churchreport-config-invalid', 'matrix-approved', 'operationExecuted', 'featureFlagChanged',
        'SpeechMessageProducts.ChurchReport\appsettings.Development.json',
        'Package02ContactBasicInfoUpdatesEnabled',
        'foreach ($name in $inputEnvironmentNames)', '[switch] $ExecuteFixture',
        'Get-P72CredentialPassword', 'Get-StrictEvidenceFromTrx', 'P7_2_EVIDENCE_JSON=',
        'SPEECHMESSAGE_P7_2_LIVE', 'P7_2_CONTACT_ID', 'P7_2_FIXTURE_OWNER',
        'WaitForExit(180000)', 'manual-reconciliation-required'
    )) {
        Assert-True $source.Contains($fragment) 'P7.2 preflight lacks a required contract boundary.'
    }

    foreach ($forbidden in @(
        'Read-Host', 'Invoke-WebRequest', 'Invoke-RestMethod', 'Invoke-Expression',
        'OfficialWorker', 'Package01FeeReadsEnabled = $true',
        'Package02ContactBasicInfoUpdatesEnabled = $true'
    )) {
        Assert-True (-not $source.Contains($forbidden)) ('P7.2 preflight must not contain forbidden behavior: ' + $forbidden)
    }

    $snapshotIndex = $source.IndexOf('foreach ($name in $inputEnvironmentNames)')
    $repositoryValidationIndex = $source.IndexOf('$resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)')
    Assert-True ($snapshotIndex -ge 0 -and $snapshotIndex -lt $repositoryValidationIndex) 'Environment snapshot must precede every validation early exit.'

    $liveSource = [IO.File]::ReadAllText($liveTestPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        '[P72Data8LiveFact]', 'P7_2_EVIDENCE_JSON=', 'SPEECHMESSAGE_P7_2_LIVE',
        'P7_2_CONTACT_ID', 'P7_2_FIXTURE_OWNER', 'P72ContactBasicInfoFixtureBridge.ExecuteAsync',
        'P72Data8ContactBasicInfoFixtureStore', 'Package02ContactBasicInfoUpdateClient',
        'EmbeddedData8Runtime', 'DisposeAsync', 'loggerFactory?.Dispose()'
    )) {
        Assert-True $liveSource.Contains($fragment) 'P7.2 live test lacks a required execution or lifecycle boundary.'
    }

    foreach ($forbidden in @(
        'Read-Host', 'OfficialWorker', 'Package01FeeReadsEnabled = true',
        'Package02ContactBasicInfoUpdatesEnabled = true',
        'OrganizationRequest', 'QueryExpression', 'RetrieveMultiple('
    )) {
        Assert-True (-not $liveSource.Contains($forbidden)) ('P7.2 live test must not contain forbidden behavior: ' + $forbidden)
    }

    # 只匯入 TRX parser 的 AST，不執行 production script 主流程；暫存 XML 不含真實路徑、
    # fixture 或 credential。這個測試保護 strict allowlist，避免格式錯誤 marker 被升格為 evidence。
    Import-ScriptFunction -Path $scriptPath -FunctionName 'Get-StrictEvidenceFromTrx'
    $global:expectedOperationId = 'memberinfo.contact.update.basic.info'
    $global:expectedProfileAlias = 'sunnyvalechback'
    $global:expectedDeploymentProfileAlias = 'crm91'
    $trxPath = Join-Path $fixtureRoot 'strict-evidence.trx'
    $validEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'go'
        reason = ''
        operationId = $global:expectedOperationId
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $true
        sentinelState = 'confirmed'
        cleanupState = 'restored'
        featureFlagChanged = $false
    }
    $validJson = $validEvidence | ConvertTo-Json -Compress
    Write-StrictTextFile -Path $trxPath -Text ('<TestRun><Output><StdOut>P7_2_EVIDENCE_JSON=' + $validJson + '</StdOut></Output></TestRun>')
    $parsedEvidence = Get-StrictEvidenceFromTrx -TrxPath $trxPath
    Assert-True ($parsedEvidence.outcome -eq 'go' -and $parsedEvidence.cleanupState -eq 'restored') 'Strict TRX parser must accept the exact sanitized go marker.'

    $invalidNoGo = [ordered]@{} + $validEvidence
    $invalidNoGo.outcome = 'no-go'
    $invalidNoGo.operationExecuted = $false
    $invalidNoGo.sentinelState = 'baseline'
    $invalidNoGo.cleanupState = 'not-required'
    $invalidJson = $invalidNoGo | ConvertTo-Json -Compress
    Write-StrictTextFile -Path $trxPath -Text ('<TestRun><Output><StdOut>P7_2_EVIDENCE_JSON=' + $invalidJson + '</StdOut></Output></TestRun>')
    $invalidRejected = $false
    try {
        [void](Get-StrictEvidenceFromTrx -TrxPath $trxPath)
    }
    catch {
        $invalidRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $invalidRejected 'A no-go marker without a fixed reason must be rejected.'

    [pscustomobject]@{ outcome = 'passed'; checks = 20 } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Force -Recurse
    }
    Remove-Variable -Name expectedOperationId, expectedProfileAlias, expectedDeploymentProfileAlias -Scope Global -ErrorAction SilentlyContinue
}
