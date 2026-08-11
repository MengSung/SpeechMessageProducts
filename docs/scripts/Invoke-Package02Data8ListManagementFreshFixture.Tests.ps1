<#
.SYNOPSIS
    驗證 P7.2 Slice C fresh-fixture PowerShell 控制面的離線授權邊界。

.DESCRIPTION
    本檔只建立 temporary repository、JSON descriptor 與 PowerShell child process；絕不讀取
    Credential Manager、不啟動 dotnet、不連線 CE，也不建立、更新或刪除任何 CRM 資料。它保護
    的契約是 fresh-fixture 的 provision 與 cleanup 都必須分別取得明確確認，且兩種 mutation
    mode 互斥。若確認缺失，runner 必須在 credential、temporary evidence、local ledger 或 child
    process 之前回傳單一去識別化 no-go JSON，避免誤觸既有 developer profile 或跨使用者狀態。

    測試只以目前 Windows identity 建立本機 descriptor shape；該 identity、GUID、temporary path
    與原始 PowerShell 錯誤均不會輸出到測試成功結果。finally 會刪除唯一 temporary root，確保
    測試 fixture、buffer 與 process output 不會跨本次測試保留。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$runnerPath = Join-Path $PSScriptRoot 'Invoke-Package02Data8ListManagementEvidence.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-fresh-fixture-script-test-' + [Guid]::NewGuid().ToString('N'))
$script:assertionCount = 0

function Assert-True {
    <#
    .SYNOPSIS
        以固定錯誤訊息執行本檔 assertions。

    .DESCRIPTION
        此 helper 不保留 runner 原始輸出或 credential-related 資訊；失敗只描述被破壞的
        contract。計數器僅限本次 test process，finally 不需要傳播到任何 parent scope。
    #>
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw $Message
    }

    $script:assertionCount++
}

function Assert-StrictTextFile {
    <#
    .SYNOPSIS
        驗證 runner 與本 contract test 都是 UTF-8 no-BOM、CRLF-only 且有 final CRLF。

    .DESCRIPTION
        byte buffer 只在此 function scope 存在，finally 會立即清除；這避免長生命週期測試
        process 保留任何來源檔內容，也使 encoding 失敗不會被文字自動修正掩蓋。
    #>
    param([string] $Path)

    $bytes = $null
    try {
        Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) 'Required script file is missing.'
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True ($bytes.Length -gt 0) 'Checked script must not be empty.'
        Assert-True (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Checked script must not contain a UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True (-not [Regex]::IsMatch($text, '(?<!\r)\n|\r(?!\n)')) 'Checked script must use CRLF-only line endings.'
        Assert-True $text.EndsWith("`r`n", [StringComparison]::Ordinal) 'Checked script must end with a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function Write-StrictTextFile {
    <#
    .SYNOPSIS
        以 repository-required encoding 寫入 temporary test input。

    .DESCRIPTION
        僅允許本測試唯一擁有的 temporary root 內路徑。檔案內容先正規化為 CRLF，並以
        UTF-8 without BOM 寫入；這避免 test input 本身意外掩蓋 runner 的 encoding gate。
    #>
    param([string] $Path, [string] $Text)

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }

    $normalized = ($Text -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

function Write-StrictJsonFile {
    <#
    .SYNOPSIS
        將 fixed test descriptor 寫成嚴格 JSON。

    .DESCRIPTION
        descriptor 僅包含合成 GUID 與目前 Windows identity shape，用來讓 runner 到達
        explicit-confirmation boundary；它不是 CE input，也不會在 test 結束後保留。
    #>
    param([string] $Path, [object] $Value)

    Write-StrictTextFile -Path $Path -Text ($Value | ConvertTo-Json -Depth 12)
}

function Invoke-RunnerJson {
    <#
    .SYNOPSIS
        執行 runner 並擷取唯一 sanitized JSON result。

    .DESCRIPTION
        child PowerShell 僅接收 temporary repository 與 descriptor paths；本 helper 不會設定
        CRM_PASSWORD 或其他 live-mode 環境變數。當 runner 實作正確時，缺少確認必須在任何
        credential/native call 或 dotnet child 前結束，故此 helper 可以離線驗證那個邊界。
    #>
    param(
        [string] $CommandPath = $runnerPath,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath,
        [switch] $ProvisionFreshFixture,
        [switch] $ReplaceStaleDescriptor,
        [switch] $CleanupFreshFixture,
        [switch] $ConfirmFreshFixtureCleanup
    )

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $CommandPath,
        '-RepositoryPath', $RepositoryPath,
        '-ProfileInputPath', $ProfilePath,
        '-SourceFixtureDescriptorPath', $SourceFixturePath,
        '-FixtureDescriptorPath', $SliceCFixturePath,
        '-Json'
    )
    if ($ProvisionFreshFixture) {
        $arguments += '-ProvisionFreshFixture'
    }
    if ($ReplaceStaleDescriptor) {
        $arguments += '-ReplaceStaleDescriptor'
    }
    if ($CleanupFreshFixture) {
        $arguments += '-CleanupFreshFixture'
    }
    if ($ConfirmFreshFixtureCleanup) {
        $arguments += '-ConfirmFreshFixtureCleanup'
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

    $jsonLines = @($lines | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_) -and
        ([string]$_).TrimStart().StartsWith('{')
    })
    Assert-True ($jsonLines.Count -eq 1) 'Runner must emit exactly one sanitized JSON line for a confirmation refusal.'
    return [pscustomobject]@{
        ExitCode = $exitCode
        Evidence = ($jsonLines[0] | ConvertFrom-Json)
    }
}

function Invoke-RunnerBinderFailure {
    <#
    .SYNOPSIS
        證明 provision 與 cleanup 不得在同一 invocation 被選取。

    .DESCRIPTION
        parameter binder 必須在 script body、Credential Manager、temporary directory 或 child process
        之前拒絕衝突的 mutation mode。binder failure 沒有 JSON 是刻意契約；此 helper 只保留計數，
        不把原始 error text 傳回或寫入成功輸出。
    #>
    param(
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath,
        [string[]] $ModeArguments = @(
            '-ProvisionFreshFixture',
            '-CleanupFreshFixture')
    )

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath,
        '-RepositoryPath', $RepositoryPath,
        '-ProfileInputPath', $ProfilePath,
        '-SourceFixtureDescriptorPath', $SourceFixturePath,
        '-FixtureDescriptorPath', $SliceCFixturePath,
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

    $jsonLines = @($lines | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_) -and
        ([string]$_).TrimStart().StartsWith('{')
    })
    return [pscustomobject]@{
        ExitCode = $exitCode
        JsonLineCount = $jsonLines.Count
    }
}

