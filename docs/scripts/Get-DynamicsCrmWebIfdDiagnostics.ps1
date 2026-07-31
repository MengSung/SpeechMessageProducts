#Requires -Version 5.1
<#
.SYNOPSIS
  在 D365APP01 上以唯讀方式擷取 CRMWeb Claims／IFD URI 失敗的最小鑑別證據。

.DESCRIPTION
  此工具只應在已登入 D365APP01、且具有既有核准管理身分的主控台中執行。它要求
  操作者明確提供 Web API root，預設不接觸 CRM；預設路徑只驗證 URI、讀取最近的
  ASP.NET 1309 事件、讀取可用的 Dynamics 設定投影，以及盤點相關 IIS HTTPS 綁定與
  憑證中繼資料。

  加上 -ProbeWhoAmI 時，工具才會以目前 Windows 身分對明確指定的目標送出一次
  唯讀 WhoAmI GET。該路徑不讀取回應本文、不建立 Cookie、Proxy、遠端工作階段或
  環境變數橋接，且由同一個 finally 區塊確定釋放 HTTP request、response、client
  與 handler。工具不接收、查詢、列印或持久化任何秘密值，也不建立輸出檔案。

  本工具的目的不是修復部署設定；它只提供支援 Dynamics Claims／IFD 設定流程所需的
  鑑別證據。不得把它當作變更 DNS、IIS、AD FS、WinRM 或 CRM 資料庫的捷徑。

.PARAMETER WebApiRoot
  必填的絕對 HTTPS Dynamics Web API root，且路徑必須精確為
  /api/data/v8.2/ 或 /api/data/v9.1/。例如：
  https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/

.PARAMETER ProbeWhoAmI
  明確啟用一次使用目前 Windows 身分的唯讀 WhoAmI GET。未提供此參數時不會產生
  CRM 網路流量。

.PARAMETER LookbackMinutes
  ASP.NET 1309 事件的回溯分鐘數。保持有界可避免在事件記錄龐大時形成長時間或高記憶體
  的列舉。

.PARAMETER MaxEvents
  最多回傳的相關 ASP.NET 1309 事件數。只保留可以定位 URI 例外的有限結果。

.PARAMETER RequestTimeoutSeconds
  啟用 WhoAmI probe 時的單次 HTTP 時限。此值僅管理本工具建立的 client，不會改變
  CRM、IIS 或系統層級逾時設定。

