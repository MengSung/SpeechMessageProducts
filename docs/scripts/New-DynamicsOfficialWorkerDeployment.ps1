<#
.SYNOPSIS
依已核准的 Microsoft 官方 Dynamics Worker 發布清單，建立一次性的部署設定檔。

.DESCRIPTION
本工具只接受明確指定的發布清單、非秘密 Profile 輸入與輸出目錄。它會先完成 JSON
結構、重複欄位、package lock、Worker 執行檔雜湊、artifact inventory、CE 版本、
Organization identity、認證聯集與所有既有輸出目標驗證，之後才開始任何部署檔案寫入。

Worker Profile 先寫入各 Worker 目錄，Gateway overlay 最後發布，讓 overlay 成為可預期的
完成邊界。每個檔案都先在相同目錄以本次呼叫專屬的暫存名稱完整寫入，再以不覆寫的 Move
提交；失敗時只清理由本次呼叫建立的暫存檔與已提交目標，既有檔案一律拒絕覆寫。

輸入只允許 credential reference，不讀取或輸出密碼、Token、Connection String、Cookie、
Private Key 或任何使用者 Session。腳本不建立網路連線、背景工作、計時器或持久 Session，
所有 Stream/Reader 均由單一同步呼叫範圍確定釋放。

.PARAMETER ManifestPath
由官方 Worker 發布流程產生、且與兩個 Worker artifact 位於同一根目錄的 JSON 清單。

.PARAMETER ProfileInputPath
只包含兩個非秘密 Worker Profile 的嚴格 JSON 輸入檔。

.PARAMETER OutputDirectory
存放 Gateway overlay 的明確目錄。工具不會覆寫任何既有輸出檔。

.PARAMETER Json
以 JSON 輸出不含秘密值的部署結果；未指定時輸出 PSCustomObject。

.OUTPUTS
System.Management.Automation.PSCustomObject，或啟用 Json 時的 JSON 文字。

.NOTES
相容 Windows PowerShell 5.1。任何驗證、暫存、提交或回滾失敗都以非零結束並保持 fail-closed。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.Serialization -ErrorAction Stop

$maximumManifestBytes = 256 * 1024
$maximumProfileInputBytes = 128 * 1024
$utf8NoBom = [Text.UTF8Encoding]::new($false, $true)
$regexTimeout = [TimeSpan]::FromMilliseconds(100)
$regexOptions = [Text.RegularExpressions.RegexOptions]::CultureInvariant

<#
.SYNOPSIS
遞迴確認 JSON object 的每一層都沒有完全同名的欄位。

.DESCRIPTION
Windows PowerShell 5.1 的 ConvertFrom-Json 會靜默保留完全同名欄位的最後一值，因此此函式
在 options binding 前，以 Ordinal 名稱集合驗證 JsonReader 產生的 object 節點。遞迴深度由
呼叫端的 XmlDictionaryReaderQuotas 限制，集合只存放目前 object 的 bounded 欄位名稱。
#>
function Assert-NoDuplicateJsonObjectProperties {
    param(
        [Xml.XmlElement] $Element,
        [string] $DocumentName
    )

    $childElements = @(
        $Element.ChildNodes |
            Where-Object { $_ -is [Xml.XmlElement] }
    )
    if ([string]::Equals(
            $Element.GetAttribute('type'),
            'object',
            [StringComparison]::Ordinal)) {
        $propertyNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($childElement in $childElements) {
            if (-not $propertyNames.Add($childElement.LocalName)) {
                throw "$DocumentName contains a duplicate JSON property."
            }
        }
    }

    foreach ($childElement in $childElements) {
        Assert-NoDuplicateJsonObjectProperties `
            -Element $childElement `
            -DocumentName $DocumentName
    }
}

<#
.SYNOPSIS
以 Windows PowerShell 5.1 可用的 bounded JsonReader 驗證原始 JSON 位元組。

.DESCRIPTION
此函式在 ConvertFrom-Json 前執行獨立解析，限制深度、字串、陣列、單次讀取與 NameTable，
避免小型輸入透過極深巢狀結構造成非預期保留或堆疊壓力。Reader 由本函式唯一擁有並在
finally 中確定 Dispose；解析成功後再檢查每個 object 的重複欄位。
#>
function Assert-BoundedDuplicateAwareJson {
    param(
        [byte[]] $Bytes,
        [int] $MaximumBytes,
        [string] $DocumentName
    )

    $reader = $null
    try {
        $quotas = [Xml.XmlDictionaryReaderQuotas]::new()
        $quotas.MaxDepth = 32
        $quotas.MaxStringContentLength = $MaximumBytes
        $quotas.MaxArrayLength = $MaximumBytes
        $quotas.MaxBytesPerRead = [Math]::Min(4096, $MaximumBytes)
        $quotas.MaxNameTableCharCount = $MaximumBytes
        $reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
            $Bytes,
            0,
            $Bytes.Length,
            [Text.Encoding]::UTF8,
            $quotas,
            $null)
        $document = [Xml.XmlDocument]::new()
        $document.PreserveWhitespace = $false
        $document.Load($reader)
    }
    catch {
        throw "$DocumentName is not a valid bounded JSON document."
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }

    if ($null -eq $document.DocumentElement) {
        throw "$DocumentName must contain one JSON object."
    }

    Assert-NoDuplicateJsonObjectProperties `
        -Element $document.DocumentElement `
        -DocumentName $DocumentName
}

