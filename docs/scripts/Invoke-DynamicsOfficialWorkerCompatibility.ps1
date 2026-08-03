<#
.SYNOPSIS
以 Gateway 對已釘選官方 Dynamics Worker 執行單一 Phase 4C 相容性 identity probe。

.DESCRIPTION
此工具先驗證發行 manifest、Gateway deployment overlay、選取 Worker 的
worker-profile.xml 與實際 executable SHA-256 是否屬於同一不可變 generation。
只有在明確指定 -EnableLiveCompatibility 時，才會以目前 Windows 主機身分透過
受保護的 Gateway operation endpoint 發出一次 runtime.health.whoami；它不會
直接呼叫 CRM HTTP 端點、不會嘗試其他 CE 版本、profile、transport 或重試路徑。

-ValidateOnly 只做上述本機檔案身分驗證，完全不建立 HttpClient、socket、背景
工作、計時器或持久 Session。無論成功或失敗，工具都只輸出 allowlist 的
package lock、worker kind、profile generation、operation、結果與時間；不輸出
endpoint、檔案路徑、組織識別、credential reference、cookie、token、CRM body
或原始例外訊息。

Live request 的 handler、client、request、response、stream、CTS 與可變 byte
buffer 全部由這個單次命令擁有，並於 finally 依反向建立順序 Dispose/清零。這讓
使用預設 Windows 主機身分的短生命期連線不會跨 profile、操作者或命令保存可變
驗證狀態，也不會讓取消、逾時或失敗路徑遺留資源。

.PARAMETER ManifestPath
官方 Worker publish 流程輸出的 official-worker-manifest.json 絕對或相對路徑。

.PARAMETER GatewayOverlayPath
部署在最終 Gateway 目錄旁、由受控部署產生的 dynamics-official-workers.gateway.json。

.PARAMETER GatewayEndpoint
實際 Gateway 的 HTTPS base URI。它只在命令記憶體內使用，絕不寫入輸出或檔案。

.PARAMETER ProfileAlias
本次明確選取且已核准的 deployment profile alias；不接受 URI、路徑或任意 selector。

.PARAMETER ExpectedWorkerKind
必須與選取 profile 對應的 OfficialCrm82Worker 或 OfficialCrm91Worker。

.PARAMETER ValidateOnly
只驗證 deployment identity chain；此模式不會建立網路資源。

.PARAMETER EnableLiveCompatibility
明確 opt-in 後才以目前 Windows 主機身分執行一次受控 operation。

.NOTES
此工具提供 Phase 4C 的 identity 基礎證據，不能單獨宣告 CE 8.2 或 CE 9.1
完整相容性通過；讀取、分頁、metadata、test-owned write/rollback、recycle、
isolation 與資源 baseline 仍需依 operation matrix 分別完成。
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

    [switch] $EnableLiveCompatibility,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
Add-Type -AssemblyName System.Web.Extensions -ErrorAction Stop

$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$maximumManifestBytes = 256 * 1024
$maximumOverlayBytes = 128 * 1024
$maximumWorkerProfileBytes = 64 * 1024
$operationId = 'runtime.health.whoami'

function Get-ResolvedExistingFile {
    <#
    .SYNOPSIS
    解析並驗證有限大小的既有檔案，不將實際路徑回顯到錯誤。

    .DESCRIPTION
    manifest、overlay 與 XML 都是 deployment-owned 身分證據；本函式在讀檔前
    拒絕空值、無法正規化、非檔案與超出上限的輸入。FileInfo 只存於呼叫範圍，
    沒有長生命期 cache 或監看器；實際內容讀取者必須在自己的 finally 清理 byte
    array，讓每次命令有明確的單一資源 owner。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $DocumentName
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$DocumentName is unavailable."
    }

    try {
        $resolved = [IO.Path]::GetFullPath($Path)
    }
    catch {
        throw "$DocumentName is unavailable."
    }

    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$DocumentName is unavailable."
    }

    try {
        $file = Get-Item -LiteralPath $resolved -ErrorAction Stop
        if ($file.Length -lt 1 -or $file.Length -gt $MaximumBytes) {
            throw "$DocumentName is unavailable."
        }

        return $resolved
    }
    catch {
        throw "$DocumentName is unavailable."
    }
}

function Skip-JsonWhitespace {
    <#
    .SYNOPSIS
    移動 JSON scanner 到下一個非空白字元。

    .DESCRIPTION
    scanner 僅處理已受檔案大小上限約束的短生命期字串；index 以 ref 傳遞，
    沒有 script-scope cursor 或跨文件 parser cache，因此不同 profile 的輸入
    不會共用可變解析狀態。
    #>
    param(
        [string] $Text,
        [ref] $Index
    )

    while ($Index.Value -lt $Text.Length) {
        $character = $Text[$Index.Value]
        if ($character -ne ' ' -and $character -ne "`t" -and
            $character -ne "`r" -and $character -ne "`n") {
            return
        }

        $Index.Value++
    }
}

function Read-JsonPropertyName {
    <#
    .SYNOPSIS
    讀取 JSON string property name，保留足以偵測 duplicate 的 Unicode 值。

    .DESCRIPTION
    只在 object key 位置呼叫。控制字元、未完成 escape 與錯誤 Unicode escape
    一律停止，不嘗試修復輸入；StringBuilder 與回傳 key 都只在呼叫堆疊內存活，
    並由下一層 HashSet 當次比較後釋放。
    #>
    param(
        [string] $Text,
        [ref] $Index
    )

    if ($Index.Value -ge $Text.Length -or $Text[$Index.Value] -ne '"') {
        throw 'Invalid JSON property name.'
    }

    $Index.Value++
    $builder = [Text.StringBuilder]::new()
    try {
        while ($Index.Value -lt $Text.Length) {
            $character = $Text[$Index.Value]
            if ($character -eq '"') {
                $Index.Value++
                return $builder.ToString()
            }
            if ([int][char] $character -lt 0x20) {
                throw 'Invalid JSON property name.'
            }
            if ($character -ne '\\') {
                [void] $builder.Append($character)
                $Index.Value++
                continue
            }

            $Index.Value++
            if ($Index.Value -ge $Text.Length) {
                throw 'Invalid JSON property name.'
            }
            $escape = $Text[$Index.Value]
            switch ($escape) {
                '"' { [void] $builder.Append('"'); $Index.Value++; continue }
                '\\' { [void] $builder.Append('\\'); $Index.Value++; continue }
                '/' { [void] $builder.Append('/'); $Index.Value++; continue }
                'b' { [void] $builder.Append([char] 8); $Index.Value++; continue }
                'f' { [void] $builder.Append([char] 12); $Index.Value++; continue }
                'n' { [void] $builder.Append("`n"); $Index.Value++; continue }
                'r' { [void] $builder.Append("`r"); $Index.Value++; continue }
                't' { [void] $builder.Append("`t"); $Index.Value++; continue }
                'u' {
                    if ($Index.Value + 4 -ge $Text.Length) {
                        throw 'Invalid JSON property name.'
                    }
                    $hex = $Text.Substring($Index.Value + 1, 4)
                    [int] $codePoint = 0
                    if (-not [int]::TryParse(
                            $hex,
                            [Globalization.NumberStyles]::AllowHexSpecifier,
                            [Globalization.CultureInfo]::InvariantCulture,
                            [ref] $codePoint)) {
                        throw 'Invalid JSON property name.'
                    }
                    [void] $builder.Append([char] $codePoint)
                    $Index.Value += 5
                    continue
                }
                default { throw 'Invalid JSON property name.' }
            }
        }

        throw 'Invalid JSON property name.'
    }
    finally {
        $builder.Clear()
    }
}

