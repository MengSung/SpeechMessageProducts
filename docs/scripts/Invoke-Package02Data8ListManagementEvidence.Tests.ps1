<#
.SYNOPSIS
    驗證 P7.2 Slice C Data8 live-evidence runner 的離線安全契約。

.DESCRIPTION
    本測試不會連線 CRM、讀取真實 password 或啟動 live child test。它以短生命週期 temporary
    repository 驗證預檢順序、descriptor schema、Credential Manager fail-closed 行為、唯一
    JSON 輸出、PowerShell 5.1 parser 與 temporary evidence strict parser。受保護的合約是任何 preflight
    failure 都不得執行 operation 或改變 feature flag，且 output 不得洩漏 path、GUID、
    credential target 或原始例外。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$runnerPath = Join-Path $PSScriptRoot 'Invoke-Package02Data8ListManagementEvidence.ps1'
$liveTestPath = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'ChurchReport.MemberInfo.Tests\LivePackage02Data8ListManagementEvidenceTests.cs'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-slice-c-script-test-' + [Guid]::NewGuid().ToString('N'))
$script:assertionCount = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
    $script:assertionCount++
}

function Assert-StrictTextFile {
    param([string] $Path)

    $bytes = $null
    try {
        Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) ('Required file is missing: ' + [IO.Path]::GetFileName($Path))
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True ($bytes.Length -gt 0) 'Checked file must not be empty.'
        Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Checked file must not contain a UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True (-not [Regex]::IsMatch($text, '(?<!\r)\n|\r(?!\n)')) 'Checked file must use CRLF-only line endings.'
        Assert-True $text.EndsWith("`r`n", [StringComparison]::Ordinal) 'Checked file must end with a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function Write-StrictTextFile {
    param([string] $Path, [string] $Text)

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }

    $normalized = ($Text -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

function Write-StrictJsonFile {
    param([string] $Path, [object] $Value)
    Write-StrictTextFile -Path $Path -Text ($Value | ConvertTo-Json -Depth 12)
}

function Invoke-RunnerJson {
    param(
        [string] $CommandPath,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $FixturePath,
        [switch] $ExecuteFixture,
        [switch] $ReconcileFixture,
        [switch] $RepairFixture,
        [switch] $RepairProbe,
        [switch] $FreshPreflightProbe
    )

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $CommandPath,
        '-RepositoryPath', $RepositoryPath,
        '-ProfileInputPath', $ProfilePath,
        '-SourceFixtureDescriptorPath', $SourceFixturePath,
        '-FixtureDescriptorPath', $FixturePath,
        '-Json'
    )
    if ($ExecuteFixture) {
        $arguments += '-ExecuteFixture'
    }
    if ($ReconcileFixture) {
        $arguments += '-ReconcileFixture'
    }
    if ($RepairFixture) {
        $arguments += '-RepairFixture'
    }
    if ($RepairProbe) {
        $arguments += '-RepairProbe'
    }
    if ($FreshPreflightProbe) {
        $arguments += '-FreshPreflightProbe'
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

    $jsonLines = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) -and ([string]$_).TrimStart().StartsWith('{') })
    Assert-True ($jsonLines.Count -eq 1) 'Runner must emit exactly one JSON line.'
    return [pscustomobject]@{
        ExitCode = $exitCode
        JsonLine = [string]$jsonLines[0]
        Evidence = ($jsonLines[0] | ConvertFrom-Json)
    }
}

function Invoke-RunnerBinderFailure {
    <#
    .SYNOPSIS
        驗證互斥 live switch 由 PowerShell parameter binder 在進入 script body 前拒絕。

    .DESCRIPTION
        這個 helper 不解析 JSON；binder 失敗時 runner 不得執行靜態檢查、Credential Manager
        讀取或任何 child process。只保留 exit code 與原始文字供測試檢查，並清除捕獲的
        process output，避免測試把憑證或路徑留在長生命週期的 managed memory。
    #>
    param(
        [string] $CommandPath,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $FixturePath,
        [string[]] $ModeArguments = @(
            '-ExecuteFixture',
            '-ReconcileFixture',
            '-RepairFixture',
            '-RepairProbe')
    )

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $CommandPath,
        '-RepositoryPath', $RepositoryPath,
        '-ProfileInputPath', $ProfilePath,
        '-SourceFixtureDescriptorPath', $SourceFixturePath,
        '-FixtureDescriptorPath', $FixturePath,
        '-Json'
    )
    $arguments += $ModeArguments
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $jsonLines = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) -and ([string]$_).TrimStart().StartsWith('{') })
    return [pscustomobject]@{
        ExitCode = $exitCode
        JsonLineCount = $jsonLines.Count
        Output = ([string]::Join("`n", [string[]]$lines))
    }
}