<#
.SYNOPSIS
讀取一個有硬性 byte 上限、嚴格 UTF-8 且拒絕重複欄位的 JSON 文件。

.DESCRIPTION
檔案大小會在讀取前後各驗證一次，以處理讀取期間的檔案變更；原始位元組先通過 bounded
duplicate-aware parser，再交給 ConvertFrom-Json。函式不回傳原始位元組或例外內容，避免
非法輸入、秘密形狀或檔案內容進入診斷輸出。
#>
function Read-BoundedJsonDocument {
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $DocumentName
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$DocumentName path is required."
    }

    try {
        $resolvedPath = [IO.Path]::GetFullPath($Path)
    }
    catch {
        throw "$DocumentName path is invalid."
    }

    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$DocumentName is missing."
    }

    $length = (Get-Item -LiteralPath $resolvedPath).Length
    if ($length -le 0 -or $length -gt $MaximumBytes) {
        throw "$DocumentName size is outside the allowed bound."
    }

    try {
        $bytes = [IO.File]::ReadAllBytes($resolvedPath)
    }
    catch {
        throw "$DocumentName could not be read."
    }

    if ($bytes.Length -le 0 -or $bytes.Length -gt $MaximumBytes) {
        throw "$DocumentName size is outside the allowed bound."
    }

    Assert-BoundedDuplicateAwareJson `
        -Bytes $bytes `
        -MaximumBytes $MaximumBytes `
        -DocumentName $DocumentName

    try {
        $text = $utf8NoBom.GetString($bytes)
        if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
            $text = $text.Substring(1)
        }

        $value = $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "$DocumentName is not a valid UTF-8 JSON document."
    }

    if ($null -eq $value) {
        throw "$DocumentName must contain one JSON object."
    }

    return [pscustomobject]@{
        Path = $resolvedPath
        Value = $value
    }
}

function Assert-JsonObject {
    param(
        [object] $Value,
        [string] $Context
    )

    if ($null -eq $Value -or -not ($Value -is [pscustomobject])) {
        throw "$Context must be a JSON object."
    }
}

function Assert-ExactProperties {
    param(
        [object] $Value,
        [string[]] $ExpectedProperties,
        [string] $Context
    )

    Assert-JsonObject -Value $Value -Context $Context
    $actualProperties = @($Value.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actualProperties.Count -ne $ExpectedProperties.Count) {
        throw "$Context contains an unexpected or missing property."
    }

    foreach ($property in $actualProperties) {
        if (-not ($ExpectedProperties -ccontains $property)) {
            throw "$Context contains an unexpected property."
        }
    }

    foreach ($property in $ExpectedProperties) {
        if (-not ($actualProperties -ccontains $property)) {
            throw "$Context is missing a required property."
        }
    }
}

function Get-RequiredString {
    param(
        [object] $Value,
        [string] $PropertyName,
        [int] $MaximumLength,
        [string] $Context
    )

    $property = $Value.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or
        -not ($property.Value -is [string]) -or
        $property.Value.Length -eq 0 -or
        $property.Value.Length -gt $MaximumLength -or
        -not [string]::Equals(
            $property.Value,
            $property.Value.Trim(),
            [StringComparison]::Ordinal)) {
        throw "$Context.$PropertyName is invalid."
    }

    return [string]$property.Value
}

function Test-IntegralJsonNumber {
    param([object] $Value)

    return $Value -is [sbyte] -or
        $Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]
}

function Assert-ExactJsonInteger {
    param(
        [object] $Value,
        [long] $Expected,
        [string] $Context
    )

    if (-not (Test-IntegralJsonNumber -Value $Value) -or
        [long]$Value -ne $Expected) {
        throw "$Context must be the integer $Expected."
    }
}

function Get-RequiredPositiveInt64 {
    param(
        [object] $Value,
        [string] $Context
    )

    if (-not (Test-IntegralJsonNumber -Value $Value)) {
        throw "$Context must be a positive integer."
    }

    try {
        $result = [Convert]::ToInt64($Value, [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "$Context must be a positive integer."
    }

    if ($result -le 0) {
        throw "$Context must be a positive integer."
    }

    return $result
}

function Test-AsciiAlphaNumeric {
    param([char] $Character)

    $code = [int]$Character
    return ($code -ge 48 -and $code -le 57) -or
        ($code -ge 65 -and $code -le 90) -or
        ($code -ge 97 -and $code -le 122)
}

function Test-SafeIdentifier {
    param(
        [string] $Value,
        [int] $MaximumLength,
        [bool] $AllowDot = $true
    )

    if ([string]::IsNullOrEmpty($Value) -or
        $Value.Length -gt $MaximumLength -or
        -not (Test-AsciiAlphaNumeric -Character $Value[0]) -or
        -not (Test-AsciiAlphaNumeric -Character $Value[$Value.Length - 1])) {
        return $false
    }

    foreach ($character in $Value.ToCharArray()) {
        if (Test-AsciiAlphaNumeric -Character $character) {
            continue
        }

        if ($character -eq '-' -or
            $character -eq '_' -or
            ($AllowDot -and $character -eq '.')) {
            continue
        }

        return $false
    }

    return $true
}

function Assert-NoForbiddenSecretShape {
    param(
        [object] $Value,
        [string] $Context
    )

    if ($null -eq $Value) {
        return
    }

    if ($Value -is [string]) {
        $assignmentPattern = '(?:password|passphrase|access[_ -]?token|refresh[_ -]?token|token|connection[_ -]?string|cookie|client[_ -]?secret|secret|credential)\s*[:=]\s*\S+'
        $bearerPattern = '\bbearer\s+[A-Za-z0-9._~+/-]+=*'
        $jwtPattern = '\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b'
        $privateKeyPattern = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        $secretOptions = $regexOptions -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase
        if ([Regex]::IsMatch($Value, $assignmentPattern, $secretOptions, $regexTimeout) -or
            [Regex]::IsMatch($Value, $bearerPattern, $secretOptions, $regexTimeout) -or
            [Regex]::IsMatch($Value, $jwtPattern, $regexOptions, $regexTimeout) -or
            [Regex]::IsMatch($Value, $privateKeyPattern, $secretOptions, $regexTimeout)) {
            throw "$Context contains secret-shaped content."
        }

        return
    }

    if ($Value -is [pscustomobject]) {
        $forbiddenNamePattern = '(?:password|passphrase|token|connection.?string|cookie|client.?secret|secret|credential)'
        $nameOptions = $regexOptions -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase
        foreach ($property in $Value.PSObject.Properties) {
            if ([Regex]::IsMatch(
                    $property.Name,
                    $forbiddenNamePattern,
                    $nameOptions,
                    $regexTimeout)) {
                throw "$Context contains a forbidden property name."
            }

            Assert-NoForbiddenSecretShape `
                -Value $property.Value `
                -Context "$Context.$($property.Name)"
        }

        return
    }

    if ($Value -is [Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            Assert-NoForbiddenSecretShape `
                -Value $item `
                -Context "$Context[$index]"
            $index++
        }
    }
}

function Get-ValidatedSha256 {
    param(
        [string] $Value,
        [string] $Context
    )

    if ($Value.Length -ne 64 -or
        -not [Regex]::IsMatch(
            $Value,
            '\A[0-9A-Fa-f]{64}\z',
            $regexOptions,
            $regexTimeout) -or
        [Regex]::IsMatch(
            $Value,
            '\A0{64}\z',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase,
            $regexTimeout) -or
        [Regex]::IsMatch(
            $Value,
            '\A([0-9A-Fa-f])\1{63}\z',
            $regexOptions,
            $regexTimeout)) {
        throw "$Context is not an approved SHA-256 value."
    }

    return $Value.ToUpperInvariant()
}

function Test-SafeRelativeExecutablePath {
    param([string] $Value)

    if ([string]::IsNullOrEmpty($Value) -or
        [IO.Path]::IsPathRooted($Value) -or
        $Value.IndexOf([char]92) -ge 0 -or
        $Value.IndexOf(':') -ge 0 -or
        $Value.StartsWith('/', [StringComparison]::Ordinal) -or
        $Value.EndsWith('/', [StringComparison]::Ordinal)) {
        return $false
    }

    $segments = @($Value.Split('/'))
    if ($segments.Count -lt 2) {
        return $false
    }

    foreach ($segment in $segments) {
        if ([string]::IsNullOrEmpty($segment) -or
            $segment -eq '.' -or
            $segment -eq '..') {
            return $false
        }
    }

    return $true
}

function Get-CanonicalOrganizationUri {
    param(
        [string] $Value,
        [string] $Context
    )

    if ($Value.Length -gt 2048 -or
        $Value.IndexOf([char]92) -ge 0) {
        throw "$Context is not a canonical HTTPS root URI."
    }

    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsWhiteSpace($character) -or [char]::IsControl($character)) {
            throw "$Context is not a canonical HTTPS root URI."
        }
    }

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::Equals(
            $uri.Scheme,
            [Uri]::UriSchemeHttps,
            [StringComparison]::OrdinalIgnoreCase) -or
        $uri.HostNameType -ne [UriHostNameType]::Dns -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment) -or
        -not [string]::Equals($uri.AbsolutePath, '/', [StringComparison]::Ordinal)) {
        throw "$Context is not a canonical HTTPS root URI."
    }

    $hostName = $uri.IdnHost.ToLowerInvariant()
    $canonical = 'https://' + $hostName
    if ($uri.Port -ne 443) {
        $canonical += ':' + $uri.Port.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    $canonical += '/'
    if (-not [string]::Equals($Value, $canonical, [StringComparison]::Ordinal)) {
        throw "$Context is not a canonical HTTPS root URI."
    }

    return [pscustomobject]@{
        Value = $canonical
        HostName = $hostName
        Port = $uri.Port
    }
}

function Get-ValidatedExpectedOrganizationId {
    param(
        [string] $Value,
        [string] $Context
    )

    $guid = [Guid]::Empty
    if ($Value.Length -ne 36 -or
        -not [Guid]::TryParseExact($Value, 'D', [ref]$guid) -or
        $guid -eq [Guid]::Empty) {
        throw "$Context must be a non-placeholder D-format GUID."
    }

    $bytes = $guid.ToByteArray()
    $allBytesIdentical = $true
    for ($index = 1; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -ne $bytes[0]) {
            $allBytesIdentical = $false
            break
        }
    }

    if ($allBytesIdentical) {
        throw "$Context must be a non-placeholder D-format GUID."
    }

    return $guid.ToString('D')
}

function Get-ValidatedHomeRealm {
    param(
        [string] $Value,
        [string] $Context
    )

    if ($Value.Length -gt 2048 -or $Value.IndexOf([char]92) -ge 0) {
        throw "$Context must be a safe HTTPS home realm."
    }

    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsWhiteSpace($character) -or [char]::IsControl($character)) {
            throw "$Context must be a safe HTTPS home realm."
        }
    }

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::Equals(
            $uri.Scheme,
            [Uri]::UriSchemeHttps,
            [StringComparison]::OrdinalIgnoreCase) -or
        $uri.HostNameType -ne [UriHostNameType]::Dns -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "$Context must be a safe HTTPS home realm."
    }

    return $Value
}

function ConvertTo-CrlfText {
    param([string] $Value)

    $normalized = $Value -replace '(?<!\r)\n', "`r`n"
    return $normalized.TrimEnd("`r", "`n") + "`r`n"
}

function New-WorkerProfileXmlText {
    param([object] $Profile)

    $stream = [IO.MemoryStream]::new()
    try {
        $settings = [Xml.XmlWriterSettings]::new()
        $settings.Encoding = [Text.UTF8Encoding]::new($false)
        $settings.Indent = $true
        $settings.IndentChars = '  '
        $settings.NewLineChars = "`r`n"
        $settings.NewLineHandling = [Xml.NewLineHandling]::Replace
        $settings.OmitXmlDeclaration = $false
        $settings.CloseOutput = $false

        $writer = [Xml.XmlWriter]::Create($stream, $settings)
        try {
            $writer.WriteStartDocument()
            $writer.WriteStartElement('officialDynamicsWorkerProfiles')
            $writer.WriteAttributeString('version', '1')
            $writer.WriteStartElement('profile')
            $writer.WriteAttributeString('generationId', $Profile.GenerationId)
            $writer.WriteAttributeString('workerKind', $Profile.WorkerKind)
            $writer.WriteAttributeString('packageLockId', $Profile.PackageLockId)
            $writer.WriteStartElement('organization')
            $writer.WriteAttributeString('hostName', $Profile.HostName)
            $writer.WriteAttributeString(
                'port',
                $Profile.Port.ToString([Globalization.CultureInfo]::InvariantCulture))
            $writer.WriteAttributeString('name', $Profile.OrganizationName)
            $writer.WriteAttributeString(
                'expectedOrganizationId',
                $Profile.ExpectedOrganizationId)
            $writer.WriteAttributeString('useSsl', 'true')
            $writer.WriteAttributeString('authentication', $Profile.Authentication)
            $writer.WriteEndElement()
            $writer.WriteStartElement('identity')
            $writer.WriteAttributeString('mode', $Profile.IdentityMode)
            if ($Profile.IdentityMode -eq 'WindowsCredentialReference') {
                $writer.WriteAttributeString('reference', $Profile.CredentialReference)
                if ($Profile.Authentication -eq 'Ifd') {
                    $writer.WriteAttributeString('homeRealm', $Profile.HomeRealm)
                }
            }

            $writer.WriteEndElement()
            $writer.WriteEndElement()
            $writer.WriteEndElement()
            $writer.WriteEndDocument()
        }
        finally {
            $writer.Dispose()
        }

        $text = $utf8NoBom.GetString($stream.ToArray())
        return ConvertTo-CrlfText -Value $text
    }
    finally {
        $stream.Dispose()
    }
}

function New-GatewayOverlayText {
    param([object[]] $Profiles)

    $profileValues = [ordered]@{}
    foreach ($profile in $Profiles) {
        $profileValues[$profile.ProfileAlias] = [ordered]@{
            WorkerProfileGenerationId = $profile.GenerationId
            WorkerKind = $profile.WorkerKind
            WorkerExecutablePath = $profile.ExecutablePath
            WorkerExecutableSha256 = $profile.ExecutableSha256
            PackageLockId = $profile.PackageLockId
            OrganizationBaseUri = $profile.OrganizationBaseUri
            Admission = [ordered]@{
                ExpectedOrganizationId = $profile.ExpectedOrganizationId
            }
        }
    }

    $overlay = [ordered]@{
        DynamicsProfiles = [ordered]@{
            Profiles = $profileValues
        }
    }
    $text = $overlay | ConvertTo-Json -Depth 8
    return ConvertTo-CrlfText -Value $text
}

<#
.SYNOPSIS
以相同目錄暫存檔與不覆寫 Move，發布本次呼叫唯一擁有的部署輸出。

.DESCRIPTION
所有 payload 必須先在記憶體完成，且所有 target 必須在呼叫前通過不存在與唯一性驗證。
本函式先完整 staging 三個 UTF-8 no-BOM 檔，再依 Worker Profile、Gateway overlay 的順序提交；
因此看見 overlay 時，兩個 Worker Profile 已經存在。任何步驟失敗時，finally 型回滾會嘗試
清除本次 GUID 暫存檔與本次成功 Move 的 target，絕不遞迴刪除輸出目錄或無關檔案。

若任何本次擁有的檔案無法清除，函式回報 rollback incomplete，不能把部分輸出宣告為成功。
此函式不保留 FileStream、Timer、Job、Session 或背景 callback。
#>
function Write-AtomicDeploymentFiles {
    param(
        [object[]] $Payloads,
        [string] $ResolvedOutputDirectory
    )

    $temporaryPaths = [Collections.Generic.List[string]]::new()
    $stagedFiles = [Collections.Generic.List[object]]::new()
    $committedPaths = [Collections.Generic.List[string]]::new()

    try {
        if (-not (Test-Path -LiteralPath $ResolvedOutputDirectory)) {
            [void][IO.Directory]::CreateDirectory($ResolvedOutputDirectory)
        }

        foreach ($payload in $Payloads) {
            $parent = Split-Path -Parent $payload.TargetPath
            if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
                throw 'A deployment output parent directory is unavailable.'
            }

            $temporaryName = '.' +
                [IO.Path]::GetFileName($payload.TargetPath) + '.' +
                [Guid]::NewGuid().ToString('N') + '.tmp'
            $temporaryPath = Join-Path $parent $temporaryName
            [IO.File]::WriteAllText($temporaryPath, $payload.Content, $utf8NoBom)
            $temporaryPaths.Add($temporaryPath)
            $stagedFiles.Add([pscustomobject]@{
                TemporaryPath = $temporaryPath
                TargetPath = $payload.TargetPath
            })
        }

        foreach ($stagedFile in $stagedFiles) {
            [IO.File]::Move($stagedFile.TemporaryPath, $stagedFile.TargetPath)
            $committedPaths.Add($stagedFile.TargetPath)
        }
    }
    catch {
        $cleanupFailed = $false
        foreach ($temporaryPath in $temporaryPaths) {
            if (Test-Path -LiteralPath $temporaryPath) {
                try {
                    Remove-Item -LiteralPath $temporaryPath -Force
                }
                catch {
                    $cleanupFailed = $true
                }
            }
        }

        foreach ($committedPath in $committedPaths) {
            if (Test-Path -LiteralPath $committedPath) {
                try {
                    Remove-Item -LiteralPath $committedPath -Force
                }
                catch {
                    $cleanupFailed = $true
                }
            }
        }

        if ($cleanupFailed) {
            throw 'Deployment output creation failed and rollback was incomplete.'
        }

        throw 'Deployment output creation failed; no deployment file was retained.'
    }
}

$approvedWorkers = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
$approvedWorkers.Add(
    'OfficialCrm82Worker',
    [pscustomobject]@{
        WorkerKind = 'OfficialCrm82Worker'
        CeVersion = '8.2'
        PackageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
        PackageLockSha256 = '4F49F64D7AD1075DE08DDF29C57317843A5BAD3CD0E6203CBC4AA3FF9BCCD58D'
        RelativeExecutablePath = 'crm82/SpeechMessage.Dynamics.Crm82Worker.exe'
    })
$approvedWorkers.Add(
    'OfficialCrm91Worker',
    [pscustomobject]@{
        WorkerKind = 'OfficialCrm91Worker'
        CeVersion = '9.1'
        PackageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
        PackageLockSha256 = 'C2FF98918A505AB260676447B719F1EA52A7516028DBACAEF2B438C68F8383EC'
        RelativeExecutablePath = 'crm91/SpeechMessage.Dynamics.Crm91Worker.exe'
    })

$manifestDocument = Read-BoundedJsonDocument `
    -Path $ManifestPath `
    -MaximumBytes $maximumManifestBytes `
    -DocumentName 'Official worker manifest'