function Read-JsonPrimitive {
    <#
    .SYNOPSIS
    驗證非容器 JSON scalar 的完整 token。

    .DESCRIPTION
    duplicate scanner 不需要 materialize scalar value，只需可靠跳過它以抵達
    下一個 property。限制 token 為 JSON literal 或完整數字，避免 scanner 因
    錯誤字元落後而把不同 object 的 key 誤判為同一層。
    #>
    param(
        [string] $Text,
        [ref] $Index
    )

    $start = $Index.Value
    while ($Index.Value -lt $Text.Length) {
        $character = $Text[$Index.Value]
        if ($character -eq ',' -or $character -eq '}' -or $character -eq ']' -or
            $character -eq ' ' -or $character -eq "`t" -or
            $character -eq "`r" -or $character -eq "`n") {
            break
        }

        $Index.Value++
    }
    if ($Index.Value -eq $start) {
        throw 'Invalid JSON scalar.'
    }

    $token = $Text.Substring($start, $Index.Value - $start)
    if ($token -cne 'true' -and $token -cne 'false' -and $token -cne 'null' -and
        -not [Regex]::IsMatch(
            $token,
            '\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z')) {
        throw 'Invalid JSON scalar.'
    }
}

function Read-JsonValueForDuplicateCheck {
    <#
    .SYNOPSIS
    遞迴走訪單一 JSON value，並將 object 交給 duplicate checker。

    .DESCRIPTION
    遞迴深度依輸入檔案既有大小上限自然受限；每層只持有自己的 HashSet，不把
    value 寫入集合。所有 path 都必須消耗一個完整 JSON value，否則 caller 會
    將文件視為 invalid 而不會進入 deployment preflight。
    #>
    param(
        [string] $Text,
        [ref] $Index,
        [int] $Depth
    )

    if ($Depth -gt 32) {
        throw 'JSON nesting is invalid.'
    }
    Skip-JsonWhitespace -Text $Text -Index $Index
    if ($Index.Value -ge $Text.Length) {
        throw 'Invalid JSON value.'
    }

    switch ($Text[$Index.Value]) {
        '{' { Read-JsonObjectForDuplicateCheck -Text $Text -Index $Index -Depth $Depth; return }
        '[' { Read-JsonArrayForDuplicateCheck -Text $Text -Index $Index -Depth $Depth; return }
        '"' { [void] (Read-JsonPropertyName -Text $Text -Index $Index); return }
        default { Read-JsonPrimitive -Text $Text -Index $Index; return }
    }
}

function Read-JsonObjectForDuplicateCheck {
    <#
    .SYNOPSIS
    驗證一個 JSON object 的 grammar 並拒絕同層 duplicate key。

    .DESCRIPTION
    每個 object 建立 Ordinal HashSet，因為 deployment configuration 對大小寫
    敏感且不能接受 case collision。HashSet 僅在此層解析期間存在；finally
    清除其 bucket 引用，避免大型輸入在長時間互動 shell 中不必要地保留 key。
    #>
    param(
        [string] $Text,
        [ref] $Index,
        [int] $Depth
    )

    if ($Text[$Index.Value] -ne '{') {
        throw 'Invalid JSON object.'
    }
    $Index.Value++
    Skip-JsonWhitespace -Text $Text -Index $Index
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    try {
        if ($Index.Value -lt $Text.Length -and $Text[$Index.Value] -eq '}') {
            $Index.Value++
            return
        }

        while ($true) {
            Skip-JsonWhitespace -Text $Text -Index $Index
            $name = Read-JsonPropertyName -Text $Text -Index $Index
            if (-not $names.Add($name)) {
                throw 'Duplicate JSON property.'
            }
            Skip-JsonWhitespace -Text $Text -Index $Index
            if ($Index.Value -ge $Text.Length -or $Text[$Index.Value] -ne ':') {
                throw 'Invalid JSON object.'
            }
            $Index.Value++
            Read-JsonValueForDuplicateCheck -Text $Text -Index $Index -Depth ($Depth + 1)
            Skip-JsonWhitespace -Text $Text -Index $Index
            if ($Index.Value -ge $Text.Length) {
                throw 'Invalid JSON object.'
            }
            if ($Text[$Index.Value] -eq '}') {
                $Index.Value++
                return
            }
            if ($Text[$Index.Value] -ne ',') {
                throw 'Invalid JSON object.'
            }
            $Index.Value++
        }
    }
    finally {
        $names.Clear()
    }
}

function Read-JsonArrayForDuplicateCheck {
    <#
    .SYNOPSIS
    驗證 JSON array grammar，並遞迴檢查每個 element object。

    .DESCRIPTION
    array 不建立 element collection；它逐一走訪後立即遞迴返回，讓額外記憶體
    與輸入大小成常數比例。這在 manifest workers array 與 deployment profiles
    都可避免將未選取內容長時間保存。
    #>
    param(
        [string] $Text,
        [ref] $Index,
        [int] $Depth
    )

    if ($Text[$Index.Value] -ne '[') {
        throw 'Invalid JSON array.'
    }
    $Index.Value++
    Skip-JsonWhitespace -Text $Text -Index $Index
    if ($Index.Value -lt $Text.Length -and $Text[$Index.Value] -eq ']') {
        $Index.Value++
        return
    }

    while ($true) {
        Read-JsonValueForDuplicateCheck -Text $Text -Index $Index -Depth ($Depth + 1)
        Skip-JsonWhitespace -Text $Text -Index $Index
        if ($Index.Value -ge $Text.Length) {
            throw 'Invalid JSON array.'
        }
        if ($Text[$Index.Value] -eq ']') {
            $Index.Value++
            return
        }
        if ($Text[$Index.Value] -ne ',') {
            throw 'Invalid JSON array.'
        }
        $Index.Value++
        Skip-JsonWhitespace -Text $Text -Index $Index
    }
}

function Assert-NoDuplicateJsonProperties {
    <#
    .SYNOPSIS
    在 ConvertFrom-Json 前完整掃描輸入，拒絕任何 object duplicate property。

    .DESCRIPTION
    Windows PowerShell 對某些同名欄位會採最後值，這對 deployment identity
    chain 是不可接受的歧義。此 gate 先執行 deterministic scanner，保證 parser
    看見 JSON 前已排除同層覆寫；它不輸出 key、value 或文件內容。
    #>
    param(
        [string] $Text,
        [string] $DocumentName
    )

    try {
        $index = 0
        Read-JsonValueForDuplicateCheck -Text $Text -Index ([ref] $index) -Depth 0
        Skip-JsonWhitespace -Text $Text -Index ([ref] $index)
        if ($index -ne $Text.Length) {
            throw 'Trailing JSON content.'
        }
    }
    catch {
        throw "$DocumentName is invalid."
    }
}

