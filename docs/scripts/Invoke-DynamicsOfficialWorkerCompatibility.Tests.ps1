<#
.SYNOPSIS
驗證官方 Dynamics Worker Phase 4C 相容性工具的本機安全邊界。

.DESCRIPTION
本測試只建立系統暫存目錄下、由本測試唯一擁有的假 Worker 發行檔、
manifest、Gateway overlay 與 worker-profile.xml。它不啟動 Dynamics、
不讀取任何真實 credential、Token、Cookie、連線字串或 CRM 回應，也不
建立外部網路連線。成功案例僅執行工具的 ValidateOnly 路徑，藉此證明
檔案身分鏈可在沒有 Live request 的情況下 fail-closed 驗證。

每個 fixture 的檔案與 byte array 都由最外層 finally 所有並清理；任何
驗證失敗時，測試仍會關閉 child PowerShell、釋放暫存檔案 handle，且只
刪除已解析且位於系統暫存根目錄內的測試專屬目錄，避免 Session、Memory
或檔案資源殘留，更不會誤刪操作者或正式環境檔案。

.NOTES
此檔案是獨立的 PowerShell 測試，不依賴 Pester；Windows PowerShell 5.1
與 CI 都能以 powershell.exe -File 執行。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptPath = Join-Path $PSScriptRoot 'Invoke-DynamicsOfficialWorkerCompatibility.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'speechmessage-dynamics-official-worker-compatibility-' +
    [Guid]::NewGuid().ToString('N'))

