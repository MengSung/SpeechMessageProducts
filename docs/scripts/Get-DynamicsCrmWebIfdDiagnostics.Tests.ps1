#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Contains {
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

$global:LASTEXITCODE = 0
$output = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
    -WebApiRoot 'https://example.invalid/api/data/v9.1/' 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "No-probe diagnostic run must exit 0; actual exit code was $LASTEXITCODE. Output: $output"
}

Assert-Contains -Actual $output -Expected 'not-requested' -Context 'No-probe execution must not contact CRM'

$escapedScriptPath = $scriptPath.Replace("'", "''")
$countCommand = "& '$escapedScriptPath' -WebApiRoot 'https://example.invalid/api/data/v9.1/' | Measure-Object | Select-Object -ExpandProperty Count"
$outputCountText = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $countCommand 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "No-probe output-count run must exit 0; actual exit code was $LASTEXITCODE. Output: $outputCountText"
}

if ($outputCountText -ne '1') {
    throw "No-probe execution must emit exactly one structured snapshot; actual pipeline object count was '$outputCountText'."
}

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

# An empty localized event query is a normal, bounded result. The stable error
# identity, not the human-localized text, defines this diagnostic contract.
function Get-WinEvent {
    [CmdletBinding()]
    param(
        [hashtable]$FilterHashtable,
        [int]$MaxEvents
    )

    $localizedNoEventMessage = -join [char[]](
        0x627E, 0x4E0D, 0x5230, 0x7B26, 0x5408, 0x6307, 0x5B9A,
        0x9078, 0x53D6, 0x6E96, 0x5247, 0x7684, 0x4E8B, 0x4EF6, 0x3002)
    $exception = [System.Exception]::new($localizedNoEventMessage)
    $errorRecord = [System.Management.Automation.ErrorRecord]::new(
        $exception,
        'NoMatchingEventsFound',
        [System.Management.Automation.ErrorCategory]::ObjectNotFound,
        $null)
    $PSCmdlet.ThrowTerminatingError($errorRecord)
}

try {
    $emptyEventSnapshot = . $scriptPath -WebApiRoot 'https://example.invalid/api/data/v9.1/'

    if ($emptyEventSnapshot.AspNet1309.Status -ne 'no-matching-events') {
        throw "A localized empty event query must be classified as no-matching-events; actual status was '$($emptyEventSnapshot.AspNet1309.Status)'."
    }

    if (@($emptyEventSnapshot.AspNet1309.Events).Count -ne 0) {
        throw 'A localized empty event query must not project any event records.'
    }

    if ($emptyEventSnapshot.AspNet1309.PSObject.Properties.Name -contains 'FailureCategory') {
        throw 'A localized empty event query must not be reported as a diagnostic failure.'
    }
}
finally {
    Remove-Item -LiteralPath Function:\Get-WinEvent -Force -ErrorAction SilentlyContinue
}

Write-Host 'Get-DynamicsCrmWebIfdDiagnostics script contract passed.'