function Read-StrictJsonObject {
    <#
    .SYNOPSIS
    以嚴格 UTF-8、檔案大小與 PowerShell duplicate-property 防護讀取 JSON object。

    .DESCRIPTION
    ConvertFrom-Json 會拒絕重複或 case-colliding property；本函式先使用 strict
    UTF-8 decoder，因此不會把無效位元組轉成替代字元後繼續推測設定。可變 byte
    array 在 finally 清零，文字與解析物件只回傳必要的 JSON object，絕不被寫入
    記錄、環境變數或跨命令靜態欄位。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $DocumentName
    )

    $bytes = $null
    $text = $null
    try {
        $resolved = Get-ResolvedExistingFile `
            -Path $Path `
            -MaximumBytes $MaximumBytes `
            -DocumentName $DocumentName
        $bytes = [IO.File]::ReadAllBytes($resolved)
        $text = $strictUtf8.GetString($bytes)
        Assert-NoDuplicateJsonProperties -Text $text -DocumentName $DocumentName
        try {
            $value = $text | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "$DocumentName is invalid."
        }

        if ($null -eq $value -or $value -isnot [Management.Automation.PSCustomObject]) {
            throw "$DocumentName is invalid."
        }

        return [pscustomobject]@{
            Path = $resolved
            Value = $value
        }
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }

        $text = $null
    }
}

function Assert-ExactJsonProperties {
    <#
    .SYNOPSIS
    確認 deployment JSON object 僅含本工具認可的欄位。

    .DESCRIPTION
    profile 與 manifest 不是任意擴充點；未知欄位可能代表未經審核的 routing 或
    secret material。此函式使用 Ordinal case-sensitive 對照並拒絕缺失、重複或
    多餘欄位，讓工具在建立任何 Live HTTP 資源前 fail-closed。
    #>
    param(
        [object] $Value,
        [string[]] $ExpectedProperties,
        [string] $Context
    )

    if ($null -eq $Value -or $Value -isnot [Management.Automation.PSCustomObject]) {
        throw "$Context is invalid."
    }

    $actual = @($Value.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actual.Count -ne $ExpectedProperties.Count) {
        throw "$Context is invalid."
    }

    $expectedSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $actualSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($name in $ExpectedProperties) {
        [void] $expectedSet.Add($name)
    }
    foreach ($name in $actual) {
        if (-not $actualSet.Add($name) -or -not $expectedSet.Contains($name)) {
            throw "$Context is invalid."
        }
    }
    foreach ($name in $ExpectedProperties) {
        if (-not $actualSet.Contains($name)) {
            throw "$Context is invalid."
        }
    }
}

function Get-RequiredJsonString {
    <#
    .SYNOPSIS
    從已驗證 object 取得有長度上限的 non-empty string。

    .DESCRIPTION
    這個 helper 不將原始值放進錯誤訊息。它避免使用者或遭竄改的 deployment
    檔案把超長字串傳入 URI、路徑或輸出物件；回傳字串僅留在當前 preflight call
    stack，完成後由 script process 結束回收。
    #>
    param(
        [object] $Value,
        [string] $PropertyName,
        [int] $MaximumLength,
        [string] $Context
    )

    $property = @($Value.PSObject.Properties |
        Where-Object { $_.Name -ceq $PropertyName })
    if ($property.Count -ne 1 -or $property[0].Value -isnot [string]) {
        throw "$Context is invalid."
    }

    $text = $property[0].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text.Length -gt $MaximumLength) {
        throw "$Context is invalid."
    }

    return $text
}

function Get-RequiredJsonInteger {
    <#
    .SYNOPSIS
    取得嚴格整數欄位並套用指定安全範圍。

    .DESCRIPTION
    artifact 長度與 schema version 必須是可比較的整數，不接受浮點、字串或
    無界值。這避免後續大小檢查溢位或因型別猜測而略過 fail-closed boundary。
    #>
    param(
        [object] $Value,
        [string] $PropertyName,
        [long] $Minimum,
        [long] $Maximum,
        [string] $Context
    )

    $property = @($Value.PSObject.Properties |
        Where-Object { $_.Name -ceq $PropertyName })
    if ($property.Count -ne 1) {
        throw "$Context is invalid."
    }

    try {
        $number = [Convert]::ToInt64(
            $property[0].Value,
            [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "$Context is invalid."
    }

    if ($number -lt $Minimum -or $number -gt $Maximum) {
        throw "$Context is invalid."
    }

    return $number
}

function Get-RequiredJsonBoolean {
    <#
    .SYNOPSIS
    取得不可被字串或數字替代的 JSON boolean。

    .DESCRIPTION
    feature gate 的 false/true 語意不得依賴 PowerShell truthiness；只有實際
    Boolean 才可通過，避免遭竄改的字串被隱式轉換後放寬目前關閉的產品功能。
    #>
    param(
        [object] $Value,
        [string] $PropertyName,
        [string] $Context
    )

    $property = @($Value.PSObject.Properties |
        Where-Object { $_.Name -ceq $PropertyName })
    if ($property.Count -ne 1 -or $property[0].Value -isnot [bool]) {
        throw "$Context is invalid."
    }

    return [bool] $property[0].Value
}

function Test-SafeIdentifier {
    <#
    .SYNOPSIS
    驗證 alias、generation 與 package lock 使用固定安全字元集合。

    .DESCRIPTION
    這些值會進入 map lookup 或 URI path segment；僅允許 ASCII 英數、dot、dash
    與 underscore，且不允許 path separator、whitespace 或控制字元。此驗證是
    純計算，沒有 I/O、cache 或共享可變狀態。
    #>
    param(
        [string] $Value,
        [int] $MaximumLength
    )

    return $Value.Length -le $MaximumLength -and
        [Regex]::IsMatch($Value, '\A[A-Za-z0-9][A-Za-z0-9._-]*\z')
}

function Assert-Sha256 {
    <#
    .SYNOPSIS
    確認 hash 為固定 64 位十六進位格式。

    .DESCRIPTION
    這只驗證 syntax；呼叫端仍會對實際 executable 計算 SHA-256 並使用
    OrdinalIgnoreCase 比較。兩層檢查避免 malformed hash 造成不明確回退。
    #>
    param(
        [string] $Value,
        [string] $Context
    )

    if (-not [Regex]::IsMatch($Value, '\A[0-9A-Fa-f]{64}\z')) {
        throw "$Context is invalid."
    }
}

function Get-ValidatedHttpsUri {
    <#
    .SYNOPSIS
    驗證沒有 user-info、query 或 fragment 的絕對 HTTPS URI。

    .DESCRIPTION
    Gateway target 與 organization base URI 都只能由 deployment operator 提供，
    但仍須拒絕可隱藏路由或身分資料的 URI 元件。回傳 Uri 只用於本次比較或
    request 建構，既不輸出也不儲存；這避免 endpoint 在證據或 Session 中殘留。
    #>
    param(
        [string] $Value,
        [string] $Context
    )

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -or
        $uri.Scheme -cne 'https' -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "$Context is invalid."
    }

    return $uri
}

function Get-ValidatedOrganizationId {
    <#
    .SYNOPSIS
    驗證 expected organization GUID 不是空白或簡單 placeholder。

    .DESCRIPTION
    所有 byte 相同的 GUID 是常見 placeholder，會讓 profile 表面上具備身分
    卻無法證明目標。byte array 在 finally 清零；回傳的小寫 canonical GUID
    僅存在於 preflight 結果，並不會輸出到相容性證據。
    #>
    param(
        [string] $Value,
        [string] $Context
    )

    $guid = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref] $guid) -or $guid -eq [Guid]::Empty) {
        throw "$Context is invalid."
    }

    $bytes = $guid.ToByteArray()
    try {
        if ((@($bytes | Select-Object -Unique)).Count -eq 1) {
            throw "$Context is invalid."
        }

        return $guid.ToString('D')
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Resolve-ManifestWorkerArtifact {
    <#
    .SYNOPSIS
    將 manifest 的相對 executable 項目解析為同一 publish 根目錄下的檔案。

    .DESCRIPTION
    相對路徑不可跳出 manifest directory，也不可改用另一個 executable 名稱。
    解析後會立刻比較檔案長度與 SHA-256；任何 mismatch 都在 Gateway 或 Worker
    尚未啟動前失敗。FileInfo 與 hash 只存於此呼叫範圍，沒有全域 artifact cache。
    #>
    param(
        [string] $ManifestDirectory,
        [object] $Worker,
        [string] $ExpectedWorkerKind
    )

    Assert-ExactJsonProperties -Value $Worker -ExpectedProperties @(
        'workerKind',
        'ceVersion',
        'packageLockId',
        'packageLockSha256',
        'relativeExecutablePath',
        'sha256',
        'executableBytes',
        'artifactFileCount',
        'artifactTotalBytes'
    ) -Context 'Official worker manifest entry'

    $workerKind = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'workerKind' -MaximumLength 64 `
        -Context 'Official worker manifest entry'
    if ($workerKind -cne $ExpectedWorkerKind) {
        throw 'Official worker manifest entry is invalid.'
    }

    $expected = switch ($ExpectedWorkerKind) {
        'OfficialCrm82Worker' {
            [pscustomobject]@{
                CeVersion = '8.2'
                ExecutableName = 'SpeechMessage.Dynamics.Crm82Worker.exe'
            }
        }
        'OfficialCrm91Worker' {
            [pscustomobject]@{
                CeVersion = '9.1'
                ExecutableName = 'SpeechMessage.Dynamics.Crm91Worker.exe'
            }
        }
        default {
            throw 'Official worker manifest entry is invalid.'
        }
    }

    $ceVersion = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'ceVersion' -MaximumLength 8 `
        -Context 'Official worker manifest entry'
    if ($ceVersion -cne $expected.CeVersion) {
        throw 'Official worker manifest entry is invalid.'
    }

    $packageLockId = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'packageLockId' -MaximumLength 128 `
        -Context 'Official worker manifest entry'
    if (-not (Test-SafeIdentifier -Value $packageLockId -MaximumLength 128)) {
        throw 'Official worker manifest entry is invalid.'
    }

    $packageLockSha256 = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'packageLockSha256' -MaximumLength 64 `
        -Context 'Official worker manifest entry'
    Assert-Sha256 -Value $packageLockSha256 -Context 'Official worker manifest entry'

    $relativeExecutablePath = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'relativeExecutablePath' -MaximumLength 512 `
        -Context 'Official worker manifest entry'
    if ([IO.Path]::IsPathRooted($relativeExecutablePath) -or
        $relativeExecutablePath.Contains('..') -or
        $relativeExecutablePath.Contains(':')) {
        throw 'Official worker manifest entry is invalid.'
    }

    try {
        $candidate = [IO.Path]::GetFullPath((Join-Path `
            $ManifestDirectory `
            ($relativeExecutablePath -replace '/', '\\')))
    }
    catch {
        throw 'Official worker manifest entry is invalid.'
    }

    $prefix = $ManifestDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [IO.Path]::GetFileName($candidate),
            $expected.ExecutableName,
            [StringComparison]::Ordinal)) {
        throw 'Official worker manifest entry is invalid.'
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'Official worker artifact is unavailable.'
    }

    $expectedSha256 = Get-RequiredJsonString `
        -Value $Worker -PropertyName 'sha256' -MaximumLength 64 `
        -Context 'Official worker manifest entry'
    Assert-Sha256 -Value $expectedSha256 -Context 'Official worker manifest entry'
    $expectedBytes = Get-RequiredJsonInteger `
        -Value $Worker -PropertyName 'executableBytes' -Minimum 1 -Maximum ([long]::MaxValue) `
        -Context 'Official worker manifest entry'
    [void] (Get-RequiredJsonInteger `
        -Value $Worker -PropertyName 'artifactFileCount' -Minimum 1 -Maximum 100000 `
        -Context 'Official worker manifest entry')
    $artifactTotalBytes = Get-RequiredJsonInteger `
        -Value $Worker -PropertyName 'artifactTotalBytes' -Minimum $expectedBytes -Maximum ([long]::MaxValue) `
        -Context 'Official worker manifest entry'
    if ($artifactTotalBytes -lt $expectedBytes) {
        throw 'Official worker manifest entry is invalid.'
    }

    try {
        $file = Get-Item -LiteralPath $candidate -ErrorAction Stop
        if ($file.Length -ne $expectedBytes) {
            throw 'Official worker artifact is invalid.'
        }

        $actualSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
        if (-not [string]::Equals(
                $actualSha256,
                $expectedSha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Official worker artifact is invalid.'
        }
    }
    catch {
        throw 'Official worker artifact is invalid.'
    }

    return [pscustomobject]@{
        WorkerKind = $workerKind
        CeVersion = $ceVersion
        PackageLockId = $packageLockId
        ExecutablePath = $candidate
        ExecutableSha256 = $expectedSha256
    }
}