$profileDocument = Read-BoundedJsonDocument `
    -Path $ProfileInputPath `
    -MaximumBytes $maximumProfileInputBytes `
    -DocumentName 'Worker profile input'

$manifest = $manifestDocument.Value
$profileInput = $profileDocument.Value
Assert-NoForbiddenSecretShape -Value $manifest -Context 'Official worker manifest'
Assert-NoForbiddenSecretShape -Value $profileInput -Context 'Worker profile input'

Assert-ExactProperties `
    -Value $manifest `
    -ExpectedProperties @(
        'schemaVersion',
        'generatedAtUtc',
        'configuration',
        'targetFramework',
        'protocolVersion',
        'featureGateMustRemainDisabled',
        'outputRoot',
        'workers'
    ) `
    -Context 'Official worker manifest'
Assert-ExactJsonInteger `
    -Value $manifest.schemaVersion `
    -Expected 1 `
    -Context 'Official worker manifest.schemaVersion'
Assert-ExactJsonInteger `
    -Value $manifest.protocolVersion `
    -Expected 1 `
    -Context 'Official worker manifest.protocolVersion'
$generatedAtUtc = Get-RequiredString `
    -Value $manifest `
    -PropertyName 'generatedAtUtc' `
    -MaximumLength 64 `
    -Context 'Official worker manifest'
