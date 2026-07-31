#Requires -Version 5.1
<#
.SYNOPSIS
  驗證 CRMWeb Claims／IFD 唯讀診斷工具的安全與資源生命週期契約。

.DESCRIPTION
  本測試只檢查工具的文字契約，並以不啟用 WhoAmI probe 的方式在子程序中驗證
  非網路路徑。它不會聯絡 Dynamics、AD FS、DNS、WinRM 或任何遠端主機，也不會
  寫入 CRM、IIS、設定檔、環境變數或輸出檔案。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Contains {
    <#
    .SYNOPSIS
      驗證唯讀工具仍保有一個必要的安全或生命週期契約片段。

    .DESCRIPTION
      集中管理文字斷言可讓每一個失敗案例回報相同格式的可追溯原因，避免測試本身
      因重複的字串比對邏輯而偏離安全邊界。
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Actual,
        [Parameter(Mandatory)]
        [string]$Expected,
        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($Actual -notmatch [regex]::Escape($Expected)) {
        throw "$Context. Expected source to contain '$Expected'."
    }
}

function Assert-NotContains {
    <#
    .SYNOPSIS
      驗證工具沒有引入任何會跨出唯讀診斷界線的命令。

    .DESCRIPTION
      這個斷言只接受正規表示式，讓每一個禁止操作能以精確字界比對，避免字串片段
      剛好出現在正常說明文字中時造成誤判。
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Actual,
        [Parameter(Mandatory)]
        [string]$ForbiddenPattern,
        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($Actual -match $ForbiddenPattern) {
        throw "$Context. Forbidden pattern '$ForbiddenPattern' was found."
    }
}

$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $repositoryPath 'docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.ps1'

if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "CRMWeb IFD diagnostic script was not found: $scriptPath"
}

$source = Get-Content -LiteralPath $scriptPath -Raw

Assert-Contains -Actual $source -Expected '[string]$WebApiRoot' -Context 'The target must be explicit'
Assert-Contains -Actual $source -Expected '[switch]$ProbeWhoAmI' -Context 'Network access must remain opt-in'
Assert-Contains -Actual $source -Expected '[ValidateRange(1, 1440)]' -Context 'Event-log lookback must remain bounded'
Assert-Contains -Actual $source -Expected 'Get-WinEvent' -Context 'The server exception discriminator must be collected locally'
Assert-Contains -Actual $source -Expected 'no-matching-events' -Context 'An empty bounded event query must not be misreported as a diagnostics failure'
Assert-Contains -Actual $source -Expected 'Get-CrmSetting' -Context 'Claims and IFD settings require supported read-only discovery'
Assert-Contains -Actual $source -Expected 'Get-WebBinding' -Context 'Relevant IIS binding evidence must remain available'
Assert-Contains -Actual $source -Expected 'UseDefaultCredentials' -Context 'The optional probe must use the current host identity'
Assert-Contains -Actual $source -Expected 'UseCookies = $false' -Context 'The optional probe must never retain an IFD browser cookie'
Assert-Contains -Actual $source -Expected 'UseProxy = $false' -Context 'The optional probe must not retain or inherit proxy routing'
Assert-Contains -Actual $source -Expected 'finally' -Context 'Disposable HTTP resources require deterministic cleanup'
Assert-Contains -Actual $source -Expected '.Dispose()' -Context 'Disposable HTTP resources must be released'
Assert-Contains -Actual $source -Expected 'MatchKind' -Context 'ASP.NET evidence must be summarized without serializing its raw message'
Assert-Contains -Actual $source -Expected 'FailureCategory' -Context 'Diagnostic failures must be classified without serializing raw exception text'
Assert-Contains -Actual $source -Expected '$record.Dispose()' -Context 'Every projected EventRecord must be deterministically released'
Assert-Contains -Actual $source -Expected '$certificate.Dispose()' -Context 'Every projected certificate object must be deterministically released'