function Get-ManifestWorker {
    <#
    .SYNOPSIS
    驗證完整雙 Worker publish manifest，並取出本次明確選取的 artifact。

    .DESCRIPTION
    publish manifest 必須同時列出 CE 8.2 與 CE 9.1，確保單 profile deployment
    不會降低已發布 artifact 的 package-lock/hash 邊界；但此 command 僅使用呼叫
    端選取的 worker，不會啟動或載入另一版本。所有選擇均以固定 kind 進行，沒有
    自動版本偵測、替代 transport 或 fallback。
    #>
    param(
        [object] $ManifestDocument,
        [string] $ExpectedWorkerKind
    )

    $manifest = $ManifestDocument.Value
    Assert-ExactJsonProperties -Value $manifest -ExpectedProperties @(
        'schemaVersion',
        'generatedAtUtc',
        'configuration',
        'targetFramework',
        'protocolVersion',
        'featureGateMustRemainDisabled',
        'outputRoot',
        'workers'
    ) -Context 'Official worker manifest'
    if ((Get-RequiredJsonInteger `
            -Value $manifest -PropertyName 'schemaVersion' -Minimum 1 -Maximum 1 `
            -Context 'Official worker manifest') -ne 1 -or
        (Get-RequiredJsonString `
            -Value $manifest -PropertyName 'configuration' -MaximumLength 16 `
            -Context 'Official worker manifest') -cne 'Release' -or
        (Get-RequiredJsonString `
            -Value $manifest -PropertyName 'targetFramework' -MaximumLength 16 `
            -Context 'Official worker manifest') -cne 'net48' -or
        (Get-RequiredJsonInteger `
            -Value $manifest -PropertyName 'protocolVersion' -Minimum 1 -Maximum 1 `
            -Context 'Official worker manifest') -ne 1 -or
        -not (Get-RequiredJsonBoolean `
            -Value $manifest -PropertyName 'featureGateMustRemainDisabled' `
            -Context 'Official worker manifest')) {
        throw 'Official worker manifest is invalid.'
    }

    [void] (Get-RequiredJsonString `
        -Value $manifest -PropertyName 'generatedAtUtc' -MaximumLength 64 `
        -Context 'Official worker manifest')
    $outputRoot = Get-RequiredJsonString `
        -Value $manifest -PropertyName 'outputRoot' -MaximumLength 2048 `
        -Context 'Official worker manifest'
    try {
        $manifestDirectory = Split-Path -Parent $ManifestDocument.Path
        $resolvedOutputRoot = [IO.Path]::GetFullPath($outputRoot)
    }
    catch {
        throw 'Official worker manifest is invalid.'
    }
    if (-not [string]::Equals(
            $resolvedOutputRoot,
            $manifestDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Official worker manifest is invalid.'
    }

    $workers = @($manifest.workers)
    if ($workers.Count -ne 2) {
        throw 'Official worker manifest is invalid.'
    }

    $seenKinds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $selectedWorker = $null
    foreach ($worker in $workers) {
        if ($worker -isnot [Management.Automation.PSCustomObject]) {
            throw 'Official worker manifest is invalid.'
        }

        $kindProperty = @($worker.PSObject.Properties |
            Where-Object { $_.Name -ceq 'workerKind' })
        if ($kindProperty.Count -ne 1 -or $kindProperty[0].Value -isnot [string]) {
            throw 'Official worker manifest is invalid.'
        }
        $kind = $kindProperty[0].Value
        if (-not $seenKinds.Add($kind) -or
            ($kind -cne 'OfficialCrm82Worker' -and $kind -cne 'OfficialCrm91Worker')) {
            throw 'Official worker manifest is invalid.'
        }

        if ($kind -ceq $ExpectedWorkerKind) {
            $selectedWorker = $worker
        }
    }
    if ($seenKinds.Count -ne 2 -or $null -eq $selectedWorker) {
        throw 'Official worker manifest is invalid.'
    }

    return Resolve-ManifestWorkerArtifact `
        -ManifestDirectory $manifestDirectory `
        -Worker $selectedWorker `
        -ExpectedWorkerKind $ExpectedWorkerKind
}

function Get-SelectedOverlayProfile {
    <#
    .SYNOPSIS
    從 Gateway overlay 選取唯一且完整對應的 profile。

    .DESCRIPTION
    overlay 是 Gateway startup 的固定 snapshot。此函式不猜測 profile、不接受
    case-insensitive substitute，也不從 checked-in appsettings 補值；選取項目必須
    完整對應 manifest 的 kind、path、hash 與 package lock，否則停止於本機
    preflight，完全不建立 HTTP 資源。
    #>
    param(
        [object] $OverlayDocument,
        [string] $ProfileAlias,
        [object] $ManifestWorker
    )

    $overlay = $OverlayDocument.Value
    Assert-ExactJsonProperties -Value $overlay -ExpectedProperties @('DynamicsProfiles') `
        -Context 'Gateway deployment overlay'
    $dynamicsProfiles = @($overlay.PSObject.Properties |
        Where-Object { $_.Name -ceq 'DynamicsProfiles' })[0].Value
    Assert-ExactJsonProperties -Value $dynamicsProfiles -ExpectedProperties @('Profiles') `
        -Context 'Gateway deployment overlay'
    $profiles = @($dynamicsProfiles.PSObject.Properties |
        Where-Object { $_.Name -ceq 'Profiles' })[0].Value
    if ($profiles -isnot [Management.Automation.PSCustomObject]) {
        throw 'Gateway deployment overlay is invalid.'
    }

    $matches = @($profiles.PSObject.Properties |
        Where-Object { $_.Name -ceq $ProfileAlias })
    if ($matches.Count -ne 1) {
        throw 'Gateway deployment overlay is invalid.'
    }

    $profile = $matches[0].Value
    Assert-ExactJsonProperties -Value $profile -ExpectedProperties @(
        'WorkerProfileGenerationId',
        'WorkerKind',
        'WorkerExecutablePath',
        'WorkerExecutableSha256',
        'PackageLockId',
        'OrganizationBaseUri',
        'Admission'
    ) -Context 'Gateway deployment profile'
    $generationId = Get-RequiredJsonString `
        -Value $profile -PropertyName 'WorkerProfileGenerationId' -MaximumLength 128 `
        -Context 'Gateway deployment profile'
    if (-not (Test-SafeIdentifier -Value $generationId -MaximumLength 128)) {
        throw 'Gateway deployment profile is invalid.'
    }
    $workerKind = Get-RequiredJsonString `
        -Value $profile -PropertyName 'WorkerKind' -MaximumLength 64 `
        -Context 'Gateway deployment profile'
    $packageLockId = Get-RequiredJsonString `
        -Value $profile -PropertyName 'PackageLockId' -MaximumLength 128 `
        -Context 'Gateway deployment profile'
    $executablePath = Get-RequiredJsonString `
        -Value $profile -PropertyName 'WorkerExecutablePath' -MaximumLength 2048 `
        -Context 'Gateway deployment profile'
    $executableSha256 = Get-RequiredJsonString `
        -Value $profile -PropertyName 'WorkerExecutableSha256' -MaximumLength 64 `
        -Context 'Gateway deployment profile'
    Assert-Sha256 -Value $executableSha256 -Context 'Gateway deployment profile'
    [void] (Get-ValidatedHttpsUri `
        -Value (Get-RequiredJsonString `
            -Value $profile -PropertyName 'OrganizationBaseUri' -MaximumLength 2048 `
            -Context 'Gateway deployment profile') `
        -Context 'Gateway deployment profile')

    $admission = @($profile.PSObject.Properties |
        Where-Object { $_.Name -ceq 'Admission' })[0].Value
    Assert-ExactJsonProperties -Value $admission -ExpectedProperties @('ExpectedOrganizationId') `
        -Context 'Gateway deployment profile admission'
    $organizationId = Get-ValidatedOrganizationId `
        -Value (Get-RequiredJsonString `
            -Value $admission -PropertyName 'ExpectedOrganizationId' -MaximumLength 36 `
            -Context 'Gateway deployment profile admission') `
        -Context 'Gateway deployment profile admission'

    try {
        $resolvedExecutablePath = [IO.Path]::GetFullPath($executablePath)
    }
    catch {
        throw 'Gateway deployment profile is invalid.'
    }
    if ($workerKind -cne $ManifestWorker.WorkerKind -or
        $packageLockId -cne $ManifestWorker.PackageLockId -or
        -not [string]::Equals(
            $resolvedExecutablePath,
            $ManifestWorker.ExecutablePath,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            $executableSha256,
            $ManifestWorker.ExecutableSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Gateway deployment profile is invalid.'
    }

    return [pscustomobject]@{
        ProfileAlias = $ProfileAlias
        WorkerKind = $workerKind
        PackageLockId = $packageLockId
        GenerationId = $generationId
        ExpectedOrganizationId = $organizationId
        ExecutablePath = $ManifestWorker.ExecutablePath
    }
}

function Assert-ExactXmlAttributes {
    <#
    .SYNOPSIS
    拒絕 worker XML element 的未知、缺失或重複屬性。

    .DESCRIPTION
    XML profile 是 Worker 讀取 authentication/identity 結構的唯一 deployment
    輸入；未被明確允許的 attribute 可能改變信任邊界。本檢查只比較 element
    metadata，不載入外部 DTD、schema 或網路資源，且不回顯 attribute value。
    #>
    param(
        [System.Xml.XmlElement] $Element,
        [string[]] $ExpectedAttributes,
        [string] $Context
    )

    if ($Element.Attributes.Count -ne $ExpectedAttributes.Count) {
        throw "$Context is invalid."
    }

    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $ExpectedAttributes) {
        [void] $expected.Add($name)
    }
    foreach ($attribute in $Element.Attributes) {
        if (-not $expected.Contains($attribute.Name)) {
            throw "$Context is invalid."
        }
    }
    foreach ($name in $ExpectedAttributes) {
        if ($null -eq $Element.GetAttributeNode($name)) {
            throw "$Context is invalid."
        }
    }
}

function Get-RequiredXmlAttribute {
    <#
    .SYNOPSIS
    讀取具長度上限的 XML attribute，且永不回顯原始值。

    .DESCRIPTION
    這與 JSON string gate 使用相同的 bounded 觀念：XML 值不可把超長內容帶入
    URI、identity 驗證或長生命期 error state。回傳內容只供當前 XML preflight
    比較，最後不會被加入輸出 evidence。
    #>
    param(
        [System.Xml.XmlElement] $Element,
        [string] $Name,
        [int] $MaximumLength,
        [string] $Context
    )

    $attribute = $Element.GetAttributeNode($Name)
    if ($null -eq $attribute) {
        throw "$Context is invalid."
    }

    $value = $attribute.Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $MaximumLength) {
        throw "$Context is invalid."
    }

    return $value
}

