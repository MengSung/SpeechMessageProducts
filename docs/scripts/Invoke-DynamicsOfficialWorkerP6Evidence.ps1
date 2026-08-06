<#
.SYNOPSIS
執行 P6 Official Worker 的固定 connection-validation evidence。

.DESCRIPTION
這個 wrapper 是 P6.2 的唯一 connection evidence 入口；呼叫端不能傳入 operation ID。
它只把固定的 `runtime.pool.validate.connection` 交給既有、已驗證 profile／artifact
identity chain 的 compatibility harness。ValidateOnly 完全不建立網路資源；Live mode
必須明確指定 `-EnableLiveEvidence`，且只使用目前 Windows identity、HTTPS Gateway、
有界 JSON response 與 bounded timeout。

Child harness 的輸出只在 process-local bounded string 中存在，成功結果重新投影成固定
sanitized schema；failure 絕不重播 child stderr。因此 endpoint、部署路徑、credential、
token、cookie、原始 CRM payload 與例外細節不會進入 evidence。所有 child output、parsed
JSON 與 argument reference 在 finally 中解除，避免互動式 PowerShell session 保留狀態。

.NOTES
Windows PowerShell 5.1 相容。這個工具不執行 write、Action、Function、generic CRUD、
FetchXML 或 fee read，也不啟動 ChurchReport。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $GatewayOverlayPath,

    [Parameter(Mandatory = $true)]
    [string] $GatewayEndpoint,

    [Parameter(Mandatory = $true)]
    [string] $ProfileAlias,

    [Parameter(Mandatory = $true)]
    [ValidateSet('OfficialCrm82Worker', 'OfficialCrm91Worker')]
    [string] $ExpectedWorkerKind,

    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 45,

    [ValidateRange(1024, 65536)]
    [int] $MaximumResponseBytes = 16384,

    [switch] $ValidateOnly,

    [switch] $EnableLiveEvidence,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$maximumChildOutputCharacters = 65536
$compatibilityScript = Join-Path $PSScriptRoot 'Invoke-DynamicsOfficialWorkerCompatibility.ps1'
$childOutput = $null
$childText = $null
$parsed = $null
$arguments = $null

function Assert-SafeChildEvidence {
    <#
    .SYNOPSIS
    驗證既有 harness 的 bounded sanitized 結果。

    .DESCRIPTION
    只接受固定 schema、固定 operation 與固定 requestExecuted 狀態；任何額外 property、
    非字串 scalar 或 operation drift 都 fail closed。函式不把實際值放進例外文字，避免
    本機部署 metadata 在失敗路徑被回顯。
    #>
    param(
        [object] $Value,
        [bool] $ExpectedRequestExecuted
    )

    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        throw 'Official worker P6 evidence result is invalid.'
    }

    $propertyNames = @($Value.PSObject.Properties | ForEach-Object Name)
    $required = @(
        'schemaVersion',
        'outcome',
        'phase',
        'profileAlias',
        'workerKind',
        'ceVersion',
        'packageLockId',
        'profileGenerationId',
        'operationId',
        'requestExecuted'
    )
    foreach ($name in $required) {
        if ($propertyNames -notcontains $name) {
            throw 'Official worker P6 evidence result is invalid.'
        }
    }

    $allowed = $required + @('httpStatusCode', 'elapsedMilliseconds')
    foreach ($name in $propertyNames) {
        if ($allowed -notcontains $name) {
            throw 'Official worker P6 evidence result is invalid.'
        }
    }

    if ($Value.schemaVersion -ne 1 -or
        $Value.phase -cne '4C' -or
        $Value.operationId -cne 'runtime.pool.validate.connection' -or
        $Value.requestExecuted -isnot [bool] -or
        [bool]$Value.requestExecuted -ne $ExpectedRequestExecuted) {
        throw 'Official worker P6 evidence result is invalid.'
    }

    $expectedOutcome = if ($ExpectedRequestExecuted) { 'passed' } else { 'validated' }
    if ($Value.outcome -cne $expectedOutcome) {
        throw 'Official worker P6 evidence result is invalid.'
    }

    foreach ($name in @(
        'profileAlias',
        'workerKind',
        'ceVersion',
        'packageLockId',
        'profileGenerationId'
    )) {
        if ($Value.$name -isnot [string] -or
            [string]::IsNullOrWhiteSpace([string]$Value.$name)) {
            throw 'Official worker P6 evidence result is invalid.'
        }
    }

    if ($ExpectedRequestExecuted -and
        ($Value.httpStatusCode -ne 200 -or
            $Value.elapsedMilliseconds -isnot [long] -or
            [long]$Value.elapsedMilliseconds -lt 0)) {
        throw 'Official worker P6 evidence result is invalid.'
    }
}