Assert-NotContains -Actual $source -ForbiddenPattern '(?im)^\s*(Set-CrmSetting|New-PSSession|Enter-PSSession|Invoke-Command|Set-Item|Set-DnsClientServerAddress|Set-WebConfigurationProperty|Add-WebConfigurationProperty|Add-Content|Set-Content|Out-File|Export-Csv|Start-Transcript)\b' -Context 'The diagnostic script must stay read-only and local'
Assert-NotContains -Actual $source -ForbiddenPattern '(?i)\b(PSCredential|ConvertTo-SecureString|AccessToken|RefreshToken|ClientSecret|Password)\b' -Context 'The diagnostic script must not accept or retain secret material'
Assert-NotContains -Actual $source -ForbiddenPattern '(?i)\$env:' -Context 'The diagnostic script must not persist bridge state in process environment variables'
Assert-NotContains -Actual $source -ForbiddenPattern '(?im)^\s*ReasonPhrase\s*=' -Context 'The optional probe must not serialize server-controlled reason text'
Assert-NotContains -Actual $source -ForbiddenPattern '(?im)^\s*Message\s*=\s*ConvertTo-SafeDiagnosticText' -Context 'ASP.NET event messages must not be serialized'
Assert-NotContains -Actual $source -ForbiddenPattern '(?im)^\s*Error\s*=\s*ConvertTo-SafeDiagnosticText' -Context 'Raw exception messages must not be serialized'

# 不帶 -ProbeWhoAmI 的子程序不應產生任何 CRM 網路流量；即使開發工作站沒有 CRM
# deployment cmdlet 或 IIS 模組，工具也必須以診斷狀態回傳，而不是改用其他設定路徑。
$global:LASTEXITCODE = 0
$output = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
    -WebApiRoot 'https://example.invalid/api/data/v9.1/' 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "No-probe diagnostic run must exit 0; actual exit code was $LASTEXITCODE. Output: $output"
}

Assert-Contains -Actual $output -Expected 'not-requested' -Context 'No-probe execution must not contact CRM'

# 設定形狀分析的暫存值只能存在於函式區域；若 PowerShell pipeline 意外輸出這些中間值，
# 可能擴散不屬於診斷合約的部署設定文字。無 network probe 的執行必須只回傳一個最終
# 結構化快照，任何額外 pipeline object 都視為隔離缺陷。
$escapedScriptPath = $scriptPath.Replace("'", "''")
$countCommand = "& '$escapedScriptPath' -WebApiRoot 'https://example.invalid/api/data/v9.1/' | Measure-Object | Select-Object -ExpandProperty Count"
$outputCountText = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $countCommand 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "No-probe output-count run must exit 0; actual exit code was $LASTEXITCODE. Output: $outputCountText"
}

if ($outputCountText -ne '1') {
    throw "No-probe execution must emit exactly one structured snapshot; actual pipeline object count was '$outputCountText'."
}

# 以帶有 cookie、Bearer 與 token sentinel 的假 ASP.NET 1309 event 執行真實 script body；輸出只能是固定分類，
# 而且 EventRecord 由工具唯一持有並在投影後 Dispose。測試不建立 CRM 連線、PSSession、cookie jar 或持久化檔案。
$fakeEvent = [pscustomobject]@{
    TimeCreated = Get-Date
    ProviderName = 'ASP.NET 4.0.30319.0'
    Id = 1309
    LevelDisplayName = 'Warning'
    Message = 'UriFormatException Cookie: cookie-sentinel; Set-Cookie: set-cookie-sentinel; Authorization: Bearer bearer-sentinel; access_token=access-sentinel; refresh_token=refresh-sentinel'
    Disposed = $false
}
$fakeEvent | Add-Member -MemberType ScriptMethod -Name Dispose -Value {
    $this.Disposed = $true
    return
}

function Get-WinEvent {
    [CmdletBinding()]
    param(
        [hashtable]$FilterHashtable,
        [int]$MaxEvents
    )

    return $fakeEvent
}

try {
    $safeSnapshot = . $scriptPath -WebApiRoot 'https://example.invalid/api/data/v9.1/'
    $safeSnapshotText = $safeSnapshot | ConvertTo-Json -Depth 8

    if (-not $fakeEvent.Disposed) {
        throw 'Projected ASP.NET EventRecord was not deterministically disposed.'
    }

    foreach ($sentinel in @('cookie-sentinel', 'set-cookie-sentinel', 'bearer-sentinel', 'access-sentinel', 'refresh-sentinel')) {
        Assert-NotContains -Actual $safeSnapshotText -ForbiddenPattern ([regex]::Escape($sentinel)) -Context 'Diagnostic snapshot must not serialize raw event secrets or session data'
    }
}
finally {
    Remove-Item -LiteralPath Function:\Get-WinEvent -Force -ErrorAction SilentlyContinue
    $fakeEvent = $null
}

Write-Host 'Get-DynamicsCrmWebIfdDiagnostics script contract passed.'