.EXAMPLE
  .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.ps1 `
      -WebApiRoot 'https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/'

.EXAMPLE
  .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.ps1 `
      -WebApiRoot 'https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/' `
      -ProbeWhoAmI
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WebApiRoot,
    [switch]$ProbeWhoAmI,
    [ValidateRange(1, 1440)]
    [int]$LookbackMinutes = 15,
    [ValidateRange(1, 20)]
    [int]$MaxEvents = 5,
    [ValidateRange(1, 30)]
    [int]$RequestTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-SafeDiagnosticText {
    <#
    .SYNOPSIS
      將診斷文字限制在可判讀的長度，並遮蔽常見敏感鍵值形式。

    .DESCRIPTION
      事件與支援 cmdlet 的例外文字可能包含不應在診斷輸出中擴散的鍵值片段。此函式
      只處理要顯示的文字副本，不會修改任何伺服器狀態或原始事件。限制長度可避免長
      stack trace 被常駐在格式化管線中，也能讓操作者聚焦於 URI 建構失敗位置。
    #>
    param(
        [AllowNull()]
        [string]$Value,
        [ValidateRange(128, 32768)]
        [int]$MaximumLength = 8192
    )

    if ($null -eq $Value) {
        return $null
    }

    $redacted = $Value -replace '(?i)\b(secret|token|pass(?:word))\b\s*[:=]\s*[^;\s]+', '$1=<redacted>'
    $redacted = $redacted -replace '(?i)(authorization\s*:\s*)\S+', '$1<redacted>'
    if ($redacted.Length -gt $MaximumLength) {
        return $redacted.Substring(0, $MaximumLength) + ' [truncated]'
    }

    return $redacted
}

function Get-CrmWebUriFormatEvents {
    <#
    .SYNOPSIS
      從本機 Application log 取得有界的 CRMWeb URI 例外事件。

    .DESCRIPTION
      ASP.NET 1309 的完整 stack trace 是區分 CRMWeb 設定邊界與 Gateway 傳輸邊界的
      主要證據。本函式只讀取指定時間窗與最大筆數，並將事件訊息複製為經遮蔽、限制
      長度的輸出值；原始 EventRecord 不會跨函式保留。
    #>
    param(
        [Parameter(Mandatory)]
        [datetime]$StartTime,
        [Parameter(Mandatory)]
        [int]$MaximumCount
    )

    $records = $null
    $events = @()
    $record = $null
    try {
        $records = @(Get-WinEvent -FilterHashtable @{
                LogName = 'Application'
                Id = 1309
                StartTime = $StartTime
            } -MaxEvents $MaximumCount -ErrorAction Stop)

        for ($recordIndex = 0; $recordIndex -lt $records.Count; $recordIndex++) {
            $record = $records[$recordIndex]
            try {
                $message = $record.Message
                if ($message -notmatch '(?i)UriFormatException|Invalid URI|hostname could not be parsed|w3wp') {
                    continue
                }

                $events += [pscustomobject]@{
                    TimeCreated = $record.TimeCreated
                    ProviderName = $record.ProviderName
                    Id = $record.Id
                    Level = $record.LevelDisplayName
                    MatchKind = 'uri-format'
                }
            }
            finally {
                # 每一筆 EventRecord 都由本函式在投影完成後釋放；即使訊息不符合篩選條件也不能留下事件控制代碼。
                if ($null -ne $record) {
                    $record.Dispose()
                    $records[$recordIndex] = $null
                    $record = $null
                }
                $message = $null
            }
        }

        return [pscustomobject]@{
            Status = 'available'
            Events = @($events)
        }
    }
    catch {
        # Get-WinEvent 在時間窗內沒有任何 1309 時會回報例外；這代表「未命中」，不是
        # 事件記錄讀取權限或 CRMWeb 診斷工具失敗。其他錯誤仍須明確標示為 unavailable。
        if ($_.FullyQualifiedErrorId -match '^NoMatchingEventsFound(?:,|$)') {
            return [pscustomobject]@{
                Status = 'no-matching-events'
                Events = @()
            }
        }

        return [pscustomobject]@{
            Status = 'unavailable'
            Events = @()
            FailureCategory = 'event-log-query-failed'
        }
    }
    finally {
        # EventRecord 只屬於此有界唯讀查詢；清除參考可避免長 stack trace 跨出函式生命週期。
        if ($null -ne $records) {
            for ($remainingRecordIndex = 0; $remainingRecordIndex -lt $records.Count; $remainingRecordIndex++) {
                $remainingRecord = $records[$remainingRecordIndex]
                if ($null -ne $remainingRecord) {
                    $remainingRecord.Dispose()
                    $records[$remainingRecordIndex] = $null
                }
            }
        }
        $record = $null
        $events = $null
        $records = $null
    }
}

function Get-CrmDeploymentSettingsEvidence {
    <#
    .SYNOPSIS
      透過本機受支援 cmdlet 投影 IFD 與 Claims 的 URI 相關設定。

    .DESCRIPTION
      此函式絕不修改設定，也不將完整設定物件或 URI 值傳出函式。它只輸出欄位名稱與
      語法形狀，例如 URI 是否存在、是否可解析為絕對 URI、或 host/domain 是否含有
      空白。這些結果已足以讓 CRM 管理者選擇受支援的設定頁面，同時避免將內部端點
      大量複製到診斷輸出。finally 會清除取得的設定物件參考；找不到 cmdlet 或權限
      不足時會回傳狀態，讓操作者保留此部署管理邊界而不是採用其他管理捷徑。
    #>
    $command = Get-Command -Name 'Get-CrmSetting' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return @([pscustomobject]@{
                SettingType = 'deployment-cmdlet'
                Status = 'unavailable'
                Properties = @()
                FailureCategory = 'deployment-cmdlet-unavailable'
            })
    }

    $result = @()
    foreach ($settingType in @('IfdSettings', 'ClaimsSettings')) {
        $setting = $null
        try {
            $setting = Get-CrmSetting -SettingType $settingType -ErrorAction Stop
            $enabledProperty = @($setting.PSObject.Properties |
                Where-Object { $_.Name -eq 'Enabled' } |
                Select-Object -First 1)
            $properties = @($setting.PSObject.Properties |
                Where-Object {
                    $_.Name -match '(?i)domain|host|uri|url|realm|federation|endpoint|address'
                } |
                ForEach-Object {
                    $rawValue = [string]$_.Value
                    $parsedValue = $null
                    $isUriLike = $_.Name -match '(?i)uri|url|endpoint|address'
                    [pscustomobject]@{
                        Name = $_.Name
                        Kind = $(if ($isUriLike) { 'uri-like' } else { 'host-domain-like' })
                        Present = -not [string]::IsNullOrWhiteSpace($rawValue)
                        AbsoluteUriSyntax = $(if ($isUriLike -and -not [string]::IsNullOrWhiteSpace($rawValue)) {
                            [uri]::TryCreate($rawValue, [UriKind]::Absolute, [ref]$parsedValue)
                        } else {
                            $null
                        })
                        ContainsWhitespace = $(if (-not $isUriLike) { $rawValue -match '\s' } else { $null })
                    }
                    $parsedValue = $null
                    $rawValue = $null
                })

            $result += [pscustomobject]@{
                SettingType = $settingType
                Status = 'available'
                Enabled = $(if ($enabledProperty.Count -eq 1 -and $enabledProperty[0].Value -is [bool]) {
                    [bool]$enabledProperty[0].Value
                } else {
                    $null
                })
                Properties = $properties
            }
        }
        catch {
            $result += [pscustomobject]@{
                SettingType = $settingType
                Status = 'unavailable'
                Enabled = $null
                Properties = @()
                FailureCategory = 'deployment-setting-query-failed'
            }
        }
        finally {
            # Dynamics deployment setting 物件不屬於輸出合約；唯讀投影完成後立即放棄參考。
            if ($setting -is [System.IDisposable]) {
                $setting.Dispose()
            }
            $enabledProperty = $null
            $properties = $null
            $setting = $null
        }
    }

    return $result
}

function Get-IisHttpsEvidence {
    <#
    .SYNOPSIS
      讀取與明確 Web API host 相關的 IIS HTTPS 綁定與憑證中繼資料。

    .DESCRIPTION
      此函式只在本機存在 WebAdministration 時載入模組，並只回傳 HTTPS binding、憑證
      指紋與有效期限。它不會安裝憑證、不會變更 binding，也不會匯出 private material。
      若 IIS 使用 wildcard binding，結果會保留該 binding 讓操作者依完整 CRM stack
      判斷是否相關，而不是由工具臆測設定是否正確。
    #>
    param(
        [Parameter(Mandatory)]
        [uri]$RootUri
    )

    if ($null -eq (Get-Module -ListAvailable -Name 'WebAdministration')) {
        return [pscustomobject]@{
            Status = 'module-unavailable'
            Bindings = @()
            Certificates = @()
        }
    }

    $certificateObjects = $null
    $certificate = $null
    try {
        Import-Module WebAdministration -ErrorAction Stop
        $escapedHost = [regex]::Escape($RootUri.Host)
        $bindings = @(Get-WebBinding -Protocol 'https' |
            Where-Object {
                $_.bindingInformation -match "(?i)$escapedHost|:\*:443:"
            } |
            Select-Object protocol, bindingInformation, certificateHash)
        $certificates = @()
        $certificateObjects = @(Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction Stop)
        for ($certificateIndex = 0; $certificateIndex -lt $certificateObjects.Count; $certificateIndex++) {
            $certificate = $certificateObjects[$certificateIndex]
            try {
                if ($certificate.Subject -notmatch "(?i)$escapedHost") {
                    continue
                }

                $certificates += [pscustomobject]@{
                    Subject = $certificate.Subject
                    Thumbprint = $certificate.Thumbprint
                    NotAfter = $certificate.NotAfter
                    HasPrivateKey = $certificate.HasPrivateKey
                }
            }
            finally {
                # 憑證 provider 物件包含 native handle；投影成中繼資料後必須立即釋放，不能交給 GC 決定時間。
                if ($certificate -is [System.IDisposable]) {
                    $certificate.Dispose()
                }
                $certificateObjects[$certificateIndex] = $null
                $certificate = $null
            }
        }

        return [pscustomobject]@{
            Status = 'available'
            Bindings = $bindings
            Certificates = $certificates
        }
    }
    catch {
        return [pscustomobject]@{
            Status = 'unavailable'
            Bindings = @()
            Certificates = @()
            FailureCategory = 'iis-evidence-query-failed'
        }
    }
    finally {
        if ($null -ne $certificateObjects) {
            for ($remainingCertificateIndex = 0; $remainingCertificateIndex -lt $certificateObjects.Count; $remainingCertificateIndex++) {
                $remainingCertificate = $certificateObjects[$remainingCertificateIndex]
                if ($remainingCertificate -is [System.IDisposable]) {
                    $remainingCertificate.Dispose()
                    $certificateObjects[$remainingCertificateIndex] = $null
                }
            }
        }
        $certificate = $null
        $certificateObjects = $null
    }
}

function Invoke-CrmWhoAmIProbe {
    <#
    .SYNOPSIS
      對明確的 CRM Web API root 執行一次不讀取本文的 WhoAmI probe。

    .DESCRIPTION
      函式為 probe 建立唯一的 handler、client、request 與 response owner。Proxy 被關閉，
      避免因機器層級代理設定而改變 CRM 目標；目前 Windows 身分只在傳輸層用於這一次
      要求。ResponseHeadersRead 避免緩衝回應本文。無論 HTTP 狀態、取消或傳輸例外，
      finally 都會依反向建立順序釋放每一個資源，避免 socket、handler 或回應物件殘留。
    #>
    param(
        [Parameter(Mandatory)]
        [uri]$RootUri,
        [Parameter(Mandatory)]
        [int]$TimeoutSeconds
    )

    $handler = $null
    $client = $null
    $request = $null
    $response = $null
    try {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.UseDefaultCredentials = $true
        $handler.UseCookies = $false
        $handler.UseProxy = $false
        $handler.AllowAutoRedirect = $false
        $client = [System.Net.Http.HttpClient]::new($handler, $false)
        $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

        $probeUri = [uri]::new($RootUri, 'WhoAmI')
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $probeUri)
        [void]$request.Headers.TryAddWithoutValidation('Accept', 'application/json')
        [void]$request.Headers.TryAddWithoutValidation('OData-Version', '4.0')
        [void]$request.Headers.TryAddWithoutValidation('OData-MaxVersion', '4.0')
        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()

        return [pscustomobject]@{
            Outcome = $(if ($response.IsSuccessStatusCode) { 'success' } else { 'http-status' })
            StatusCode = [int]$response.StatusCode
            AuthenticationSchemes = @($response.Headers.WwwAuthenticate |
                ForEach-Object {
                    if ($_.Scheme -match '^(?i:Negotiate|NTLM|Basic|Bearer)$') {
                        $_.Scheme
                    }
                    else {
                        'other'
                    }
                } |
                Select-Object -Unique)
        }
    }
    catch {
        return [pscustomobject]@{
            Outcome = 'transport-failure'
            FailureCategory = 'whoami-request-failed'
        }
    }
    finally {
        # 釋放順序與建立相反，且 handler 不是 HttpClient 的 owned handler，避免雙重 owner。
        if ($null -ne $response) {
            $response.Dispose()
            $response = $null
        }
        if ($null -ne $request) {
            $request.Dispose()
            $request = $null
        }
        if ($null -ne $client) {
            $client.Dispose()
            $client = $null
        }
        if ($null -ne $handler) {
            $handler.Dispose()
            $handler = $null
        }
    }
}

$rootUri = $null
if (-not [uri]::TryCreate($WebApiRoot, [UriKind]::Absolute, [ref]$rootUri)) {
    throw 'WebApiRoot must be an absolute HTTPS Dynamics Web API root. No CRM request was made.'
}

if ($rootUri.Scheme -ne [uri]::UriSchemeHttps -or
    -not [string]::IsNullOrWhiteSpace($rootUri.UserInfo) -or
    -not [string]::IsNullOrWhiteSpace($rootUri.Query) -or
    -not [string]::IsNullOrWhiteSpace($rootUri.Fragment)) {
    throw 'WebApiRoot must be HTTPS without user information, query, or fragment. No CRM request was made.'
}

$apiPath = $rootUri.AbsolutePath.TrimEnd('/')
if ($apiPath -notmatch '^/api/data/v(8\.2|9\.1)$') {
    throw 'WebApiRoot path must be exactly /api/data/v8.2/ or /api/data/v9.1/. No CRM request was made.'
}

$eventEvidence = $null
$settingsEvidence = $null
$iisEvidence = $null
$probeEvidence = $null
try {
    $eventEvidence = Get-CrmWebUriFormatEvents -StartTime (Get-Date).AddMinutes(-$LookbackMinutes) -MaximumCount $MaxEvents
    $settingsEvidence = @(Get-CrmDeploymentSettingsEvidence)
    $iisEvidence = Get-IisHttpsEvidence -RootUri $rootUri
    if ($ProbeWhoAmI) {
        $probeEvidence = Invoke-CrmWhoAmIProbe -RootUri $rootUri -TimeoutSeconds $RequestTimeoutSeconds
    }
    else {
        $probeEvidence = [pscustomobject]@{
            Outcome = 'not-requested'
        }
    }

    [pscustomobject]@{
        Tool = 'Dynamics CRMWeb IFD diagnostics'
        CollectedAt = Get-Date
        Computer = [Environment]::MachineName
        Identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        WebApiRoot = $rootUri.AbsoluteUri
        ProbeOutcome = $probeEvidence.Outcome
        Probe = $probeEvidence
        AspNet1309 = $eventEvidence
        DeploymentSettings = $settingsEvidence
        IisHttps = $iisEvidence
    }
}
finally {
    # 最終輸出建立後不保留事件、設定、IIS 或 HTTP 結果的額外參考，所有跨呼叫資源都有各自 owner。
    $eventEvidence = $null
    $settingsEvidence = $null
    $iisEvidence = $null
    $probeEvidence = $null
    $rootUri = $null
}