function Invoke-RunnerWithSyntheticFailingChild {
    <#
    .SYNOPSIS
        以本機合成 child 重現「非零結束但遺留格式正確 evidence」的 fail-closed 回歸情境。

    .DESCRIPTION
        此測試輔助程式只在測試暫存目錄建立受控的 dotnet.cmd 與 PowerShell child。child 不會
        啟動 dotnet、讀取 Credential Manager、連線 CE 或變更任何功能旗標；它只把固定、已消毒的
        五項 operation evidence 寫入 parent 預先配置的環境路徑，然後以 17 結束。暫存 runner
        僅將 Credential Manager 的存在檢查與密碼讀取替換成測試常值，以隔離本回歸所保護的 child
        process 邊界，避免測試碰觸真實認證資料。

        呼叫端將假的 dotnet 目錄暫時置於 PATH 最前方；finally 無條件還原原始 PATH，因此 fake
        executable、子程序輸出與環境變數不會跨測試保留。child 在 finally 清除只含已消毒 evidence
        的 byte buffer；真正 runner 仍擁有 evidence 暫存目錄並於其 finally 移除，故本測試同時
        驗證任何 residual evidence 都不得在非零 child exit 後跨越 handoff 信任邊界。
    #>
    param(
        [string] $CommandPath,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $FixturePath,
        [string] $TemporaryRoot
    )

    $syntheticRunnerPath = Join-Path $TemporaryRoot 'Invoke-Package02Data8ListManagementEvidence.synthetic-child.ps1'
    $fakeDotnetDirectory = Join-Path $TemporaryRoot 'synthetic-dotnet'
    $fakeChildPath = Join-Path $fakeDotnetDirectory 'synthetic-child.ps1'
    $fakeDotnetPath = Join-Path $fakeDotnetDirectory 'dotnet.cmd'
    [void][IO.Directory]::CreateDirectory($fakeDotnetDirectory)

    # 測試複本只抽換 native credential gate，讓 regression 可在沒有認證資料、沒有 CE 的環境中
    # 到達 child process 邊界；production runner 的 credential 生命周期與 fail-closed 行為不會被修改。
    $syntheticRunner = [IO.File]::ReadAllText($CommandPath, [Text.UTF8Encoding]::new($false, $true))
    $syntheticRunner = $syntheticRunner.Replace(
        'return [SpeechMessage.P72SliceC.CredentialPresenceReader]::Exists($credentialTarget)',
        'return $true')
    $syntheticRunner = $syntheticRunner.Replace(
        'return [SpeechMessage.P72SliceCLive.CredentialReader]::ReadGenericSecret($credentialTarget)',
        "return 'synthetic-test-secret'")
    $dotnetSelectionLine = '$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue'
    Assert-True $syntheticRunner.Contains($dotnetSelectionLine) 'Synthetic child regression requires the runner dotnet command selection seam.'
    # 測試 runner 必須明確選到暫存 dotnet.cmd；只調整 command lookup，不改變 parent
    # 在 child 結束、drain stdout/stderr、判斷 ExitCode 與清理 evidence 的實際控制流程。
    # 這避開 Windows 全域 dotnet.exe 與 .cmd 副檔名優先序的非決定性競爭。
    $syntheticRunner = $syntheticRunner.Replace(
        $dotnetSelectionLine,
        '$dotnetCommand = Get-Command $env:SPEECHMESSAGE_P72_SYNTHETIC_DOTNET_PATH -CommandType Application -ErrorAction SilentlyContinue')
    Write-StrictTextFile $syntheticRunnerPath $syntheticRunner

    $syntheticOperations = @()
    foreach ($operationId in @(
        'list.members.add.many',
        'list.members.remove.one',
        'listmanagement.smallgroup.update.fields',
        'contact.assign.owner',
        'newperson.contact.transfer.between.lists')) {
        $syntheticOperations += [ordered]@{
            operationId = $operationId
            outcome = 'go'
            reason = ''
            operationExecuted = $true
            reconciliationState = 'expected'
            cleanupState = 'restored'
        }
    }

    $syntheticEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'go'
        reason = ''
        profileAlias = 'sunnyvalechback'
        deploymentProfileAlias = 'crm91'
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $true
        featureFlagChanged = $false
        operations = $syntheticOperations
    }
    $evidenceBytes = [Text.UTF8Encoding]::new($false).GetBytes(($syntheticEvidence | ConvertTo-Json -Compress -Depth 8))
    try {
        $evidencePayload = [Convert]::ToBase64String($evidenceBytes)
    }
    finally {
        # 雖然此 payload 僅含固定的消毒測試資料，仍在完成 base64 轉換後清除原始 buffer，避免測試
        # 逐次累積大型配置；實際 runner 對 child evidence 也採單一 owner 與 finally 清理契約。
        [Array]::Clear($evidenceBytes, 0, $evidenceBytes.Length)
    }

    $fakeChild = @(
        '$ErrorActionPreference = ''Stop''',
        '$evidencePath = $env:P7_2_SLICE_C_EVIDENCE_PATH',
        'if ([string]::IsNullOrWhiteSpace($evidencePath)) { exit 19 }',
        '$payload = $null',
        'try {',
        ('    $payload = [Convert]::FromBase64String(''' + $evidencePayload + ''')'),
        '    $json = [Text.UTF8Encoding]::new($false, $true).GetString($payload)',
        '    [IO.File]::WriteAllText($evidencePath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))',
        '}',
        'finally {',
        '    if ($null -ne $payload) { [Array]::Clear($payload, 0, $payload.Length) }',
        '}',
        'exit 17'
    ) -join "`r`n"
    Write-StrictTextFile $fakeChildPath $fakeChild
    Write-StrictTextFile $fakeDotnetPath (@(
        '@echo off',
        ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $fakeChildPath + '" %*'),
        'exit /b %ERRORLEVEL%'
    ) -join "`r`n")

    $previousSyntheticDotnetPath = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_DOTNET_PATH', 'Process')
    try {
        # 以 process-scoped 明確路徑選擇暫存 dotnet.cmd，不依賴 PATH 或 PATHEXT；這可避免
        # 系統安裝的 dotnet.exe 影響故障注入。finally 會無條件還原此測試專用環境值。
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_DOTNET_PATH', $fakeDotnetPath, 'Process')
        $resolvedFakeDotnet = @(Get-Command $fakeDotnetPath -CommandType Application -ErrorAction Stop)
        Assert-True (
            $resolvedFakeDotnet.Count -eq 1 -and
            [string]::Equals(
                [IO.Path]::GetFullPath($resolvedFakeDotnet[0].Source),
                [IO.Path]::GetFullPath($fakeDotnetPath),
                [StringComparison]::OrdinalIgnoreCase)) 'Synthetic non-zero child must be selected instead of the installed dotnet executable.'
        return Invoke-RunnerJson $syntheticRunnerPath $RepositoryPath $ProfilePath $SourceFixturePath $FixturePath -ExecuteFixture
    }
    finally {
        # 即使 assertion、runner 或 parser 擲出例外，也必須立即還原測試專用 selector，
        # 避免後續測試或使用者命令繼承假的 dotnet.cmd 路徑。
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_DOTNET_PATH', $previousSyntheticDotnetPath, 'Process')
    }
}

function Import-ScriptFunction {
    param([string] $Path, [string] $FunctionName)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Runner must be valid Windows PowerShell syntax.'
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $FunctionName
    }, $true)
    Assert-True ($null -ne $functionAst) ('Required runner function is missing: ' + $FunctionName)
    $body = $functionAst.Body.Extent.Text.Trim()
    $body = $body.Substring(1, $body.Length - 2)
    Set-Item -Path ('Function:\global:' + $FunctionName) -Value ([ScriptBlock]::Create($body))
}

function New-TestRepository {
    param([string] $Root)

    $realRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $matrixSource = Join-Path $realRoot '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $matrixTarget = Join-Path $Root '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    Write-StrictTextFile $matrixTarget ([IO.File]::ReadAllText($matrixSource, [Text.UTF8Encoding]::new($false, $true)))
    Write-StrictTextFile (Join-Path $Root 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj') '<Project Sdk="Microsoft.NET.Sdk"></Project>'
    Write-StrictTextFile (Join-Path $Root 'SpeechMessageProducts.ChurchReport\appsettings.json') @'
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
        [bool] $BasicEnabled = $false,
        [bool] $ProfileEnabled = $false
    )

    $basic = if ($BasicEnabled) { 'true' } else { 'false' }
    $profile = if ($ProfileEnabled) { 'true' } else { 'false' }
    Write-StrictTextFile (Join-Path $Root 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') @"
{
  "DynamicsAccess": {
    "Package01FeeReadsEnabled": false,
    "Package02ContactBasicInfoUpdatesEnabled": $basic,
    "Package02ContactProfileOperationsEnabled": $profile,
    "ConnectionMode": "$Mode",
    "ProfileAlias": "sunnyvalechback"
  }
}
"@
}

function Write-TestProfileInput {
    param([string] $Path, [string] $CredentialTarget)

    Write-StrictJsonFile $Path ([ordered]@{
        schemaVersion = 1
        profiles = @(
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

function Write-TestSourceFixture {
    param([string] $Path, [string] $ContactId, [string] $Identity)

    Write-StrictJsonFile $Path ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-contact-basic-info'
        profileAlias = 'sunnyvalechback'
        ceVersion = '9.1'
        connector = 'Data8'
        marker = 'p7.2-contact-basic-info'
        contactId = $ContactId
        ownerIdentity = $Identity
    })
}

function Write-TestSliceCFixture {
    param(
        [string] $Path,
        [string] $Identity,
        [string] $Marker = 'p7.2-list-management',
        [string] $CeVersion = '9.1',
        [string] $Connector = 'Data8',
        [string] $ExpectedRelationshipListId = '99999999-9999-9999-9999-999999999999'
    )

    Write-StrictJsonFile $Path ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-list-management'
        profileAlias = 'sunnyvalechback'
        ceVersion = $CeVersion
        connector = $Connector
        marker = $Marker
        ownerIdentity = $Identity
        addListId = '11111111-1111-1111-1111-111111111111'
        removeListId = '22222222-2222-2222-2222-222222222222'
        smallGroupListId = '33333333-3333-3333-3333-333333333333'
        smallGroupTargetLeaderContactId = '44444444-4444-4444-4444-444444444444'
        smallGroupExpectedRelationshipListId = $ExpectedRelationshipListId
        transferSourceListId = '66666666-6666-6666-6666-666666666666'
        transferTargetListId = '77777777-7777-7777-7777-777777777777'
        transferWeekStartUtc = '2026-08-09T00:00:00.0000000+00:00'
    })
}

try {
    foreach ($path in @($PSCommandPath, $runnerPath, $liveTestPath)) {
        Assert-StrictTextFile $path
    }

    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Runner must parse under Windows PowerShell 5.1.'

    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    $missingRepository = Join-Path $fixtureRoot 'missing-repository'
    $profilePath = Join-Path $fixtureRoot 'official-worker-profile-input.json'
    $sourceFixturePath = Join-Path $fixtureRoot 'contact-basic-info-fixture.json'
    $sliceCFixturePath = Join-Path $fixtureRoot 'list-management-fixture.json'
    $missingResult = Invoke-RunnerJson $runnerPath $missingRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($missingResult.ExitCode -eq 1) 'Missing repository must fail before profile, credential, fixture or child-process access.'
    Assert-True ($missingResult.Evidence.outcome -eq 'error' -and $missingResult.Evidence.reason -eq 'repository-invalid') 'Missing repository must use the sanitized error category.'
    Assert-True (-not $missingResult.Evidence.operationExecuted -and -not $missingResult.Evidence.featureFlagChanged) 'Repository failure must execute no operation and change no flag.'
    foreach ($forbiddenValue in @($fixtureRoot, '11111111-1111-1111-1111-111111111111', 'speechmessage.crm91.p62')) {
        Assert-True (-not $missingResult.JsonLine.Contains($forbiddenValue)) 'Missing-repository output leaked sensitive local input.'
    }

    $testRepository = Join-Path $fixtureRoot 'repository'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $contactId = '88888888-8888-8888-8888-888888888888'
    New-TestRepository $testRepository
    Write-TestSourceFixture $sourceFixturePath $contactId $identity
    Write-TestSliceCFixture $sliceCFixturePath $identity
    $missingTarget = 'speechmessage.p72.slice-c.missing.' + [Guid]::NewGuid().ToString('N')
    Write-TestProfileInput $profilePath $missingTarget
    $missingCredentialRunner = Join-Path $fixtureRoot 'Invoke-Package02Data8ListManagementEvidence.missing-credential.ps1'
    $runnerText = [IO.File]::ReadAllText($runnerPath, [Text.UTF8Encoding]::new($false, $true))
    Write-StrictTextFile $missingCredentialRunner $runnerText.Replace('speechmessage.crm91.p62', $missingTarget)

    Write-TestDevelopmentConfiguration $testRepository 'DedicatedGateway'
    $modeMismatch = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($modeMismatch.ExitCode -eq 2 -and $modeMismatch.Evidence.reason -eq 'churchreport-config-invalid') 'Wrong hosting mode must fail before Credential Manager access.'
    Write-TestDevelopmentConfiguration $testRepository 'Embedded' -BasicEnabled $true
    $flagMismatch = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($flagMismatch.ExitCode -eq 2 -and $flagMismatch.Evidence.reason -eq 'churchreport-config-invalid') 'Enabled Package02 consumer flag must fail before any child process.'
    Assert-True (-not $flagMismatch.Evidence.featureFlagChanged) 'Runner must never change a feature flag while reporting mismatch.'
    Write-TestDevelopmentConfiguration $testRepository 'Embedded'

    Write-TestSliceCFixture $sliceCFixturePath $identity -Marker 'wrong-marker'
    $fixtureMismatch = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($fixtureMismatch.ExitCode -eq 2 -and $fixtureMismatch.Evidence.reason -eq 'fixture-input-invalid') 'Marker mismatch must fail closed before credentials.'
    Assert-True (-not $fixtureMismatch.JsonLine.Contains($contactId)) 'Fixture failure must not reveal the Slice A contact GUID.'
    Write-TestSliceCFixture $sliceCFixturePath $identity

    # 保護契約：smallGroupExpectedRelationshipListId 必須是 descriptor 的非空 GUID；故障注入為空值。
    # 決定性斷言：runner 必須在讀取 Credential Manager 或啟動 child process 前以 fixture-input-invalid 失敗關閉。
    Write-TestSliceCFixture $sliceCFixturePath $identity -ExpectedRelationshipListId ''
    $missingRelationshipList = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($missingRelationshipList.ExitCode -eq 2 -and $missingRelationshipList.Evidence.reason -eq 'fixture-input-invalid') 'Missing small-group expected relationship list ID must fail closed before credentials.'
    Assert-True (-not $missingRelationshipList.Evidence.operationExecuted -and $missingRelationshipList.Evidence.preflightOnly) 'Missing relationship list ID must not start a live operation.'
    Write-TestSliceCFixture $sliceCFixturePath $identity

    # relationship list 是只讀 expected projection 的獨立 fixture；若重用 add/remove/transfer 任一
    # mutation list，遠端 provenance 即使名稱正確也無法證明 rollback graph 互不干擾。決定性斷言是
    # runner 在 Credential Manager 或 child process 前回傳 fixture-input-invalid。
    Write-TestSliceCFixture $sliceCFixturePath $identity -ExpectedRelationshipListId '11111111-1111-1111-1111-111111111111'
    $duplicateRelationshipList = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($duplicateRelationshipList.ExitCode -eq 2 -and $duplicateRelationshipList.Evidence.reason -eq 'fixture-input-invalid') 'Expected relationship list must be distinct from every Slice C mutation list.'
    Assert-True (-not $duplicateRelationshipList.Evidence.operationExecuted -and $duplicateRelationshipList.Evidence.preflightOnly) 'Duplicate relationship list must not start a live operation.'
    Write-TestSliceCFixture $sliceCFixturePath $identity

    # 保護契約：legacy descriptor 不得再能指定任意 CRM systemuser。故障注入為加入舊 targetOwnerId 欄位；
    # 決定性斷言：exact schema 驗證必須在 Credential Manager 或 child process 前 fail closed。
    $legacyOwnerDescriptor = [IO.File]::ReadAllText($sliceCFixturePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
    $legacyOwnerDescriptor | Add-Member -NotePropertyName targetOwnerId -NotePropertyValue '55555555-5555-5555-5555-555555555555'
    Write-StrictJsonFile $sliceCFixturePath $legacyOwnerDescriptor
    $legacyOwnerRejected = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($legacyOwnerRejected.ExitCode -eq 2 -and $legacyOwnerRejected.Evidence.reason -eq 'fixture-input-invalid') 'Legacy targetOwnerId must be rejected before Credential Manager access.'
    Assert-True (-not $legacyOwnerRejected.Evidence.operationExecuted -and $legacyOwnerRejected.Evidence.preflightOnly) 'Legacy targetOwnerId must not start a live operation.'
    Write-TestSliceCFixture $sliceCFixturePath $identity

    $credentialMissing = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($credentialMissing.ExitCode -eq 2 -and $credentialMissing.Evidence.reason -eq 'credential-unavailable') 'Missing Generic Credential target must fail closed.'
    Assert-True (-not $credentialMissing.Evidence.operationExecuted -and $credentialMissing.Evidence.preflightOnly) 'Credential preflight failure must not start a live operation.'
    foreach ($forbiddenValue in @($fixtureRoot, $contactId, $missingTarget, '11111111-1111-1111-1111-111111111111')) {
        Assert-True (-not $credentialMissing.JsonLine.Contains($forbiddenValue)) 'Credential failure output leaked a path, GUID, or target.'
    }

    # reconciliation 也必須在相同 credential gate 後才可啟動 child；缺少 Generic Credential 時，
    # parent 仍要明確標示這是 live-intent No-Go（不是 preflight success），並由 parent 自己產生
    # safeToRetry=false。決定性 assertion 同時保護 zero mutation 與不改旗標。
    $reconciliationCredentialMissing = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath -ReconcileFixture
    Assert-True ($reconciliationCredentialMissing.ExitCode -eq 2 -and $reconciliationCredentialMissing.Evidence.reason -eq 'credential-unavailable') 'Reconciliation missing credential must fail closed before child launch.'
    Assert-True (-not $reconciliationCredentialMissing.Evidence.preflightOnly -and
        -not $reconciliationCredentialMissing.Evidence.operationExecuted -and
        -not $reconciliationCredentialMissing.Evidence.featureFlagChanged -and
        $reconciliationCredentialMissing.Evidence.safeToRetry -eq $false) 'Reconciliation credential failure must remain no-go, mutation-free, and non-retryable.'

    $repairCredentialMissing = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath -RepairFixture
    Assert-True ($repairCredentialMissing.ExitCode -eq 2 -and $repairCredentialMissing.Evidence.reason -eq 'credential-unavailable') 'Repair missing credential must fail closed before child launch.'
    Assert-True (-not $repairCredentialMissing.Evidence.preflightOnly -and
        -not $repairCredentialMissing.Evidence.operationExecuted -and
        -not $repairCredentialMissing.Evidence.featureFlagChanged -and
        $repairCredentialMissing.Evidence.safeToRetry -eq $false) 'Repair credential failure must remain no-go, mutation-free, and non-retryable.'

    $repairProbeCredentialMissing = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath -RepairProbe
    Assert-True ($repairProbeCredentialMissing.ExitCode -eq 2 -and $repairProbeCredentialMissing.Evidence.reason -eq 'credential-unavailable') 'Repair probe missing credential must fail closed before child launch.'
    Assert-True (-not $repairProbeCredentialMissing.Evidence.preflightOnly -and
        -not $repairProbeCredentialMissing.Evidence.operationExecuted -and
        -not $repairProbeCredentialMissing.Evidence.featureFlagChanged -and
        $repairProbeCredentialMissing.Evidence.safeToRetry -eq $false) 'Repair probe credential failure must remain no-go, mutation-free, and non-retryable.'

    # Fresh preflight probe 是獨立的 read-only child lane；缺少 credential 時仍必須在 child、
    # ledger、descriptor publication 或任何 CRM mutation 前停止。它不能因為名稱帶有 preflight
    # 就退回普通預檢模式，否則 caller 會誤把未執行的 CE proof 當作條件已改變。
    $freshPreflightProbeCredentialMissing = Invoke-RunnerJson $missingCredentialRunner $testRepository $profilePath $sourceFixturePath $sliceCFixturePath -FreshPreflightProbe
    Assert-True ($freshPreflightProbeCredentialMissing.ExitCode -eq 2 -and $freshPreflightProbeCredentialMissing.Evidence.reason -eq 'credential-unavailable') 'Fresh preflight probe missing credential must fail closed before child launch.'
    Assert-True (-not $freshPreflightProbeCredentialMissing.Evidence.preflightOnly -and
        -not $freshPreflightProbeCredentialMissing.Evidence.operationExecuted -and
        -not $freshPreflightProbeCredentialMissing.Evidence.featureFlagChanged -and
        $freshPreflightProbeCredentialMissing.Evidence.safeToRetry -eq $false) 'Fresh preflight credential failure must remain a zero-mutation, non-retryable no-go.'

    foreach ($modeArguments in @(
        @('-ExecuteFixture', '-ReconcileFixture'),
        @('-ExecuteFixture', '-RepairFixture'),
        @('-ExecuteFixture', '-RepairProbe'),
        @('-ExecuteFixture', '-FreshPreflightProbe'),
        @('-ReconcileFixture', '-RepairFixture'),
        @('-ReconcileFixture', '-RepairProbe'),
        @('-ReconcileFixture', '-FreshPreflightProbe'),
        @('-RepairFixture', '-RepairProbe'),
        @('-RepairFixture', '-FreshPreflightProbe'),
        @('-RepairProbe', '-FreshPreflightProbe'))) {
        $pairBinderFailure = Invoke-RunnerBinderFailure `
            $runnerPath `
            $testRepository `
            $profilePath `
            $sourceFixturePath `
            $sliceCFixturePath `
            -ModeArguments $modeArguments
        Assert-True ($pairBinderFailure.ExitCode -ne 0 -and $pairBinderFailure.JsonLineCount -eq 0) 'Every pair of live lanes must be rejected by the parameter binder before script body execution.'
    }

    $binderFailure = Invoke-RunnerBinderFailure $runnerPath $testRepository $profilePath $sourceFixturePath $sliceCFixturePath
    Assert-True ($binderFailure.ExitCode -ne 0 -and $binderFailure.JsonLineCount -eq 0) 'All four live lanes must be rejected together by the parameter binder before script body execution.'

    $source = [IO.File]::ReadAllText($runnerPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        'P7_2_SLICE_C_EVIDENCE_PATH', 'Get-StrictSliceCEvidenceFile',
        'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', 'Get-StrictSliceCReconciliationEvidenceFile',
        'SPEECHMESSAGE_P7_2_SLICE_C_LIVE', 'P7_2_SLICE_C_CONTACT_ID',
        'SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE',
        'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
        'smallGroupExpectedRelationshipListId',
        'P7_2_SLICE_C_TRANSFER_WEEK_START_UTC', 'P72Data8ListManagementEvidence.json',
        'P72Data8ListManagementReconciliationEvidence.json',
        'WaitForExit(180000)', 'CredRead', 'CredFree',
        'contact-basic-info-fixture.json', 'list-management-fixture.json',
        'featureFlagChanged = $false', '[switch] $ExecuteFixture', '[switch] $ReconcileFixture', '[switch] $RepairFixture',
        '[switch] $RepairProbe', 'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', 'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE',
        'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', 'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', 'Get-StrictSliceCRepairEvidenceFile',
        'Get-StrictSliceCRepairProbeEvidenceFile',
        'P72Data8ListManagementRepairEvidence.json', 'Repair_package02_data8_relationship_fixture_emits_sanitized_evidence',
        'P72Data8ListManagementRepairProbeEvidence.json', 'Probe_package02_data8_relationship_fixture_emits_sanitized_evidence',
        'Package02ContactBasicInfoUpdatesEnabled', 'Package02ContactProfileOperationsEnabled',
        'Test-SliceCFixtureDescriptor', 'New-NotStartedOperations', 'Remove-OwnedSliceCTemporaryDirectory',
        'New-TemporaryCleanupFailureResult', 'Complete-HandoffResult'
    )) {
        Assert-True $source.Contains($fragment) ('Runner lacks required contract boundary: ' + $fragment)
    }
    foreach ($forbidden in @('Read-Host', 'Invoke-WebRequest', 'Invoke-RestMethod', 'Invoke-Expression', 'OrganizationRequest', 'task.py start')) {
        Assert-True (-not $source.Contains($forbidden)) ('Runner contains forbidden behavior: ' + $forbidden)
    }
    Assert-True (-not $source.Contains('P7_2_SLICE_C_EVIDENCE_JSON=')) 'Runner must not depend on unstable TRX stdout evidence markers.'
    Assert-True (-not $source.Contains('Get-StrictSliceCEvidenceFromTrx')) 'Runner must not retain an obsolete TRX evidence parser after moving to the guarded evidence file.'
    Assert-True (-not $source.Contains('P7_2_SLICE_C_RETIRED_TRX_EVIDENCE')) 'Runner must not retain a retired TRX marker contract.'
    Assert-True (-not $source.Contains('P7_2_SLICE_C_TARGET_OWNER_ID')) 'Runner must not propagate an arbitrary descriptor-supplied CRM owner identity.'

    $liveSource = [IO.File]::ReadAllText($liveTestPath, [Text.UTF8Encoding]::new($false, $true))
    foreach ($fragment in @(
        '[P72Data8SliceCLiveFact]', 'P7_2_SLICE_C_EVIDENCE_PATH', 'WriteSliceCEvidenceFile',
        '[P72Data8SliceCReconcileFact]', 'Reconcile_package02_data8_list_management_emits_sanitized_reconciliation',
        '[P72Data8SliceCRepairFact]', 'Repair_package02_data8_relationship_fixture_emits_sanitized_evidence',
        'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', 'WriteSliceCRepairEvidenceFile',
        '[P72Data8SliceCRepairProbeFact]', 'Probe_package02_data8_relationship_fixture_emits_sanitized_evidence',
        'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', 'WriteSliceCRepairProbeEvidenceFile',
        'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', 'P72Data8ListManagementReconciliationEvidence.json',
        'P72ListManagementFixtureBridge.ExecuteAddMembersAsync',
        'P72ListManagementFixtureBridge.ExecuteRemoveMemberAsync',
        'P72ListManagementFixtureBridge.ExecuteSmallGroupFieldsAsync',
        'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
        'fixture.SmallGroupExpectedRelationshipListId',
        'P72ListManagementFixtureBridge.ExecuteOwnerAssignmentAsync',
        'P72ListManagementFixtureBridge.ExecuteTransferAsync',
        'P72Data8ListManagementFixtureStore', 'Package02ListManagementClient',
        'TryProveFixtureGraph', 'DisposeRuntimeAsync', 'DisposeStore', 'DisposeLogger'
    )) {
        Assert-True $liveSource.Contains($fragment) ('Live test lacks required execution or lifecycle boundary: ' + $fragment)
    }
    Assert-True (-not $liveSource.Contains('P7_2_SLICE_C_EVIDENCE_JSON=')) 'Live test must not emit evidence through unstable TRX stdout.'
    Assert-True (-not $liveSource.Contains('P7_2_SLICE_C_TARGET_OWNER_ID')) 'Live test must derive the assignment target from the verified Data8 identity.'
    foreach ($forbidden in @('Read-Host', 'OrganizationRequest', 'QueryExpression', 'RetrieveMultiple(', 'OfficialWorker')) {
        Assert-True (-not $liveSource.Contains($forbidden)) ('Live test contains forbidden direct CRM or transport behavior: ' + $forbidden)
    }

    # Execute lane 的 graph proof 必須在任何 baseline read 與第一個 bridge dispatch 前，以 store 所有的
    # direct-Retrieve provenance projection 證明遠端 contact/list 都是 task-owned。這是 source-level ordering
    # contract：若未來重構將該呼叫移到 baseline 或 mutation 之後，本測試會在無 CE credential 的離線環境
    # fail closed，避免 descriptor 僅靠本機 GUID 或 Windows identity 取得寫入資格。
    $proveGraphStart = $liveSource.IndexOf('private static bool TryProveFixtureGraph', [StringComparison]::Ordinal)
    $proveGraphEnd = $liveSource.IndexOf('private static SliceCOperationEvidence ToEvidence', $proveGraphStart, [StringComparison]::Ordinal)
    Assert-True ($proveGraphStart -ge 0 -and $proveGraphEnd -gt $proveGraphStart) 'Execute fixture proof method must remain a bounded source scope.'
    $proveGraphSource = $liveSource.Substring($proveGraphStart, $proveGraphEnd - $proveGraphStart)
    $provenanceIndex = $proveGraphSource.IndexOf('TryValidateTaskOwnedSliceCFixtureGraph', [StringComparison]::Ordinal)
    $firstBaselineReadIndex = $proveGraphSource.IndexOf('ReadMembership', [StringComparison]::Ordinal)
    Assert-True ($provenanceIndex -ge 0 -and $firstBaselineReadIndex -gt $provenanceIndex) 'Task-owned fixture provenance must be proven before every execute-lane baseline read.'

    # 此區塊刻意只掃描 reconciliation method 本身，不因既有 execute lane 的 client／bridge
    # 合法寫入呼叫而產生誤報。契約要求新 lane 必須先以 WhoAmI 綁定 service owner，並
    # 僅以 fixture store 的 read projection 形成 no-go，不得藉由 restore 或 SDK mutation
    # 將缺失 baseline 偽裝為可以重試。
    $reconciliationMethodStart = $liveSource.IndexOf('Reconcile_package02_data8_list_management_emits_sanitized_reconciliation', [StringComparison]::Ordinal)
    $reconciliationMethodEnd = $liveSource.IndexOf('private static bool TryProveFixtureGraph', $reconciliationMethodStart, [StringComparison]::Ordinal)
    Assert-True ($reconciliationMethodStart -ge 0 -and $reconciliationMethodEnd -gt $reconciliationMethodStart) 'Reconciliation method must have a bounded source scope before the execute graph helper.'
    $reconciliationMethodSource = $liveSource.Substring($reconciliationMethodStart, $reconciliationMethodEnd - $reconciliationMethodStart)
    Assert-True $reconciliationMethodSource.Contains('ResolveFixtureTargetOwnerIdAsync') 'Reconciliation must bind its target owner through the existing WhoAmI resolver.'
    $hasIndependentReadProof = $reconciliationMethodSource.Contains('ReadMembership') -and
        $reconciliationMethodSource.Contains('ReadSmallGroupFields') -and
        $reconciliationMethodSource.Contains('ResolveSmallGroupExpected') -and
        $reconciliationMethodSource.Contains('ReadOwnerId') -and
        $reconciliationMethodSource.Contains('ReadTransferGraph')
    Assert-True ($reconciliationMethodSource.Contains('TryProveFixtureGraph') -or $hasIndependentReadProof) 'Reconciliation must retain an explicit read-only fixture proof.'
    foreach ($forbidden in @('Package02ListManagementClient', 'P72ListManagementFixtureBridge.Execute', 'Restore', 'Update(', 'Delete(', 'Assign')) {
        Assert-True (-not $reconciliationMethodSource.Contains($forbidden)) ('Reconciliation method contains a forbidden mutation boundary: ' + $forbidden)
    }

    Import-ScriptFunction $runnerPath 'New-HandoffResult'
    Import-ScriptFunction $runnerPath 'Read-StrictJsonFile'
    Import-ScriptFunction $runnerPath 'Read-StrictTextFile'
    Import-ScriptFunction $runnerPath 'Get-StrictSliceCEvidenceFile'
    Import-ScriptFunction $runnerPath 'Get-StrictSliceCReconciliationEvidenceFile'
    Import-ScriptFunction $runnerPath 'Get-StrictSliceCRepairEvidenceFile'
    Import-ScriptFunction $runnerPath 'Get-StrictSliceCRepairProbeEvidenceFile'
    Import-ScriptFunction $runnerPath 'Get-StrictFreshPreflightProbeEvidenceFile'
    Import-ScriptFunction $runnerPath 'Remove-OwnedSliceCTemporaryDirectory'
    Import-ScriptFunction $runnerPath 'New-TemporaryCleanupFailureResult'
    Import-ScriptFunction $runnerPath 'Complete-HandoffResult'
    $global:expectedProfileAlias = 'sunnyvalechback'
    $global:expectedDeploymentProfileAlias = 'crm91'
    $global:expectedOperationIds = @(
        'list.members.add.many',
        'list.members.remove.one',
        'listmanagement.smallgroup.update.fields',
        'contact.assign.owner',
        'newperson.contact.transfer.between.lists'
    )
    $validOperations = @()
    foreach ($operationId in $global:expectedOperationIds) {
        $validOperations += [ordered]@{
            operationId = $operationId
            outcome = 'go'
            reason = ''
            operationExecuted = $true
            reconciliationState = 'expected'
            cleanupState = 'restored'
        }
    }
    $evidencePath = Join-Path $fixtureRoot 'strict-slice-c-evidence.json'
    $validEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'go'
        reason = ''
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $true
        featureFlagChanged = $false
        operations = $validOperations
    }
    Write-StrictJsonFile $evidencePath $validEvidence
    $parsed = Get-StrictSliceCEvidenceFile $evidencePath
    Assert-True ($parsed.outcome -eq 'go' -and @($parsed.operations).Count -eq 5) 'Strict parser must accept the exact five-operation sanitized evidence file.'

    # JSON 規範允許單獨 CR 作為 whitespace；若 strict reader 僅拒絕 bare LF，便會把非 CRLF-only
    # descriptor 當成有效控制平面輸入。以下以仍保有 final CRLF 的最小 JSON 注入 standalone CR，
    # 兩個 reader 都必須在任何 schema 或 descriptor 消費前回傳固定 failure reason。
    $embeddedStandaloneCrPath = Join-Path $fixtureRoot 'embedded-standalone-cr.json'
    [IO.File]::WriteAllText(
        $embeddedStandaloneCrPath,
        "{`r`"schemaVersion`":1}`r`n",
        [Text.UTF8Encoding]::new($false))
    $embeddedStandaloneCrFailure = 'embedded-standalone-cr-rejected'
    $jsonStandaloneCrRejected = $false
    try {
        $null = Read-StrictJsonFile `
            -Path $embeddedStandaloneCrPath `
            -MaximumBytes 1024 `
            -FailureReason $embeddedStandaloneCrFailure `
            -RequireFinalCrLf
    }
    catch {
        $jsonStandaloneCrRejected = $_.Exception.Message -eq $embeddedStandaloneCrFailure
    }
    $textStandaloneCrRejected = $false
    try {
        $null = Read-StrictTextFile `
            -Path $embeddedStandaloneCrPath `
            -MaximumBytes 1024 `
            -FailureReason $embeddedStandaloneCrFailure
    }
    catch {
        $textStandaloneCrRejected = $_.Exception.Message -eq $embeddedStandaloneCrFailure
    }
    Assert-True ($jsonStandaloneCrRejected -and $textStandaloneCrRejected) 'Strict local readers must reject embedded standalone CR even when the file ends in CRLF.'

    $repairEvidencePath = Join-Path $fixtureRoot 'strict-slice-c-repair-evidence.json'
    $validRepairEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'go'
        reason = ''
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $true
        readBackConfirmed = $true
        featureFlagChanged = $false
    }
    Write-StrictJsonFile $repairEvidencePath $validRepairEvidence
    $parsedRepair = Get-StrictSliceCRepairEvidenceFile $repairEvidencePath
    Assert-True ($parsedRepair.outcome -eq 'go' -and $parsedRepair.readBackConfirmed) 'Strict parser must accept a confirmed one-update repair evidence file.'

    $repairProbeEvidencePath = Join-Path $fixtureRoot 'strict-slice-c-repair-probe-evidence.json'
    $validRepairProbeEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'no-go'
        reason = 'repair-preconditions-proven'
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $false
        readOnlyProbeExecuted = $true
        featureFlagChanged = $false
        probe = [ordered]@{
            sourceContactMarkerValid = $true
            smallGroupListValid = $true
            expectedRelationshipListValid = $true
            targetLeaderMarkerValid = $true
            expectedRelationshipRaceLeaderMatches = $true
            expectedRelationshipFieldsState = 'blank'
            preconditionState = 'blank-repairable'
        }
    }
    Write-StrictJsonFile $repairProbeEvidencePath $validRepairProbeEvidence
    $parsedRepairProbe = Get-StrictSliceCRepairProbeEvidenceFile $repairProbeEvidencePath
    Assert-True ($parsedRepairProbe.outcome -eq 'no-go' -and
        $parsedRepairProbe.reason -eq 'repair-preconditions-proven' -and
        $parsedRepairProbe.readOnlyProbeExecuted -and
        $parsedRepairProbe.preconditionState -eq 'blank-repairable' -and
        $parsedRepairProbe.probe.expectedRelationshipFieldsState -eq 'blank') 'Strict probe parser must accept only a completed read-only precondition projection.'

    $missingProbePropertyEvidence = $validRepairProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $missingProbePropertyEvidence.probe.PSObject.Properties.Remove('expectedRelationshipFieldsState')
    Write-StrictJsonFile $repairProbeEvidencePath $missingProbePropertyEvidence
    $missingProbePropertyRejected = $false
    try {
        [void](Get-StrictSliceCRepairProbeEvidenceFile $repairProbeEvidencePath)
    }
    catch {
        $missingProbePropertyRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $missingProbePropertyRejected 'Strict probe parser must reject a missing fixed projection property.'

    $extraProbePropertyEvidence = $validRepairProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $extraProbePropertyEvidence.probe | Add-Member -NotePropertyName unexpected -NotePropertyValue 'must-not-cross-handoff'
    Write-StrictJsonFile $repairProbeEvidencePath $extraProbePropertyEvidence
    $extraProbePropertyRejected = $false
    try {
        [void](Get-StrictSliceCRepairProbeEvidenceFile $repairProbeEvidencePath)
    }
    catch {
        $extraProbePropertyRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $extraProbePropertyRejected 'Strict probe parser must reject an extra projection property.'

    $mutatingProbeEvidence = $validRepairProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $mutatingProbeEvidence.operationExecuted = $true
    Write-StrictJsonFile $repairProbeEvidencePath $mutatingProbeEvidence
    $mutatingProbeRejected = $false
    try {
        [void](Get-StrictSliceCRepairProbeEvidenceFile $repairProbeEvidencePath)
    }
    catch {
        $mutatingProbeRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $mutatingProbeRejected 'Strict probe parser must reject operationExecuted=true.'

    # Fresh preflight probe 的 wire schema 必須獨立於 stale relationship RepairProbe。此合成
    # evidence 只使用固定分類；決定性 assertions 是 parser 接受完整 read-only go projection，
    # 但拒絕多餘欄位、任何 mutation/feature flag bit，以及會將 CRM identity 混入 evidence 的
    # caller-controlled text。檔案與 byte buffer 都是 temporary-root owned，finally 會刪除。
    $freshPreflightProbeEvidencePath = Join-Path $fixtureRoot 'strict-fresh-preflight-probe-evidence.json'
    $validFreshPreflightProbeEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'go'
        reason = 'fresh-preconditions-proven'
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $false
        readOnlyProbeExecuted = $true
        featureFlagChanged = $false
        probe = [ordered]@{
            requestShape = 'valid'
            operationalLists = 'valid'
            leaderMarker = 'valid'
            ownerKind = 'systemuser'
            ownerState = 'active'
            ownerRelation = 'different-from-data8'
            weeklyReport = 'exactly-one-active'
        }
    }
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $validFreshPreflightProbeEvidence
    $parsedFreshPreflightProbe = Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath
    Assert-True ($parsedFreshPreflightProbe.outcome -eq 'go' -and
        $parsedFreshPreflightProbe.reason -eq 'fresh-preconditions-proven' -and
        $parsedFreshPreflightProbe.readOnlyProbeExecuted -and
        $parsedFreshPreflightProbe.probe.ownerRelation -eq 'different-from-data8') 'Strict fresh preflight parser must accept only the complete fixed read-only go projection.'

    # 使用者已確認：exact target-list/UTC-Sunday 交集為零筆 weekly report 是正常狀態。parser 必須
    # 接受同樣完整的 read-only go evidence，但只允許固定 zero-active 分類；測試不啟動 child、
    # 不讀取 credential，也不建立 ledger 或任何 CRM mutation。
    $zeroActiveFreshPreflightEvidence = $validFreshPreflightProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $zeroActiveFreshPreflightEvidence.probe.weeklyReport = 'zero-active'
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $zeroActiveFreshPreflightEvidence
    $parsedZeroActiveFreshPreflightProbe = Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath
    Assert-True ($parsedZeroActiveFreshPreflightProbe.outcome -eq 'go' -and
        $parsedZeroActiveFreshPreflightProbe.reason -eq 'fresh-preconditions-proven' -and
        $parsedZeroActiveFreshPreflightProbe.readOnlyProbeExecuted -and
        $parsedZeroActiveFreshPreflightProbe.probe.weeklyReport -eq 'zero-active') 'Strict fresh preflight parser must accept the complete zero-active read-only go projection.'

    # 重複週報是唯一允許保留的週報資料 no-go：parser 必須接受固定、去識別化的
    # duplicate-active 分類與完整零 mutation 證據，讓 parent 能停止 fixture cycle；它不可攜帶
    # CRM ID、名稱、數量或任何足以挑選、合併、建立或修補週報的資料。
    $duplicateActiveFreshPreflightEvidence = $validFreshPreflightProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $duplicateActiveFreshPreflightEvidence.outcome = 'no-go'
    $duplicateActiveFreshPreflightEvidence.reason = 'fresh-preconditions-not-proven'
    $duplicateActiveFreshPreflightEvidence.probe.weeklyReport = 'duplicate-active'
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $duplicateActiveFreshPreflightEvidence
    $parsedDuplicateActiveFreshPreflightProbe = Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath
    Assert-True ($parsedDuplicateActiveFreshPreflightProbe.outcome -eq 'no-go' -and
        $parsedDuplicateActiveFreshPreflightProbe.reason -eq 'fresh-preconditions-not-proven' -and
        $parsedDuplicateActiveFreshPreflightProbe.readOnlyProbeExecuted -and
        $parsedDuplicateActiveFreshPreflightProbe.probe.weeklyReport -eq 'duplicate-active') 'Strict fresh preflight parser must accept the complete duplicate-active no-go projection.'

    # 舊合併分類無法辨識零筆與重複週報，會破壞新的業務語意；嚴格 parser 必須拒絕它，確保
    # parent 無法把歷史 evidence 當成新版 fresh fixture 的可執行條件。
    $legacyWeeklyReportCategoryEvidence = $validFreshPreflightProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $legacyWeeklyReportCategoryEvidence.probe.weeklyReport = 'not-exactly-one-active'
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $legacyWeeklyReportCategoryEvidence
    $legacyWeeklyReportCategoryRejected = $false
    try {
        [void](Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath)
    }
    catch {
        $legacyWeeklyReportCategoryRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $legacyWeeklyReportCategoryRejected 'Strict fresh preflight parser must reject the obsolete merged weekly-report category.'

    $extraFreshPreflightProperty = $validFreshPreflightProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $extraFreshPreflightProperty.probe | Add-Member -NotePropertyName crmId -NotePropertyValue '11111111-1111-1111-1111-111111111111'
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $extraFreshPreflightProperty
    $extraFreshPreflightPropertyRejected = $false
    try {
        [void](Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath)
    }
    catch {
        $extraFreshPreflightPropertyRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $extraFreshPreflightPropertyRejected 'Fresh preflight parser must reject an extra CRM identity property.'

    $mutatingFreshPreflightEvidence = $validFreshPreflightProbeEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $mutatingFreshPreflightEvidence.operationExecuted = $true
    Write-StrictJsonFile $freshPreflightProbeEvidencePath $mutatingFreshPreflightEvidence
    $mutatingFreshPreflightRejected = $false
    try {
        [void](Get-StrictFreshPreflightProbeEvidenceFile $freshPreflightProbeEvidencePath)
    }
    catch {
        $mutatingFreshPreflightRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $mutatingFreshPreflightRejected 'Fresh preflight parser must reject operationExecuted=true.'

    # child 的 process exit code 是 parent 的可信度邊界：即使 child 留下外觀正確的
    # evidence，非零結束仍可能代表部分 operation、cleanup 或 runtime fault，不能讓
    # parent 只看檔案內容而宣告 go。這個合成 child 不啟動 CE、不讀取 credential，
    # 只寫入固定去識別化 evidence 後以非零碼結束，專門鎖定 parent 的 fail-closed 契約。
    $syntheticChildResult = Invoke-RunnerWithSyntheticFailingChild `
        $missingCredentialRunner `
        $testRepository `
        $profilePath `
        $sourceFixturePath `
        $sliceCFixturePath `
        $fixtureRoot
    Assert-True ($syntheticChildResult.ExitCode -eq 2) 'A non-zero child exit must make the parent return the no-go exit code.'
    Assert-True (
        $syntheticChildResult.Evidence.outcome -eq 'no-go' -and
        $syntheticChildResult.Evidence.reason -eq 'child-process-failed'
    ) ('A non-zero child exit must remain child-process-failed no-go even when evidence is otherwise valid. Actual sanitized reason: ' + $syntheticChildResult.Evidence.reason)
    Assert-True $syntheticChildResult.Evidence.operationExecuted 'An execute-lane child failure must conservatively preserve possible operation execution.'
    Assert-True (-not $syntheticChildResult.Evidence.featureFlagChanged) 'A child process failure must never imply a feature-flag change.'
    Assert-True (@($syntheticChildResult.Evidence.operations).Count -eq 5) 'An execute-lane child failure must expose exactly five sanitized not-started operations.'

    $notRunOperations = @()
    foreach ($operationId in $global:expectedOperationIds) {
        $notRunOperations += [ordered]@{
            operationId = $operationId
            outcome = 'not-run'
            reason = 'prior-operation-no-go'
            operationExecuted = $false
            reconciliationState = 'not-started'
            cleanupState = 'not-started'
        }
    }
    $fixtureNoGoEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'no-go'
        reason = 'fixture-precondition-failed'
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        operationExecuted = $false
        featureFlagChanged = $false
        operations = $notRunOperations
    }
    Write-StrictJsonFile $evidencePath $fixtureNoGoEvidence
    $parsedNoGo = Get-StrictSliceCEvidenceFile $evidencePath
    Assert-True ($parsedNoGo.outcome -eq 'no-go' -and $parsedNoGo.reason -eq 'fixture-precondition-failed' -and -not $parsedNoGo.operationExecuted) 'Valid fixture-precondition no-go evidence must remain visible to the parent handoff.'

    $reconciliationStateValues = @(
        'baseline-absent',
        'baseline-present',
        'not-expected-baseline-unproven',
        'non-target-baseline-unproven',
        'baseline-shape-unproven')
    $reconciliationOperations = @()
    for ($index = 0; $index -lt $global:expectedOperationIds.Count; $index++) {
        $reconciliationOperations += [ordered]@{
            operationId = $global:expectedOperationIds[$index]
            outcome = 'not-run'
            reason = 'baseline-unprovable'
            operationExecuted = $false
            reconciliationState = $reconciliationStateValues[$index]
            cleanupState = 'not-applicable'
        }
    }
    $reconciliationEvidence = [ordered]@{
        schemaVersion = 1
        outcome = 'no-go'
        reason = 'baseline-unprovable'
        profileAlias = $global:expectedProfileAlias
        deploymentProfileAlias = $global:expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $false
        readOnlyProbeExecuted = $true
        operationExecuted = $false
        featureFlagChanged = $false
        ownerBinding = 'matches-service-identity'
        probeStage = 'fixture-store-created'
        operations = $reconciliationOperations
    }
    Write-StrictJsonFile $evidencePath $reconciliationEvidence
    $parsedReconciliation = Get-StrictSliceCReconciliationEvidenceFile $evidencePath
    Assert-True ($parsedReconciliation.outcome -eq 'no-go' -and
        $parsedReconciliation.reason -eq 'baseline-unprovable' -and
        $parsedReconciliation.readOnlyProbeExecuted -and
        $parsedReconciliation.ownerBinding -eq 'matches-service-identity' -and
        $parsedReconciliation.probeStage -eq 'fixture-store-created' -and
        $parsedReconciliation.states.addMembership -eq 'baseline-absent' -and
        @($parsedReconciliation.operations).Count -eq 5) 'Strict reconciliation parser must accept only the fixed read-only schema.'

    # cleanup 是 read-only child 的 release-blocking 邊界。即使五段 projection 都已完成，
    # 任一 owner 無法釋放時，child 只能回報 cleanup-failure 與 readOnlyProbeExecuted=false；
    # parent 必須保留這個原因，而不是把它重新分類成一般的 baseline-unprovable。
    $cleanupFailureEvidence = $reconciliationEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $cleanupFailureEvidence.reason = 'cleanup-failure'
    $cleanupFailureEvidence.readOnlyProbeExecuted = $false
    Write-StrictJsonFile $evidencePath $cleanupFailureEvidence
    $parsedCleanupFailure = Get-StrictSliceCReconciliationEvidenceFile $evidencePath
    Assert-True ($parsedCleanupFailure.outcome -eq 'no-go' -and
        $parsedCleanupFailure.reason -eq 'cleanup-failure' -and
        -not $parsedCleanupFailure.readOnlyProbeExecuted -and
        @($parsedCleanupFailure.operations).Count -eq 5) 'Strict reconciliation parser must preserve a cleanup-failure no-go without claiming a completed read-only probe.'

    $childRetryField = $reconciliationEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $childRetryField | Add-Member -NotePropertyName safeToRetry -NotePropertyValue $false
    Write-StrictJsonFile $evidencePath $childRetryField
    $childRetryFieldRejected = $false
    try {
        [void](Get-StrictSliceCReconciliationEvidenceFile $evidencePath)
    }
    catch {
        $childRetryFieldRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $childRetryFieldRejected 'Child reconciliation evidence must not be allowed to declare safeToRetry.'

    $unknownReconciliationState = $reconciliationEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $unknownReconciliationState.operations[0].reconciliationState = 'unknown-state'
    Write-StrictJsonFile $evidencePath $unknownReconciliationState
    $unknownStateRejected = $false
    try {
        [void](Get-StrictSliceCReconciliationEvidenceFile $evidencePath)
    }
    catch {
        $unknownStateRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $unknownStateRejected 'Strict reconciliation parser must reject an unknown state category.'

    $unexpectedPropertyEvidence = $validEvidence | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $unexpectedPropertyEvidence.operations[0] | Add-Member -NotePropertyName unexpected -NotePropertyValue 'must-not-cross-handoff'
    Write-StrictJsonFile $evidencePath $unexpectedPropertyEvidence
    $unexpectedPropertyRejected = $false
    try {
        [void](Get-StrictSliceCEvidenceFile $evidencePath)
    }
    catch {
        $unexpectedPropertyRejected = $_.Exception.Message -eq 'evidence-result-unavailable'
    }
    Assert-True $unexpectedPropertyRejected 'Strict parser must reject unexpected child operation properties rather than reserializing them.'

    # 保護契約：parent 只可移除自己建立於 OS temp 下、名稱為 Slice C prefix 加 32 位 nonce 的目錄。
    # 故障注入同時包含非 Slice C 根目錄與僅有 prefix、卻不是 nonce 形狀的目錄；決定性斷言是唯一
    # 合法 owned directory 可完整刪除，兩種非 owned path 都必須保持存在，避免 cleanup 擴大為任意刪除。
    $ownedTemporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-slice-c-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($ownedTemporaryDirectory)
    Write-StrictTextFile (Join-Path $ownedTemporaryDirectory 'evidence.json') '{"sanitized":true}'
    Assert-True (Remove-OwnedSliceCTemporaryDirectory $ownedTemporaryDirectory) 'Owned Slice C temporary directory must be removed after evidence parsing.'
    Assert-True (-not (Test-Path -LiteralPath $ownedTemporaryDirectory)) 'Owned Slice C temporary directory must not remain after successful cleanup.'
    Assert-True (-not (Remove-OwnedSliceCTemporaryDirectory $fixtureRoot)) 'Cleanup helper must reject a non-Slice-C temporary directory.'
    Assert-True (Test-Path -LiteralPath $fixtureRoot -PathType Container) 'Cleanup helper must not delete a non-owned directory.'
    $prefixOnlyTemporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-slice-c-not-a-nonce-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($prefixOnlyTemporaryDirectory)
    Assert-True (-not (Remove-OwnedSliceCTemporaryDirectory $prefixOnlyTemporaryDirectory)) 'Cleanup helper must reject a Slice C prefix without the runner-owned nonce shape.'
    Assert-True (Test-Path -LiteralPath $prefixOnlyTemporaryDirectory -PathType Container) 'Cleanup helper must not delete a prefix-only directory.'
    Remove-Item -LiteralPath $prefixOnlyTemporaryDirectory -Force -Recurse

    # 保護契約：temporary evidence directory 的 cleanup 一旦失敗，已完成的正常 child、timeout 與
    # child-failure 結果都不得再宣稱可放行。故障注入使用三種已去識別化 internal result；決定性
    # 斷言是固定 no-go reason 覆寫原 outcome，但保留 operationExecuted 與五筆已投影 operation，
    # 讓 operator 知道不應重送可能已 dispatch 的 CE mutation。
    $normalCleanupFailure = New-TemporaryCleanupFailureResult ([pscustomobject]@{
        preflightOnly = $false
        operationExecuted = $true
        operations = $validOperations
    })
    Assert-True ($normalCleanupFailure.outcome -eq 'no-go' -and $normalCleanupFailure.reason -eq 'temporary-cleanup-failed' -and $normalCleanupFailure.operationExecuted -and @($normalCleanupFailure.operations).Count -eq 5) 'Normal child result must become a sanitized no-go when temporary cleanup fails.'
    $timeoutCleanupFailure = New-TemporaryCleanupFailureResult ([pscustomobject]@{
        preflightOnly = $false
        operationExecuted = $true
        operations = $notRunOperations
    })
    Assert-True ($timeoutCleanupFailure.outcome -eq 'no-go' -and $timeoutCleanupFailure.reason -eq 'temporary-cleanup-failed' -and $timeoutCleanupFailure.operationExecuted -and @($timeoutCleanupFailure.operations).Count -eq 5) 'Timeout result must remain no-go when temporary cleanup fails.'
    $childFailureCleanupFailure = New-TemporaryCleanupFailureResult ([pscustomobject]@{
        preflightOnly = $false
        operationExecuted = $true
        operations = $notRunOperations
    })
    Assert-True ($childFailureCleanupFailure.outcome -eq 'no-go' -and $childFailureCleanupFailure.reason -eq 'temporary-cleanup-failed' -and $childFailureCleanupFailure.operationExecuted -and @($childFailureCleanupFailure.operations).Count -eq 5) 'Child-failure result must remain no-go when temporary cleanup fails.'

    # 保護契約：Complete-HandoffResult 必須在輸出 JSON 前執行 cleanup，且 caller 的 exit code 決策
    # 只能讀取 cleanup 後的最終 outcome。故障注入為 non-owned directory，讓受限 helper 安全拒絕；
    # 決定性斷言是輸出投影與 script-owned final outcome 都是 temporary-cleanup-failed No-Go。
    $script:temporaryDirectory = $fixtureRoot
    $script:temporaryDirectoryCreated = $true
    $script:completedHandoffOutcome = $null
    $global:capturedHandoffResult = $null
    function global:Write-HandoffResult {
        param([object] $Result)
        [void]($global:capturedHandoffResult = $Result)
    }
    Complete-HandoffResult ([pscustomobject]@{
        preflightOnly = $false
        operationExecuted = $true
        operations = $validOperations
    })
    Assert-True ($global:capturedHandoffResult.outcome -eq 'no-go' -and $global:capturedHandoffResult.reason -eq 'temporary-cleanup-failed') 'Completion boundary must output the cleanup-failure no-go instead of the child success.'
    Assert-True ($script:completedHandoffOutcome -eq 'no-go') 'Exit-code selection must observe the cleanup-adjusted no-go outcome.'
    $script:temporaryDirectory = $null
    $script:temporaryDirectoryCreated = $false
    Remove-Item -LiteralPath Function:\global:Write-HandoffResult -Force
    Remove-Variable -Name capturedHandoffResult -Scope Global -ErrorAction SilentlyContinue

    [ordered]@{ outcome = 'passed'; checks = $script:assertionCount } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Force -Recurse
    }
    Remove-Variable -Name expectedProfileAlias, expectedDeploymentProfileAlias, expectedOperationIds -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name capturedHandoffResult -Scope Global -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath Function:\global:Write-HandoffResult -Force -ErrorAction SilentlyContinue
}