$parsedGeneratedAtUtc = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParseExact(
        $generatedAtUtc,
        'O',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsedGeneratedAtUtc) -or
    $parsedGeneratedAtUtc.Offset -ne [TimeSpan]::Zero) {
    throw 'Official worker manifest.generatedAtUtc must be an UTC round-trip timestamp.'
}

if (-not [string]::Equals(
        (Get-RequiredString `
            -Value $manifest `
            -PropertyName 'configuration' `
            -MaximumLength 32 `
            -Context 'Official worker manifest'),
        'Release',
        [StringComparison]::Ordinal) -or
    -not [string]::Equals(
        (Get-RequiredString `
            -Value $manifest `
            -PropertyName 'targetFramework' `
            -MaximumLength 32 `
            -Context 'Official worker manifest'),
        'net48',
        [StringComparison]::Ordinal) -or
    -not ($manifest.featureGateMustRemainDisabled -is [bool]) -or
    -not $manifest.featureGateMustRemainDisabled) {
    throw 'Official worker manifest build or feature-gate metadata is invalid.'
}

$manifestDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $manifestDocument.Path))
$manifestOutputRoot = Get-RequiredString `
    -Value $manifest `
    -PropertyName 'outputRoot' `
    -MaximumLength 32767 `
    -Context 'Official worker manifest'