function Assert-True {
    <#
    .SYNOPSIS
    以固定例外終止測試的最小 assertion。

    .DESCRIPTION
    此 helper 不保留 assertion 歷程或輸入物件；失敗訊息只能由測試固定
    字串構成，避免 fixture 內可能模擬的路徑或身分資料進入輸出。呼叫者
    在 assertion 後立即結束，最外層 finally 仍是所有 fixture 資源的唯一
    清理者，因此不會遺留跨案例狀態。
    #>
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function ConvertTo-CrlfText {
    <#
    .SYNOPSIS
    將測試產生的文字正規化為 UTF-8 no-BOM 所需的 CRLF 結尾。

    .DESCRIPTION
    Fixture 的 JSON 與 XML 也是實際部署工具要解析的契約輸入，因此測試
    必須主動排除混合行尾造成的 Windows PowerShell 5.1 假陰性。此函式
    僅處理呼叫端傳入的短生命期文字，沒有快取、背景工作或跨測試保存的
    buffer；寫檔完成後呼叫端唯一持有 fixture 路徑。
    #>
    param([string] $Value)

    return ($Value -replace '(?<!\r)\n', "`r`n").TrimEnd("`r", "`n") + "`r`n"
}

function Write-StrictUtf8Text {
    <#
    .SYNOPSIS
    寫入測試專屬、UTF-8 without BOM、CRLF 結尾的文字檔。

    .DESCRIPTION
    寫入位置只來自 New-CompatibilityFixture 已建立的暫存樹；本 helper
    不接受正式部署根目錄，也不累積 FileStream。File.WriteAllText 會在
    同步呼叫返回前關閉 handle，之後由最外層 finally 回收整個 fixture。
    #>
    param(
        [string] $Path,
        [string] $Value
    )

    [IO.File]::WriteAllText(
        $Path,
        (ConvertTo-CrlfText -Value $Value),
        [Text.UTF8Encoding]::new($false))
}

function Write-StrictJson {
    <#
    .SYNOPSIS
    將 fixture 物件轉成有界、固定編碼的 JSON。

    .DESCRIPTION
    JSON 只包含假 artifact 的公開身分資料。此 helper 不放入密碼、token、
    cookie 或 CRM payload；ConvertTo-Json 的結果在寫檔後沒有被保存，因此
    不會成為長生命期記憶體或測試間 Session 狀態。
    #>
    param(
        [string] $Path,
        [object] $Value
    )

    Write-StrictUtf8Text -Path $Path -Value ($Value | ConvertTo-Json -Depth 12)
}

function Assert-StrictTextFile {
    <#
    .SYNOPSIS
    驗證 script 或 fixture 文字檔的 UTF-8/no-BOM/CRLF 契約。

    .DESCRIPTION
    讀取的 byte array 僅存在於本函式範圍；finally 會清零可變陣列，避免
    測試輸入延長存活。這也能在修改腳本時及早攔下 LF-only 行尾被 Windows
    PowerShell 5.1 誤判為註解或語法內容的問題。
    #>
    param([string] $Path)

    $bytes = $null
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True -Condition ($bytes.Length -gt 0) `
            -Message 'The checked text file is empty.'
        Assert-True -Condition (-not (
            $bytes.Length -ge 3 -and
            $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and
            $bytes[2] -eq 0xBF
        )) -Message 'The checked text file contains a UTF-8 BOM.'

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True -Condition (-not [Regex]::IsMatch($text, '(?<!\r)\n')) `
            -Message 'The checked text file contains an LF-only line ending.'
        Assert-True -Condition ($text.EndsWith("`r`n", [StringComparison]::Ordinal)) `
            -Message 'The checked text file lacks a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function New-CompatibilityFixture {
    <#
    .SYNOPSIS
    建立一組最小且自洽的官方 Worker 發行身分鏈 fixture。

    .DESCRIPTION
    兩個 manifest worker 會存在，以重現正式 publish manifest 的固定完整
    inventory；但 Gateway overlay 只選取 CE 8.2，以驗證新版單 profile 部署
    合約。所有檔案位於全域 fixtureRoot 子樹，且回傳的物件不含秘密資料。
    測試不啟動 executable，因此假 artifact 不具可執行內容也不會留下子程序。
    #>
    param([string] $Name)

    $root = Join-Path $fixtureRoot $Name
    $publishedRoot = Join-Path $root 'published'
    $crm82Directory = Join-Path $publishedRoot 'crm82'
    $crm91Directory = Join-Path $publishedRoot 'crm91'
    $gatewayDirectory = Join-Path $root 'gateway'
    foreach ($directory in @(
        $crm82Directory,
        $crm91Directory,
        $gatewayDirectory
    )) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $crm82Executable = Join-Path $crm82Directory 'SpeechMessage.Dynamics.Crm82Worker.exe'
    $crm91Executable = Join-Path $crm91Directory 'SpeechMessage.Dynamics.Crm91Worker.exe'
    [IO.File]::WriteAllBytes(
        $crm82Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-official-crm82-worker'))
    [IO.File]::WriteAllBytes(
        $crm91Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-official-crm91-worker'))

    $crm82Hash = (Get-FileHash -LiteralPath $crm82Executable -Algorithm SHA256).Hash
    $crm91Hash = (Get-FileHash -LiteralPath $crm91Executable -Algorithm SHA256).Hash
    $crm82Lock = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
    $crm91Lock = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
    $generation = 'crm82-test-generation-0001'
    $organizationId = '7cda29bc-a18b-4c9b-8fb0-4c0fb547e5b1'

    $manifestPath = Join-Path $publishedRoot 'official-worker-manifest.json'
    Write-StrictJson -Path $manifestPath -Value ([ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        configuration = 'Release'
        targetFramework = 'net48'
        protocolVersion = 1
        featureGateMustRemainDisabled = $true
        outputRoot = $publishedRoot
        workers = @(
            [ordered]@{
                workerKind = 'OfficialCrm82Worker'
                ceVersion = '8.2'
                packageLockId = $crm82Lock
                packageLockSha256 = ('A' * 64)
                relativeExecutablePath = 'crm82/SpeechMessage.Dynamics.Crm82Worker.exe'
                sha256 = $crm82Hash
                executableBytes = (Get-Item -LiteralPath $crm82Executable).Length
                artifactFileCount = 1
                artifactTotalBytes = (Get-Item -LiteralPath $crm82Executable).Length
            },
            [ordered]@{
                workerKind = 'OfficialCrm91Worker'
                ceVersion = '9.1'
                packageLockId = $crm91Lock
                packageLockSha256 = ('B' * 64)
                relativeExecutablePath = 'crm91/SpeechMessage.Dynamics.Crm91Worker.exe'
                sha256 = $crm91Hash
                executableBytes = (Get-Item -LiteralPath $crm91Executable).Length
                artifactFileCount = 1
                artifactTotalBytes = (Get-Item -LiteralPath $crm91Executable).Length
            }
        )
    })

    $workerProfilePath = Join-Path $crm82Directory 'worker-profile.xml'
    Write-StrictUtf8Text -Path $workerProfilePath -Value @"
<?xml version="1.0" encoding="utf-8"?>
<officialDynamicsWorkerProfiles version="1">
  <profile generationId="$generation" workerKind="OfficialCrm82Worker" packageLockId="$crm82Lock">
    <organization hostName="crm82.fixture.invalid" port="443" name="crm82fixture" expectedOrganizationId="$organizationId" useSsl="true" authentication="ActiveDirectory" />
    <identity mode="HostIdentity" />
  </profile>
</officialDynamicsWorkerProfiles>
"@

    $overlayPath = Join-Path $gatewayDirectory 'dynamics-official-workers.gateway.json'
    Write-StrictJson -Path $overlayPath -Value ([ordered]@{
        DynamicsProfiles = [ordered]@{
            Profiles = [ordered]@{
                crm82 = [ordered]@{
                    WorkerProfileGenerationId = $generation
                    WorkerKind = 'OfficialCrm82Worker'
                    WorkerExecutablePath = [IO.Path]::GetFullPath($crm82Executable)
                    WorkerExecutableSha256 = $crm82Hash
                    PackageLockId = $crm82Lock
                    OrganizationBaseUri = 'https://crm82.fixture.invalid/'
                    Admission = [ordered]@{
                        ExpectedOrganizationId = $organizationId
                    }
                }
            }
        }
    })

    return [pscustomobject]@{
        Root = $root
        ManifestPath = $manifestPath
        OverlayPath = $overlayPath
        Crm82Executable = $crm82Executable
        Crm82ProfilePath = $workerProfilePath
        GatewayEndpoint = 'https://gateway.fixture.invalid/'
    }
}

function Invoke-CompatibilityHarness {
    <#
    .SYNOPSIS
    在獨立 child PowerShell 中執行被測相容性工具。

    .DESCRIPTION
    Child process 隔離 StrictMode、exception 與任何 HttpClient handler 狀態。
    ValidateOnly 是唯一在本測試中允許的成功模式，因此不會開啟網路 socket；
    child 結束後其 process-local 記憶體、handler、CTS 與環境狀態都由作業系統
    回收。回傳值只保留 exit code 與短文字供 assertion 使用，且不寫入檔案。
    #>
    param(
        [object] $Fixture,
        [switch] $ValidateOnly,
        [switch] $EnableLiveCompatibility,
        [string] $ProfileAlias = 'crm82',
        [string] $GatewayEndpoint = $Fixture.GatewayEndpoint
    )

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ManifestPath', $Fixture.ManifestPath,
        '-GatewayOverlayPath', $Fixture.OverlayPath,
        '-GatewayEndpoint', $GatewayEndpoint,
        '-ProfileAlias', $ProfileAlias,
        '-ExpectedWorkerKind', 'OfficialCrm82Worker',
        '-Json'
    )
    if ($ValidateOnly) {
        $arguments += '-ValidateOnly'
    }
    if ($EnableLiveCompatibility) {
        $arguments += '-EnableLiveCompatibility'
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $output -join [Environment]::NewLine
    }
}

function Assert-PreflightFailure {
    <#
    .SYNOPSIS
    證明 invalid identity chain 在任何 Live request 前 fail-closed。

    .DESCRIPTION
    所有負面案例強制走 ValidateOnly，因此即使腳本回歸而嘗試建立 network
    request，測試的 assertion 也會失敗。mutate 只可改變本案例新建 fixture；
    外層 finally 仍以唯一 owner 方式清理所有案例，避免測試順序造成跨 profile
    或跨 Session 汙染。
    #>
    param(
        [string] $Name,
        [scriptblock] $Mutate
    )

    $fixture = New-CompatibilityFixture -Name $Name
    & $Mutate $fixture
    $result = Invoke-CompatibilityHarness -Fixture $fixture -ValidateOnly
    Assert-True -Condition ($result.ExitCode -ne 0) `
        -Message 'Invalid compatibility preflight was accepted.'
    Assert-True -Condition (-not $result.Text.Contains($fixture.GatewayEndpoint)) `
        -Message 'Compatibility failure exposed the Gateway endpoint.'
}

try {
    Assert-True -Condition (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        -Message 'The official Dynamics worker compatibility harness is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    $validFixture = New-CompatibilityFixture -Name 'valid-validate-only'
    $validResult = Invoke-CompatibilityHarness -Fixture $validFixture -ValidateOnly
    Assert-True -Condition ($validResult.ExitCode -eq 0) `
        -Message 'ValidateOnly preflight rejected a coherent official worker identity chain.'
    $validEvidence = $validResult.Text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($validEvidence.outcome -eq 'validated') `
        -Message 'ValidateOnly must report a sanitized validated outcome.'
    Assert-True -Condition ($validEvidence.operationId -eq 'runtime.health.whoami') `
        -Message 'The compatibility harness selected an unapproved operation.'
    Assert-True -Condition ($validEvidence.workerKind -eq 'OfficialCrm82Worker') `
        -Message 'ValidateOnly reported an unexpected worker kind.'
    Assert-True -Condition ($validEvidence.PSObject.Properties.Name -notcontains 'gatewayEndpoint') `
        -Message 'Compatibility evidence must not expose the Gateway endpoint.'
    Assert-True -Condition (-not $validResult.Text.Contains($validFixture.GatewayEndpoint)) `
        -Message 'Compatibility evidence exposed the Gateway endpoint.'
    Assert-True -Condition (-not $validResult.Text.Contains($validFixture.Root)) `
        -Message 'Compatibility evidence exposed a deployment path.'

    $noOptInResult = Invoke-CompatibilityHarness -Fixture $validFixture
    Assert-True -Condition ($noOptInResult.ExitCode -ne 0) `
        -Message 'The compatibility harness accepted execution without an explicit mode.'
    Assert-True -Condition (-not $noOptInResult.Text.Contains($validFixture.GatewayEndpoint)) `
        -Message 'No-opt-in failure exposed the Gateway endpoint.'

    Assert-PreflightFailure -Name 'artifact-tamper' -Mutate {
        param($fixture)
        [IO.File]::WriteAllBytes(
            $fixture.Crm82Executable,
            [Text.Encoding]::ASCII.GetBytes('tampered-worker-artifact'))
    }
    Assert-PreflightFailure -Name 'overlay-worker-kind-drift' -Mutate {
        param($fixture)
        $overlay = Get-Content -LiteralPath $fixture.OverlayPath -Raw -Encoding utf8 |
            ConvertFrom-Json -ErrorAction Stop
        $overlay.DynamicsProfiles.Profiles.crm82.WorkerKind = 'OfficialCrm91Worker'
        Write-StrictJson -Path $fixture.OverlayPath -Value $overlay
    }
    Assert-PreflightFailure -Name 'overlay-duplicate-property' -Mutate {
        param($fixture)
        $json = Get-Content -LiteralPath $fixture.OverlayPath -Raw -Encoding utf8
        Write-StrictUtf8Text -Path $fixture.OverlayPath -Value (
            $json.Replace(
                '"WorkerKind":  "OfficialCrm82Worker",',
                '"WorkerKind":  "OfficialCrm82Worker", "WorkerKind":  "OfficialCrm82Worker",'))
    }
    Assert-PreflightFailure -Name 'worker-profile-package-lock-drift' -Mutate {
        param($fixture)
        $xml = Get-Content -LiteralPath $fixture.Crm82ProfilePath -Raw -Encoding utf8
        Write-StrictUtf8Text -Path $fixture.Crm82ProfilePath -Value (
            $xml.Replace(
                'crm82-xrmtooling-8.2.0.5-core-8.2.0.2',
                'unapproved-package-lock'))
    }

    $httpFixture = New-CompatibilityFixture -Name 'http-endpoint-rejected'
    $httpResult = Invoke-CompatibilityHarness `
        -Fixture $httpFixture `
        -ValidateOnly `
        -GatewayEndpoint 'http://gateway.fixture.invalid/'
    Assert-True -Condition ($httpResult.ExitCode -ne 0) `
        -Message 'The compatibility harness accepted a non-HTTPS Gateway endpoint.'

    $source = [IO.File]::ReadAllText(
        $scriptPath,
        [Text.UTF8Encoding]::new($false, $true))
    foreach ($requiredSourceFragment in @(
        '[System.Net.Http.HttpClientHandler]::new()',
        'UseCookies = $false',
        'UseProxy = $false',
        'AllowAutoRedirect = $false',
        'AutomaticDecompression = [Net.DecompressionMethods]::None',
        'ResponseHeadersRead',
        '[Array]::Clear('
    )) {
        Assert-True -Condition ($source.Contains($requiredSourceFragment)) `
            -Message 'The compatibility harness is missing a required lifecycle boundary.'
    }
    foreach ($forbiddenSourceFragment in @(
        '/api/data/',
        'Invoke-WebRequest',
        'Invoke-RestMethod',
        'Authorization',
        'Bearer ',
        'Basic '
    )) {
        Assert-True -Condition (-not $source.Contains($forbiddenSourceFragment)) `
            -Message 'The compatibility harness contains a forbidden transport fallback.'
    }

    'All official Dynamics worker compatibility harness tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedFixture.StartsWith(
                $resolvedTemp,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a compatibility fixture outside the temporary directory.'
        }

        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