function New-TestRepository {
    <#
    .SYNOPSIS
        建立足以通過純本機 preflight 的最小 temporary repository。

    .DESCRIPTION
        所有檔案都由此測試建立並由 finally 遞迴刪除。matrix 只複製已核准的 repository bytes；
        config 固定為 Embedded + Data8 且所有 product flags 為 false，避免測試成為 feature-flag
        或 connector routing 的替代入口。
    #>
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
    Write-StrictTextFile (Join-Path $Root 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') @'
{
  "DynamicsAccess": {
    "Package01FeeReadsEnabled": false,
    "Package02ContactBasicInfoUpdatesEnabled": false,
    "Package02ContactProfileOperationsEnabled": false,
    "ConnectionMode": "Embedded",
    "ProfileAlias": "sunnyvalechback"
  }
}
'@
}

function Get-ProcessEnvironmentSnapshot {
    <#
    .SYNOPSIS
        擷取本 contract suite 會故意污染的 process environment 值。

    .DESCRIPTION
        fresh-fixture parent 會暫時設定 credential、mode、ledger 與 evidence 的 process variables。
        此 helper 只保存本機測試刻意放入的固定 sentinel，並在 caller 的 finally 還原；它不會讀取
        真實 Credential Manager、browser session 或任何使用者祕密。這讓下方 assertion 能確認
        runner 自己的 finally 沒有把 child-bound mutable state 留給後續 invocation。
    #>
    param([string[]] $Names)

    $snapshot = [ordered]@{}
    foreach ($name in $Names) {
        $snapshot[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    return $snapshot
}

function Restore-ProcessEnvironmentSnapshot {
    <#
    .SYNOPSIS
        以原本的 process scope 值還原測試環境。

    .DESCRIPTION
        所有 synthetic selector、temporary LOCALAPPDATA 與 fresh-fixture sentinel 都由本 suite
        單獨擁有。即使 runner、fake child 或 assertion 失敗，本 helper 仍在 finally 執行，避免
        下一個合約測試繼承假的 dotnet、credential marker 或 descriptor 路徑。
    #>
    param([System.Collections.IDictionary] $Snapshot)

    foreach ($name in $Snapshot.Keys) {
        # $null 表示原本不存在，必須交給 .NET 移除而不是轉成空字串；否則 test 自己會留下
        # 一個空的 environment entry，反而掩蓋 runner 對 absent variable 的 restoration defect。
        [Environment]::SetEnvironmentVariable([string]$name, $Snapshot[$name], 'Process')
    }
}

function Get-FileFingerprint {
    <#
    .SYNOPSIS
        取得 descriptor 的不可逆雜湊，以驗證拒絕路徑不發佈任何 local ID。

    .DESCRIPTION
        測試不把 descriptor 原文、GUID 或 owner identity 放進 assertion message。SHA-256 僅用於
        比較同一個 temporary test input 的位元組是否完全未變；這直接保護 ambiguous、incomplete
        或 child failure 時不可覆寫 stale descriptor 的控制面契約。
    #>
    param([string] $Path)

    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) 'Required temporary descriptor is missing.'
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Write-StrictFreshSeed {
    <#
    .SYNOPSIS
        建立 current-user 專屬的 synthetic static seed，不夾帶任何 active fresh graph ID。

    .DESCRIPTION
        seed 是 fresh preflight/provision 的唯一靜態輸入；source contact、fresh leader 與 relationship
        list 都必須在每輪 provision 後由 ledger 證明並另行發佈。此 helper 因此只寫五個 list、baseline
        leader、UTC Sunday 與去識別化 deployment metadata，讓測試可驗證 cleanup 不會破壞下一輪輸入。
    #>
    param([string] $Path, [string] $OwnerIdentity)

    Write-StrictJsonFile -Path $Path -Value ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-slice-c-seed'
        profileAlias = 'sunnyvalechback'
        deploymentProfileAlias = 'crm91'
        ceVersion = '9.1'
        connector = 'Data8'
        marker = 'p7.2-list-management-seed'
        ownerIdentity = $OwnerIdentity
        addListId = '11111111-1111-1111-1111-111111111111'
        removeListId = '22222222-2222-2222-2222-222222222222'
        smallGroupListId = '33333333-3333-3333-3333-333333333333'
        baselineLeaderContactId = '44444444-4444-4444-4444-444444444444'
        transferSourceListId = '66666666-6666-6666-6666-666666666666'
        transferTargetListId = '77777777-7777-7777-7777-777777777777'
        transferWeekStartUtc = '2026-08-09T00:00:00.0000000+00:00'
    })
}

function Assert-DescriptorsRemainUnpublished {
    <#
    .SYNOPSIS
        驗證 source 與 Slice C descriptor 在 no-go path 保持原狀。

    .DESCRIPTION
        fresh provision 的 child 即使已寫入外觀正確的 evidence 或 ledger，只要 exit code、schema、
        read-back 或 final graph proof 有任何不確定性，parent 都不得發佈 fresh ID。此 assertion
        使用雜湊而非輸出內容，避免測試記錄暴露 descriptor 中本來就受保護的 local identifiers。
    #>
    param(
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath,
        [string] $ExpectedSourceFingerprint,
        [string] $ExpectedSliceCFingerprint
    )

    $sourceIsUnpublished = if ([string]::IsNullOrEmpty($ExpectedSourceFingerprint)) {
        -not (Test-Path -LiteralPath $SourceFixturePath -PathType Leaf)
    }
    else {
        (Get-FileFingerprint $SourceFixturePath) -ceq $ExpectedSourceFingerprint
    }
    $sliceCIsUnpublished = if ([string]::IsNullOrEmpty($ExpectedSliceCFingerprint)) {
        -not (Test-Path -LiteralPath $SliceCFixturePath -PathType Leaf)
    }
    else {
        (Get-FileFingerprint $SliceCFixturePath) -ceq $ExpectedSliceCFingerprint
    }
    Assert-True ($sourceIsUnpublished -and $sliceCIsUnpublished) 'Fresh-fixture no-go path must not publish or overwrite either descriptor.'
}

function Assert-DescriptorsQuarantinedAfterPublicationFailure {
    <#
    .SYNOPSIS
        驗證 partial fresh descriptor publication 不會重新啟用 stale descriptor bytes。

    .DESCRIPTION
        source descriptor 已寫入但 Slice C descriptor 尚未完成時，將舊 bytes 回寫會把已不再可信
        的 pre-provision graph 重新標示為可用，並可能讓後續 cleanup 讀取錯誤 session/profile 的 ID。
        此 assertion 只檢查 test-owned exact paths 都不存在；remote mutation recovery 唯一保留物必須
        是 strict current-user pending ledger，而不是可被一般 execution lane 誤用的 descriptor pair。
    #>
    param(
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath
    )

    Assert-True (
        -not (Test-Path -LiteralPath $SourceFixturePath -PathType Leaf) -and
        -not (Test-Path -LiteralPath $SliceCFixturePath -PathType Leaf)
    ) 'Partial descriptor publication must quarantine both exact descriptor paths instead of restoring stale bytes.'
}

function Assert-StrictPendingFreshFixtureLedger {
    <#
    .SYNOPSIS
        驗證 publication failure 後保留的 recovery ledger 仍是 strict v2 current-user state。

    .DESCRIPTION
        ledger 是 ambiguous/partial provision 唯一可接受的 local recovery input。此 helper 不輸出
        GUID、nonce、owner 或 path；它只檢查 UTF-8/CRLF、固定 top-level schema、current Windows
        owner、fresh-graph-proven stage 及 immutable original target leader，證明 parent 沒有用 stale
        descriptor 當作 fallback，也沒有遺失 cleanup/reconciliation 所需的 exact-ID state。
    #>
    param(
        [string] $Path,
        [string] $OwnerIdentity,
        [string] $ExpectedOriginalTargetLeaderContactId
    )

    Assert-StrictTextFile $Path
    $ledger = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
    $expectedNames = @(
        'schemaVersion',
        'fixtureId',
        'profileAlias',
        'ceVersion',
        'connector',
        'ownerIdentity',
        'stage',
        'nonce',
        'sourceContactId',
        'leaderContactId',
        'relationshipListId',
        'originalTargetLeaderContactId')
    $actualNames = @($ledger.PSObject.Properties.Name)
    $nonce = [Guid]::Empty
    $sourceId = [Guid]::Empty
    $leaderId = [Guid]::Empty
    $relationshipId = [Guid]::Empty
    Assert-True (
        $actualNames.Count -eq $expectedNames.Count -and
        @($actualNames | Where-Object { $_ -cnotin $expectedNames }).Count -eq 0 -and
        @($expectedNames | Where-Object { $_ -cnotin $actualNames }).Count -eq 0 -and
        $ledger.schemaVersion -eq 2 -and
        $ledger.fixtureId -eq 'p7.2-slice-c-fresh-fixture' -and
        $ledger.profileAlias -eq 'crm91' -and
        $ledger.ceVersion -eq '9.1' -and
        $ledger.connector -eq 'Data8' -and
        [string]::Equals([string]$ledger.ownerIdentity, $OwnerIdentity, [StringComparison]::OrdinalIgnoreCase) -and
        $ledger.stage -eq 'fresh-graph-proven' -and
        [Guid]::TryParse([string]$ledger.nonce, [ref]$nonce) -and $nonce -ne [Guid]::Empty -and
        [Guid]::TryParse([string]$ledger.sourceContactId, [ref]$sourceId) -and $sourceId -ne [Guid]::Empty -and
        [Guid]::TryParse([string]$ledger.leaderContactId, [ref]$leaderId) -and $leaderId -ne [Guid]::Empty -and
        [Guid]::TryParse([string]$ledger.relationshipListId, [ref]$relationshipId) -and $relationshipId -ne [Guid]::Empty -and
        $ledger.originalTargetLeaderContactId -eq $ExpectedOriginalTargetLeaderContactId
    ) 'Publication failure must retain one strict v2 current-user pending ledger without a stale descriptor fallback.'
}

function ConvertTo-Base64StrictJsonPayload {
    <#
    .SYNOPSIS
        將固定 synthetic child payload 編碼成短生命週期 UTF-8 bytes。

    .DESCRIPTION
        fake child 只能得到本測試建立的去識別化 JSON；base64 避免將 JSON quote 或換行拼入 cmd
        command line。byte buffer 在轉換後立即清除，且 payload 僅含合成 GUID、固定 reason 與
        本機 current-user shape，絕不含密碼、endpoint、cookie 或真實 CRM response。
    #>
    param([object] $Value)

    $bytes = $null
    try {
        $json = ($Value | ConvertTo-Json -Compress -Depth 12) + "`r`n"
        $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($json)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function ConvertTo-Base64StrictTextPayload {
    <#
    .SYNOPSIS
        將已精確形成的 synthetic JSON bytes 安全傳遞給 fake child。

    .DESCRIPTION
        少數 schema 邊界必須保留 JSON 原始 numeric token，例如 decimal、exponent 或 quoted
        number；ConvertTo-Json 會正規化它們，因而無法驗證 parent 是否拒絕非整數 token。此 helper
        僅接受 test-owned 字串，正規化為 UTF-8 no-BOM、CRLF-only、final CRLF，再以 base64 避免
        shell quoting 改寫 payload。buffer 的唯一 owner 是本函式，finally 會清除它，且測試不會輸出
        payload、temporary path 或任何 production identity。
    #>
    param([string] $Text)

    $bytes = $null
    try {
        $normalized = ($Text -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
        $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($normalized)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function New-SyntheticFreshFixtureEvidence {
    <#
    .SYNOPSIS
        建立與 fresh child wire contract 對齊的最小 evidence。

    .DESCRIPTION
        所有欄位、lane、outcome 和 publication bit 都由 test 固定指定，藉此驗證 parent strict
        parser 不接受 child 額外夾帶的 identity 或錯誤資訊。此物件從不代表真實 CE 執行結果；
        它只用於離線故障注入，最後由 temporary root 的 finally 刪除。
    #>
    param(
        [string] $Lane,
        [string] $Outcome,
        [string] $Reason,
        [bool] $OperationExecuted,
        [bool] $DescriptorPublicationReady
    )

    return [ordered]@{
        schemaVersion = 1
        lane = $Lane
        outcome = $Outcome
        reason = $Reason
        operationExecuted = $OperationExecuted
        descriptorPublicationReady = $DescriptorPublicationReady
        featureFlagChanged = $false
    }
}

function New-SyntheticFreshFixtureDiagnostic {
    <#
    .SYNOPSIS
        建立 fresh provision 非零 child exit 專用的最小去識別化診斷 payload。

    .DESCRIPTION
        此物件只模擬 parent-owned temporary diagnostic file 的兩個允許欄位；測試可在不接觸
        credential、CRM response 或真實 child output 的情況下注入未知欄位與未知 category。
        所有 payload 都只在 scenario temporary root 存活，讓負向 parser 測試能明確證明 malformed
        diagnostic 不得穿越 child-process-failed 邊界。
    #>
    param(
        [string] $Category,
        [switch] $IncludeUnexpectedProperty
    )

    $diagnostic = [ordered]@{
        schemaVersion = 1
        category = $Category
    }
    if ($IncludeUnexpectedProperty) {
        $diagnostic.unexpectedChildField = 'synthetic-only'
    }

    return $diagnostic
}

function New-SyntheticFreshFixtureLedger {
    <#
    .SYNOPSIS
        建立 parent 僅在完整 provision 後可讀取的 current-user ledger shape。

    .DESCRIPTION
        GUID 都是固定合成值，與 temporary descriptor 及任何 CRM record 無關。正常 shape 使用
        fresh-graph-proven；測試可刻意加入未知欄位或未完成 stage，確認 parent 不會把 partial
        recovery state 當成 descriptor publication 的授權來源。
    #>
    param(
        [string] $OwnerIdentity,
        [string] $Stage = 'fresh-graph-proven',
        [string] $OriginalTargetLeaderContactId = '44444444-4444-4444-4444-444444444444',
        [switch] $IncludeUnexpectedProperty
    )

    $ledger = [ordered]@{
        # schema v2 明確保留 provision 前的 descriptor target leader。cleanup 僅能從這個
        # immutable recovery value 建構 baseline request；它不可被 publication 後的新 leader 覆蓋。
        schemaVersion = 2
        fixtureId = 'p7.2-slice-c-fresh-fixture'
        profileAlias = 'crm91'
        ceVersion = '9.1'
        connector = 'Data8'
        ownerIdentity = $OwnerIdentity
        stage = $Stage
        # nonce 由 parent 每次 invocation 產生；fake child 在寫入前以自己收到的 process value
        # 取代此 sentinel，讓測試同時保護 parent 對 ledger nonce 的 cross-process binding。
        nonce = '__PARENT_NONCE__'
        sourceContactId = '99999999-9999-9999-9999-999999999999'
        leaderContactId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
        relationshipListId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
        originalTargetLeaderContactId = $OriginalTargetLeaderContactId
    }
    if ($IncludeUnexpectedProperty) {
        # 不可接受的欄位模擬 child 越過 evidence/ledger 邊界攜帶未定義資料；值固定且不含祕密。
        $ledger.unexpectedChildField = 'synthetic-only'
    }

    return $ledger
}

function New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral {
    <#
    .SYNOPSIS
        以指定 JSON literal 建立 synthetic fresh-fixture payload，保留 schemaVersion 的原始 token。

    .DESCRIPTION
        此 helper 只服務 strict parser regression：PowerShell JSON serializer 會將 quoted、decimal
        或 exponent numeric token 正規化，使測試無法區分它們與合法整數。先由既有 fixture 產生完整
        test-owned ledger，再只替換第一個 schemaVersion scalar，確保其餘 owner、nonce、GUID 與
        stage 均維持已驗證 baseline。任何 serializer shape 漂移會立即讓測試失敗，避免 fault
        injection 靜默失效或誤把未知 payload 帶進 child process。
    #>
    param(
        [object] $Value,
        [int] $ExpectedSchemaVersion,
        [string] $SchemaVersionLiteral
    )

    # 同一個 test-only helper 同時覆蓋 evidence 與 ledger，避免兩條跨程序 schema 測試各自
    # 接受不同的 JSON token shape；其餘 fixture 欄位保持不變，故失敗只代表 schema 邊界拒絕。
    $json = $Value | ConvertTo-Json -Compress -Depth 12
    $expectedToken = '"schemaVersion":' + [string]$ExpectedSchemaVersion
    Assert-True $json.Contains($expectedToken) 'Synthetic fresh payload must expose exactly one serializable schemaVersion token.'
    return $json.Replace($expectedToken, ('"schemaVersion":' + $SchemaVersionLiteral))
}

function Write-SyntheticFreshChild {
    <#
    .SYNOPSIS
        建立不呼叫 dotnet、Credential Manager 或 CE 的 fake fresh child。

    .DESCRIPTION
        child 只寫 parent 已提供的 evidence/ledger paths，再以指定 exit code 結束。它同時把 parent
        傳入的 mode 和 temporary directory 觀測值寫入另一個 test-owned file，讓 test 能在 parent
        process 結束後驗證 environment recovery 與 directory cleanup。觀測檔只存在 temporary root，
        不會進 console、TRX 或 repository。
    #>
    param(
        [string] $Path,
        [object] $Evidence,
        [string] $EvidenceJsonOverride = $null,
        [object] $Ledger,
        [object] $LedgerJsonOverride = $null,
        [string] $DiagnosticCategory = '',
        [string] $DiagnosticJsonOverride = $null,
        [int] $ExitCode
    )

    $evidencePayload = if (-not [string]::IsNullOrEmpty($EvidenceJsonOverride)) {
        ConvertTo-Base64StrictTextPayload $EvidenceJsonOverride
    }
    else {
        ConvertTo-Base64StrictJsonPayload $Evidence
    }
    $diagnosticPayload = if (-not [string]::IsNullOrEmpty($DiagnosticJsonOverride)) {
        ConvertTo-Base64StrictTextPayload $DiagnosticJsonOverride
    }
    elseif (-not [string]::IsNullOrWhiteSpace($DiagnosticCategory)) {
        ConvertTo-Base64StrictJsonPayload (New-SyntheticFreshFixtureDiagnostic -Category $DiagnosticCategory)
    }
    else {
        ''
    }
    $writeDiagnostic = if ([string]::IsNullOrEmpty($diagnosticPayload)) { 'false' } else { 'true' }
    $ledgerPayload = if ($null -ne $LedgerJsonOverride) {
        ConvertTo-Base64StrictTextPayload $LedgerJsonOverride
    }
    elseif ($null -eq $Ledger) {
        ''
    }
    else {
        ConvertTo-Base64StrictJsonPayload $Ledger
    }
    $writeLedger = if ($null -eq $Ledger -and $null -eq $LedgerJsonOverride) { 'false' } else { 'true' }
    $childSource = @'
$ErrorActionPreference = 'Stop'

function Write-EncodedSyntheticFile {
    param([string] $TargetPath, [string] $Payload)

    $buffer = $null
    try {
        $buffer = [Convert]::FromBase64String($Payload)
        [IO.File]::WriteAllBytes($TargetPath, $buffer)
    }
    finally {
        if ($null -ne $buffer) {
            [Array]::Clear($buffer, 0, $buffer.Length)
        }
    }
}

function Write-EncodedSyntheticLedger {
    param([string] $TargetPath, [string] $Payload)

    $payloadBytes = $null
    $ledgerBytes = $null
    try {
        $payloadBytes = [Convert]::FromBase64String($Payload)
        $ledgerText = [Text.UTF8Encoding]::new($false, $true).GetString($payloadBytes)
        $parentNonce = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_NONCE', 'Process')
        if ([string]::IsNullOrWhiteSpace($parentNonce)) {
            throw 'synthetic-fresh-parent-nonce-missing'
        }
        $ledgerText = $ledgerText.Replace('__PARENT_NONCE__', $parentNonce)
        $ledgerBytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($ledgerText)
        [IO.File]::WriteAllBytes($TargetPath, $ledgerBytes)
    }
    finally {
        if ($null -ne $payloadBytes) {
            [Array]::Clear($payloadBytes, 0, $payloadBytes.Length)
        }
        if ($null -ne $ledgerBytes) {
            [Array]::Clear($ledgerBytes, 0, $ledgerBytes.Length)
        }
    }
}

$evidencePath = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_EVIDENCE_PATH', 'Process')
$diagnosticPath = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH', 'Process')
$ledgerPath = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_PATH', 'Process')
$ledgerRoot = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_ROOT', 'Process')
$childObservationPath = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_CHILD_OBSERVATION_PATH', 'Process')
if ([string]::IsNullOrWhiteSpace($evidencePath) -or [string]::IsNullOrWhiteSpace($childObservationPath)) {
    throw 'synthetic-fresh-child-input-missing'
}

if ('__WRITE_LEDGER__' -ceq 'true') {
    if ([string]::IsNullOrWhiteSpace($ledgerPath) -or [string]::IsNullOrWhiteSpace($ledgerRoot)) {
        throw 'synthetic-fresh-ledger-input-missing'
    }
    if (-not (Test-Path -LiteralPath $ledgerRoot -PathType Container)) {
        # ledger root 的唯一建立者是 parent；child 不得補建未知或跨 session 的 control-plane
        # directory。缺失時立即失敗，才能驗證 fresh ledger 不會在未證明 owner 的路徑發佈。
        throw 'synthetic-fresh-parent-owned-ledger-root-missing'
    }
    Write-EncodedSyntheticLedger -TargetPath $ledgerPath -Payload '__LEDGER_PAYLOAD__'
}

Write-EncodedSyntheticFile -TargetPath $evidencePath -Payload '__EVIDENCE_PAYLOAD__'
if ('__WRITE_DIAGNOSTIC__' -ceq 'true' -and
    -not [string]::IsNullOrWhiteSpace($diagnosticPath)) {
    Write-EncodedSyntheticFile -TargetPath $diagnosticPath -Payload '__DIAGNOSTIC_PAYLOAD__'
}
$observation = [ordered]@{
    evidenceDirectory = [IO.Path]::GetDirectoryName($evidencePath)
    ledgerPath = $ledgerPath
    ledgerRoot = $ledgerRoot
    provisionMode = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION', 'Process')
    cleanupMode = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP', 'Process')
    descriptorConfirmation = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION', 'Process')
    diagnosticPath = $diagnosticPath
    diagnosticFileExists = -not [string]::IsNullOrWhiteSpace($diagnosticPath) -and (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)
    # cleanup child 不可使用 provision 時才需要的 descriptor target，也不可繼承 legacy
    # Slice C target。兩者都記錄為 $null 才代表 parent 已從 child process environment 移除。
    freshExistingTargetLeaderId = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID', 'Process')
    legacyTargetLeaderContactId = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID', 'Process')
    # 此值不是 production allowlist 的成員；它模擬另一個 shell 或舊版 runner 遺留的 legacy state。
    # fresh child 看見此 sentinel 即代表 parent 隔離不完整，可能跨 session 重用 mutable fixture input。
    undeclaredLegacySentinel = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_UNDECLARED_LEGACY_SENTINEL', 'Process')
    # 名稱帶有 FRESH_ 但不在 parent 的精確 control-plane allowlist；它仍是跨 session legacy state，
    # 若 child 看見它，即代表 prefix-based exclusion 錯把未知 mutable input 當成當前 invocation binding。
    undeclaredFreshLegacySentinel = [Environment]::GetEnvironmentVariable('P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL', 'Process')
    # SPEECHMESSAGE_ namespace 也可由長壽命 shell 留下未知 fresh state；它不是任何 parent-owned
    # binding，fresh child 看見它即代表第二個 legacy namespace 未被完整 snapshot/scrub。
    undeclaredSpeechmessageFreshLegacySentinel = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL', 'Process')
}
$legacyEnvironment = [ordered]@{}
foreach ($legacyName in @(
    'SPEECHMESSAGE_P7_2_SLICE_C_LIVE',
    'SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE',
    'P7_2_SLICE_C_FIXTURE_OWNER',
    'P7_2_SLICE_C_FIXTURE_MARKER',
    'P7_2_SLICE_C_CONTACT_ID',
    'P7_2_SLICE_C_ADD_LIST_ID',
    'P7_2_SLICE_C_REMOVE_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID',
    'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_WEEK_START_UTC',
    'P7_2_SLICE_C_EVIDENCE_PATH',
    'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH',
    'P7_2_SLICE_C_EVIDENCE_JSON',
    'P7_2_SLICE_C_RETIRED_TRX_EVIDENCE',
    'P7_2_SLICE_C_TARGET_OWNER_ID')) {
    $legacyEnvironment[$legacyName] = [Environment]::GetEnvironmentVariable($legacyName, 'Process')
}
$observation.legacyEnvironment = $legacyEnvironment
[IO.File]::WriteAllText(
    $childObservationPath,
    ($observation | ConvertTo-Json -Compress -Depth 4) + "`r`n",
    [Text.UTF8Encoding]::new($false))
exit __EXIT_CODE__
'@
    $childSource = $childSource.Replace('__EVIDENCE_PAYLOAD__', $evidencePayload)
    $childSource = $childSource.Replace('__DIAGNOSTIC_PAYLOAD__', $diagnosticPayload)
    $childSource = $childSource.Replace('__LEDGER_PAYLOAD__', $ledgerPayload)
    $childSource = $childSource.Replace('__WRITE_LEDGER__', $writeLedger)
    $childSource = $childSource.Replace('__WRITE_DIAGNOSTIC__', $writeDiagnostic)
    $childSource = $childSource.Replace('__EXIT_CODE__', [string]$ExitCode)
    Write-StrictTextFile -Path $Path -Text $childSource
}

function New-SyntheticFreshRunner {
    <#
    .SYNOPSIS
        建立只替換 credential/dotnet seam 的 runner test copy。

    .DESCRIPTION
        production runner 本身完全不修改。temporary copy 保留真實的 parameter binding、environment
        snapshot、child exit-code gate、strict parser、descriptor publication 與 finally cleanup control
        flow，只把 native Credential Manager reader 換成固定 test secret，並精確選取 fake dotnet.cmd。
        結尾 observer 在原本 finally 之後讀回 variables，故可驗證 production source 的 restoration
        迴圈真的先完成，而不是僅檢查 test parent 的獨立 process environment。
    #>
    param(
        [string] $DestinationPath,
        [string] $RunnerPath,
        [switch] $FailFreshDescriptorPublicationAfterSourceWrite,
        [switch] $OmitFreshLedgerRootForChild
    )

    $syntheticRunner = [IO.File]::ReadAllText($RunnerPath, [Text.UTF8Encoding]::new($false, $true))
    $credentialPresenceLine = 'return [SpeechMessage.P72SliceC.CredentialPresenceReader]::Exists($credentialTarget)'
    $credentialPasswordLine = 'return [SpeechMessage.P72SliceCLive.CredentialReader]::ReadGenericSecret($credentialTarget)'
    $dotnetSelectionLine = '$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue'
    $fixturePublicationLine = '        Write-AtomicStrictJsonFile -Path $FixturePath -JsonText $fixtureText'
    $freshLedgerRootLine = '        [Environment]::SetEnvironmentVariable(''P7_2_SLICE_C_FRESH_LEDGER_ROOT'', [string]$freshControlPlaneRoots.ledgerRoot, ''Process'')'
    $omittedFreshLedgerRootLine = '        [Environment]::SetEnvironmentVariable(''P7_2_SLICE_C_FRESH_LEDGER_ROOT'', $null, ''Process'')'
    $exitLine = 'exit $scriptExitCode'
    Assert-True $syntheticRunner.Contains($credentialPresenceLine) 'Synthetic fresh runner requires the credential-presence seam.'
    Assert-True $syntheticRunner.Contains($credentialPasswordLine) 'Synthetic fresh runner requires the credential-password seam.'
    Assert-True $syntheticRunner.Contains($dotnetSelectionLine) 'Synthetic fresh runner requires the dotnet selection seam.'
    if ($FailFreshDescriptorPublicationAfterSourceWrite) {
        Assert-True $syntheticRunner.Contains($fixturePublicationLine) 'Synthetic fresh runner requires the second descriptor-publication seam.'
    }
    if ($OmitFreshLedgerRootForChild) {
        Assert-True $syntheticRunner.Contains($freshLedgerRootLine) 'Synthetic fresh runner requires the parent-owned ledger-root environment seam.'
    }
    Assert-True $syntheticRunner.Contains($exitLine) 'Synthetic fresh runner requires the final process-exit seam.'

    $syntheticRunner = $syntheticRunner.Replace($credentialPresenceLine, 'return $true')
    $syntheticRunner = $syntheticRunner.Replace($credentialPasswordLine, "return 'synthetic-fresh-test-secret'")
    $syntheticRunner = $syntheticRunner.Replace(
        $dotnetSelectionLine,
        '$dotnetCommand = Get-Command $env:SPEECHMESSAGE_P72_SYNTHETIC_FRESH_DOTNET_PATH -CommandType Application -ErrorAction SilentlyContinue')
    if ($FailFreshDescriptorPublicationAfterSourceWrite) {
        # 第一個 descriptor 已由真實 parent code 寫入；只把第二次 write 換成固定 synthetic fault，
        # 使測試能驗證 parent 的 partial-publication quarantine，而不修改 production runner 或磁碟。
        $syntheticRunner = $syntheticRunner.Replace(
            $fixturePublicationLine,
            "        throw 'synthetic-fresh-descriptor-publication-failure'")
    }
    if ($OmitFreshLedgerRootForChild) {
        # fault injection 僅移除 child 可見的 root binding；parent 仍會照 production flow 建立自己的
        # root。fake child 因此必須拒絕無 owner-proven root 的 ledger write，而不是自行建立資料夾。
        $syntheticRunner = $syntheticRunner.Replace(
            $freshLedgerRootLine,
            $omittedFreshLedgerRootLine)
    }

    $restorationObserver = @'
$restorationObservationPath = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_RESTORE_OBSERVATION_PATH', 'Process')
if (-not [string]::IsNullOrWhiteSpace($restorationObservationPath)) {
    $restoredEnvironment = [ordered]@{}
    foreach ($environmentName in $inputEnvironmentNames) {
        $restoredEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
    }
    [IO.File]::WriteAllText(
        $restorationObservationPath,
        ($restoredEnvironment | ConvertTo-Json -Compress -Depth 4) + "`r`n",
        [Text.UTF8Encoding]::new($false))
}
exit $scriptExitCode
'@
    $syntheticRunner = $syntheticRunner.Replace($exitLine, $restorationObserver)
    Write-StrictTextFile -Path $DestinationPath -Text $syntheticRunner
}

function New-SyntheticFreshScenarioContext {
    <#
    .SYNOPSIS
        建立單一 synthetic fresh-fixture invocation 的獨立 LOCALAPPDATA control plane。

    .DESCRIPTION
        每個情境都複製 descriptor 到自己的 temporary LOCALAPPDATA\SpeechMessage\Dynamics\P7.2
        根目錄，讓 parent 的 exact current-user path 驗證保持真實。同時，任何 ambiguous、child
        failure 或 schema rejection 留下的 ledger 都只能阻擋該情境，不能污染下一個 fault-injection
        test 或使用者的實際 profile。複製內容與 fingerprints 僅留在 suite 擁有的 temporary root，
        finally 會由最外層清理；不會讀取或覆寫實際 LOCALAPPDATA descriptor。
    #>
    param(
        [string] $TemporaryRoot,
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath
    )

    $scenarioRoot = Join-Path $TemporaryRoot ('synthetic-fresh-' + [Guid]::NewGuid().ToString('N'))
    $localAppDataRoot = Join-Path $scenarioRoot 'local-app-data'
    $descriptorRoot = Join-Path $localAppDataRoot 'SpeechMessage\Dynamics\P7.2'
    $scenarioSourceFixturePath = Join-Path $descriptorRoot 'contact-basic-info-fixture.json'
    $scenarioSliceCFixturePath = Join-Path $descriptorRoot 'list-management-fixture.json'
    $scenarioSeedPath = Join-Path $descriptorRoot 'fresh-slice-c-seed.json'
    $sourceText = [IO.File]::ReadAllText($SourceFixturePath, [Text.UTF8Encoding]::new($false, $true))
    $sliceCText = [IO.File]::ReadAllText($SliceCFixturePath, [Text.UTF8Encoding]::new($false, $true))
    try {
        $seedOwnerIdentity = [string](($sliceCText | ConvertFrom-Json).ownerIdentity)
        Write-StrictFreshSeed -Path $scenarioSeedPath -OwnerIdentity $seedOwnerIdentity
    }
    finally {
        $sourceText = $null
        $sliceCText = $null
    }

    return [pscustomobject]@{
        ScenarioRoot = $scenarioRoot
        LocalAppDataRoot = $localAppDataRoot
        DescriptorRoot = $descriptorRoot
        SourceFixturePath = $scenarioSourceFixturePath
        SliceCFixturePath = $scenarioSliceCFixturePath
        SeedPath = $scenarioSeedPath
        SeedFingerprint = Get-FileFingerprint $scenarioSeedPath
        SourceFingerprint = $null
        SliceCFingerprint = $null
    }
}

function Invoke-SyntheticFreshProvision {
    <#
    .SYNOPSIS
        在無 CRM I/O 的條件下驅動 parent fresh-provision child boundary。

    .DESCRIPTION
        每個 scenario 都使用新的 temporary runner、cmd shim、child、evidence 和 ledger payload。fake
        child 不執行 dotnet test；它只模擬 child 完成/失敗後 parent 必須處理的可信度邊界。selector 與
        observation variables 在 finally 還原，因此任何失敗都不會改變後續測試或使用者 shell。
    #>
    param(
        [string] $Scenario,
        [string] $TemporaryRoot,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $SourceFixturePath,
        [string] $SliceCFixturePath,
        [string] $OwnerIdentity,
        [string] $DiagnosticJsonOverride = $null
    )

    # PowerShell 的變數名不分大小寫；不可使用 $scenario 計算 local context，否則會與已經
    # 約束為 [string] 的 $Scenario 參數相同，而把 PSCustomObject 強制轉為字串。該轉換會讓后續
    # descriptor path 與現實 CRM 測試流程在任何 child process 開始前就失去 provenance。
    $scenarioContext = New-SyntheticFreshScenarioContext `
        -TemporaryRoot $TemporaryRoot `
        -SourceFixturePath $SourceFixturePath `
        -SliceCFixturePath $SliceCFixturePath
    $scenarioRoot = [string]$scenarioContext.ScenarioRoot
    $syntheticRunnerPath = Join-Path $scenarioRoot 'Invoke-Package02Data8ListManagementEvidence.synthetic-fresh.ps1'
    $fakeDotnetDirectory = Join-Path $scenarioRoot 'dotnet'
    $fakeDotnetPath = Join-Path $fakeDotnetDirectory 'dotnet.cmd'
    $fakeChildPath = Join-Path $fakeDotnetDirectory 'synthetic-fresh-child.ps1'
    $childObservationPath = Join-Path $scenarioRoot 'child-observation.json'
    $restorationObservationPath = Join-Path $scenarioRoot 'restoration-observation.json'
    [void][IO.Directory]::CreateDirectory($fakeDotnetDirectory)

    $ledger = $null
    $evidenceJsonOverride = $null
    $ledgerJsonOverride = $null
    $exitCode = 0
    $diagnosticCategory = ''
    $injectPublicationFailure = $false
    $omitFreshLedgerRootForChild = $false
    switch ($Scenario) {
        'child-nonzero-valid-evidence' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $diagnosticCategory = 'fixture-precondition-failed'
            $exitCode = 17
            break
        }
        'evidence-extra-property' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $evidence.unexpectedChildField = 'synthetic-only'
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            break
        }
        'evidence-unknown-provision-reason' {
            # no-go 的 publication bit 合法但 reason 不屬於 provision allowlist；parent 不得把 child
            # 自訂字串投影到 console，也不得從 no-go evidence 推論可安全保留任何 descriptor。
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'no-go' 'synthetic-unknown-provision-reason' $true $false
            break
        }
        'evidence-schema-quoted-one' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral '"1"'
            break
        }
        'evidence-schema-decimal-one' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral '1.0'
            break
        }
        'evidence-schema-exponent-one' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral '1e0'
            break
        }
        'evidence-schema-bool-one' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral 'true'
            break
        }
        'evidence-schema-null' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral 'null'
            break
        }
        'evidence-schema-array' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $evidenceJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $evidence -ExpectedSchemaVersion 1 -SchemaVersionLiteral '[]'
            break
        }
        'ledger-extra-property' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity -IncludeUnexpectedProperty
            break
        }
        'ledger-schema-v1' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $ledger.schemaVersion = 1
            break
        }
        'ledger-schema-missing' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            [void]$ledger.Remove('schemaVersion')
            break
        }
        'ledger-schema-quoted-two' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $ledgerJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $ledger -ExpectedSchemaVersion 2 -SchemaVersionLiteral '"2"'
            break
        }
        'ledger-schema-decimal-two' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $ledgerJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $ledger -ExpectedSchemaVersion 2 -SchemaVersionLiteral '2.0'
            break
        }
        'ledger-schema-exponent-two' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $ledgerJsonOverride = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $ledger -ExpectedSchemaVersion 2 -SchemaVersionLiteral '2e0'
            break
        }
        'ledger-missing-original-target-leader' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            [void]$ledger.Remove('originalTargetLeaderContactId')
            break
        }
        'ledger-wrong-original-target-leader' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger `
                -OwnerIdentity $OwnerIdentity `
                -OriginalTargetLeaderContactId 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
            break
        }
        'descriptor-publication-failure' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $injectPublicationFailure = $true
            break
        }
        'provisioning-ambiguous' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'no-go' 'provisioning-ambiguous' $true $false
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity -Stage 'source-contact-created'
            break
        }
        'fresh-graph-unproven' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'no-go' 'fresh-graph-unproven' $true $false
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity -Stage 'baseline-owner-assigned'
            break
        }
        'fresh-graph-proven' {
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            break
        }
        'missing-parent-owned-ledger-root' {
            # child 仍收到 ledger/evidence write 請求，但 synthetic runner 會移除 root binding；只有
            # parent 已建立且傳入的 root 才能承載 child ledger，child 不可自行補建。
            $evidence = New-SyntheticFreshFixtureEvidence 'provision' 'go' 'fresh-fixture-provisioned' $true $true
            $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity
            $omitFreshLedgerRootForChild = $true
            break
        }
        default {
            throw 'Unknown synthetic fresh-fixture scenario.'
        }
    }

    # [string] parameter 對 $null 會 coercion 成 empty string；只有 raw token scenario 才傳入
    # override，否則維持原有 object serializer 路徑，避免 fake child 將正常 ledger 寫成空 payload。
    if ($null -eq $ledgerJsonOverride) {
        Write-SyntheticFreshChild `
            -Path $fakeChildPath `
            -Evidence $evidence `
            -EvidenceJsonOverride $evidenceJsonOverride `
            -Ledger $ledger `
            -DiagnosticCategory $diagnosticCategory `
            -DiagnosticJsonOverride $DiagnosticJsonOverride `
            -ExitCode $exitCode
    }
    else {
        Write-SyntheticFreshChild `
            -Path $fakeChildPath `
            -Evidence $evidence `
            -EvidenceJsonOverride $evidenceJsonOverride `
            -Ledger $ledger `
            -LedgerJsonOverride $ledgerJsonOverride `
            -DiagnosticCategory $diagnosticCategory `
            -DiagnosticJsonOverride $DiagnosticJsonOverride `
            -ExitCode $exitCode
    }
    Write-StrictTextFile -Path $fakeDotnetPath -Text (@(
        '@echo off',
        ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $fakeChildPath + '" %*'),
        'exit /b %ERRORLEVEL%'
    ) -join "`r`n")
    New-SyntheticFreshRunner `
        -DestinationPath $syntheticRunnerPath `
        -RunnerPath $runnerPath `
        -FailFreshDescriptorPublicationAfterSourceWrite:$injectPublicationFailure `
        -OmitFreshLedgerRootForChild:$omitFreshLedgerRootForChild

    $selectorNames = @(
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_DOTNET_PATH',
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_CHILD_OBSERVATION_PATH',
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_RESTORE_OBSERVATION_PATH')
    $selectorSnapshot = Get-ProcessEnvironmentSnapshot $selectorNames
    $localAppDataSnapshot = Get-ProcessEnvironmentSnapshot @('LOCALAPPDATA')
    try {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_DOTNET_PATH', $fakeDotnetPath, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_CHILD_OBSERVATION_PATH', $childObservationPath, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_RESTORE_OBSERVATION_PATH', $restorationObservationPath, 'Process')
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', [string]$scenarioContext.LocalAppDataRoot, 'Process')

        $result = Invoke-RunnerJson `
            -CommandPath $syntheticRunnerPath `
            -RepositoryPath $RepositoryPath `
            -ProfilePath $ProfilePath `
            -SourceFixturePath $scenarioContext.SourceFixturePath `
            -SliceCFixturePath $scenarioContext.SliceCFixturePath `
            -ProvisionFreshFixture `
            -ReplaceStaleDescriptor
        return [pscustomobject]@{
            Result = $result
            ExpectedLedger = $ledger
            ChildObservationPath = $childObservationPath
            RestorationObservationPath = $restorationObservationPath
            ScenarioRoot = $scenarioContext.ScenarioRoot
            LocalAppDataRoot = $scenarioContext.LocalAppDataRoot
            SourceFixturePath = $scenarioContext.SourceFixturePath
            SliceCFixturePath = $scenarioContext.SliceCFixturePath
            SeedPath = $scenarioContext.SeedPath
            ExpectedSeedFingerprint = $scenarioContext.SeedFingerprint
            ExpectedSourceFingerprint = $scenarioContext.SourceFingerprint
            ExpectedSliceCFingerprint = $scenarioContext.SliceCFingerprint
        }
    }
    finally {
        Restore-ProcessEnvironmentSnapshot $selectorSnapshot
        Restore-ProcessEnvironmentSnapshot $localAppDataSnapshot
    }
}

function Invoke-SyntheticFreshCleanup {
    <#
    .SYNOPSIS
        以同一個已發佈 fresh graph 的 synthetic LOCALAPPDATA root 驅動 cleanup child。

    .DESCRIPTION
        此 helper 只接受先前 successful provision 回傳的 scenario，藉此讓 cleanup parent 先讀取
        schema v2、current-user-bound、fresh-graph-proven ledger，再把同一 nonce 交給 fake child。
        fake child 只寫去識別化 cleanup evidence 與 final ledger stage；它不執行 dotnet、Credential
        Manager 或 CRM。特別保留 child observation，以驗證 cleanup 不會從已發佈 descriptor 或 legacy
        Slice C environment 重送 target-leader ID，避免跨 mode 的 mutable-state leakage。
    #>
    param(
        [object] $ProvisionScenario,
        [string] $RepositoryPath,
        [string] $ProfilePath,
        [string] $OwnerIdentity,
        [ValidateSet('success', 'invalid-post-child-ledger', 'altered-post-child-original-baseline', 'unknown-cleanup-reason')]
        [string] $Scenario = 'success'
    )

    $cleanupRoot = Join-Path ([string]$ProvisionScenario.ScenarioRoot) ('synthetic-cleanup-' + [Guid]::NewGuid().ToString('N'))
    $syntheticRunnerPath = Join-Path $cleanupRoot 'Invoke-Package02Data8ListManagementEvidence.synthetic-fresh-cleanup.ps1'
    $fakeDotnetDirectory = Join-Path $cleanupRoot 'dotnet'
    $fakeDotnetPath = Join-Path $fakeDotnetDirectory 'dotnet.cmd'
    $fakeChildPath = Join-Path $fakeDotnetDirectory 'synthetic-fresh-cleanup-child.ps1'
    $childObservationPath = Join-Path $cleanupRoot 'child-observation.json'
    $restorationObservationPath = Join-Path $cleanupRoot 'restoration-observation.json'
    [void][IO.Directory]::CreateDirectory($fakeDotnetDirectory)

    $evidence = New-SyntheticFreshFixtureEvidence 'cleanup' 'go' 'fresh-fixture-cleaned' $true $false
    $ledger = New-SyntheticFreshFixtureLedger $OwnerIdentity -Stage 'cleanup-leader-contact-deleted'
    switch ($Scenario) {
        'invalid-post-child-ledger' {
            # pre-child ledger 由 parent 以 strict v2 驗證；此處只在 child 已完成宣告後覆寫為 v1，
            # 以驗證 post-child re-read 失敗時不得刪除仍可供人工 recovery 的 descriptors 或 ledger。
            $ledger.schemaVersion = 1
            break
        }
        'altered-post-child-original-baseline' {
            # child 仍寫出完整 v2 ledger，但竄改 cleanup 前已 proven 的 immutable baseline；parent
            # 必須把它視為 transaction-boundary failure，保留 descriptors/ledger 供明確復原而非刪除。
            $ledger = New-SyntheticFreshFixtureLedger `
                -OwnerIdentity $OwnerIdentity `
                -Stage 'cleanup-leader-contact-deleted' `
                -OriginalTargetLeaderContactId 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
            break
        }
        'unknown-cleanup-reason' {
            # cleanup 的 no-go reason 亦必須與 provision 分開 allowlist；未知字串不得穿越 parent
            # handoff boundary，即使其 descriptorPublicationReady=false 看似安全也一樣。這個 child
            # 故意不重寫 ledger，讓後續成功 cleanup 可證明拒絕 evidence 沒有破壞既有 recovery state。
            $evidence = New-SyntheticFreshFixtureEvidence 'cleanup' 'no-go' 'synthetic-unknown-cleanup-reason' $true $false
            $ledger = $null
            break
        }
        'success' {
            break
        }
    }
    Write-SyntheticFreshChild -Path $fakeChildPath -Evidence $evidence -Ledger $ledger -ExitCode 0
    Write-StrictTextFile -Path $fakeDotnetPath -Text (@(
        '@echo off',
        ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $fakeChildPath + '" %*'),
        'exit /b %ERRORLEVEL%'
    ) -join "`r`n")
    New-SyntheticFreshRunner -DestinationPath $syntheticRunnerPath -RunnerPath $runnerPath

    $selectorNames = @(
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_DOTNET_PATH',
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_CHILD_OBSERVATION_PATH',
        'SPEECHMESSAGE_P72_SYNTHETIC_FRESH_RESTORE_OBSERVATION_PATH')
    $selectorSnapshot = Get-ProcessEnvironmentSnapshot $selectorNames
    $localAppDataSnapshot = Get-ProcessEnvironmentSnapshot @('LOCALAPPDATA')
    try {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_DOTNET_PATH', $fakeDotnetPath, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_CHILD_OBSERVATION_PATH', $childObservationPath, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P72_SYNTHETIC_FRESH_RESTORE_OBSERVATION_PATH', $restorationObservationPath, 'Process')
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', [string]$ProvisionScenario.LocalAppDataRoot, 'Process')

        $result = Invoke-RunnerJson `
            -CommandPath $syntheticRunnerPath `
            -RepositoryPath $RepositoryPath `
            -ProfilePath $ProfilePath `
            -SourceFixturePath $ProvisionScenario.SourceFixturePath `
            -SliceCFixturePath $ProvisionScenario.SliceCFixturePath `
            -CleanupFreshFixture `
            -ConfirmFreshFixtureCleanup
        return [pscustomobject]@{
            Result = $result
            ExpectedLedger = $ledger
            ChildObservationPath = $childObservationPath
            RestorationObservationPath = $restorationObservationPath
        }
    }
    finally {
        Restore-ProcessEnvironmentSnapshot $selectorSnapshot
        Restore-ProcessEnvironmentSnapshot $localAppDataSnapshot
    }
}

try {
    Assert-StrictTextFile $PSCommandPath
    Assert-StrictTextFile $runnerPath

    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    $repositoryPath = Join-Path $fixtureRoot 'repository'
    $profilePath = Join-Path $fixtureRoot 'official-worker-profile-input.json'
    # runner 的 fresh control plane 只接受 current-user LOCALAPPDATA 下的固定 descriptor paths。
    # suite 使用獨立 temporary root，避免測試讀寫開發者現有 profile 或把 ledger 留給下一個 session。
    $testLocalAppData = Join-Path $fixtureRoot 'synthetic-local-app-data'
    $descriptorRoot = Join-Path $testLocalAppData 'SpeechMessage\Dynamics\P7.2'
    $sourceFixturePath = Join-Path $descriptorRoot 'contact-basic-info-fixture.json'
    $sliceCFixturePath = Join-Path $descriptorRoot 'list-management-fixture.json'
    New-TestRepository -Root $repositoryPath

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Write-StrictJsonFile $profilePath ([ordered]@{
        schemaVersion = 1
        profiles = @(
            [ordered]@{
                profileAlias = 'crm91'
                workerKind = 'OfficialCrm91Worker'
                authentication = 'Ifd'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = 'speechmessage.crm91.p62'
                }
            }
        )
    })
    Write-StrictJsonFile $sourceFixturePath ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-contact-basic-info'
        profileAlias = 'sunnyvalechback'
        ceVersion = '9.1'
        connector = 'Data8'
        marker = 'p7.2-contact-basic-info'
        contactId = 'aaaaaaaa-1111-1111-1111-111111111111'
        ownerIdentity = $identity
    })
    Write-StrictJsonFile $sliceCFixturePath ([ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-list-management'
        profileAlias = 'sunnyvalechback'
        ceVersion = '9.1'
        connector = 'Data8'
        marker = 'p7.2-list-management'
        ownerIdentity = $identity
        addListId = '11111111-1111-1111-1111-111111111111'
        removeListId = '22222222-2222-2222-2222-222222222222'
        smallGroupListId = '33333333-3333-3333-3333-333333333333'
        smallGroupTargetLeaderContactId = '44444444-4444-4444-4444-444444444444'
        smallGroupExpectedRelationshipListId = '55555555-5555-5555-5555-555555555555'
        transferSourceListId = '66666666-6666-6666-6666-666666666666'
        transferTargetListId = '77777777-7777-7777-7777-777777777777'
        transferWeekStartUtc = '2026-08-09T00:00:00.0000000+00:00'
    })
    $freshSeedPath = Join-Path $descriptorRoot 'fresh-slice-c-seed.json'
    Write-StrictFreshSeed -Path $freshSeedPath -OwnerIdentity $identity

    $provisionWithoutConfirmation = Invoke-RunnerJson `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -ProvisionFreshFixture
    Assert-True ($provisionWithoutConfirmation.ExitCode -eq 2) 'Provision without explicit descriptor-publication confirmation must be a no-go.'
    Assert-True (
        $provisionWithoutConfirmation.Evidence.outcome -eq 'no-go' -and
        $provisionWithoutConfirmation.Evidence.reason -eq 'fresh-fixture-confirmation-required' -and
        -not $provisionWithoutConfirmation.Evidence.operationExecuted -and
        -not $provisionWithoutConfirmation.Evidence.featureFlagChanged) 'Provision confirmation refusal must happen before credential, child process, ledger publication or CRM mutation.'

    $cleanupWithoutConfirmation = Invoke-RunnerJson `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -CleanupFreshFixture
    Assert-True ($cleanupWithoutConfirmation.ExitCode -eq 2) 'Cleanup without explicit confirmation must be a no-go.'
    Assert-True (
        $cleanupWithoutConfirmation.Evidence.outcome -eq 'no-go' -and
        $cleanupWithoutConfirmation.Evidence.reason -eq 'fresh-fixture-cleanup-confirmation-required' -and
        -not $cleanupWithoutConfirmation.Evidence.operationExecuted -and
        -not $cleanupWithoutConfirmation.Evidence.featureFlagChanged) 'Cleanup confirmation refusal must happen before credential, child process, ledger deletion or CRM mutation.'

    $mutuallyExclusiveModes = Invoke-RunnerBinderFailure `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath
    Assert-True ($mutuallyExclusiveModes.ExitCode -ne 0) 'Provision and cleanup must be mutually exclusive parameter sets.'
    Assert-True ($mutuallyExclusiveModes.JsonLineCount -eq 0) 'Mutually exclusive fresh-fixture modes must be rejected before script-body JSON output.'

    # fresh provision/cleanup 也不得與既有 live mutation lane 混合。此處刻意包含兩個 confirmation
    # switch，證明 binder 的拒絕不依賴「少傳確認」這種較弱條件，而是在進入 script body 前封鎖兩條
    # 遠端 mutation family 的交錯組合。
    $freshProvisionExecuteConflict = Invoke-RunnerBinderFailure `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -ModeArguments @('-ProvisionFreshFixture', '-ReplaceStaleDescriptor', '-ExecuteFixture')
    Assert-True ($freshProvisionExecuteConflict.ExitCode -ne 0 -and $freshProvisionExecuteConflict.JsonLineCount -eq 0) 'Fresh provision and ExecuteFixture must be mutually exclusive before the script body starts.'

    $freshCleanupReconcileConflict = Invoke-RunnerBinderFailure `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -ModeArguments @('-CleanupFreshFixture', '-ConfirmFreshFixtureCleanup', '-ReconcileFixture')
    Assert-True ($freshCleanupReconcileConflict.ExitCode -ne 0 -and $freshCleanupReconcileConflict.JsonLineCount -eq 0) 'Fresh cleanup and ReconcileFixture must be mutually exclusive before the script body starts.'

    # confirmation switches 不得自行形成隱藏 mutation mode；這些兩條拒絕都必須發生在 credential、
    # temporary directory、ledger 或 fake child 之前，並維持單一 sanitized no-go JSON。
    $misusedProvisionConfirmation = Invoke-RunnerJson `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -ReplaceStaleDescriptor
    Assert-True ($misusedProvisionConfirmation.ExitCode -eq 2 -and $misusedProvisionConfirmation.Evidence.outcome -eq 'no-go' -and $misusedProvisionConfirmation.Evidence.reason -eq 'fresh-fixture-confirmation-misused') 'ReplaceStaleDescriptor without fresh provision must fail closed before any child work.'

    $misusedCleanupConfirmation = Invoke-RunnerJson `
        -RepositoryPath $repositoryPath `
        -ProfilePath $profilePath `
        -SourceFixturePath $sourceFixturePath `
        -SliceCFixturePath $sliceCFixturePath `
        -ConfirmFreshFixtureCleanup
    Assert-True ($misusedCleanupConfirmation.ExitCode -eq 2 -and $misusedCleanupConfirmation.Evidence.outcome -eq 'no-go' -and $misusedCleanupConfirmation.Evidence.reason -eq 'fresh-fixture-confirmation-misused') 'ConfirmFreshFixtureCleanup without fresh cleanup must fail closed before any child work.'

    # 下列 process sentinel 逐一對應 runner 的受控 environment input。它們都是固定測試字串，不是
    # credential；runner child 完成後 temporary copy 的 finally observer 會讀回這些值，以證明
    # parent 沒有把 password、mode、ledger/evidence path 或 descriptor scalar 留在下一次 invocation。
    $freshEnvironmentNames = @(
        'CRM_PASSWORD',
        'SPEECHMESSAGE_P7_2_SLICE_C_LIVE',
        'SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE',
        'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR',
        'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE',
        'P7_2_SLICE_C_FIXTURE_OWNER',
        'P7_2_SLICE_C_FIXTURE_MARKER',
        'P7_2_SLICE_C_CONTACT_ID',
        'P7_2_SLICE_C_ADD_LIST_ID',
        'P7_2_SLICE_C_REMOVE_LIST_ID',
        'P7_2_SLICE_C_SMALL_GROUP_LIST_ID',
        'P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID',
        'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
        'P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID',
        'P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID',
        'P7_2_SLICE_C_TRANSFER_WEEK_START_UTC',
        'P7_2_SLICE_C_EVIDENCE_PATH',
        'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH',
        'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH',
        'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH',
        'P7_2_SLICE_C_EVIDENCE_JSON',
        'P7_2_SLICE_C_RETIRED_TRX_EVIDENCE',
        'P7_2_SLICE_C_TARGET_OWNER_ID',
        # 未宣告 legacy key 代表升級後仍可能留在長壽命 shell 的 state；它必須被 fresh parent
        # snapshot、對 child 清空並在 finally 還原，不能因不在靜態清單而跨 session 流入 child。
        'P7_2_SLICE_C_UNDECLARED_LEGACY_SENTINEL',
        # 此 key 刻意使用 FRESH_ 前綴卻不是任何已知 control-plane binding。它驗證 parent 的
        # allowlist 必須精確且大小寫不敏感，而非以 prefix 誤放行未知跨 session mutable state。
        'P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL',
        # 第二個歷史 namespace 同樣不能因為名稱含 SPEECHMESSAGE_ 而繞過 fresh child 的精確
        # control-plane allowlist；此 sentinel 保護 parent snapshot、scrub 與 finally restore 三段。
        'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL',
        'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION',
        'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP',
        'P7_2_SLICE_C_FRESH_LEDGER_ROOT',
        'P7_2_SLICE_C_FRESH_LEDGER_PATH',
        'P7_2_SLICE_C_FRESH_EVIDENCE_PATH',
        'P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH',
        'P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION',
        'P7_2_SLICE_C_FRESH_NONCE',
        'P7_2_SLICE_C_FRESH_OWNER',
        'P7_2_SLICE_C_FRESH_ADD_LIST_ID',
        'P7_2_SLICE_C_FRESH_REMOVE_LIST_ID',
        'P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID',
        'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
        'P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID',
        'P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID',
        'P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC'
    )
    $environmentSnapshot = Get-ProcessEnvironmentSnapshot $freshEnvironmentNames
    $localAppDataSnapshot = Get-ProcessEnvironmentSnapshot @('LOCALAPPDATA')
    try {
        $sentinelValues = [ordered]@{}
        $sentinelIndex = 0
        foreach ($environmentName in $freshEnvironmentNames) {
            $sentinelValues[$environmentName] = 'fresh-fixture-contract-sentinel-' + $sentinelIndex
            [Environment]::SetEnvironmentVariable($environmentName, $sentinelValues[$environmentName], 'Process')
            $sentinelIndex++
        }

        # 目前 process 的 LOCALAPPDATA 只在這段 bounded invocation 內指向 suite-owned root；
        # helper 會為每個 child scenario 再建立更窄的子 root，finally 逐層還原原始值。
        [void][IO.Directory]::CreateDirectory($testLocalAppData)
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', $testLocalAppData, 'Process')

        # confirmed cleanup 在沒有 strict current-user ledger 時不得讀取 credential 或啟動 child。這是
        # cleanup parameter-set 的正向 gate：它與「未確認」拒絕不同，證明確認本身不會跳過 ledger
        # provenance。
        $cleanupWithoutLedger = Invoke-RunnerJson `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -CleanupFreshFixture `
            -ConfirmFreshFixtureCleanup
        Assert-True (
            $cleanupWithoutLedger.ExitCode -eq 2 -and
            $cleanupWithoutLedger.Evidence.outcome -eq 'no-go' -and
            $cleanupWithoutLedger.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
            -not $cleanupWithoutLedger.Evidence.operationExecuted -and
            -not $cleanupWithoutLedger.Evidence.safeToRetry
        ) 'Confirmed fresh cleanup without a strict ledger must fail before credential access or a child process.'

        # 非零 child exit 是 process lifecycle failure，不是「可接受 child evidence」的替代訊號。
        # fake child 故意留下完整、看似 go 的 evidence/ledger 並以 17 結束；parent 必須先信任
        # ExitCode，再拒絕所有檔案內容，保留 no-go、safeToRetry=false 並不發佈 descriptor。
        $nonzeroChild = Invoke-SyntheticFreshProvision `
            -Scenario 'child-nonzero-valid-evidence' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (Test-Path -LiteralPath $nonzeroChild.ChildObservationPath -PathType Leaf) 'Synthetic child must expose its test-only boundary observation before parent evidence is asserted.'
        $nonzeroChildDiagnosticObservation = [IO.File]::ReadAllText($nonzeroChild.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        $diagnosticCategoryProperty = @($nonzeroChild.Result.Evidence.PSObject.Properties | Where-Object { $_.Name -ceq 'diagnosticCategory' })
        Assert-True (
            $nonzeroChild.Result.ExitCode -eq 2 -and
            $nonzeroChild.Result.Evidence.outcome -eq 'no-go' -and
            $nonzeroChild.Result.Evidence.reason -eq 'child-process-failed' -and
            $diagnosticCategoryProperty.Count -eq 1 -and
            [string]$diagnosticCategoryProperty[0].Value -eq 'fixture-precondition-failed' -and
            -not [string]::IsNullOrWhiteSpace([string]$nonzeroChildDiagnosticObservation.diagnosticPath) -and
            [bool]$nonzeroChildDiagnosticObservation.diagnosticFileExists -and
            $nonzeroChild.Result.Evidence.operationExecuted -and
            -not $nonzeroChild.Result.Evidence.safeToRetry
        ) ('A non-zero fresh child exit must retain only its allowlisted diagnostic category; diagnostic path was bound=' +
            (-not [string]::IsNullOrWhiteSpace([string]$nonzeroChildDiagnosticObservation.diagnosticPath)) +
            ', file existed in child=' + [bool]$nonzeroChildDiagnosticObservation.diagnosticFileExists)
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $nonzeroChild.SourceFixturePath `
            -SliceCFixturePath $nonzeroChild.SliceCFixturePath `
            -ExpectedSourceFingerprint $nonzeroChild.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $nonzeroChild.ExpectedSliceCFingerprint
        Assert-True (Test-Path -LiteralPath $nonzeroChild.ChildObservationPath -PathType Leaf) 'Synthetic child must expose only test-owned cleanup observations.'
        $nonzeroChildObservation = [IO.File]::ReadAllText($nonzeroChild.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            -not [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.provisionMode) -and
            [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.cleanupMode) -and
            $nonzeroChildObservation.descriptorConfirmation -eq 'replace-stale-descriptor' -and
            -not [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.ledgerRoot) -and
            (Test-Path -LiteralPath ([string]$nonzeroChildObservation.ledgerRoot) -PathType Container) -and
            [string]::Equals(
                [IO.Path]::GetDirectoryName([string]$nonzeroChildObservation.ledgerPath),
                [string]$nonzeroChildObservation.ledgerRoot,
                [StringComparison]::OrdinalIgnoreCase)
        ) 'Provision child must receive only the fixed provision mode and descriptor confirmation variables.'
        foreach ($legacyProperty in @($nonzeroChildObservation.legacyEnvironment.PSObject.Properties)) {
            Assert-True (
                [string]::IsNullOrWhiteSpace([string]$legacyProperty.Value)
            ) 'Fresh provision child must not inherit legacy Slice C fixture, contact, mode or evidence environment variables.'
        }
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.undeclaredLegacySentinel)
        ) 'Fresh provision child must not inherit an undeclared P7_2_SLICE_C legacy sentinel.'
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.undeclaredFreshLegacySentinel)
        ) 'Fresh provision child must not inherit an undeclared FRESH-prefixed P7_2_SLICE_C legacy sentinel.'
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$nonzeroChildObservation.undeclaredSpeechmessageFreshLegacySentinel)
        ) 'Fresh provision child must not inherit an undeclared FRESH-prefixed SPEECHMESSAGE_P7_2_SLICE_C legacy sentinel.'
        Assert-True (
            -not (Test-Path -LiteralPath ([string]$nonzeroChildObservation.evidenceDirectory) -PathType Container)
        ) 'Parent must remove its fresh child temporary evidence directory after a non-zero child exit.'
        Assert-True (Test-Path -LiteralPath $nonzeroChild.RestorationObservationPath -PathType Leaf) 'Synthetic runner must observe the post-finally environment state.'
        $restoredEnvironment = [IO.File]::ReadAllText($nonzeroChild.RestorationObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        foreach ($environmentName in $freshEnvironmentNames) {
            $restoredProperty = @($restoredEnvironment.PSObject.Properties | Where-Object { $_.Name -ceq $environmentName })
            Assert-True (
                $restoredProperty.Count -eq 1 -and
                [string]$restoredProperty[0].Value -ceq [string]$sentinelValues[$environmentName]
            ) 'Fresh parent finally must restore every process environment variable after child failure.'
        }

        # Diagnostic file 僅能在非零 child exit 後提供固定 category。以下 fault injection 保留
        # child-process-failed 的既有 no-go，不讓 quoted/decimal/exponent/bool/null schema、額外欄位
        # 或未知 category 變成可觀測或可重試的 child evidence。
        $validDiagnostic = New-SyntheticFreshFixtureDiagnostic -Category 'fixture-precondition-failed'
        $extraFieldDiagnostic = New-SyntheticFreshFixtureDiagnostic `
            -Category 'fixture-precondition-failed' `
            -IncludeUnexpectedProperty
        $invalidDiagnosticScenarios = @(
            [pscustomobject]@{
                Name = 'diagnostic-schema-quoted-one'
                Json = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $validDiagnostic -ExpectedSchemaVersion 1 -SchemaVersionLiteral '"1"'
            },
            [pscustomobject]@{
                Name = 'diagnostic-schema-decimal-one'
                Json = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $validDiagnostic -ExpectedSchemaVersion 1 -SchemaVersionLiteral '1.0'
            },
            [pscustomobject]@{
                Name = 'diagnostic-schema-exponent-one'
                Json = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $validDiagnostic -ExpectedSchemaVersion 1 -SchemaVersionLiteral '1e0'
            },
            [pscustomobject]@{
                Name = 'diagnostic-schema-bool-one'
                Json = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $validDiagnostic -ExpectedSchemaVersion 1 -SchemaVersionLiteral 'true'
            },
            [pscustomobject]@{
                Name = 'diagnostic-schema-null'
                Json = New-SyntheticFreshFixtureJsonWithSchemaVersionLiteral -Value $validDiagnostic -ExpectedSchemaVersion 1 -SchemaVersionLiteral 'null'
            },
            [pscustomobject]@{
                Name = 'diagnostic-extra-property'
                Json = $extraFieldDiagnostic | ConvertTo-Json -Compress -Depth 12
            },
            [pscustomobject]@{
                Name = 'diagnostic-unknown-category'
                Json = New-SyntheticFreshFixtureDiagnostic -Category 'synthetic-unknown-diagnostic-category' | ConvertTo-Json -Compress -Depth 12
            },
            [pscustomobject]@{
                Name = 'diagnostic-duplicate-schema-version'
                Json = '{"schemaVersion":1,"schemaVersion":1,"category":"fixture-precondition-failed"}'
            },
            [pscustomobject]@{
                Name = 'diagnostic-duplicate-escaped-schema-version'
                Json = '{"schemaVersion":1,"\u0073chemaVersion":1,"category":"fixture-precondition-failed"}'
            },
            [pscustomobject]@{
                Name = 'diagnostic-duplicate-category'
                Json = '{"schemaVersion":1,"category":"fixture-precondition-failed","category":"fixture-precondition-failed"}'
            }
        )
        foreach ($invalidDiagnosticScenario in $invalidDiagnosticScenarios) {
            $invalidDiagnostic = Invoke-SyntheticFreshProvision `
                -Scenario 'child-nonzero-valid-evidence' `
                -TemporaryRoot $fixtureRoot `
                -RepositoryPath $repositoryPath `
                -ProfilePath $profilePath `
                -SourceFixturePath $sourceFixturePath `
                -SliceCFixturePath $sliceCFixturePath `
                -OwnerIdentity $identity `
                -DiagnosticJsonOverride ([string]$invalidDiagnosticScenario.Json)
            $invalidDiagnosticCategoryProperty = @(
                $invalidDiagnostic.Result.Evidence.PSObject.Properties |
                    Where-Object { $_.Name -ceq 'diagnosticCategory' }
            )
            Assert-True (
                $invalidDiagnostic.Result.ExitCode -eq 2 -and
                $invalidDiagnostic.Result.Evidence.outcome -eq 'no-go' -and
                $invalidDiagnostic.Result.Evidence.reason -eq 'child-process-failed' -and
                $invalidDiagnosticCategoryProperty.Count -eq 0 -and
                $invalidDiagnostic.Result.Evidence.operationExecuted -and
                -not $invalidDiagnostic.Result.Evidence.safeToRetry
            ) ('Invalid child diagnostic must remain omitted from the child-process-failed no-go: ' +
                [string]$invalidDiagnosticScenario.Name)
            Assert-DescriptorsRemainUnpublished `
                -SourceFixturePath $invalidDiagnostic.SourceFixturePath `
                -SliceCFixturePath $invalidDiagnostic.SliceCFixturePath `
                -ExpectedSourceFingerprint $invalidDiagnostic.ExpectedSourceFingerprint `
                -ExpectedSliceCFingerprint $invalidDiagnostic.ExpectedSliceCFingerprint
            $invalidDiagnosticObservation = [IO.File]::ReadAllText(
                $invalidDiagnostic.ChildObservationPath,
                [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
            Assert-True ([bool]$invalidDiagnosticObservation.diagnosticFileExists) (
                'Diagnostic parser regression must prove the malformed file reached the parent boundary: ' +
                [string]$invalidDiagnosticScenario.Name)
        }

        # strict evidence schema 必須拒絕額外欄位；child exit code 為零也不能讓看似 successful 的
        # provision evidence 把未經 allowlist 的資料跨 process boundary，或讓 parent 發佈 descriptor。
        # child 僅可在 parent 已建立且明確交付的 ledger root 寫入。此 fault 刻意清除 child 的 root
        # binding；預期是 child-process-failed，而不是 child 自行建立目錄、寫入 ledger 或發佈 descriptor。
        $missingParentOwnedLedgerRoot = Invoke-SyntheticFreshProvision `
            -Scenario 'missing-parent-owned-ledger-root' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $missingParentOwnedLedgerRoot.Result.ExitCode -eq 2 -and
            $missingParentOwnedLedgerRoot.Result.Evidence.outcome -eq 'no-go' -and
            $missingParentOwnedLedgerRoot.Result.Evidence.reason -eq 'child-process-failed' -and
            $missingParentOwnedLedgerRoot.Result.Evidence.operationExecuted -and
            -not $missingParentOwnedLedgerRoot.Result.Evidence.safeToRetry
        ) 'Fresh provision must require a parent-owned ledger root before child or ledger publication.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $missingParentOwnedLedgerRoot.SourceFixturePath `
            -SliceCFixturePath $missingParentOwnedLedgerRoot.SliceCFixturePath `
            -ExpectedSourceFingerprint $missingParentOwnedLedgerRoot.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $missingParentOwnedLedgerRoot.ExpectedSliceCFingerprint
        $missingParentOwnedLedgerPath = Join-Path `
            ([string]$missingParentOwnedLedgerRoot.LocalAppDataRoot) `
            'SpeechMessage\Dynamics\P7.2\FreshSliceC\fresh-slice-c-ledger.json'
        Assert-True (
            -not (Test-Path -LiteralPath $missingParentOwnedLedgerPath -PathType Leaf)
        ) 'A missing parent-owned ledger root must leave no child-created ledger publication.'

        $extraEvidence = Invoke-SyntheticFreshProvision `
            -Scenario 'evidence-extra-property' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $extraEvidence.Result.ExitCode -eq 2 -and
            $extraEvidence.Result.Evidence.outcome -eq 'no-go' -and
            $extraEvidence.Result.Evidence.reason -eq 'fresh-fixture-evidence-unavailable' -and
            $extraEvidence.Result.Evidence.operationExecuted -and
            -not $extraEvidence.Result.Evidence.safeToRetry
        ) 'Fresh evidence with an unexpected field must be rejected as a non-retryable no-go.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $extraEvidence.SourceFixturePath `
            -SliceCFixturePath $extraEvidence.SliceCFixturePath `
            -ExpectedSourceFingerprint $extraEvidence.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $extraEvidence.ExpectedSliceCFingerprint

        # strict ledger schema 使用同一個 no-go publication boundary。evidence 完全合法且 child 成功
        # 結束仍不足夠：ledger 多出一個欄位時，parent 不能猜測 ID、不能補欄位，也不能覆寫 descriptor。
        # no-go evidence 仍可能包含 child 任意字串；parent 必須依 lane allowlist 投影，否則未知 reason
        # 會穿越 process boundary。拒絕時不得發佈 provision descriptor，即使 child 宣告它已執行操作。
        $unknownProvisionReason = Invoke-SyntheticFreshProvision `
            -Scenario 'evidence-unknown-provision-reason' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $unknownProvisionReason.Result.ExitCode -eq 2 -and
            $unknownProvisionReason.Result.Evidence.outcome -eq 'no-go' -and
            $unknownProvisionReason.Result.Evidence.reason -eq 'fresh-fixture-evidence-unavailable' -and
            $unknownProvisionReason.Result.Evidence.operationExecuted -and
            -not $unknownProvisionReason.Result.Evidence.safeToRetry
        ) 'A child-provided provision reason outside its allowlist must become a sanitized no-go.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $unknownProvisionReason.SourceFixturePath `
            -SliceCFixturePath $unknownProvisionReason.SliceCFixturePath `
            -ExpectedSourceFingerprint $unknownProvisionReason.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $unknownProvisionReason.ExpectedSliceCFingerprint

        $extraLedger = Invoke-SyntheticFreshProvision `
            -Scenario 'ledger-extra-property' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $extraLedger.Result.ExitCode -eq 2 -and
            $extraLedger.Result.Evidence.outcome -eq 'no-go' -and
            $extraLedger.Result.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
            $extraLedger.Result.Evidence.operationExecuted -and
            -not $extraLedger.Result.Evidence.safeToRetry
        ) 'Fresh ledger with an unexpected field must be rejected before descriptor publication.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $extraLedger.SourceFixturePath `
            -SliceCFixturePath $extraLedger.SliceCFixturePath `
            -ExpectedSourceFingerprint $extraLedger.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $extraLedger.ExpectedSliceCFingerprint

        # ambiguous 與 incomplete 兩種 provision outcome 都必須保留 child 所報的非重試分類，且不允許
        # parent 讀取/發佈 partial ledger。保留 ledger 是後續 read-only reconciliation 的唯一資料，不是
        # 重送、刪除或覆寫 stale descriptor 的權限。
        # schema v2 的 original target leader 是 provision 前唯一可接受的 baseline binding。缺欄位時
        # parent 不可以已發佈的新 leader、child 環境或任何 descriptor fallback 補值，否則 cleanup
        # 可能把另一個 session 的 target 當成原始 baseline。
        # schemaVersion 是 parent/child ledger contract 的型別邊界，不是可被 PowerShell coercion 接受的
        # 值比較。v1、缺失、quoted、decimal 與 exponent 都不可取得 descriptor 發佈權限。
        foreach ($ledgerSchemaScenario in @(
                'ledger-schema-v1',
                'ledger-schema-missing',
                'ledger-schema-quoted-two',
                'ledger-schema-decimal-two',
                'ledger-schema-exponent-two')) {
            $invalidLedgerSchema = Invoke-SyntheticFreshProvision `
                -Scenario $ledgerSchemaScenario `
                -TemporaryRoot $fixtureRoot `
                -RepositoryPath $repositoryPath `
                -ProfilePath $profilePath `
                -SourceFixturePath $sourceFixturePath `
                -SliceCFixturePath $sliceCFixturePath `
                -OwnerIdentity $identity
            Assert-True (
                $invalidLedgerSchema.Result.ExitCode -eq 2 -and
                $invalidLedgerSchema.Result.Evidence.outcome -eq 'no-go' -and
                $invalidLedgerSchema.Result.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
                $invalidLedgerSchema.Result.Evidence.operationExecuted -and
                -not $invalidLedgerSchema.Result.Evidence.safeToRetry
            ) ('Fresh ledger schema scenario must fail closed: ' + $ledgerSchemaScenario)
            Assert-DescriptorsRemainUnpublished `
                -SourceFixturePath $invalidLedgerSchema.SourceFixturePath `
                -SliceCFixturePath $invalidLedgerSchema.SliceCFixturePath `
                -ExpectedSourceFingerprint $invalidLedgerSchema.ExpectedSourceFingerprint `
                -ExpectedSliceCFingerprint $invalidLedgerSchema.ExpectedSliceCFingerprint
        }

        # evidence schemaVersion 是 descriptor publication 前的獨立 trust boundary。JSON 的字面值
        # 必須恰為 integral numeric 1；相等比較不可接受 quoted、decimal、exponent、bool、null
        # 或集合 shape，否則 child 能以非預期 wire schema 驅動本機 descriptor 發布。
        foreach ($evidenceSchemaScenario in @(
                'evidence-schema-quoted-one',
                'evidence-schema-decimal-one',
                'evidence-schema-exponent-one',
                'evidence-schema-bool-one',
                'evidence-schema-null',
                'evidence-schema-array')) {
            $invalidEvidenceSchema = Invoke-SyntheticFreshProvision `
                -Scenario $evidenceSchemaScenario `
                -TemporaryRoot $fixtureRoot `
                -RepositoryPath $repositoryPath `
                -ProfilePath $profilePath `
                -SourceFixturePath $sourceFixturePath `
                -SliceCFixturePath $sliceCFixturePath `
                -OwnerIdentity $identity
            Assert-True (
                $invalidEvidenceSchema.Result.ExitCode -eq 2 -and
                $invalidEvidenceSchema.Result.Evidence.outcome -eq 'no-go' -and
                $invalidEvidenceSchema.Result.Evidence.reason -eq 'fresh-fixture-evidence-unavailable' -and
                $invalidEvidenceSchema.Result.Evidence.operationExecuted -and
                -not $invalidEvidenceSchema.Result.Evidence.safeToRetry
            ) ('Fresh evidence schema scenario must fail closed before descriptor publication: ' + $evidenceSchemaScenario)
            Assert-DescriptorsRemainUnpublished `
                -SourceFixturePath $invalidEvidenceSchema.SourceFixturePath `
                -SliceCFixturePath $invalidEvidenceSchema.SliceCFixturePath `
                -ExpectedSourceFingerprint $invalidEvidenceSchema.ExpectedSourceFingerprint `
                -ExpectedSliceCFingerprint $invalidEvidenceSchema.ExpectedSliceCFingerprint
        }

        $missingOriginalTargetLeader = Invoke-SyntheticFreshProvision `
            -Scenario 'ledger-missing-original-target-leader' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $missingOriginalTargetLeader.Result.ExitCode -eq 2 -and
            $missingOriginalTargetLeader.Result.Evidence.outcome -eq 'no-go' -and
            $missingOriginalTargetLeader.Result.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
            $missingOriginalTargetLeader.Result.Evidence.operationExecuted -and
            -not $missingOriginalTargetLeader.Result.Evidence.safeToRetry
        ) 'A v2 ledger without the original target leader must fail closed before descriptor publication.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $missingOriginalTargetLeader.SourceFixturePath `
            -SliceCFixturePath $missingOriginalTargetLeader.SliceCFixturePath `
            -ExpectedSourceFingerprint $missingOriginalTargetLeader.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $missingOriginalTargetLeader.ExpectedSliceCFingerprint

        # 即使欄位存在，original target leader 也必須等於 provision 前 descriptor 的固定值。這個
        # forged GUID 不可觸發「接受 child 指定 baseline」或 publish；它只可得到去識別化 no-go。
        $wrongOriginalTargetLeader = Invoke-SyntheticFreshProvision `
            -Scenario 'ledger-wrong-original-target-leader' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $wrongOriginalTargetLeader.Result.ExitCode -eq 2 -and
            $wrongOriginalTargetLeader.Result.Evidence.outcome -eq 'no-go' -and
            $wrongOriginalTargetLeader.Result.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
            $wrongOriginalTargetLeader.Result.Evidence.operationExecuted -and
            -not $wrongOriginalTargetLeader.Result.Evidence.safeToRetry
        ) 'A v2 ledger whose original target leader differs from the pre-provision descriptor must fail closed.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $wrongOriginalTargetLeader.SourceFixturePath `
            -SliceCFixturePath $wrongOriginalTargetLeader.SliceCFixturePath `
            -ExpectedSourceFingerprint $wrongOriginalTargetLeader.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $wrongOriginalTargetLeader.ExpectedSliceCFingerprint

        # 在第一個 descriptor write 後讓第二個 write 固定失敗。最危險的錯誤是將兩個 stale
        # descriptor bytes 回寫為「可用」；那會讓正常 execution/cleanup lane 錯把舊 graph 當成
        # fresh graph。唯一可恢復狀態必須是 strict pending ledger，並由另一條 explicit lane 處理。
        $partialPublicationFailure = Invoke-SyntheticFreshProvision `
            -Scenario 'descriptor-publication-failure' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $partialPublicationFailure.Result.ExitCode -eq 2 -and
            $partialPublicationFailure.Result.Evidence.outcome -eq 'no-go' -and
            $partialPublicationFailure.Result.Evidence.reason -eq 'fresh-descriptor-publication-failed' -and
            $partialPublicationFailure.Result.Evidence.operationExecuted -and
            -not $partialPublicationFailure.Result.Evidence.safeToRetry
        ) ('A partial fresh descriptor publication failure must be a non-retryable sanitized no-go; actual=' +
            [string]$partialPublicationFailure.Result.ExitCode + '/' +
            [string]$partialPublicationFailure.Result.Evidence.outcome + '/' +
            [string]$partialPublicationFailure.Result.Evidence.reason)
        Assert-DescriptorsQuarantinedAfterPublicationFailure `
            -SourceFixturePath $partialPublicationFailure.SourceFixturePath `
            -SliceCFixturePath $partialPublicationFailure.SliceCFixturePath
        $partialPublicationObservation = [IO.File]::ReadAllText($partialPublicationFailure.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-StrictPendingFreshFixtureLedger `
            -Path ([string]$partialPublicationObservation.ledgerPath) `
            -OwnerIdentity $identity `
            -ExpectedOriginalTargetLeaderContactId ([string]$partialPublicationFailure.ExpectedLedger.originalTargetLeaderContactId)

        $ambiguousProvision = Invoke-SyntheticFreshProvision `
            -Scenario 'provisioning-ambiguous' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $ambiguousProvision.Result.ExitCode -eq 2 -and
            $ambiguousProvision.Result.Evidence.outcome -eq 'no-go' -and
            $ambiguousProvision.Result.Evidence.reason -eq 'provisioning-ambiguous' -and
            $ambiguousProvision.Result.Evidence.operationExecuted -and
            -not $ambiguousProvision.Result.Evidence.safeToRetry
        ) 'Ambiguous provision must remain a non-retryable no-go even when a pending ledger exists.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $ambiguousProvision.SourceFixturePath `
            -SliceCFixturePath $ambiguousProvision.SliceCFixturePath `
            -ExpectedSourceFingerprint $ambiguousProvision.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $ambiguousProvision.ExpectedSliceCFingerprint
        $ambiguousObservation = [IO.File]::ReadAllText($ambiguousProvision.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (Test-Path -LiteralPath ([string]$ambiguousObservation.ledgerPath) -PathType Leaf) 'Ambiguous provision must retain its local pending ledger for later exact-ID reconciliation.'

        $incompleteProvision = Invoke-SyntheticFreshProvision `
            -Scenario 'fresh-graph-unproven' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $incompleteProvision.Result.ExitCode -eq 2 -and
            $incompleteProvision.Result.Evidence.outcome -eq 'no-go' -and
            $incompleteProvision.Result.Evidence.reason -eq 'fresh-graph-unproven' -and
            $incompleteProvision.Result.Evidence.operationExecuted -and
            -not $incompleteProvision.Result.Evidence.safeToRetry
        ) 'Incomplete final graph proof must not become an implicit descriptor publication approval.'
        Assert-DescriptorsRemainUnpublished `
            -SourceFixturePath $incompleteProvision.SourceFixturePath `
            -SliceCFixturePath $incompleteProvision.SliceCFixturePath `
            -ExpectedSourceFingerprint $incompleteProvision.ExpectedSourceFingerprint `
            -ExpectedSliceCFingerprint $incompleteProvision.ExpectedSliceCFingerprint

        # 正向 control：只有 child exit=0、exact evidence、fresh-graph-proven ledger 與 parent cleanup
        # 全部成立時才允許發佈三個固定 descriptor scalar。此測試同時防止修正拒絕路徑時意外讓正常
        # provision 永久不可用，並驗證 atomic writer 維持 repository-required text encoding。
        $provenProvision = Invoke-SyntheticFreshProvision `
            -Scenario 'fresh-graph-proven' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $provenProvision.Result.ExitCode -eq 0 -and
            $provenProvision.Result.Evidence.outcome -eq 'go' -and
            $provenProvision.Result.Evidence.reason -eq 'fresh-fixture-provisioned' -and
            $provenProvision.Result.Evidence.operationExecuted -and
            -not $provenProvision.Result.Evidence.featureFlagChanged -and
            -not $provenProvision.Result.Evidence.safeToRetry
        ) 'Only complete fresh provision evidence and a strict final ledger may publish the local descriptor scalars.'
        $publishedSourceFixture = [IO.File]::ReadAllText($provenProvision.SourceFixturePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        $publishedSliceCFixture = [IO.File]::ReadAllText($provenProvision.SliceCFixturePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            $publishedSourceFixture.contactId -eq $provenProvision.ExpectedLedger.sourceContactId -and
            $publishedSliceCFixture.smallGroupTargetLeaderContactId -eq $provenProvision.ExpectedLedger.leaderContactId -and
            $publishedSliceCFixture.smallGroupExpectedRelationshipListId -eq $provenProvision.ExpectedLedger.relationshipListId -and
            $provenProvision.ExpectedLedger.schemaVersion -eq 2 -and
            $provenProvision.ExpectedLedger.originalTargetLeaderContactId -eq '44444444-4444-4444-4444-444444444444' -and
            $provenProvision.ExpectedLedger.originalTargetLeaderContactId -ne $provenProvision.ExpectedLedger.leaderContactId
        ) 'Successful provision must publish only the three descriptor scalars proven by a v2 ledger that preserves the pre-publication target leader.'
        Assert-True (
            (Get-FileFingerprint $provenProvision.SeedPath) -ceq $provenProvision.ExpectedSeedFingerprint
        ) 'Successful provision must leave the static seed byte-identical.'
        Assert-StrictTextFile $provenProvision.SourceFixturePath
        Assert-StrictTextFile $provenProvision.SliceCFixturePath
        $provenObservation = [IO.File]::ReadAllText($provenProvision.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            -not (Test-Path -LiteralPath ([string]$provenObservation.evidenceDirectory) -PathType Container)
        ) 'Parent must remove its fresh child temporary evidence directory after successful descriptor publication.'

        # cleanup 只從 successful provision 的 v2 ledger 取得 original target leader；它絕不可把已發佈
        # fresh leader 或 legacy Slice C target 放回 child environment。這同時防止 cross-mode state
        # leakage，並保護 cleanup request 不會誤用另一個 descriptor/session 的 baseline。
        $provenLedgerPath = Join-Path ([string]$provenProvision.LocalAppDataRoot) 'SpeechMessage\Dynamics\P7.2\FreshSliceC\fresh-slice-c-ledger.json'
        Assert-True (Test-Path -LiteralPath $provenLedgerPath -PathType Leaf) 'Successful provision must retain its v2 ledger until explicit cleanup completes.'
        $publishedSourceFingerprint = Get-FileFingerprint $provenProvision.SourceFixturePath
        $publishedSliceCFingerprint = Get-FileFingerprint $provenProvision.SliceCFixturePath

        # cleanup child 也不可將任意 no-go reason 透過 handoff 回傳。拒絕未知 reason 時，parent 必須
        # 保留已證明的 v2 ledger 與 descriptor pair，讓 operator 仍能執行後續一次明確 cleanup。
        $unknownCleanupReason = Invoke-SyntheticFreshCleanup `
            -ProvisionScenario $provenProvision `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -OwnerIdentity $identity `
            -Scenario 'unknown-cleanup-reason'
        Assert-True (
            $unknownCleanupReason.Result.ExitCode -eq 2 -and
            $unknownCleanupReason.Result.Evidence.outcome -eq 'no-go' -and
            $unknownCleanupReason.Result.Evidence.reason -eq 'fresh-fixture-evidence-unavailable' -and
            $unknownCleanupReason.Result.Evidence.operationExecuted -and
            -not $unknownCleanupReason.Result.Evidence.safeToRetry
        ) 'A child-provided cleanup reason outside its allowlist must become a sanitized no-go.'
        $unknownCleanupObservation = [IO.File]::ReadAllText(
            $unknownCleanupReason.ChildObservationPath,
            [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$unknownCleanupObservation.undeclaredFreshLegacySentinel)
        ) 'Fresh cleanup no-go child must not inherit an undeclared FRESH-prefixed P7_2_SLICE_C legacy sentinel.'
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$unknownCleanupObservation.undeclaredSpeechmessageFreshLegacySentinel)
        ) 'Fresh cleanup no-go child must not inherit an undeclared FRESH-prefixed SPEECHMESSAGE_P7_2_SLICE_C legacy sentinel.'
        Assert-True (Test-Path -LiteralPath $unknownCleanupReason.RestorationObservationPath -PathType Leaf) 'Synthetic cleanup no-go runner must observe the post-finally environment state.'
        $restoredUnknownCleanupEnvironment = [IO.File]::ReadAllText(
            $unknownCleanupReason.RestorationObservationPath,
            [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        $restoredUnknownFreshLegacyProperty = @(
            $restoredUnknownCleanupEnvironment.PSObject.Properties |
                Where-Object { $_.Name -ceq 'P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL' }
        )
        Assert-True (
            $restoredUnknownFreshLegacyProperty.Count -eq 1 -and
            [string]$restoredUnknownFreshLegacyProperty[0].Value -ceq
                [string]$sentinelValues['P7_2_SLICE_C_FRESH_UNDECLARED_LEGACY_SENTINEL']
        ) 'Fresh cleanup no-go finally must restore the undeclared FRESH-prefixed legacy sentinel.'
        Assert-True (
            (Get-FileFingerprint $provenProvision.SourceFixturePath) -ceq $publishedSourceFingerprint -and
            (Get-FileFingerprint $provenProvision.SliceCFixturePath) -ceq $publishedSliceCFingerprint -and
            (Test-Path -LiteralPath $provenLedgerPath -PathType Leaf)
        ) 'Rejected cleanup evidence must preserve the current-user recovery ledger and published descriptor pair.'

        $successfulCleanup = Invoke-SyntheticFreshCleanup `
            -ProvisionScenario $provenProvision `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -OwnerIdentity $identity
        Assert-True (
            $successfulCleanup.Result.ExitCode -eq 0 -and
            $successfulCleanup.Result.Evidence.outcome -eq 'go' -and
            $successfulCleanup.Result.Evidence.reason -eq 'fresh-fixture-cleaned' -and
            $successfulCleanup.Result.Evidence.operationExecuted -and
            -not $successfulCleanup.Result.Evidence.safeToRetry
        ) 'Only a valid v2 ledger and complete cleanup evidence may finish the explicit cleanup lane.'
        $cleanupObservation = [IO.File]::ReadAllText($successfulCleanup.ChildObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$cleanupObservation.provisionMode) -and
            -not [string]::IsNullOrWhiteSpace([string]$cleanupObservation.cleanupMode) -and
            $cleanupObservation.descriptorConfirmation -eq 'cleanup-fresh-fixture'
        ) 'Cleanup child must receive only the cleanup mode and confirmation boundary.'
        Assert-True (
            $null -eq $cleanupObservation.freshExistingTargetLeaderId -and
            $null -eq $cleanupObservation.legacyTargetLeaderContactId
        ) 'Cleanup child must receive neither the fresh nor legacy target-leader environment variable.'
        foreach ($legacyProperty in @($cleanupObservation.legacyEnvironment.PSObject.Properties)) {
            Assert-True (
                [string]::IsNullOrWhiteSpace([string]$legacyProperty.Value)
            ) 'Fresh cleanup child must not inherit legacy Slice C fixture, contact, mode or evidence environment variables.'
        }
        Assert-True (
            [string]::IsNullOrWhiteSpace([string]$cleanupObservation.undeclaredLegacySentinel)
        ) 'Fresh cleanup child must not inherit an undeclared P7_2_SLICE_C legacy sentinel.'
        Assert-True (Test-Path -LiteralPath $successfulCleanup.RestorationObservationPath -PathType Leaf) 'Synthetic cleanup runner must observe the post-finally environment state.'
        $restoredCleanupEnvironment = [IO.File]::ReadAllText($successfulCleanup.RestorationObservationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        foreach ($environmentName in $freshEnvironmentNames) {
            $restoredProperty = @($restoredCleanupEnvironment.PSObject.Properties | Where-Object { $_.Name -ceq $environmentName })
            Assert-True (
                $restoredProperty.Count -eq 1 -and
                [string]$restoredProperty[0].Value -ceq [string]$sentinelValues[$environmentName]
            ) 'Fresh parent finally must restore every process environment variable after successful cleanup.'
        }
        Assert-True (
            -not (Test-Path -LiteralPath ([string]$cleanupObservation.evidenceDirectory) -PathType Container)
        ) 'Parent must remove its fresh child temporary evidence directory after successful cleanup.'
        Assert-True (
            -not (Test-Path -LiteralPath $provenProvision.SourceFixturePath -PathType Leaf) -and
            -not (Test-Path -LiteralPath $provenProvision.SliceCFixturePath -PathType Leaf) -and
            -not (Test-Path -LiteralPath $provenLedgerPath -PathType Leaf)
        ) 'Successful cleanup must remove only the matching fresh descriptors and completed current-user ledger.'
        Assert-True (
            (Test-Path -LiteralPath $provenProvision.SeedPath -PathType Leaf) -and
            (Get-FileFingerprint $provenProvision.SeedPath) -ceq $provenProvision.ExpectedSeedFingerprint
        ) 'Successful cleanup must retain the static seed byte-identical for the next fresh cycle.'

        # 使用獨立 successful provision 建立 valid pre-cleanup baseline。child 隨後回報合法 cleanup
        # evidence 卻把 ledger 改寫為 v1；parent 必須 fail closed，且不得因 cleanup 開始過就刪除
        # descriptor 或唯一的 recovery ledger。這保護 post-child re-read 的 transaction boundary。
        $invalidPostChildLedgerProvision = Invoke-SyntheticFreshProvision `
            -Scenario 'fresh-graph-proven' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $invalidPostChildLedgerProvision.Result.ExitCode -eq 0 -and
            $invalidPostChildLedgerProvision.Result.Evidence.outcome -eq 'go'
        ) 'Cleanup post-child ledger regression requires an independently proven fresh fixture.'
        $invalidPostChildLedgerPath = Join-Path `
            ([string]$invalidPostChildLedgerProvision.LocalAppDataRoot) `
            'SpeechMessage\Dynamics\P7.2\FreshSliceC\fresh-slice-c-ledger.json'
        $invalidPreCleanupSourceFingerprint = Get-FileFingerprint $invalidPostChildLedgerProvision.SourceFixturePath
        $invalidPreCleanupSliceCFingerprint = Get-FileFingerprint $invalidPostChildLedgerProvision.SliceCFixturePath
        $invalidPostChildLedgerCleanup = Invoke-SyntheticFreshCleanup `
            -ProvisionScenario $invalidPostChildLedgerProvision `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -OwnerIdentity $identity `
            -Scenario 'invalid-post-child-ledger'
        Assert-True (
            $invalidPostChildLedgerCleanup.Result.ExitCode -eq 2 -and
            $invalidPostChildLedgerCleanup.Result.Evidence.outcome -eq 'no-go' -and
            $invalidPostChildLedgerCleanup.Result.Evidence.reason -eq 'fresh-fixture-ledger-unavailable' -and
            $invalidPostChildLedgerCleanup.Result.Evidence.operationExecuted -and
            -not $invalidPostChildLedgerCleanup.Result.Evidence.safeToRetry
        ) 'An invalid post-child cleanup ledger must be a sanitized fail-closed result, never an unsafe success.'
        Assert-True (
            (Get-FileFingerprint $invalidPostChildLedgerProvision.SourceFixturePath) -ceq $invalidPreCleanupSourceFingerprint -and
            (Get-FileFingerprint $invalidPostChildLedgerProvision.SliceCFixturePath) -ceq $invalidPreCleanupSliceCFingerprint -and
            (Test-Path -LiteralPath $invalidPostChildLedgerPath -PathType Leaf)
        ) 'Invalid post-child cleanup ledger must preserve both descriptors and the recovery ledger.'
        $invalidPostChildLedger = [IO.File]::ReadAllText(
            $invalidPostChildLedgerPath,
            [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            $invalidPostChildLedger.schemaVersion -eq 1
        ) 'Cleanup regression must prove the parent observed the child-replaced invalid ledger rather than a stale baseline.'

        # 第二個獨立 fresh graph 將 cleanup child 的 final ledger 維持為完整 v2，但修改 provision
        # snapshot 的 original target-leader baseline。這不是 schema 損壞而是跨 child transaction
        # tampering；parent 必須回報專屬 cleanup failure，且不得移除 descriptors 或唯一 recovery ledger。
        $alteredBaselineProvision = Invoke-SyntheticFreshProvision `
            -Scenario 'fresh-graph-proven' `
            -TemporaryRoot $fixtureRoot `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -SourceFixturePath $sourceFixturePath `
            -SliceCFixturePath $sliceCFixturePath `
            -OwnerIdentity $identity
        Assert-True (
            $alteredBaselineProvision.Result.ExitCode -eq 0 -and
            $alteredBaselineProvision.Result.Evidence.outcome -eq 'go'
        ) 'Altered post-cleanup baseline regression requires an independently proven fresh fixture.'
        $alteredBaselineLedgerPath = Join-Path `
            ([string]$alteredBaselineProvision.LocalAppDataRoot) `
            'SpeechMessage\Dynamics\P7.2\FreshSliceC\fresh-slice-c-ledger.json'
        $alteredBaselineSourceFingerprint = Get-FileFingerprint $alteredBaselineProvision.SourceFixturePath
        $alteredBaselineSliceCFingerprint = Get-FileFingerprint $alteredBaselineProvision.SliceCFixturePath
        $alteredBaselineCleanup = Invoke-SyntheticFreshCleanup `
            -ProvisionScenario $alteredBaselineProvision `
            -RepositoryPath $repositoryPath `
            -ProfilePath $profilePath `
            -OwnerIdentity $identity `
            -Scenario 'altered-post-child-original-baseline'
        Assert-True (
            $alteredBaselineCleanup.Result.ExitCode -eq 2 -and
            $alteredBaselineCleanup.Result.Evidence.outcome -eq 'no-go' -and
            $alteredBaselineCleanup.Result.Evidence.reason -eq 'fresh-fixture-ledger-cleanup-failed' -and
            $alteredBaselineCleanup.Result.Evidence.operationExecuted -and
            -not $alteredBaselineCleanup.Result.Evidence.safeToRetry
        ) 'A valid v2 post-cleanup ledger with an altered original baseline must fail closed.'
        Assert-True (
            (Get-FileFingerprint $alteredBaselineProvision.SourceFixturePath) -ceq $alteredBaselineSourceFingerprint -and
            (Get-FileFingerprint $alteredBaselineProvision.SliceCFixturePath) -ceq $alteredBaselineSliceCFingerprint -and
            (Test-Path -LiteralPath $alteredBaselineLedgerPath -PathType Leaf)
        ) 'Altered post-cleanup baseline must retain both descriptors and its recovery ledger.'
        $alteredBaselineLedger = [IO.File]::ReadAllText(
            $alteredBaselineLedgerPath,
            [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        Assert-True (
            $alteredBaselineLedger.schemaVersion -is [int] -and
            $alteredBaselineLedger.schemaVersion -eq 2 -and
            $alteredBaselineLedger.originalTargetLeaderContactId -eq 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
        ) 'The cleanup assertion must observe the child-written valid v2 ledger rather than a cached pre-cleanup value.'
    }
    finally {
        Restore-ProcessEnvironmentSnapshot $environmentSnapshot
        Restore-ProcessEnvironmentSnapshot $localAppDataSnapshot
    }

    Write-Output ('Passed ' + $script:assertionCount + ' fresh-fixture PowerShell contract assertions.')
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Force -Recurse -ErrorAction SilentlyContinue
    }
}