function Invoke-BoundedCompatibilityHarness {
    <#
    .SYNOPSIS
    在獨立 child PowerShell 中執行固定 operation 的 compatibility harness。

    .DESCRIPTION
    參數以 process boundary 傳入，child 只會收到 repository path 與非秘密 selector；
    operation ID 在此函式內固定，不能由 wrapper caller 覆寫。child stdout 只保留在
    65536 字元上限內，失敗時丟棄所有文字並回傳固定例外，避免把 child error 或部署
    identity 洩漏到主控台。
    #>
    param()

    if (-not (Test-Path -LiteralPath $compatibilityScript -PathType Leaf)) {
        throw 'Official worker P6 evidence operation failed.'
    }

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $compatibilityScript,
        '-ManifestPath', $ManifestPath,
        '-GatewayOverlayPath', $GatewayOverlayPath,
        '-GatewayEndpoint', $GatewayEndpoint,
        '-ProfileAlias', $ProfileAlias,
        '-ExpectedWorkerKind', $ExpectedWorkerKind,
        '-TimeoutSeconds', $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-MaximumResponseBytes', $MaximumResponseBytes.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-OperationId', 'runtime.pool.validate.connection',
        '-Json'
    )
    if ($ValidateOnly) {
        $arguments += '-ValidateOnly'
    }
    if ($EnableLiveEvidence) {
        $arguments += '-EnableLiveCompatibility'
    }

    try {
        $childOutput = @(& powershell.exe @arguments 2>&1)
        $childExitCode = $LASTEXITCODE
        $childText = $childOutput -join [Environment]::NewLine
        if ($childText.Length -gt $maximumChildOutputCharacters -or
            $childExitCode -ne 0) {
            throw 'Official worker P6 evidence operation failed.'
        }

        try {
            $parsed = $childText | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw 'Official worker P6 evidence operation failed.'
        }

        Assert-SafeChildEvidence `
            -Value $parsed `
            -ExpectedRequestExecuted $EnableLiveEvidence.IsPresent
        return $parsed
    }
    catch {
        if ($_.Exception.Message -eq 'Official worker P6 evidence operation failed.') {
            throw $_.Exception
        }

        throw 'Official worker P6 evidence operation failed.'
    }
}

if (($ValidateOnly -and $EnableLiveEvidence) -or
    (-not $ValidateOnly -and -not $EnableLiveEvidence)) {
    throw 'Select exactly one explicit P6 evidence execution mode.'
}

try {
    $childEvidence = Invoke-BoundedCompatibilityHarness
    $result = [ordered]@{
        schemaVersion = 1
        outcome = $childEvidence.outcome
        phase = '4C'
        profileAlias = $childEvidence.profileAlias
        workerKind = $childEvidence.workerKind
        ceVersion = $childEvidence.ceVersion
        packageLockId = $childEvidence.packageLockId
        profileGenerationId = $childEvidence.profileGenerationId
        operationId = 'runtime.pool.validate.connection'
        requestExecuted = [bool]$childEvidence.requestExecuted
    }
    if ($EnableLiveEvidence) {
        $result.httpStatusCode = [int]$childEvidence.httpStatusCode
        $result.elapsedMilliseconds = [long]$childEvidence.elapsedMilliseconds
    }

    if ($Json) {
        $result | ConvertTo-Json -Depth 4
    }
    else {
        [pscustomobject]$result
    }
}
finally {
    $childOutput = $null
    $childText = $null
    $parsed = $null
    $arguments = $null
}
