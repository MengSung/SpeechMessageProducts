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
Assert-Contains -Actual $source -Expected "'Microsoft.Crm.PowerShell'" -Context 'The official Dynamics deployment snap-in must be the only activation candidate'
Assert-Contains -Actual $source -Expected 'Get-PSSnapin -Registered' -Context 'The diagnostic must distinguish an unregistered deployment snap-in from a missing command'
Assert-Contains -Actual $source -Expected 'Add-PSSnapin -Name $crmSnapInName -ErrorAction Stop' -Context 'A supported Desktop PowerShell shell must temporarily activate the registered deployment snap-in'
Assert-Contains -Actual $source -Expected 'Remove-PSSnapin -Name $activation.SnapInName -ErrorAction Stop' -Context 'A snap-in added by this diagnostic must be deterministically removed'
Assert-Contains -Actual $source -Expected '$crmSnapInAddedHere' -Context 'The diagnostic must never remove a caller-owned deployment snap-in'
Assert-Contains -Actual $source -Expected 'desktop-powershell-required' -Context 'PowerShell edition incompatibility must be surfaced without falling back to unsupported tooling'
Assert-Contains -Actual $source -Expected 'temporarily-loaded' -Context 'Successful safe snap-in activation must be observable in the structured snapshot'
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

# 在獨立 scope 模擬已註冊但尚未載入的官方 snap-in。測試只驗證診斷自己的生命週期：
# 取得 cmdlet 後必須讀取兩種設定，並在函式返回前卸載由它新增的 snap-in。
# Dot-source the diagnostic once so its private helper functions are available in this
# test script's scope. The invocation remains no-probe and completes before command
# mocks are installed, so environment observation cannot affect the fixture below.
$null = . $scriptPath -WebApiRoot 'https://example.invalid/api/data/v9.1/'

# Dynamics Deployment Web Service 可能將 Deployment Manager 以裸主機名稱儲存的
# ExternalDomain 表示成 HTTPS 根 URI。此回歸測試確認診斷只比較正規化主機與安全
# URI 形狀，絕不可用顯示字串直接比較或將原始設定值輸出至診斷證據。
$expectedExternalDomain = 'auth.speechmessage.com.tw'
$uriExternalDomainEvidence = Get-CrmIfdExternalDomainMatchEvidence `
    -ExternalDomainValue ([uri]'https://auth.speechmessage.com.tw/') `
    -ExpectedHost $expectedExternalDomain

if (-not $uriExternalDomainEvidence.NormalizedHostMatches -or
    -not $uriExternalDomainEvidence.MatchesExpectedContract -or
    $uriExternalDomainEvidence.Representation -ne 'absolute-https-root-uri') {
    throw 'An HTTPS root Uri whose normalized host matches the IFD External Domain contract must pass.'
}

$uriExternalDomainEvidenceText = $uriExternalDomainEvidence | ConvertTo-Json -Depth 4
Assert-NotContains -Actual $uriExternalDomainEvidenceText -ForbiddenPattern ([regex]::Escape($expectedExternalDomain)) -Context 'External-domain semantic evidence must not serialize the persisted setting value'

$pathExternalDomainEvidence = Get-CrmIfdExternalDomainMatchEvidence `
    -ExternalDomainValue ([uri]'https://auth.speechmessage.com.tw/not-a-root') `
    -ExpectedHost $expectedExternalDomain

if ($pathExternalDomainEvidence.MatchesExpectedContract -or
    -not $pathExternalDomainEvidence.HasUnexpectedUriShape) {
    throw 'An ExternalDomain URI with a non-root path must fail the IFD contract.'
}

# 同名函式不是官方 Dynamics Deployment cmdlet 的證據；在它可能被呼叫、讀取任何
# 設定或建立可保留狀態前，診斷必須 fail closed 並拒絕這種陰影命令。
& {
    function Get-Command {
        param(
            [string]$Name,
            [object]$ErrorAction
        )

        if ($Name -eq 'Get-CrmSetting') {
            return [pscustomobject]@{
                Name = 'Get-CrmSetting'
                CommandType = 'Function'
            }
        }

        return $null
    }

    $untrustedSnapshot = Initialize-CrmDeploymentCommand
    if ($untrustedSnapshot.Evidence.Activation -ne 'untrusted-command') {
        throw "A non-cmdlet Get-CrmSetting shadow must be rejected; actual '$($untrustedSnapshot.Evidence.Activation)'."
    }
}