function Assert-SelectedWorkerProfile {
    <#
    .SYNOPSIS
    驗證選取 Worker 旁的 XML profile 與 overlay/manifest 為同一 generation。

    .DESCRIPTION
    XML 以 DTD 禁用、resolver=null、大小/UTF-8/CRLF 限制讀取，防止外部 entity、
    不受限檔案或編碼推測。identity union 僅驗證 mode/reference/home realm 的結構，
    不會讀取 credential 本體。XmlReader、MemoryStream 與可變 bytes 都在 finally
    釋放或清零，使每次 preflight 沒有可跨 Session 或 profile 重用的 parser state。
    #>
    param([object] $OverlayProfile)

    $workerDirectory = Split-Path -Parent $OverlayProfile.ExecutablePath
    $workerProfilePath = Join-Path $workerDirectory 'worker-profile.xml'
    $bytes = $null
    $stream = $null
    $reader = $null
    $document = $null
    try {
        $resolvedProfilePath = Get-ResolvedExistingFile `
            -Path $workerProfilePath `
            -MaximumBytes $maximumWorkerProfileBytes `
            -DocumentName 'Official worker profile'
        $bytes = [IO.File]::ReadAllBytes($resolvedProfilePath)
        if ($bytes.Length -ge 3 -and
            $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            throw 'Official worker profile is invalid.'
        }
        $text = $strictUtf8.GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) {
            throw 'Official worker profile is invalid.'
        }
        $text = $null

        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = $maximumWorkerProfileBytes
        $settings.MaxCharactersFromEntities = 0
        $settings.CloseInput = $false
        $stream = [IO.MemoryStream]::new($bytes, $false)
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)

        $root = $document.DocumentElement
        if ($null -eq $root -or $root.Name -cne 'officialDynamicsWorkerProfiles') {
            throw 'Official worker profile is invalid.'
        }
        Assert-ExactXmlAttributes -Element $root -ExpectedAttributes @('version') `
            -Context 'Official worker profile'
        if ((Get-RequiredXmlAttribute `
                -Element $root -Name 'version' -MaximumLength 8 `
                -Context 'Official worker profile') -cne '1' -or
            @($root.SelectNodes('*')).Count -ne 1) {
            throw 'Official worker profile is invalid.'
        }

        $profileNodes = @($root.SelectNodes('./profile'))
        if ($profileNodes.Count -ne 1) {
            throw 'Official worker profile is invalid.'
        }
        $profile = [System.Xml.XmlElement] $profileNodes[0]
        Assert-ExactXmlAttributes -Element $profile -ExpectedAttributes @(
            'generationId', 'workerKind', 'packageLockId'
        ) -Context 'Official worker profile'
        if ((Get-RequiredXmlAttribute `
                -Element $profile -Name 'generationId' -MaximumLength 128 `
                -Context 'Official worker profile') -cne $OverlayProfile.GenerationId -or
            (Get-RequiredXmlAttribute `
                -Element $profile -Name 'workerKind' -MaximumLength 64 `
                -Context 'Official worker profile') -cne $OverlayProfile.WorkerKind -or
            (Get-RequiredXmlAttribute `
                -Element $profile -Name 'packageLockId' -MaximumLength 128 `
                -Context 'Official worker profile') -cne $OverlayProfile.PackageLockId -or
            @($profile.SelectNodes('*')).Count -ne 2) {
            throw 'Official worker profile is invalid.'
        }

        $organizationNodes = @($profile.SelectNodes('./organization'))
        $identityNodes = @($profile.SelectNodes('./identity'))
        if ($organizationNodes.Count -ne 1 -or $identityNodes.Count -ne 1) {
            throw 'Official worker profile is invalid.'
        }
        $organization = [System.Xml.XmlElement] $organizationNodes[0]
        Assert-ExactXmlAttributes -Element $organization -ExpectedAttributes @(
            'hostName', 'port', 'name', 'expectedOrganizationId', 'useSsl', 'authentication'
        ) -Context 'Official worker profile organization'
        $hostName = Get-RequiredXmlAttribute `
            -Element $organization -Name 'hostName' -MaximumLength 253 `
            -Context 'Official worker profile organization'
        $port = Get-RequiredXmlAttribute `
            -Element $organization -Name 'port' -MaximumLength 5 `
            -Context 'Official worker profile organization'
        $organizationName = Get-RequiredXmlAttribute `
            -Element $organization -Name 'name' -MaximumLength 100 `
            -Context 'Official worker profile organization'
        $organizationId = Get-ValidatedOrganizationId `
            -Value (Get-RequiredXmlAttribute `
                -Element $organization -Name 'expectedOrganizationId' -MaximumLength 36 `
                -Context 'Official worker profile organization') `
            -Context 'Official worker profile organization'
        $authentication = Get-RequiredXmlAttribute `
            -Element $organization -Name 'authentication' -MaximumLength 32 `
            -Context 'Official worker profile organization'
        [int] $parsedPort = 0
        if (-not [int]::TryParse(
                $port,
                [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref] $parsedPort) -or
            $parsedPort -lt 1 -or $parsedPort -gt 65535 -or
            -not (Test-SafeIdentifier -Value $organizationName -MaximumLength 100) -or
            [string]::IsNullOrWhiteSpace($hostName) -or
            (Get-RequiredXmlAttribute `
                -Element $organization -Name 'useSsl' -MaximumLength 5 `
                -Context 'Official worker profile organization') -cne 'true' -or
            ($authentication -cne 'ActiveDirectory' -and $authentication -cne 'Ifd') -or
            $organizationId -cne $OverlayProfile.ExpectedOrganizationId) {
            throw 'Official worker profile organization is invalid.'
        }

        $identity = [System.Xml.XmlElement] $identityNodes[0]
        $identityMode = Get-RequiredXmlAttribute `
            -Element $identity -Name 'mode' -MaximumLength 64 `
            -Context 'Official worker profile identity'
        switch ($identityMode) {
            'HostIdentity' {
                Assert-ExactXmlAttributes -Element $identity -ExpectedAttributes @('mode') `
                    -Context 'Official worker profile identity'
                if ($authentication -cne 'ActiveDirectory') {
                    throw 'Official worker profile identity is invalid.'
                }
            }
            'WindowsCredentialReference' {
                if ($authentication -ceq 'Ifd') {
                    Assert-ExactXmlAttributes `
                        -Element $identity `
                        -ExpectedAttributes @('mode', 'reference', 'homeRealm') `
                        -Context 'Official worker profile identity'
                    [void] (Get-ValidatedHttpsUri `
                        -Value (Get-RequiredXmlAttribute `
                            -Element $identity -Name 'homeRealm' -MaximumLength 2048 `
                            -Context 'Official worker profile identity') `
                        -Context 'Official worker profile identity')
                }
                else {
                    Assert-ExactXmlAttributes `
                        -Element $identity `
                        -ExpectedAttributes @('mode', 'reference') `
                        -Context 'Official worker profile identity'
                }
                $reference = Get-RequiredXmlAttribute `
                    -Element $identity -Name 'reference' -MaximumLength 256 `
                    -Context 'Official worker profile identity'
                if (-not (Test-SafeIdentifier -Value $reference -MaximumLength 256)) {
                    throw 'Official worker profile identity is invalid.'
                }
            }
            default {
                throw 'Official worker profile identity is invalid.'
            }
        }
    }
    catch {
        if ($_.Exception.Message -like 'Official worker profile*') {
            throw $_.Exception
        }

        throw 'Official worker profile is invalid.'
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }

        $document = $null
        $text = $null
    }
}

function Invoke-CompatibilityPreflight {
    <#
    .SYNOPSIS
    組合 manifest、overlay 與 XML identity chain 的無網路驗證。

    .DESCRIPTION
    此函式是 ValidateOnly 與 Live mode 的共同第一步；任一缺失、hash drift、
    generation mismatch 或 identity union mismatch 都會在建立 handler/client 前
    拒絕。回傳物件只含可安全記錄的值，沒有 path、endpoint、organization 或
    credential material，因此後續輸出不會把 deployment internals 洩漏出去。
    #>
    param(
        [string] $ManifestPath,
        [string] $GatewayOverlayPath,
        [string] $ProfileAlias,
        [string] $ExpectedWorkerKind
    )

    if (-not (Test-SafeIdentifier -Value $ProfileAlias -MaximumLength 128)) {
        throw 'Compatibility profile selector is invalid.'
    }

    $manifestDocument = Read-StrictJsonObject `
        -Path $ManifestPath `
        -MaximumBytes $maximumManifestBytes `
        -DocumentName 'Official worker manifest'
    $overlayDocument = Read-StrictJsonObject `
        -Path $GatewayOverlayPath `
        -MaximumBytes $maximumOverlayBytes `
        -DocumentName 'Gateway deployment overlay'
    $manifestWorker = Get-ManifestWorker `
        -ManifestDocument $manifestDocument `
        -ExpectedWorkerKind $ExpectedWorkerKind
    $overlayProfile = Get-SelectedOverlayProfile `
        -OverlayDocument $overlayDocument `
        -ProfileAlias $ProfileAlias `
        -ManifestWorker $manifestWorker
    Assert-SelectedWorkerProfile -OverlayProfile $overlayProfile

    return [pscustomobject]@{
        ProfileAlias = $overlayProfile.ProfileAlias
        WorkerKind = $overlayProfile.WorkerKind
        CeVersion = $manifestWorker.CeVersion
        PackageLockId = $overlayProfile.PackageLockId
        ProfileGenerationId = $overlayProfile.GenerationId
    }
}

function Assert-SucceededGatewayResponse {
    <#
    .SYNOPSIS
    以固定上限讀取 Gateway response，且只接受成功 envelope。

    .DESCRIPTION
    response 可能來自錯誤部署或上游故障，不能以 ReadAsStringAsync 等無界 API
    直接配置記憶體。本函式先檢查 Content-Length，再以 MaximumResponseBytes + 1
    的固定 byte array stream 讀取；任何超限、無效 UTF-8、非 object 或不含 true
    succeeded 都 fail-closed。raw body 僅存在於 local string/JSON object，絕不
    輸出；stream、可變 buffer 與暫存引用都在 finally 釋放或清零。
    #>
    param(
        [System.Net.Http.HttpContent] $Content,
        [int] $MaximumBytes
    )

    $stream = $null
    $buffer = $null
    $payload = $null
    $decoded = $null
    try {
        if ($null -eq $Content -or
            ($Content.Headers.ContentLength -is [long] -and
                $Content.Headers.ContentLength.Value -gt $MaximumBytes)) {
            throw 'Gateway compatibility response is invalid.'
        }

        $buffer = [byte[]]::new($MaximumBytes + 1)
        $stream = $Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $offset = 0
        while ($true) {
            $read = $stream.Read($buffer, $offset, $buffer.Length - $offset)
            if ($read -eq 0) {
                break
            }

            $offset += $read
            if ($offset -gt $MaximumBytes) {
                throw 'Gateway compatibility response is invalid.'
            }
        }
        if ($offset -lt 2) {
            throw 'Gateway compatibility response is invalid.'
        }

        $payload = $strictUtf8.GetString($buffer, 0, $offset)
        $serializer = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
        $serializer.MaxJsonLength = $MaximumBytes
        $serializer.RecursionLimit = 16
        $decoded = $serializer.DeserializeObject($payload)
        if ($decoded -isnot [Collections.IDictionary] -or
            -not $decoded.Contains('succeeded') -or
            $decoded['succeeded'] -isnot [bool] -or
            -not [bool] $decoded['succeeded']) {
            throw 'Gateway compatibility response is invalid.'
        }
    }
    catch {
        if ($_.Exception.Message -eq 'Gateway compatibility response is invalid.') {
            throw $_.Exception
        }

        throw 'Gateway compatibility response is invalid.'
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if ($null -ne $buffer) {
            [Array]::Clear($buffer, 0, $buffer.Length)
        }

        $decoded = $null
        $payload = $null
    }
}

function Invoke-GatewayIdentityOperation {
    <#
    .SYNOPSIS
    以目前 Windows 主機身分向 Gateway 執行一次受控 identity operation。

    .DESCRIPTION
    此函式沒有 retry、transport fallback 或可呼叫端指定的 operation/path。它只
    組合固定 /v1/organizations/{alias}/operations/runtime.health.whoami URI，並
    禁用 cookie、proxy、redirect、automatic decompression 與預先驗證。handler
    僅能使用目前 Windows identity，沒有密碼輸入、持久 token 或外部 Session。

    每個 IDisposable 由此函式唯一擁有；finally 先 Dispose response/request，
    再清零 request bytes、Dispose client/handler/CTS。建立順序與釋放順序明確
    對稱，任何 handshake、timeout、取消、非 200 或 body parse 失敗都不會留下
    socket、stream、cookie container、timer 或跨 profile 可變狀態。
    #>
    param(
        [Uri] $GatewayUri,
        [string] $ProfileAlias,
        [int] $TimeoutSeconds,
        [int] $MaximumResponseBytes
    )

    $handler = $null
    $client = $null
    $request = $null
    $response = $null
    $cts = $null
    $requestBytes = $null
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $baseUri = $GatewayUri.AbsoluteUri.TrimEnd('/') + '/'
        $relativeOperation = 'v1/organizations/' +
            [Uri]::EscapeDataString($ProfileAlias) +
            '/operations/' + $operationId
        $operationUri = [Uri]::new([Uri] $baseUri, $relativeOperation)

        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.UseCookies = $false
        $handler.UseProxy = $false
        $handler.AllowAutoRedirect = $false
        $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
        $handler.UseDefaultCredentials = $true
        $handler.PreAuthenticate = $false
        $handler.MaxConnectionsPerServer = 1
        $handler.MaxResponseHeadersLength = 16

        $client = [System.Net.Http.HttpClient]::new($handler, $false)
        $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
        $cts = [Threading.CancellationTokenSource]::new(
            [TimeSpan]::FromSeconds($TimeoutSeconds))
        $requestBytes = [Text.UTF8Encoding]::new($false).GetBytes('{"parameters":{}}')
        $request = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Post,
            $operationUri)
        $request.Content = [System.Net.Http.ByteArrayContent]::new($requestBytes)
        $request.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new(
            'application/json')
        $request.Headers.Accept.Add(
            [System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))

        try {
            $response = $client.SendAsync(
                $request,
                [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead,
                $cts.Token).GetAwaiter().GetResult()
        }
        catch [OperationCanceledException] {
            throw 'Gateway compatibility request timed out.'
        }
        catch {
            throw 'Gateway compatibility request failed.'
        }

        if ($response.StatusCode -ne [System.Net.HttpStatusCode]::OK -or
            $null -eq $response.Content.Headers.ContentType -or
            $response.Content.Headers.ContentType.MediaType -cne 'application/json') {
            throw 'Gateway compatibility operation was rejected.'
        }

        Assert-SucceededGatewayResponse `
            -Content $response.Content `
            -MaximumBytes $MaximumResponseBytes
        $stopwatch.Stop()
        return [pscustomobject]@{
            HttpStatusCode = [int] $response.StatusCode
            ElapsedMilliseconds = [long] $stopwatch.ElapsedMilliseconds
        }
    }
    finally {
        if ($stopwatch.IsRunning) {
            $stopwatch.Stop()
        }
        if ($null -ne $response) {
            $response.Dispose()
        }
        if ($null -ne $request) {
            $request.Dispose()
        }
        if ($null -ne $requestBytes) {
            [Array]::Clear($requestBytes, 0, $requestBytes.Length)
        }
        if ($null -ne $client) {
            $client.Dispose()
        }
        if ($null -ne $handler) {
            $handler.Dispose()
        }
        if ($null -ne $cts) {
            $cts.Dispose()
        }
    }
}

if (($ValidateOnly -and $EnableLiveCompatibility) -or
    (-not $ValidateOnly -and -not $EnableLiveCompatibility)) {
    throw 'Select exactly one explicit compatibility execution mode.'
}

$preflight = $null
$gatewayUri = $null
try {
    # 即使 ValidateOnly 不會建立網路資源，也必須先驗證 Gateway 目標是安全的 HTTPS
    # base URI；這使部署宣告與日後 Live 執行共用相同 fail-closed 邊界，避免驗證報告
    # 認可一個永遠不可能安全執行的設定。URI 僅保留在本次程序的區域變數，finally 會清除
    # 參考；不會輸出、快取或跨程序傳遞，因此不會形成 endpoint 或 session 狀態的留存。
    $gatewayUri = Get-ValidatedHttpsUri `
        -Value $GatewayEndpoint `
        -Context 'Gateway compatibility target'

    $preflight = Invoke-CompatibilityPreflight `
        -ManifestPath $ManifestPath `
        -GatewayOverlayPath $GatewayOverlayPath `
        -ProfileAlias $ProfileAlias `
        -ExpectedWorkerKind $ExpectedWorkerKind

    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'validated'
        phase = '4C'
        profileAlias = $preflight.ProfileAlias
        workerKind = $preflight.WorkerKind
        ceVersion = $preflight.CeVersion
        packageLockId = $preflight.PackageLockId
        profileGenerationId = $preflight.ProfileGenerationId
        operationId = $operationId
        requestExecuted = $false
    }

    if ($EnableLiveCompatibility) {
        $liveResult = Invoke-GatewayIdentityOperation `
            -GatewayUri $gatewayUri `
            -ProfileAlias $preflight.ProfileAlias `
            -TimeoutSeconds $TimeoutSeconds `
            -MaximumResponseBytes $MaximumResponseBytes
        $result.outcome = 'passed'
        $result.requestExecuted = $true
        $result.httpStatusCode = $liveResult.HttpStatusCode
        $result.elapsedMilliseconds = $liveResult.ElapsedMilliseconds
    }

    if ($Json) {
        $result | ConvertTo-Json -Depth 4
    }
    else {
        [pscustomobject] $result
    }
}
finally {
    $gatewayUri = $null
    # preflight 僅保存 allowlisted scalar；命令完成仍主動解除參考，避免互動式 host
    # 在後續命令意外重用 profile generation、artifact path 或解析物件。
    $preflight = $null
}