if (-not [IO.Path]::IsPathRooted($manifestOutputRoot)) {
    throw 'Official worker manifest.outputRoot must be absolute.'
}

try {
    $resolvedManifestOutputRoot = [IO.Path]::GetFullPath($manifestOutputRoot)
}
catch {
    throw 'Official worker manifest.outputRoot is invalid.'
}

if (-not [string]::Equals(
        $resolvedManifestOutputRoot,
        $manifestDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Official worker manifest.outputRoot must match the manifest directory.'
}

$manifestWorkers = @($manifest.workers)
if ($manifestWorkers.Count -ne 2) {
    throw 'Official worker manifest must contain exactly two workers.'
}

$validatedWorkers = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
foreach ($worker in $manifestWorkers) {
    $context = 'Official worker manifest worker'
    Assert-ExactProperties `
        -Value $worker `
        -ExpectedProperties @(
            'workerKind',
            'ceVersion',
            'packageLockId',
            'packageLockSha256',
            'relativeExecutablePath',
            'sha256',
            'executableBytes',
            'artifactFileCount',
            'artifactTotalBytes'
        ) `
        -Context $context

    $workerKind = Get-RequiredString `
        -Value $worker `
        -PropertyName 'workerKind' `
        -MaximumLength 64 `
        -Context $context
    if (-not $approvedWorkers.ContainsKey($workerKind) -or
        $validatedWorkers.ContainsKey($workerKind)) {
        throw 'Official worker manifest contains an unapproved or duplicate worker kind.'
    }

    $definition = $approvedWorkers[$workerKind]
    $ceVersion = Get-RequiredString `
        -Value $worker `
        -PropertyName 'ceVersion' `
        -MaximumLength 16 `
        -Context $context
    $packageLockId = Get-RequiredString `
        -Value $worker `
        -PropertyName 'packageLockId' `
        -MaximumLength 128 `
        -Context $context
    $relativeExecutablePath = Get-RequiredString `
        -Value $worker `
        -PropertyName 'relativeExecutablePath' `
        -MaximumLength 512 `
        -Context $context
    if (-not (Test-SafeRelativeExecutablePath -Value $relativeExecutablePath) -or
        -not [string]::Equals(
            $ceVersion,
            $definition.CeVersion,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $packageLockId,
            $definition.PackageLockId,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $relativeExecutablePath,
            $definition.RelativeExecutablePath,
            [StringComparison]::Ordinal)) {
        throw 'Official worker manifest contains unapproved worker metadata.'
    }

    $packageLockSha256 = Get-ValidatedSha256 `
        -Value (Get-RequiredString `
            -Value $worker `
            -PropertyName 'packageLockSha256' `
            -MaximumLength 64 `
            -Context $context) `
        -Context "$context.packageLockSha256"
    if (-not [string]::Equals(
            $packageLockSha256,
            $definition.PackageLockSha256,
            [StringComparison]::Ordinal)) {
        throw 'Official worker manifest package-lock integrity verification failed.'
    }

    $manifestExecutableSha256 = Get-ValidatedSha256 `
        -Value (Get-RequiredString `
            -Value $worker `
            -PropertyName 'sha256' `
            -MaximumLength 64 `
            -Context $context) `
        -Context "$context.sha256"
    $manifestExecutableBytes = Get-RequiredPositiveInt64 `
        -Value $worker.executableBytes `
        -Context "$context.executableBytes"
    $artifactFileCount = Get-RequiredPositiveInt64 `
        -Value $worker.artifactFileCount `
        -Context "$context.artifactFileCount"
    $artifactTotalBytes = Get-RequiredPositiveInt64 `
        -Value $worker.artifactTotalBytes `
        -Context "$context.artifactTotalBytes"
    if ($artifactFileCount -lt 1 -or $artifactTotalBytes -lt $manifestExecutableBytes) {
        throw 'Official worker manifest artifact counts are invalid.'
    }

    $relativeSystemPath = $relativeExecutablePath.Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    $executablePath = [IO.Path]::GetFullPath((
        Join-Path $manifestDirectory $relativeSystemPath
    ))
    $manifestPrefix = $manifestDirectory.TrimEnd([char]92, [char]47) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $executablePath.StartsWith(
            $manifestPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw 'Official worker manifest executable path is unavailable.'
    }

    $actualExecutableBytes = (Get-Item -LiteralPath $executablePath).Length
    $actualExecutableSha256 = (
        Get-FileHash -LiteralPath $executablePath -Algorithm SHA256
    ).Hash.ToUpperInvariant()
    if ($actualExecutableBytes -ne $manifestExecutableBytes -or
        -not [string]::Equals(
            $actualExecutableSha256,
            $manifestExecutableSha256,
            [StringComparison]::Ordinal)) {
        throw 'Official worker manifest executable integrity verification failed.'
    }

    $actualArtifactFileCount = [long]0
    $actualArtifactTotalBytes = [long]0
    foreach ($artifactPath in [IO.Directory]::EnumerateFiles(
            (Split-Path -Parent $executablePath),
            '*',
            [IO.SearchOption]::TopDirectoryOnly)) {
        $actualArtifactFileCount++
        if ($actualArtifactFileCount -gt 4096) {
            throw 'Official worker artifact file count exceeds the deployment bound.'
        }

        $artifactLength = (Get-Item -LiteralPath $artifactPath).Length
        if ($artifactLength -lt 0 -or
            $actualArtifactTotalBytes -gt [long]::MaxValue - $artifactLength) {
            throw 'Official worker artifact byte count exceeds the deployment bound.'
        }

        $actualArtifactTotalBytes += $artifactLength
    }

    if ($actualArtifactFileCount -ne $artifactFileCount -or
        $actualArtifactTotalBytes -ne $artifactTotalBytes) {
        throw 'Official worker manifest artifact inventory verification failed.'
    }

    $validatedWorkers.Add(
        $workerKind,
        [pscustomobject]@{
            WorkerKind = $workerKind
            CeVersion = $ceVersion
            PackageLockId = $packageLockId
            ExecutablePath = $executablePath
            ExecutableDirectory = Split-Path -Parent $executablePath
            ExecutableSha256 = $actualExecutableSha256
        })
}

foreach ($approvedKind in $approvedWorkers.Keys) {
    if (-not $validatedWorkers.ContainsKey($approvedKind)) {
        throw 'Official worker manifest is missing an approved worker.'
    }
}

Assert-ExactProperties `
    -Value $profileInput `
    -ExpectedProperties @('schemaVersion', 'profiles') `
    -Context 'Worker profile input'
Assert-ExactJsonInteger `
    -Value $profileInput.schemaVersion `
    -Expected 1 `
    -Context 'Worker profile input.schemaVersion'

$inputProfiles = @($profileInput.profiles)
# Phase 4C 的相容性證據以「每一個已核准 profile」為單位，而不是要求兩個 CE
# 目標必須同時具備可部署的組織身分與認證資料。發布 manifest 仍必須完整驗證兩個
# 官方 Worker，避免部署輸入藉由省略 artifact 而放寬 package-lock/hash 邊界；然而
# profile 輸入可安全地只選擇其中一個已驗證 Worker。這可讓 CE 8.2 或 CE 9.1
# 分別完成相容性驗證，同時不猜測另一目標的組織、credential reference 或 home realm。
# 未選取 Worker 不會取得 XML、overlay 節點或任何跨 profile 的可變狀態，因此不會
# 產生 Session、credential 或資源所有權外洩。
if ($inputProfiles.Count -lt 1 -or $inputProfiles.Count -gt 2) {
    throw 'Worker profile input must contain one or two profiles.'
}

$profileAliases = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$profileWorkerKinds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$validatedProfiles = [Collections.Generic.List[object]]::new(2)
foreach ($profile in $inputProfiles) {
    $context = 'Worker profile input profile'
    Assert-ExactProperties `
        -Value $profile `
        -ExpectedProperties @(
            'profileAlias',
            'workerKind',
            'packageLockId',
            'profileGenerationId',
            'organizationBaseUri',
            'organizationName',
            'expectedOrganizationId',
            'authentication',
            'identity'
        ) `
        -Context $context

    $profileAlias = Get-RequiredString `
        -Value $profile `
        -PropertyName 'profileAlias' `
        -MaximumLength 128 `
        -Context $context
    $workerKind = Get-RequiredString `
        -Value $profile `
        -PropertyName 'workerKind' `
        -MaximumLength 64 `
        -Context $context
    $packageLockId = Get-RequiredString `
        -Value $profile `
        -PropertyName 'packageLockId' `
        -MaximumLength 128 `
        -Context $context
    $generationId = Get-RequiredString `
        -Value $profile `
        -PropertyName 'profileGenerationId' `
        -MaximumLength 128 `
        -Context $context
    if (-not (Test-SafeIdentifier -Value $profileAlias -MaximumLength 128) -or
        -not (Test-SafeIdentifier -Value $generationId -MaximumLength 128) -or
        -not $validatedWorkers.ContainsKey($workerKind) -or
        -not $profileAliases.Add($profileAlias) -or
        -not $profileWorkerKinds.Add($workerKind)) {
        throw 'Worker profile input contains an invalid or duplicate selector.'
    }

    $validatedWorker = $validatedWorkers[$workerKind]
    if (-not [string]::Equals(
            $packageLockId,
            $validatedWorker.PackageLockId,
            [StringComparison]::Ordinal)) {
        throw 'Worker profile package lock does not match the published worker.'
    }

    $organizationUri = Get-CanonicalOrganizationUri `
        -Value (Get-RequiredString `
            -Value $profile `
            -PropertyName 'organizationBaseUri' `
            -MaximumLength 2048 `
            -Context $context) `
        -Context "$context.organizationBaseUri"
    $organizationName = Get-RequiredString `
        -Value $profile `
        -PropertyName 'organizationName' `
        -MaximumLength 100 `
        -Context $context
    if (-not (Test-SafeIdentifier `
            -Value $organizationName `
            -MaximumLength 100 `
            -AllowDot $false)) {
        throw 'Worker profile organizationName is invalid.'
    }

    $expectedOrganizationId = Get-ValidatedExpectedOrganizationId `
        -Value (Get-RequiredString `
            -Value $profile `
            -PropertyName 'expectedOrganizationId' `
            -MaximumLength 36 `
            -Context $context) `
        -Context "$context.expectedOrganizationId"
    $authentication = Get-RequiredString `
        -Value $profile `
        -PropertyName 'authentication' `
        -MaximumLength 32 `
        -Context $context
    if ($authentication -cne 'ActiveDirectory' -and $authentication -cne 'Ifd') {
        throw 'Worker profile authentication mode is invalid.'
    }

    $identity = $profile.identity
    Assert-JsonObject -Value $identity -Context "$context.identity"
    $identityPropertyNames = @(
        $identity.PSObject.Properties | ForEach-Object { $_.Name }
    )
    if (-not ($identityPropertyNames -ccontains 'mode')) {
        throw 'Worker profile identity.mode is required.'
    }

    $identityMode = Get-RequiredString `
        -Value $identity `
        -PropertyName 'mode' `
        -MaximumLength 64 `
        -Context "$context.identity"
    $credentialReference = $null
    $homeRealm = $null
    switch ($identityMode) {
        'HostIdentity' {
            Assert-ExactProperties `
                -Value $identity `
                -ExpectedProperties @('mode') `
                -Context "$context.identity"
            if ($authentication -cne 'ActiveDirectory') {
                throw 'IFD profiles require a Windows credential reference.'
            }
        }
        'WindowsCredentialReference' {
            if ($authentication -ceq 'Ifd') {
                Assert-ExactProperties `
                    -Value $identity `
                    -ExpectedProperties @('mode', 'reference', 'homeRealm') `
                    -Context "$context.identity"
            }
            else {
                Assert-ExactProperties `
                    -Value $identity `
                    -ExpectedProperties @('mode', 'reference') `
                    -Context "$context.identity"
            }

            $credentialReference = Get-RequiredString `
                -Value $identity `
                -PropertyName 'reference' `
                -MaximumLength 256 `
                -Context "$context.identity"
            if (-not (Test-SafeIdentifier `
                    -Value $credentialReference `
                    -MaximumLength 256)) {
                throw 'Worker profile credential reference is not a safe identifier.'
            }

            if ($authentication -ceq 'Ifd') {
                $homeRealm = Get-ValidatedHomeRealm `
                    -Value (Get-RequiredString `
                        -Value $identity `
                        -PropertyName 'homeRealm' `
                        -MaximumLength 2048 `
                        -Context "$context.identity") `
                    -Context "$context.identity.homeRealm"
            }
        }
        default {
            throw 'Worker profile identity mode is invalid.'
        }
    }

    $workerProfilePath = Join-Path `
        $validatedWorker.ExecutableDirectory `
        'worker-profile.xml'
    $validatedProfiles.Add([pscustomobject]@{
        ProfileAlias = $profileAlias
        WorkerKind = $workerKind
        PackageLockId = $packageLockId
        GenerationId = $generationId
        OrganizationBaseUri = $organizationUri.Value
        HostName = $organizationUri.HostName
        Port = $organizationUri.Port
        OrganizationName = $organizationName
        ExpectedOrganizationId = $expectedOrganizationId
        Authentication = $authentication
        IdentityMode = $identityMode
        CredentialReference = $credentialReference
        HomeRealm = $homeRealm
        ExecutablePath = $validatedWorker.ExecutablePath
        ExecutableSha256 = $validatedWorker.ExecutableSha256
        WorkerProfilePath = $workerProfilePath
    })
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw 'OutputDirectory is required.'
}

try {
    $resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
}
catch {
    throw 'OutputDirectory is invalid.'
}

if (Test-Path -LiteralPath $resolvedOutputDirectory -PathType Leaf) {
    throw 'OutputDirectory must be a directory path.'
}

$overlayPath = Join-Path `
    $resolvedOutputDirectory `
    'dynamics-official-workers.gateway.json'
$targetPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($profile in $validatedProfiles) {
    if (-not $targetPaths.Add($profile.WorkerProfilePath) -or
        (Test-Path -LiteralPath $profile.WorkerProfilePath)) {
        throw 'A worker-profile.xml target already exists or is duplicated.'
    }
}

if (-not $targetPaths.Add($overlayPath) -or
    (Test-Path -LiteralPath $overlayPath)) {
    throw 'The Gateway overlay target already exists or is duplicated.'
}

$payloads = [Collections.Generic.List[object]]::new(3)
foreach ($profile in $validatedProfiles) {
    $xmlText = New-WorkerProfileXmlText -Profile $profile
    if ($utf8NoBom.GetByteCount($xmlText) -gt 64 * 1024) {
        throw 'Generated worker profile exceeds the runtime file-size bound.'
    }

    $payloads.Add([pscustomobject]@{
        TargetPath = $profile.WorkerProfilePath
        Content = $xmlText
    })
}

$payloads.Add([pscustomobject]@{
    TargetPath = $overlayPath
    Content = New-GatewayOverlayText -Profiles @($validatedProfiles)
})

Write-AtomicDeploymentFiles `
    -Payloads @($payloads) `
    -ResolvedOutputDirectory $resolvedOutputDirectory

$resultWorkers = @($validatedProfiles | ForEach-Object {
    [ordered]@{
        profileAlias = $_.ProfileAlias
        workerKind = $_.WorkerKind
        workerExecutablePath = $_.ExecutablePath
        workerExecutableSha256 = $_.ExecutableSha256
        workerProfilePath = $_.WorkerProfilePath
        packageLockId = $_.PackageLockId
        profileGenerationId = $_.GenerationId
    }
})
$result = [ordered]@{
    schemaVersion = 1
    outcome = 'provisioned'
    manifestPath = $manifestDocument.Path
    profileInputPath = $profileDocument.Path
    outputDirectory = $resolvedOutputDirectory
    overlayPath = $overlayPath
    featureGateMustRemainDisabled = $true
    workers = $resultWorkers
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6
}
else {
    [pscustomobject]$result
}