& {
    $activationState = [pscustomobject]@{
        Loaded = $false
        RemoveCalls = 0
    }

    function Get-Command {
        param(
            [string]$Name,
            [object]$ErrorAction
        )

        if ($Name -eq 'Get-CrmSetting' -and $activationState.Loaded) {
            return [pscustomobject]@{
                Name = 'Get-CrmSetting'
                CommandType = 'Cmdlet'
                PSSnapIn = [pscustomobject]@{ Name = 'Microsoft.Crm.PowerShell' }
            }
        }

        return $null
    }

    function Get-PSSnapin {
        param(
            [string]$Name,
            [switch]$Registered,
            [object]$ErrorAction
        )

        if ($Registered) {
            return [pscustomobject]@{ Name = 'Microsoft.Crm.PowerShell' }
        }

        if ($activationState.Loaded) {
            return [pscustomobject]@{ Name = 'Microsoft.Crm.PowerShell' }
        }

        throw [System.InvalidOperationException]::new('The test snap-in is not loaded.')
    }

    function Add-PSSnapin {
        param(
            [string]$Name,
            [object]$ErrorAction
        )

        if ($Name -ne 'Microsoft.Crm.PowerShell') {
            throw 'Only the approved Dynamics snap-in may be activated.'
        }

        $activationState.Loaded = $true
    }

    function Remove-PSSnapin {
        param(
            [string]$Name,
            [object]$ErrorAction
        )

        if ($Name -ne 'Microsoft.Crm.PowerShell') {
            throw 'Only the approved Dynamics snap-in may be removed.'
        }

        $activationState.Loaded = $false
        $activationState.RemoveCalls++
    }

    function Get-CrmSetting {
        param([string]$SettingType)

        return [pscustomobject]@{
            Enabled = $true
            ExternalDomain = [uri]'https://auth.speechmessage.com.tw/'
            FederationMetadataUrl = 'https://example.invalid/federation-metadata'
            SessionSecurityTokenLifetimeInHours = 24
        }
    }

    $activationSnapshot = Get-CrmDeploymentSettingsEvidence `
        -ExpectedIfdExternalDomain 'auth.speechmessage.com.tw'
    if ($activationSnapshot.Shell.Activation -ne 'temporarily-loaded') {
        throw "A registered Dynamics snap-in must be reported as temporarily loaded; actual '$($activationSnapshot.Shell.Activation)'."
    }

    if (@($activationSnapshot.Settings).Count -ne 2) {
        throw 'A temporarily activated Dynamics cmdlet must read both IFD and Claims setting shapes.'
    }

    $projectedPropertyNames = @(
        $activationSnapshot.Settings |
            ForEach-Object { $_.Properties } |
            ForEach-Object { $_.Name })
    if ($projectedPropertyNames -contains 'SessionSecurityTokenLifetimeInHours') {
        throw 'A scalar token lifetime must not be misclassified as a URI-like Claims/IFD setting.'
    }

    $ifdSettingsEvidence = @($activationSnapshot.Settings |
        Where-Object { $_.SettingType -eq 'IfdSettings' } |
        Select-Object -First 1)
    if ($ifdSettingsEvidence.Count -ne 1 -or
        -not $ifdSettingsEvidence[0].ExternalDomainExpectation.MatchesExpectedContract) {
        throw 'The IFD settings projection must semantically accept an equivalent HTTPS ExternalDomain URI.'
    }

    $ifdSettingsEvidenceText = $ifdSettingsEvidence[0] | ConvertTo-Json -Depth 8
    Assert-NotContains -Actual $ifdSettingsEvidenceText -ForbiddenPattern 'auth\.speechmessage\.com\.tw' -Context 'IFD expectation evidence must not serialize the persisted external domain'

    if ($activationState.Loaded -or $activationState.RemoveCalls -ne 1) {
        throw 'A Dynamics snap-in activated by the diagnostic must be removed exactly once before the diagnostic returns.'
    }
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
