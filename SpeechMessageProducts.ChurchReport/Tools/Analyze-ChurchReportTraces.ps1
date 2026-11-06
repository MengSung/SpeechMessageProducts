<#
檔案整體說明

目的與定位：合併分析ChurchReport的Dataverse、應用效能與舊ToolUtility追蹤，按既有事件識別關聯資料以定位慢請求；輸出是診斷彙總，不代表可公開原始業務本文。

主要功能與流程：
操作入口包含Resolve-InputPath、New-TraceReader、Get-RecordValue、Convert-ToLong、Convert-ToLocalTimestamp、Update-TimeRange、Update-SensitiveCounts、Get-SensitiveTotal、Convert-ToSafeLabel、Add-Reason；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
    Analyzes ChurchReport Dataverse, application-performance, and legacy ToolUtility traces together.

.DESCRIPTION
    Opens inputs only with FileShare.ReadWrite/Delete and builds bounded, line-by-line aggregates.
    It never deletes, truncates, rotates, or modifies source traces. Reports contain statistics and
    de-identified summaries only; raw trace lines, tokens, credentials, emails, phone numbers, GUIDs,
    and request/lease identifiers are never copied into the report.

    PASS requires all three files and enough parseable evidence. Missing or insufficient data is WARN.
    Parse failures, unpaired requests/leases, pool-isolation violations, or sensitive-pattern hits are
    FAIL with exit code 2. Analyzer/report-generation failure uses exit code 1.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Analyze-ChurchReportTraces.ps1 `
      -TraceDirectory '<configured trace directory>' `
      -ReportPath '<configured trace directory>\ChurchReport-Trace-Report.md'
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$TraceDirectory = ('D:\' + ([char]0x9664) + ([char]0x932F) + ([char]0x8FFD) + ([char]0x8E64)),

    [Parameter()]
    [string]$DataverseTracePath,

    [Parameter()]
    [string]$ApplicationTracePath,

    [Parameter()]
    [string]$ToolUtilityTracePath,

    [Parameter()]
    [string]$ReportPath,

    [Parameter()]
    [ValidateRange(1, 100)]
    [int]$Top = 20,

    [Parameter()]
    [ValidateRange(100, 1000000)]
    [int]$MaxPairEntries = 100000,

    [Parameter()]
    [ValidateRange(1, 3600000)]
    [int]$SlowRequestThresholdMs = 1000,

    # 單一 request 內同一張 entity 被查詢幾次即視為 N+1 徵兆。
    # 預設 5：正常的表單頁面通常只會對同一張表查一到兩次，連續 5 次以上幾乎必然是迴圈查詢。
    [Parameter()]
    [ValidateRange(2, 1000)]
    [int]$NPlusOneThreshold = 5
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 已內建 code page 950；PowerShell 7/.NET Core 則需註冊
# CodePages provider 才能可靠建立 Big5 reader。註冊本身是程序級、無檔案副作用，
# 且在 Framework 執行環境不存在 provider 時安全略過。
Set-Variable -Name registerProvider -Value $null -Force
Set-Variable -Name providerType -Value $null -Force
try {
    $registerProvider = (([System.Text.Encoding]).GetMethods() |
        Where-Object { $_.Name -eq 'RegisterProvider' } |
        Select-Object -First 1)
    $providerAssembly = [System.Reflection.Assembly]::Load('System.Text.Encoding.CodePages')
    $providerType = $providerAssembly.GetType('System.Text.CodePagesEncodingProvider', $false)
}
catch {
    # Windows PowerShell 5.1 already exposes code page 950; older Framework runtimes
    # may not expose the .NET Core provider and can continue without registration.
}
if ($null -ne $registerProvider -and $null -ne $providerType) {
    $provider = $providerType.GetProperty('Instance').GetValue($null, $null)
    [void]$registerProvider.Invoke($null, @($provider))
}

function Resolve-InputPath {
    param(
        [string]$ExplicitPath,
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($ExplicitPath)) {
        Join-Path -Path $Directory -ChildPath $FileName
    }
    else {
        $ExplicitPath
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function New-TraceReader {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Text.Encoding]$Encoding
    )

    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete),
        65536,
        [System.IO.FileOptions]::SequentialScan)

    try {
        return New-Object System.IO.StreamReader($stream, $Encoding, $true, 65536, $false)
    }
    catch {
        $stream.Dispose()
        throw
    }
}

function Get-RecordValue {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Record.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Convert-ToLong {
    param($Value)

    $parsed = 0L
    if ($null -ne $Value -and [long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed)) {
        return $parsed
    }

    return 0L
}

function Convert-ToLocalTimestamp {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AllowWhiteSpaces,
        [ref]$parsed)) {
        return $parsed.LocalDateTime
    }

    return $null
}

function Update-TimeRange {
    param(
        [Parameter(Mandatory = $true)]$State,
        $Timestamp
    )

    if ($null -eq $Timestamp) {
        return
    }

    if ($null -eq $State.StartTime -or $Timestamp -lt $State.StartTime) {
        $State.StartTime = $Timestamp
    }
    if ($null -eq $State.EndTime -or $Timestamp -gt $State.EndTime) {
        $State.EndTime = $Timestamp
    }
}

function Update-SensitiveCounts {
    param(
        [Parameter(Mandatory = $true)][string]$Line,
        [Parameter(Mandatory = $true)]$Counts
    )

    # Retain counts only. Never retain a match or raw line, so the report cannot become a second sensitive-data store.
    $patterns = @{
        ([regex]::Unescape('\u654f\u611f\u6b04\u4f4d\u503c')) = '(?i)\b(?:password|passwd|pwd|token|secret|authorization|cookie|credential|username|email|phone|mobile|address)\b[^:=\r\n]{0,3}[:=]\s*(?!"?(?:null|none|"))\S+'
        ([regex]::Unescape('Bearer\uff0fJWT')) = '(?i)\bBearer\s+[A-Za-z0-9._~+/-]{12,}|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        ([regex]::Unescape('\u96fb\u5b50\u90f5\u4ef6')) = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b'
        ([regex]::Unescape('\u81fa\u7063\u8eab\u5206\u8b49\u5b57\u865f\u683c\u5f0f')) = '(?i)\b[A-Z][12]\d{8}\b'
    }

    foreach ($name in $patterns.Keys) {
        if ([regex]::IsMatch($Line, $patterns[$name])) {
            if ($Counts.ContainsKey($name)) {
                $Counts[$name]++
            }
            else {
                $Counts[$name] = 1L
            }
        }
    }
}

function Get-SensitiveTotal {
    param([Parameter(Mandatory = $true)]$Counts)

    $total = 0L
    foreach ($value in $Counts.Values) {
        $total += [long]$value
    }
    return $total
}

function Convert-ToSafeLabel {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ([regex]::Unescape('(\u7a7a\u767d)'))
    }

    $safe = $Value -replace '[\r\n|]', ' '
    $safe = $safe -replace '\?.*$', '?...'
    $safe = $safe -replace '(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b', '<GUID>'
    $safe = $safe -replace '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b', '<EMAIL>'
    $safe = $safe -replace '\b\d{6,}\b', '<NUMBER>'
    if ($safe.Length -gt 120) {
        $safe = $safe.Substring(0, 120) + '...'
    }
    return $safe
}

function Add-Reason {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][ValidateSet('WARN', 'FAIL')][string]$Severity,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Severity -eq 'FAIL') {
        $Result.Status = 'FAIL'
    }
    elseif ($Result.Status -eq 'PASS') {
        $Result.Status = 'WARN'
    }
    [void]$Result.Reasons.Add($Message)
}

function New-BaseResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return [pscustomobject]@{
        Name = $Name
        Path = $Path
        Exists = $false
        Length = 0L
        LastWriteTime = $null
        Status = 'PASS'
        Reasons = New-Object 'System.Collections.Generic.List[string]'
        Lines = 0L
        StartTime = $null
        EndTime = $null
        ReadError = $null
        SensitiveCounts = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::OrdinalIgnoreCase)
    }
}

function Analyze-DataverseTrace {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$PairLimit,
        [Parameter(Mandatory = $true)][int]$SlowRequestMs,
        [Parameter(Mandatory = $true)][int]$NPlusOneThreshold,
        [Parameter(Mandatory = $true)][int]$EndpointLimit
    )

    $result = New-BaseResult -Name 'Dataverse JSONL' -Path $Path
    $result | Add-Member NoteProperty Parsed ([long]0)
    $result | Add-Member NoteProperty ParseErrors ([long]0)
    $result | Add-Member NoteProperty EventCounts (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::Ordinal))
    $result | Add-Member NoteProperty MissingRequestEnds ([long]0)
    $result | Add-Member NoteProperty OrphanRequestEnds ([long]0)
    $result | Add-Member NoteProperty MissingReturns ([long]0)
    $result | Add-Member NoteProperty OrphanReturns ([long]0)
    $result | Add-Member NoteProperty PairOverflow ([long]0)
    $result | Add-Member NoteProperty RequestDurationCount ([long]0)
    $result | Add-Member NoteProperty RequestDurationSum ([long]0)
    $result | Add-Member NoteProperty RequestDurationMax ([long]0)
    $result | Add-Member NoteProperty AcquireWaitCount ([long]0)
    $result | Add-Member NoteProperty AcquireWaitSum ([long]0)
    $result | Add-Member NoteProperty AcquireWaitMax ([long]0)
    $result | Add-Member NoteProperty HeldCount ([long]0)
    $result | Add-Member NoteProperty HeldSum ([long]0)
    $result | Add-Member NoteProperty HeldMax ([long]0)
    $result | Add-Member NoteProperty HealthFailures ([long]0)
    $result | Add-Member NoteProperty Timeouts ([long]0)
    $result | Add-Member NoteProperty CleanupBelowMinSnapshots ([long]0)
    $result | Add-Member NoteProperty CallerStateViolations ([long]0)
    $result | Add-Member NoteProperty DroppedEvents ([long]0)
    $result | Add-Member NoteProperty InvalidPseudonyms ([long]0)
    $result | Add-Member NoteProperty UniquePseudonyms ([long]0)

    # ---- 故障淘汰：例外分類是否生效的直接證據 ----
    $result | Add-Member NoteProperty ReturnHealthy ([long]0)
    $result | Add-Member NoteProperty ReturnFaulted ([long]0)
    $result | Add-Member NoteProperty DisposeByReason (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::Ordinal))

    # ---- 建線可觀測性：wait 必須能被 hit/miss/fail 完整解釋 ----
    $result | Add-Member NoteProperty AcquireHits ([long]0)
    $result | Add-Member NoteProperty AcquireMisses ([long]0)
    $result | Add-Member NoteProperty AcquireFails ([long]0)
    $result | Add-Member NoteProperty AcquireFailByPhase (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::Ordinal))
    $result | Add-Member NoteProperty CreateOk ([long]0)
    $result | Add-Member NoteProperty CreateFailed ([long]0)
    $result | Add-Member NoteProperty CreateMsSum ([long]0)
    $result | Add-Member NoteProperty CreateMsMax ([long]0)
    $result | Add-Member NoteProperty ErrorKinds (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::Ordinal))

    # ---- 效能歸因與 N+1 ----
    $result | Add-Member NoteProperty CrmOpCount ([long]0)
    $result | Add-Member NoteProperty CrmMsTotal ([long]0)
    $result | Add-Member NoteProperty CrmFailures ([long]0)
    $result | Add-Member NoteProperty RequestCrmMsSum ([long]0)
    $result | Add-Member NoteProperty EntityStats (New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal))
    $result | Add-Member NoteProperty EntityOverflow ([long]0)
    $result | Add-Member NoteProperty NPlusOneRequests (New-Object 'System.Collections.Generic.List[object]')
    $result | Add-Member NoteProperty SlowRequests (New-Object 'System.Collections.Generic.List[object]')

    # ---- Session Leakage ----
    $result | Add-Member NoteProperty LeaseOutstandingRequests ([long]0)
    $result | Add-Member NoteProperty LeaseOutstandingMax ([long]0)
    $result | Add-Member NoteProperty ConcurrentGatewayEvents ([long]0)
    $result | Add-Member NoteProperty ConcurrentGatewayRequests ([long]0)
    $result | Add-Member NoteProperty ConcurrentGatewayMax ([long]0)
    $result | Add-Member NoteProperty ScopeEndLeaseHeld ([long]0)
    $result | Add-Member NoteProperty MaxDepthObserved ([long]0)
    $result | Add-Member NoteProperty LeaseOverlaps ([long]0)

    # ---- 資源趨勢：記憶體洩漏的唯一證據來源 ----
    $result | Add-Member NoteProperty ProcFirst $null
    $result | Add-Member NoteProperty ProcLast $null
    $result | Add-Member NoteProperty ProcSamples ([long]0)
    $result | Add-Member NoteProperty PoolFirst $null
    $result | Add-Member NoteProperty PoolLast $null
    $result | Add-Member NoteProperty PoolSamples ([long]0)
    $result | Add-Member NoteProperty PoolAliveMax ([long]0)
    $result | Add-Member NoteProperty SubPoolsMax ([long]0)

    # ---- 鎖競爭回歸哨兵 ----
    $result | Add-Member NoteProperty LockWaitCount ([long]0)
    $result | Add-Member NoteProperty LockWaitMax ([long]0)
    $result | Add-Member NoteProperty CleanupEvicted ([long]0)

    if (-not [System.IO.File]::Exists($Path)) {
        Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u627e\u4e0d\u5230\u6a94\u6848\uff1b\u4e09\u6a94\u8b49\u64da\u96c6\u5408\u4e0d\u5b8c\u6574\u3002'))
        return $result
    }

    $item = Get-Item -LiteralPath $Path
    $result.Exists = $true
    $result.Length = $item.Length
    $result.LastWriteTime = $item.LastWriteTime

    $requestBegins = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $requestEnds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $acquired = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $returned = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $users = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    # clientId -> 目前未歸還的 leaseId。用於偵測同一條實體連線的租約時間區間重疊：
    # 一旦重疊，就代表兩個 request 同時持有同一條連線，這是 Session Leakage 的終極判準。
    $leasedClients = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    $leaseToClient = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $reader = $null

    try {
        $reader = New-TraceReader -Path $Path -Encoding $utf8
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            $result.Lines++
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            Update-SensitiveCounts -Line $line -Counts $result.SensitiveCounts
            try {
                $record = $line | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                $result.ParseErrors++
                continue
            }

            $result.Parsed++
            Update-TimeRange -State $result -Timestamp (Convert-ToLocalTimestamp (Get-RecordValue -Record $record -Name 'ts'))

            $eventName = [string](Get-RecordValue -Record $record -Name 'ev')
            if ([string]::IsNullOrWhiteSpace($eventName)) {
                $eventName = ([regex]::Unescape('(\u7f3a\u5c11 ev)'))
            }
            if ($result.EventCounts.ContainsKey($eventName)) {
                $result.EventCounts[$eventName]++
            }
            else {
                $result.EventCounts[$eventName] = 1L
            }

            $traceId = [string](Get-RecordValue -Record $record -Name 'traceId')
            $leaseId = [string](Get-RecordValue -Record $record -Name 'leaseId')
            $user = [string](Get-RecordValue -Record $record -Name 'user')
            if (-not [string]::IsNullOrWhiteSpace($user)) {
                if ($user -notmatch '^u_[0-9a-f]{8}$') {
                    $result.InvalidPseudonyms++
                }
                elseif ($users.Count -lt $PairLimit) {
                    [void]$users.Add($user)
                }
                else {
                    $result.PairOverflow++
                }
            }

            switch ($eventName) {
                'request.begin' {
                    if (-not [string]::IsNullOrWhiteSpace($traceId)) {
                        if ($requestBegins.Count -lt $PairLimit) { [void]$requestBegins.Add($traceId) } else { $result.PairOverflow++ }
                    }
                }
                'request.end' {
                    if (-not [string]::IsNullOrWhiteSpace($traceId)) {
                        if ($requestEnds.Count -lt $PairLimit) { [void]$requestEnds.Add($traceId) } else { $result.PairOverflow++ }
                    }
                    $duration = Convert-ToLong (Get-RecordValue -Record $record -Name 'durationMs')
                    $result.RequestDurationCount++
                    $result.RequestDurationSum += $duration
                    if ($duration -gt $result.RequestDurationMax) { $result.RequestDurationMax = $duration }

                    $crmMs = Convert-ToLong (Get-RecordValue -Record $record -Name 'crmMs')
                    $result.RequestCrmMsSum += $crmMs

                    # 租約洩漏：request 結束時仍未歸還的租約數，正常恆為 0。
                    $outstanding = Convert-ToLong (Get-RecordValue -Record $record -Name 'leaseOutstanding')
                    if ($outstanding -gt 0) {
                        $result.LeaseOutstandingRequests++
                        if ($outstanding -gt $result.LeaseOutstandingMax) { $result.LeaseOutstandingMax = $outstanding }
                    }

                    # 平行 Gateway：同一個 scoped Gateway 被多執行緒同時進入，是連線共用的前兆。
                    if ((Convert-ToLong (Get-RecordValue -Record $record -Name 'concurrentGateway')) -gt 0) {
                        $result.ConcurrentGatewayRequests++
                    }

                    $depth = Convert-ToLong (Get-RecordValue -Record $record -Name 'maxDepth')
                    if ($depth -gt $result.MaxDepthObserved) { $result.MaxDepthObserved = $depth }

                    $topCount = Convert-ToLong (Get-RecordValue -Record $record -Name 'topEntityCount')
                    if ($topCount -ge $NPlusOneThreshold -and $result.NPlusOneRequests.Count -lt $EndpointLimit) {
                        [void]$result.NPlusOneRequests.Add([pscustomobject]@{
                            TraceId    = Convert-ToSafeLabel $traceId
                            Entity     = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'topEntity'))
                            TopCount   = $topCount
                            CrmCount   = Convert-ToLong (Get-RecordValue -Record $record -Name 'crmCount')
                            CrmMs      = $crmMs
                            DurationMs = $duration
                        })
                    }

                    if ($duration -ge $SlowRequestMs -and $result.SlowRequests.Count -lt $EndpointLimit) {
                        [void]$result.SlowRequests.Add([pscustomobject]@{
                            TraceId    = Convert-ToSafeLabel $traceId
                            DurationMs = $duration
                            CrmMs      = $crmMs
                            AppMs      = [Math]::Max(0, $duration - $crmMs)
                            CrmCount   = Convert-ToLong (Get-RecordValue -Record $record -Name 'crmCount')
                            TopEntity  = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'topEntity'))
                            TopCount   = $topCount
                        })
                    }
                }
                'pool.acquire.hit' {
                    $result.AcquireHits++
                    if (-not [string]::IsNullOrWhiteSpace($leaseId)) {
                        if ($acquired.Count -lt $PairLimit) { [void]$acquired.Add($leaseId) } else { $result.PairOverflow++ }
                        $clientId = [string](Get-RecordValue -Record $record -Name 'clientId')
                        if (-not [string]::IsNullOrWhiteSpace($clientId)) {
                            # 同一個 clientId 在前一條租約歸還前又被租出去，代表連線被兩個 request 共用。
                            if ($leasedClients.ContainsKey($clientId)) { $result.LeaseOverlaps++ }
                            $leasedClients[$clientId] = $leaseId
                            if ($leaseToClient.Count -lt $PairLimit) { $leaseToClient[$leaseId] = $clientId }
                        }
                    }
                }
                'pool.acquire.miss' {
                    $result.AcquireMisses++
                    if (-not [string]::IsNullOrWhiteSpace($leaseId)) {
                        if ($acquired.Count -lt $PairLimit) { [void]$acquired.Add($leaseId) } else { $result.PairOverflow++ }
                        $clientId = [string](Get-RecordValue -Record $record -Name 'clientId')
                        if (-not [string]::IsNullOrWhiteSpace($clientId)) {
                            if ($leasedClients.ContainsKey($clientId)) { $result.LeaseOverlaps++ }
                            $leasedClients[$clientId] = $leaseId
                            if ($leaseToClient.Count -lt $PairLimit) { $leaseToClient[$leaseId] = $clientId }
                        }
                    }
                }
                'pool.acquire.fail' {
                    # 建線失敗事件。加入之前，失敗的 acquire 只留下一筆 wait 而無任何結果，
                    # 最慢的那些 request 在稽核檔中形同消失。
                    $result.AcquireFails++
                    $phase = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'phase'))
                    if ([string]::IsNullOrWhiteSpace($phase)) { $phase = '(unknown)' }
                    if ($result.AcquireFailByPhase.ContainsKey($phase)) { $result.AcquireFailByPhase[$phase]++ }
                    else { $result.AcquireFailByPhase[$phase] = [long]1 }
                    $kind = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'errKind'))
                    if (-not [string]::IsNullOrWhiteSpace($kind)) {
                        if ($result.ErrorKinds.ContainsKey($kind)) { $result.ErrorKinds[$kind]++ }
                        else { $result.ErrorKinds[$kind] = [long]1 }
                    }
                }
                'pool.create.end' {
                    # 建線耗時。此段目前雖已移出子池鎖，數值仍是冷啟動延遲的主要成分。
                    $ms = Convert-ToLong (Get-RecordValue -Record $record -Name 'ms')
                    $result.CreateMsSum += $ms
                    if ($ms -gt $result.CreateMsMax) { $result.CreateMsMax = $ms }
                    if ((Get-RecordValue -Record $record -Name 'ok') -eq $true) { $result.CreateOk++ }
                    else {
                        $result.CreateFailed++
                        $kind = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'errKind'))
                        if (-not [string]::IsNullOrWhiteSpace($kind)) {
                            if ($result.ErrorKinds.ContainsKey($kind)) { $result.ErrorKinds[$kind]++ }
                            else { $result.ErrorKinds[$kind] = [long]1 }
                        }
                    }
                }
                'pool.lock.wait' {
                    $result.LockWaitCount++
                    $waited = Convert-ToLong (Get-RecordValue -Record $record -Name 'waitedMs')
                    if ($waited -gt $result.LockWaitMax) { $result.LockWaitMax = $waited }
                }
                'gateway.concurrent' {
                    $result.ConcurrentGatewayEvents++
                    $active = Convert-ToLong (Get-RecordValue -Record $record -Name 'activeCalls')
                    if ($active -gt $result.ConcurrentGatewayMax) { $result.ConcurrentGatewayMax = $active }
                }
                'gateway.scope.end' {
                    if ((Get-RecordValue -Record $record -Name 'leaseStillHeld') -eq $true) { $result.ScopeEndLeaseHeld++ }
                }
                'crm.op' {
                    # entity 與 ms 是分辨「一個慢查詢」與「同一張表被查很多次」的唯一依據，
                    # 這兩者的處置方式完全相反。
                    $result.CrmOpCount++
                    $ms = Convert-ToLong (Get-RecordValue -Record $record -Name 'ms')
                    $result.CrmMsTotal += $ms
                    if ((Get-RecordValue -Record $record -Name 'ok') -eq $false) { $result.CrmFailures++ }
                    $entity = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'entity'))
                    if (-not [string]::IsNullOrWhiteSpace($entity)) {
                        if ($result.EntityStats.ContainsKey($entity)) {
                            $stat = $result.EntityStats[$entity]
                            $stat.Count++
                            $stat.Ms += $ms
                            if ($ms -gt $stat.Max) { $stat.Max = $ms }
                        }
                        elseif ($result.EntityStats.Count -lt $PairLimit) {
                            $result.EntityStats[$entity] = [pscustomobject]@{ Count = [long]1; Ms = [long]$ms; Max = [long]$ms }
                        }
                        else { $result.EntityOverflow++ }
                    }
                }
                'proc.snapshot' {
                    # 只保留首尾兩筆即可檢定單調成長，避免長時間執行時把整串樣本留在記憶體。
                    $result.ProcSamples++
                    $sample = [pscustomobject]@{
                        Ts        = [string](Get-RecordValue -Record $record -Name 'ts')
                        ManagedMb = Convert-ToLong (Get-RecordValue -Record $record -Name 'managedMb')
                        HeapMb    = Convert-ToLong (Get-RecordValue -Record $record -Name 'heapMb')
                        PrivateMb = Convert-ToLong (Get-RecordValue -Record $record -Name 'privateMb')
                        Gen2      = Convert-ToLong (Get-RecordValue -Record $record -Name 'gen2')
                        Handles   = Convert-ToLong (Get-RecordValue -Record $record -Name 'handles')
                        Threads   = Convert-ToLong (Get-RecordValue -Record $record -Name 'threads')
                        Pending   = Convert-ToLong (Get-RecordValue -Record $record -Name 'pendingWorkItems')
                    }
                    if ($null -eq $result.ProcFirst) { $result.ProcFirst = $sample }
                    $result.ProcLast = $sample
                }
                'pool.snapshot' {
                    $result.PoolSamples++
                    $sample = [pscustomobject]@{
                        Ts        = [string](Get-RecordValue -Record $record -Name 'ts')
                        Idle      = Convert-ToLong (Get-RecordValue -Record $record -Name 'idle')
                        Leased    = Convert-ToLong (Get-RecordValue -Record $record -Name 'leased')
                        Alive     = Convert-ToLong (Get-RecordValue -Record $record -Name 'alive')
                        Pending   = Convert-ToLong (Get-RecordValue -Record $record -Name 'pending')
                        Created   = Convert-ToLong (Get-RecordValue -Record $record -Name 'created')
                        Discarded = Convert-ToLong (Get-RecordValue -Record $record -Name 'discarded')
                        Acquires  = Convert-ToLong (Get-RecordValue -Record $record -Name 'totalAcquires')
                        Releases  = Convert-ToLong (Get-RecordValue -Record $record -Name 'totalReleases')
                        SubPools  = Convert-ToLong (Get-RecordValue -Record $record -Name 'subPools')
                    }
                    if ($null -eq $result.PoolFirst) { $result.PoolFirst = $sample }
                    $result.PoolLast = $sample
                    if ($sample.Alive -gt $result.PoolAliveMax) { $result.PoolAliveMax = $sample.Alive }
                    if ($sample.SubPools -gt $result.SubPoolsMax) { $result.SubPoolsMax = $sample.SubPools }
                }
                'pool.return' {
                    if (-not [string]::IsNullOrWhiteSpace($leaseId)) {
                        if ($returned.Count -lt $PairLimit) { [void]$returned.Add($leaseId) } else { $result.PairOverflow++ }
                    }
                    $held = Convert-ToLong (Get-RecordValue -Record $record -Name 'heldMs')
                    $result.HeldCount++
                    $result.HeldSum += $held
                    if ($held -gt $result.HeldMax) { $result.HeldMax = $held }
                    if (-not [string]::IsNullOrWhiteSpace([string](Get-RecordValue -Record $record -Name 'callerIdAtReturn'))) {
                        $result.CallerStateViolations++
                    }
                    # faulted 歸還代表該連線被銷毀並重建。例外分類修正前，一個打錯的欄位名
                    # 會讓 5.7% 的操作走上這條路徑，形成不必要的連線抖動。
                    if ([string](Get-RecordValue -Record $record -Name 'state') -eq 'faulted') { $result.ReturnFaulted++ }
                    else { $result.ReturnHealthy++ }
                    if (-not [string]::IsNullOrWhiteSpace($leaseId) -and $leaseToClient.ContainsKey($leaseId)) {
                        [void]$leasedClients.Remove($leaseToClient[$leaseId])
                        [void]$leaseToClient.Remove($leaseId)
                    }
                }
                'pool.acquire.wait' {
                    $waited = Convert-ToLong (Get-RecordValue -Record $record -Name 'waitedMs')
                    $result.AcquireWaitCount++
                    $result.AcquireWaitSum += $waited
                    if ($waited -gt $result.AcquireWaitMax) { $result.AcquireWaitMax = $waited }
                }
                'pool.acquire.timeout' { $result.Timeouts++ }
                'pool.health' {
                    $health = Get-RecordValue -Record $record -Name 'result'
                    if ($health -ne $true) { $result.HealthFailures++ }
                }
                'pool.cleanup' {
                    # 舊規則只看 idleAfter < minSize，但池中沒有閒置連線時（idle = 0）該條件恆為真，
                    # 實測產生 158 筆假陽性。真正的越線形狀是「淘汰前高於保底、淘汰後低於保底」。
                    $idleBefore = Convert-ToLong (Get-RecordValue -Record $record -Name 'idleBefore')
                    $idleAfter = Convert-ToLong (Get-RecordValue -Record $record -Name 'idleAfter')
                    $minSize = Convert-ToLong (Get-RecordValue -Record $record -Name 'minSize')
                    $result.CleanupEvicted += Convert-ToLong (Get-RecordValue -Record $record -Name 'evicted')
                    if ($idleAfter -lt $minSize -and $idleBefore -gt $minSize) { $result.CleanupBelowMinSnapshots++ }
                }
                'pool.dispose' {
                    $reason = Convert-ToSafeLabel ([string](Get-RecordValue -Record $record -Name 'reason'))
                    if ([string]::IsNullOrWhiteSpace($reason)) { $reason = '(unknown)' }
                    if ($result.DisposeByReason.ContainsKey($reason)) { $result.DisposeByReason[$reason]++ }
                    else { $result.DisposeByReason[$reason] = [long]1 }
                }
                'trace.dropped' { $result.DroppedEvents += Convert-ToLong (Get-RecordValue -Record $record -Name 'count') }
            }
        }
    }
    catch {
        $result.ReadError = $_.Exception.Message
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    $result.UniquePseudonyms = $users.Count
    if ($result.PairOverflow -eq 0) {
        foreach ($id in $requestBegins) { if (-not $requestEnds.Contains($id)) { $result.MissingRequestEnds++ } }
        foreach ($id in $requestEnds) { if (-not $requestBegins.Contains($id)) { $result.OrphanRequestEnds++ } }
        foreach ($id in $acquired) { if (-not $returned.Contains($id)) { $result.MissingReturns++ } }
        foreach ($id in $returned) { if (-not $acquired.Contains($id)) { $result.OrphanReturns++ } }
    }

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u6a94\u6848\u7121\u6cd5\u5b8c\u6574\u89e3\u78bc\u6216\u8b80\u53d6\u3002')) }
    if ($result.Parsed -eq 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u6c92\u6709\u53ef\u89e3\u6790\u7684 JSONL \u4e8b\u4ef6\uff1b\u8b49\u64da\u4e0d\u8db3\u3002')) }
    if ($result.ParseErrors -gt 0) { Add-Reason -Result $result -Severity FAIL -Message (([regex]::Unescape('{0} \u884c JSONL \u7121\u6cd5\u89e3\u6790\u3002')) -f $result.ParseErrors) }
    if ($result.PairOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u914d\u5c0d\u8ffd\u8e64\u8d85\u904e\u8a18\u61b6\u9ad4\u4e0a\u9650\uff1b\u914d\u5c0d\u7d50\u679c\u50c5\u662f\u90e8\u5206\u8b49\u64da\u3002')) }
    if (($result.MissingRequestEnds + $result.OrphanRequestEnds) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('request.begin\uff0frequest.end \u4e8b\u4ef6\u672a\u5b8c\u6574\u914d\u5c0d\u3002')) }
    if (($result.MissingReturns + $result.OrphanReturns) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u79df\u7d04\u53d6\u5f97\uff0f\u6b78\u9084\u4e8b\u4ef6\u672a\u5b8c\u6574\u914d\u5c0d\u3002')) }
    if ($result.HealthFailures -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u5075\u6e2c\u5230 Pool \u5065\u5eb7\u6aa2\u67e5\u5931\u6557\u3002')) }
    if ($result.CallerStateViolations -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u6b78\u9084\u7684\u79df\u7d04\u4ecd\u4fdd\u7559\u547c\u53eb\u7aef\u72c0\u614b\u3002')) }
    if ($result.InvalidPseudonyms -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u4f7f\u7528\u8005\u6b04\u4f4d\u503c\u4e0d\u7b26\u5408\u77ed\u671f\u865b\u64ec\u8b58\u5225\u78bc\u683c\u5f0f\u3002')) }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u5075\u6e2c\u5230\u7591\u4f3c\u654f\u611f\u8cc7\u6599\u6a21\u5f0f\uff1b\u5831\u544a\u5df2\u7701\u7565\u539f\u59cb\u503c\u3002')) }
    if ($result.Timeouts -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u5075\u6e2c\u5230 Pool \u53d6\u5f97\u903e\u6642\u3002')) }

    # ================= Session Leakage：由重到輕 =================
    if ($result.LeaseOverlaps -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message (([regex]::Unescape('\u540c\u4e00\u689d\u5be6\u9ad4\u9023\u7dda\u51fa\u73fe {0:N0} \u6b21\u79df\u7d04\u5340\u9593\u91cd\u758a\uff1b\u66fe\u6709\u5169\u500b request \u540c\u6642\u6301\u6709\u540c\u4e00\u689d\u9023\u7dda\u3002')) -f $result.LeaseOverlaps)
    }
    if ($result.LeaseOutstandingRequests -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message (([regex]::Unescape('{0:N0} \u500b request \u7d50\u675f\u6642\u4ecd\u6709\u672a\u6b78\u9084\u79df\u7d04\uff08\u6700\u591a {1:N0} \u689d\uff09\uff1b\u79df\u7d04\u6d29\u6f0f\u3002')) -f $result.LeaseOutstandingRequests, $result.LeaseOutstandingMax)
    }
    if ($result.ConcurrentGatewayEvents -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message (([regex]::Unescape('\u5075\u6e2c\u5230 {0:N0} \u6b21\u5e73\u884c\u9032\u5165\u540c\u4e00\u500b scoped Gateway\uff08\u6700\u9ad8\u540c\u6642 {1:N0} \u689d\uff09\uff1bGateway \u7684 depth \u8207 lease \u6b04\u4f4d\u7121\u540c\u6b65\u4fdd\u8b77\uff0c\u6b64\u8def\u5f91\u53ef\u80fd\u9020\u6210\u8de8 request \u5171\u7528\u9023\u7dda\u3002')) -f $result.ConcurrentGatewayEvents, $result.ConcurrentGatewayMax)
    }
    if ($result.ScopeEndLeaseHeld -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('{0:N0} \u6b21 Gateway \u91cb\u653e\u6642\u4ecd\u6301\u6709\u79df\u7d04\uff1b\u79df\u7d04\u662f\u9760 DI \u56de\u6536 scope \u624d\u88ab\u6551\u56de\uff0c\u800c\u975e\u6b63\u5e38\u57f7\u884c\u8def\u5f91\u3002')) -f $result.ScopeEndLeaseHeld)
    }

    # ================= 建線可觀測性不變量 =================
    $acquireResults = $result.AcquireHits + $result.AcquireMisses + $result.AcquireFails
    if ($result.AcquireWaitCount -ne $acquireResults) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u53d6\u5f97\u7b49\u5f85 {0:N0} \u7b46\u7121\u6cd5\u88ab\u7d50\u679c\u5b8c\u6574\u89e3\u91cb\uff08hit + miss + fail = {1:N0}\uff09\uff1b\u6709 acquire \u672a\u7559\u4e0b\u7d50\u679c\u4e8b\u4ef6\u3002')) -f $result.AcquireWaitCount, $acquireResults)
    }
    if ($result.CreateFailed -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u5efa\u7acb\u9023\u7dda\u5931\u6557 {0:N0} \u6b21\uff1b\u8acb\u6aa2\u8996\u932f\u8aa4\u7a2e\u985e\u5206\u4f48\u3002')) -f $result.CreateFailed)
    }

    # ================= 故障淘汰比率 =================
    $returnTotal = $result.ReturnHealthy + $result.ReturnFaulted
    if ($returnTotal -gt 0) {
        $faultRate = [double]$result.ReturnFaulted / [double]$returnTotal
        if ($faultRate -ge 0.01) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u79df\u7d04\u4ee5 faulted \u6b78\u9084\u7684\u6bd4\u7387\u70ba {0:P1}\uff08{1:N0}/{2:N0}\uff09\uff1b\u6bcf\u4e00\u6b21\u90fd\u6703\u92b7\u6bc0\u4e26\u91cd\u5efa\u4e00\u689d\u9023\u7dda\u3002')) -f $faultRate, $result.ReturnFaulted, $returnTotal)
        }
    }

    # ================= 效能 =================
    if ($result.NPlusOneRequests.Count -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u5075\u6e2c\u5230 N+1 \u5fb5\u5146\uff1a\u6709 request \u5c0d\u540c\u4e00\u5f35 entity \u67e5\u8a62\u9054 {0:N0} \u6b21\u4ee5\u4e0a\u3002')) -f $NPlusOneThreshold)
    }
    if ($result.LockWaitCount -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u5075\u6e2c\u5230 {0:N0} \u6b21\u5b50\u6c60\u9396\u7b49\u5f85\uff08\u6700\u9577 {1:N0} \u6beb\u79d2\uff09\uff1b\u5efa\u7dda\u79fb\u51fa\u9396\u4e4b\u5f8c\u6b64\u4e8b\u4ef6\u4e0d\u61c9\u51fa\u73fe\uff0c\u5c6c\u56de\u6b78\u5fb5\u5146\u3002')) -f $result.LockWaitCount, $result.LockWaitMax)
    }
    if ($result.CrmFailures -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('{0:N0} \u6b21 CRM \u64cd\u4f5c\u4ee5\u5931\u6557\u7d50\u675f\u3002')) -f $result.CrmFailures)
    }

    # ================= 資源趨勢 =================
    if ($result.ProcSamples -lt 2) {
        Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u7a0b\u5e8f\u8cc7\u6e90\u5feb\u7167\u5c11\u65bc\u5169\u7b46\uff1b\u7121\u6cd5\u6aa2\u5b9a\u8a18\u61b6\u9ad4\u6216\u63a7\u5236\u4ee3\u78bc\u662f\u5426\u55ae\u8abf\u6210\u9577\uff0c\u672c\u6b21\u57f7\u884c\u4e0d\u69cb\u6210\u7121\u6d29\u6f0f\u7684\u8b49\u64da\u3002'))
    }
    else {
        $handleGrowth = $result.ProcLast.Handles - $result.ProcFirst.Handles
        $privateGrowth = $result.ProcLast.PrivateMb - $result.ProcFirst.PrivateMb
        if ($handleGrowth -ge 500) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u63a7\u5236\u4ee3\u78bc\u6578\u6210\u9577 {0:N0}\uff08{1:N0} \u81f3 {2:N0}\uff09\uff1b\u975e\u53d7\u63a7\u8cc7\u6e90\u53ef\u80fd\u672a\u91cb\u653e\u3002')) -f $handleGrowth, $result.ProcFirst.Handles, $result.ProcLast.Handles)
        }
        if ($privateGrowth -ge 256) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u79c1\u6709\u8a18\u61b6\u9ad4\u6210\u9577 {0:N0} MB\uff08{1:N0} \u81f3 {2:N0}\uff09\u3002')) -f $privateGrowth, $result.ProcFirst.PrivateMb, $result.ProcLast.PrivateMb)
        }
    }
    if ($result.PoolSamples -ge 2) {
        $aliveGrowth = $result.PoolLast.Alive - $result.PoolFirst.Alive
        if ($aliveGrowth -ge 5) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u5b58\u6d3b\u9023\u7dda\u6578\u6210\u9577 {0:N0}\uff08{1:N0} \u81f3 {2:N0}\uff09\uff1b\u9023\u7dda\u53ef\u80fd\u53ea\u9032\u4e0d\u51fa\u3002')) -f $aliveGrowth, $result.PoolFirst.Alive, $result.PoolLast.Alive)
        }
        $unreturned = $result.PoolLast.Acquires - $result.PoolLast.Releases
        if ($unreturned -gt 0) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u6700\u5f8c\u4e00\u7b46\u9023\u7dda\u6c60\u5feb\u7167\u986f\u793a\u7d2f\u8a08\u79df\u501f\u6bd4\u6b78\u9084\u591a {0:N0} \u6b21\uff1b\u53ef\u80fd\u6709\u79df\u7d04\u672a\u6b78\u9084\u3002')) -f $unreturned)
        }
        if ($result.PoolLast.SubPools -gt $result.PoolFirst.SubPools) {
            Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('\u5b50\u6c60\u6578\u91cf\u7531 {0:N0} \u6210\u9577\u70ba {1:N0}\uff1b\u5b50\u6c60\u76ee\u524d\u6c92\u6709\u56de\u6536\u8def\u5f91\uff0c\u555f\u7528 per-user \u9694\u96e2\u5f8c\u6703\u55ae\u8abf\u7d2f\u7a4d\u3002')) -f $result.PoolFirst.SubPools, $result.PoolLast.SubPools)
        }
    }
    if ($result.DroppedEvents -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u6709 Trace \u4e8b\u4ef6\u88ab\u4e1f\u68c4\uff1b\u8b49\u64da\u53ef\u80fd\u4e0d\u5b8c\u6574\u3002')) }

    return $result
}

function Analyze-ApplicationTrace {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$EndpointLimit,
        [Parameter(Mandatory = $true)][int]$SlowThreshold
    )

    $result = New-BaseResult -Name 'Trace.log' -Path $Path
    $result | Add-Member NoteProperty PerfCount ([long]0)
    $result | Add-Member NoteProperty NPlusOneCount ([long]0)
    $result | Add-Member NoteProperty GapCount ([long]0)
    $result | Add-Member NoteProperty StartupCount ([long]0)
    $result | Add-Member NoteProperty StartupMax ([long]0)
    $result | Add-Member NoteProperty ErrorCount ([long]0)
    $result | Add-Member NoteProperty WarningCount ([long]0)
    $result | Add-Member NoteProperty SlowCount ([long]0)
    $result | Add-Member NoteProperty EndpointOverflow ([long]0)
    $result | Add-Member NoteProperty Endpoints (New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase))

    if (-not [System.IO.File]::Exists($Path)) {
        Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u627e\u4e0d\u5230\u6a94\u6848\uff1b\u4e09\u6a94\u8b49\u64da\u96c6\u5408\u4e0d\u5b8c\u6574\u3002'))
        return $result
    }

    $item = Get-Item -LiteralPath $Path
    $result.Exists = $true
    $result.Length = $item.Length
    $result.LastWriteTime = $item.LastWriteTime
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $reader = $null
    $perfPattern = [regex]'\[Perf\]\s+path=(?<path>\S+)\s+total=(?<total>\d+)ms\s+action=(?<action>\d+)ms\s+crm\{n=(?<n>\d+),ms=(?<crm>\d+)\}\s+gap=(?<gap>\d+)ms'
    $startupPattern = [regex]'\[Perf-Startup\]\s+phase=(?<phase>\S+)\s+ms=(?<ms>\d+)'
    $timestampPattern = [regex]'\[(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]'

    try {
        $reader = New-TraceReader -Path $Path -Encoding $utf8
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            $result.Lines++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            Update-SensitiveCounts -Line $line -Counts $result.SensitiveCounts

            $tsMatch = $timestampPattern.Match($line)
            if ($tsMatch.Success) {
                Update-TimeRange -State $result -Timestamp (Convert-ToLocalTimestamp $tsMatch.Groups['ts'].Value)
            }

            if ($line -match '(?i)\b(error|exception|fatal)\b') { $result.ErrorCount++ }
            if ($line -match '(?i)\b(warn|warning)\b') { $result.WarningCount++ }
            if ($line.Contains('[Perf-N+1]')) { $result.NPlusOneCount++ }
            if ($line.Contains('[Perf-Gap]')) { $result.GapCount++ }

            $startup = $startupPattern.Match($line)
            if ($startup.Success) {
                $result.StartupCount++
                $startupMs = [long]$startup.Groups['ms'].Value
                if ($startupMs -gt $result.StartupMax) { $result.StartupMax = $startupMs }
            }

            $perf = $perfPattern.Match($line)
            if (-not $perf.Success) { continue }
            $result.PerfCount++
            $pathLabel = Convert-ToSafeLabel $perf.Groups['path'].Value
            $total = [long]$perf.Groups['total'].Value
            $crmN = [long]$perf.Groups['n'].Value
            $crmMs = [long]$perf.Groups['crm'].Value
            $gap = [long]$perf.Groups['gap'].Value
            if ($total -ge $SlowThreshold) { $result.SlowCount++ }

            if (-not $result.Endpoints.ContainsKey($pathLabel)) {
                if ($result.Endpoints.Count -ge $EndpointLimit) {
                    $result.EndpointOverflow++
                    continue
                }
                $result.Endpoints[$pathLabel] = [pscustomobject]@{
                    Path = $pathLabel; Hits = 0L; TotalSum = 0L; MaxTotal = 0L
                    CrmCountSum = 0L; CrmMsSum = 0L; MaxCrmN = 0L
                    GapSum = 0L; MaxGap = 0L
                }
            }

            $endpoint = $result.Endpoints[$pathLabel]
            $endpoint.Hits++
            $endpoint.TotalSum += $total
            $endpoint.CrmCountSum += $crmN
            $endpoint.CrmMsSum += $crmMs
            $endpoint.GapSum += $gap
            if ($total -gt $endpoint.MaxTotal) { $endpoint.MaxTotal = $total }
            if ($crmN -gt $endpoint.MaxCrmN) { $endpoint.MaxCrmN = $crmN }
            if ($gap -gt $endpoint.MaxGap) { $endpoint.MaxGap = $gap }
        }
    }
    catch {
        $result.ReadError = $_.Exception.Message
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u6a94\u6848\u7121\u6cd5\u4ee5 UTF-8 \u5b8c\u6574\u89e3\u78bc\u6216\u8b80\u53d6\u3002')) }
    if ($result.PerfCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u6c92\u6709 [Perf] \u4e8b\u4ef6\uff1b\u7121\u6cd5\u8a55\u4f30\u7aef\u9ede\u6548\u80fd\u3002')) }
    if ($result.EndpointOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u7aef\u9ede\u7a2e\u985e\u6578\u8d85\u904e\u6709\u9650\u805a\u5408\u4e0a\u9650\u3002')) }
    if ($result.SlowCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message (([regex]::Unescape('{0} \u500b\u8acb\u6c42\u9054\u5230 {1} \u6beb\u79d2\u6162\u8acb\u6c42\u9580\u6abb\u3002')) -f $result.SlowCount, $SlowThreshold) }
    if ($result.NPlusOneCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u5075\u6e2c\u5230 [Perf-N+1] \u6307\u6a19\u3002')) }
    if ($result.GapCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u5075\u6e2c\u5230 [Perf-Gap] \u6307\u6a19\u3002')) }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u5075\u6e2c\u5230\u932f\u8aa4\uff0f\u4f8b\u5916\uff0f\u81f4\u547d\u932f\u8aa4\u95dc\u9375\u5b57\u3002')) }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u5075\u6e2c\u5230\u7591\u4f3c\u654f\u611f\u8cc7\u6599\u6a21\u5f0f\uff1b\u5831\u544a\u5df2\u7701\u7565\u539f\u59cb\u503c\u3002')) }

    return $result
}

function Analyze-ToolUtilityTrace {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$CategoryLimit
    )

    $result = New-BaseResult -Name 'CHURCH_REPORT_TRACE.TXT' -Path $Path
    $result | Add-Member NoteProperty EntryCount ([long]0)
    $result | Add-Member NoteProperty ErrorCount ([long]0)
    $result | Add-Member NoteProperty CategoryOverflow ([long]0)
    $result | Add-Member NoteProperty EncodingUsed ([regex]::Unescape('Big5\uff08950 \u7de8\u78bc\u9801\uff09'))
    $result | Add-Member NoteProperty Categories (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::OrdinalIgnoreCase))

    if (-not [System.IO.File]::Exists($Path)) {
        Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u627e\u4e0d\u5230\u6a94\u6848\uff1b\u4e09\u6a94\u8b49\u64da\u96c6\u5408\u4e0d\u5b8c\u6574\u3002'))
        return $result
    }

    $item = Get-Item -LiteralPath $Path
    $result.Exists = $true
    $result.Length = $item.Length
    $result.LastWriteTime = $item.LastWriteTime
    $big5 = [System.Text.Encoding]::GetEncoding(
        950,
        [System.Text.EncoderFallback]::ExceptionFallback,
        [System.Text.DecoderFallback]::ExceptionFallback)
    $reader = $null
    $localizedErrorPattern = (([char]0x5931).ToString() + ([char]0x6557).ToString() + '|' +
        ([char]0x932F).ToString() + ([char]0x8AA4).ToString() + '|' +
        ([char]0x4F8B).ToString() + ([char]0x5916).ToString())
    $timePattern = [regex]'(?i)^Time\s*=\s*(?<ts>.+?)\s*$'
    $messagePattern = [regex]'(?i)^StringToProcess\s*=\s*(?<message>.*)$'

    try {
        $reader = New-TraceReader -Path $Path -Encoding $big5
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            $result.Lines++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            Update-SensitiveCounts -Line $line -Counts $result.SensitiveCounts
            if ($line -match ('(?i)\b(error|exception|fatal)\b|' + $localizedErrorPattern)) { $result.ErrorCount++ }

            $timeMatch = $timePattern.Match($line)
            if ($timeMatch.Success) {
                Update-TimeRange -State $result -Timestamp (Convert-ToLocalTimestamp $timeMatch.Groups['ts'].Value)
            }

            $messageMatch = $messagePattern.Match($line)
            if (-not $messageMatch.Success) { continue }
            $result.EntryCount++
            $message = $messageMatch.Groups['message'].Value.Trim()
            $category = ([regex]::Unescape('(\u672a\u5206\u985e)'))
            $categoryMatch = [regex]::Match($message, '^\[(?<category>[^\]\r\n]{1,40})\]')
            if ($categoryMatch.Success) {
                $category = Convert-ToSafeLabel ('[' + $categoryMatch.Groups['category'].Value + ']')
            }

            if ($result.Categories.ContainsKey($category)) {
                $result.Categories[$category]++
            }
            elseif ($result.Categories.Count -lt $CategoryLimit) {
                $result.Categories[$category] = 1L
            }
            else {
                $result.CategoryOverflow++
            }
        }
    }
    catch {
        $result.ReadError = $_.Exception.Message
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u6a94\u6848\u7121\u6cd5\u4ee5 Big5 \u5b8c\u6574\u89e3\u78bc\u6216\u8b80\u53d6\u3002')) }
    if ($result.EntryCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u6c92\u6709 StringToProcess \u9805\u76ee\uff1b\u8b49\u64da\u4e0d\u8db3\u6216\u683c\u5f0f\u4e0d\u53d7\u652f\u63f4\u3002')) }
    if ($result.CategoryOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u985e\u5225\u7a2e\u985e\u6578\u8d85\u904e\u6709\u9650\u805a\u5408\u4e0a\u9650\u3002')) }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ([regex]::Unescape('\u5075\u6e2c\u5230\u82f1\u6587\u6216\u7e41\u9ad4\u4e2d\u6587\u932f\u8aa4\u6307\u6a19\u3002')) }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ([regex]::Unescape('\u5075\u6e2c\u5230\u7591\u4f3c\u654f\u611f\u8cc7\u6599\u6a21\u5f0f\uff1b\u5831\u544a\u5df2\u7701\u7565\u539f\u59cb\u503c\u3002')) }

    return $result
}

function Format-NullableTime {
    param($Value)
    if ($null -eq $Value) { return ([regex]::Unescape('\u7121\u8cc7\u6599')) }
    return ([DateTime]$Value).ToString('yyyy-MM-dd HH:mm:ss.fff')
}

function Format-Average {
    param([long]$Sum, [long]$Count)
    if ($Count -le 0) { return ([regex]::Unescape('\u7121\u8cc7\u6599')) }
    return [Math]::Round($Sum / [double]$Count, 2).ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Add-ReasonsToReport {
    param(
        [Parameter(Mandatory = $true)]$Lines,
        [Parameter(Mandatory = $true)]$Result
    )
    if ($Result.Reasons.Count -eq 0) {
        [void]$Lines.Add(([regex]::Unescape('- \u672a\u5075\u6e2c\u5230\u660e\u78ba\u9055\u898f\u3002')))
        return
    }
    foreach ($reason in $Result.Reasons) {
        [void]$Lines.Add(('- {0}' -f $reason))
    }
}

function Add-SensitiveTable {
    param(
        [Parameter(Mandatory = $true)]$Lines,
        [Parameter(Mandatory = $true)]$Counts
    )
    if ($Counts.Count -eq 0) {
        [void]$Lines.Add(([regex]::Unescape('- \u6f5b\u5728\u654f\u611f\u8cc7\u6599\u6a21\u5f0f\u547d\u4e2d\uff1a0')))
        return
    }
    [void]$Lines.Add(([regex]::Unescape('| \u6a21\u5f0f | \u547d\u4e2d\u7684\u884c\u6578 |')))
    [void]$Lines.Add('|---|---:|')
    foreach ($entry in ($Counts.GetEnumerator() | Sort-Object Name)) {
        [void]$Lines.Add(('| {0} | {1} |' -f $entry.Key, $entry.Value))
    }
}

try {
    $traceDirectoryFull = [System.IO.Path]::GetFullPath($TraceDirectory)
    $dataversePath = Resolve-InputPath -ExplicitPath $DataverseTracePath -Directory $traceDirectoryFull -FileName 'dataverse-trace.jsonl'
    $applicationPath = Resolve-InputPath -ExplicitPath $ApplicationTracePath -Directory $traceDirectoryFull -FileName 'Trace.log'
    $toolUtilityPath = Resolve-InputPath -ExplicitPath $ToolUtilityTracePath -Directory $traceDirectoryFull -FileName 'CHURCH_REPORT_TRACE.TXT'
    if ([string]::IsNullOrWhiteSpace($ReportPath)) {
        $ReportPath = Join-Path -Path $traceDirectoryFull -ChildPath 'ChurchReport-Trace-Report.md'
    }
    $reportPathFull = [System.IO.Path]::GetFullPath($ReportPath)

    $dataverse = Analyze-DataverseTrace -Path $dataversePath -PairLimit $MaxPairEntries -SlowRequestMs $SlowRequestThresholdMs -NPlusOneThreshold $NPlusOneThreshold -EndpointLimit $Top
    $application = Analyze-ApplicationTrace -Path $applicationPath -EndpointLimit $MaxPairEntries -SlowThreshold $SlowRequestThresholdMs
    $toolUtility = Analyze-ToolUtilityTrace -Path $toolUtilityPath -CategoryLimit ([Math]::Min($MaxPairEntries, 10000))
    $results = @($dataverse, $application, $toolUtility)

    $overall = 'PASS'
    if (@($results | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0) {
        $overall = 'FAIL'
    }
    elseif (@($results | Where-Object { $_.Status -eq 'WARN' }).Count -gt 0) {
        $overall = 'WARN'
    }

    $timed = @($results | Where-Object { $null -ne $_.StartTime -and $null -ne $_.EndTime })
    $crossStatus = 'PASS'
    $crossNotes = New-Object 'System.Collections.Generic.List[string]'
    if ($timed.Count -lt 3) {
        $crossStatus = 'WARN'
        [void]$crossNotes.Add(([regex]::Unescape('\u81f3\u5c11\u6709\u4e00\u500b\u6a94\u6848\u6c92\u6709\u53ef\u8fa8\u8b58\u7684\u6642\u9593\u7bc4\u570d\uff1b\u7121\u6cd5\u9032\u884c\u5b8c\u6574\u4e8b\u4ef6\u5c0d\u9f4a\u3002')))
    }
    else {
        $latestStart = ($timed | Sort-Object StartTime -Descending | Select-Object -First 1).StartTime
        $earliestEnd = ($timed | Sort-Object EndTime | Select-Object -First 1).EndTime
        if ($latestStart -gt $earliestEnd) {
            $crossStatus = 'WARN'
            [void]$crossNotes.Add(([regex]::Unescape('\u53ef\u8fa8\u8b58\u7684\u6642\u9593\u7bc4\u570d\u6c92\u6709\u660e\u78ba\u91cd\u758a\uff1b\u8de8\u6a94\u6848\u56e0\u679c\u95dc\u806f\u9700\u8981\u4e00\u6b21\u53d7\u63a7\u7684\u5b8c\u6574\u91cd\u73fe\u3002')))
        }
        else {
            [void]$crossNotes.Add(([regex]::Unescape('\u53ef\u8fa8\u8b58\u7684\u6642\u9593\u7bc4\u570d\u6709\u91cd\u758a\uff0c\u53ef\u652f\u63f4\u91dd\u5c0d\u540c\u4e00\u6b21\u91cd\u73fe\u9032\u884c\u4eba\u5de5\u95dc\u806f\u3002')))
        }
    }
    if ($crossStatus -eq 'WARN' -and $overall -eq 'PASS') { $overall = 'WARN' }

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add(([regex]::Unescape('# ChurchReport \u4e09\u6a94 Trace \u5206\u6790\u5831\u544a')))
    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('- \u7522\u751f\u6642\u9593\uff1a{0}')) -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')))
    [void]$lines.Add((([regex]::Unescape('- \u6574\u9ad4\u72c0\u614b\uff1a**{0}**')) -f $overall))
    [void]$lines.Add((([regex]::Unescape('- \u6162\u8acb\u6c42\u9580\u6abb\uff1a{0} \u6beb\u79d2')) -f $SlowRequestThresholdMs))
    [void]$lines.Add((([regex]::Unescape('- \u914d\u5c0d\uff0f\u805a\u5408\u8a18\u61b6\u9ad4\u4e0a\u9650\uff1a{0:N0} \u7b46\u3002\u6ea2\u4f4d\u6703\u6a19\u8a18\u70ba WARN\uff0c\u4e0d\u6703\u88ab\u8aa4\u5224\u70ba PASS\u3002')) -f $MaxPairEntries))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('## \u57f7\u884c\u6458\u8981')))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u6a94\u6848 | \u72c0\u614b | \u884c\u6578 | \u5927\u5c0f\uff08\u4f4d\u5143\u7d44\uff09 | \u6642\u9593\u7bc4\u570d |')))
    [void]$lines.Add('|---|---|---:|---:|---|')
    foreach ($result in $results) {
        $range = if ($null -eq $result.StartTime) { ([regex]::Unescape('\u7121\u8cc7\u6599')) } else { (Format-NullableTime $result.StartTime) + ([regex]::Unescape(' \u81f3 ')) + (Format-NullableTime $result.EndTime) }
        [void]$lines.Add(('| {0} | **{1}** | {2:N0} | {3:N0} | {4} |' -f $result.Name, $result.Status, $result.Lines, $result.Length, $range))
    }

    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('## \u6a94\u6848\u6e05\u55ae\u8207\u552f\u8b80\u5951\u7d04')))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u6a94\u6848 | \u8def\u5f91 | \u5b58\u5728 | \u6700\u5f8c\u4fee\u6539\u6642\u9593 |')))
    [void]$lines.Add('|---|---|---|---|')
    foreach ($result in $results) {
        $modified = if ($null -eq $result.LastWriteTime) { ([regex]::Unescape('\u7121\u8cc7\u6599')) } else { $result.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') }
        $existsLabel = if ($result.Exists) { ([regex]::Unescape('\u662f')) } else { ([regex]::Unescape('\u5426')) }
        [void]$lines.Add(('| {0} | `{1}` | {2} | {3} |' -f $result.Name, ($result.Path -replace '\|', '\|'), $existsLabel, $modified))
    }
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('\u6240\u6709\u8f38\u5165\u5747\u4ee5 `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete` \u4e32\u6d41\u8b80\u53d6\uff1b\u5206\u6790\u5668\u4e0d\u6703\u4fee\u6539\u539f\u59cb Trace\u3002')))

    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('## Dataverse \u7ba1\u7406\u8207\u9694\u96e2\uff08{0}\uff09')) -f $dataverse.Status))
    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('- JSONL\uff1a{0:N0} \u884c\uff0c\u6210\u529f\u89e3\u6790 {1:N0} \u884c\uff0c\u89e3\u6790\u932f\u8aa4 {2:N0} \u884c')) -f $dataverse.Lines, $dataverse.Parsed, $dataverse.ParseErrors))
    [void]$lines.Add((([regex]::Unescape('- \u8acb\u6c42\u914d\u5c0d\uff1a\u7f3a\u5c11\u7d50\u675f {0:N0}\uff0c\u591a\u51fa\u7684\u7d50\u675f {1:N0}')) -f $dataverse.MissingRequestEnds, $dataverse.OrphanRequestEnds))
    [void]$lines.Add((([regex]::Unescape('- \u79df\u7d04\u914d\u5c0d\uff1a\u7f3a\u5c11\u6b78\u9084 {0:N0}\uff0c\u591a\u51fa\u7684\u6b78\u9084 {1:N0}')) -f $dataverse.MissingReturns, $dataverse.OrphanReturns))
    [void]$lines.Add((([regex]::Unescape('- \u8acb\u6c42\u8017\u6642\uff1a{0:N0} \u7b46\uff0c\u5e73\u5747 {1} \u6beb\u79d2\uff0c\u6700\u5927 {2:N0} \u6beb\u79d2')) -f $dataverse.RequestDurationCount, (Format-Average $dataverse.RequestDurationSum $dataverse.RequestDurationCount), $dataverse.RequestDurationMax))
    [void]$lines.Add((([regex]::Unescape('- \u53d6\u5f97\u7b49\u5f85\uff1a{0:N0} \u7b46\uff0c\u5e73\u5747 {1} \u6beb\u79d2\uff0c\u6700\u5927 {2:N0} \u6beb\u79d2\uff0c\u903e\u6642 {3:N0} \u6b21')) -f $dataverse.AcquireWaitCount, (Format-Average $dataverse.AcquireWaitSum $dataverse.AcquireWaitCount), $dataverse.AcquireWaitMax, $dataverse.Timeouts))
    [void]$lines.Add((([regex]::Unescape('- \u79df\u7d04\u6301\u6709\uff1a{0:N0} \u7b46\uff0c\u5e73\u5747 {1} \u6beb\u79d2\uff0c\u6700\u5927 {2:N0} \u6beb\u79d2')) -f $dataverse.HeldCount, (Format-Average $dataverse.HeldSum $dataverse.HeldCount), $dataverse.HeldMax))
    [void]$lines.Add((([regex]::Unescape('- \u9023\u7dda\u6c60\uff08Pool\uff09\uff1a\u5065\u5eb7\u6aa2\u67e5\u5931\u6557 {0:N0} \u6b21\u3001\u4f4e\u65bc MinSize \u7684\u6e05\u7406\u5feb\u7167 {1:N0} \u6b21\u3001\u672a\u6e05\u9664\u547c\u53eb\u7aef\u72c0\u614b {2:N0} \u6b21\u3001\u4e1f\u68c4\u4e8b\u4ef6 {3:N0} \u6b21')) -f $dataverse.HealthFailures, $dataverse.CleanupBelowMinSnapshots, $dataverse.CallerStateViolations, $dataverse.DroppedEvents))
    [void]$lines.Add((([regex]::Unescape('- \u79df\u7d04\u6b78\u9084\u72c0\u614b\uff1ahealthy {0:N0}\u3001faulted {1:N0}{2}')) -f $dataverse.ReturnHealthy, $dataverse.ReturnFaulted, $(
        if (($dataverse.ReturnHealthy + $dataverse.ReturnFaulted) -gt 0) {
            ([regex]::Unescape('\uff08faulted \u6bd4\u7387 {0:P1}\uff09')) -f ([double]$dataverse.ReturnFaulted / [double]($dataverse.ReturnHealthy + $dataverse.ReturnFaulted))
        } else { '' })))
    [void]$lines.Add((([regex]::Unescape('- \u5efa\u7acb\u9023\u7dda\uff1a\u6210\u529f {0:N0}\u3001\u5931\u6557 {1:N0}\u3001\u5e73\u5747 {2} \u6beb\u79d2\u3001\u6700\u9577 {3:N0} \u6beb\u79d2')) -f $dataverse.CreateOk, $dataverse.CreateFailed, (Format-Average $dataverse.CreateMsSum ($dataverse.CreateOk + $dataverse.CreateFailed)), $dataverse.CreateMsMax))
    [void]$lines.Add((([regex]::Unescape('- \u53d6\u5f97\u7d50\u679c\u914d\u5c0d\uff1await {0:N0} = hit {1:N0} + miss {2:N0} + fail {3:N0}{4}')) -f $dataverse.AcquireWaitCount, $dataverse.AcquireHits, $dataverse.AcquireMisses, $dataverse.AcquireFails, $(
        if ($dataverse.AcquireWaitCount -eq ($dataverse.AcquireHits + $dataverse.AcquireMisses + $dataverse.AcquireFails)) { ([regex]::Unescape('\u3000\u2714 \u4e0d\u8b8a\u91cf\u6210\u7acb')) } else { ([regex]::Unescape('\u3000\u2718 \u4e0d\u8b8a\u91cf\u4e0d\u6210\u7acb')) })))
    [void]$lines.Add((([regex]::Unescape('- \u6e05\u7406\u6dd8\u6c70\uff1a{0:N0} \u689d\uff1b\u8d8a\u904e\u4fdd\u5e95\u7684\u6e05\u7406 {1:N0} \u6b21')) -f $dataverse.CleanupEvicted, $dataverse.CleanupBelowMinSnapshots))
    [void]$lines.Add(([regex]::Unescape('- \u6e05\u7406\u5224\u8b80\uff1a\u53ea\u6709\u300c\u6dd8\u6c70\u524d\u9ad8\u65bc MinSize\u3001\u6dd8\u6c70\u5f8c\u4f4e\u65bc MinSize\u300d\u624d\u8a08\u70ba\u8d8a\u7dda\u3002\u820a\u898f\u5247\u55ae\u770b `idleAfter < minSize`\uff0c\u5728\u6c60\u4e2d\u6c92\u6709\u9592\u7f6e\u9023\u7dda\uff08idle = 0\uff09\u6642\u6046\u70ba\u771f\uff0c\u6703\u7522\u751f\u5927\u91cf\u5047\u967d\u6027\u3002')))
    if ($dataverse.DisposeByReason.Count -gt 0) {
        $disposeText = (($dataverse.DisposeByReason.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { '{0} {1:N0}' -f $_.Key, $_.Value }) -join ([regex]::Unescape('\u3001')))
        [void]$lines.Add((([regex]::Unescape('- \u9023\u7dda\u6dd8\u6c70\u539f\u56e0\uff1a{0}')) -f $disposeText))
    }
    if ($dataverse.ErrorKinds.Count -gt 0) {
        $kindText = (($dataverse.ErrorKinds.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { '{0} {1:N0}' -f $_.Key, $_.Value }) -join ([regex]::Unescape('\u3001')))
        [void]$lines.Add((([regex]::Unescape('- \u932f\u8aa4\u7a2e\u985e\uff08\u50c5\u578b\u5225\u540d\u7a31\uff0c\u4e0d\u542b\u8a0a\u606f\u5167\u5bb9\uff09\uff1a{0}')) -f $kindText))
    }
    [void]$lines.Add((([regex]::Unescape('- \u4f7f\u7528\u8005\u9694\u96e2\uff1a\u6709\u6548\u865b\u64ec\u8b58\u5225\u78bc {0:N0} \u500b\uff0c\u683c\u5f0f\u9055\u898f {1:N0} \u6b21')) -f $dataverse.UniquePseudonyms, $dataverse.InvalidPseudonyms))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('### \u4e8b\u4ef6\u7d71\u8a08')))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u4e8b\u4ef6 | \u6b21\u6578 |')))
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($dataverse.EventCounts.GetEnumerator() | Sort-Object Name)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')

    # ===================== 效能歸因 =====================
    [void]$lines.Add(([regex]::Unescape('### \u6548\u80fd\u6b78\u56e0')))
    [void]$lines.Add('')
    $appMs = [Math]::Max(0, $dataverse.RequestDurationSum - $dataverse.RequestCrmMsSum)
    [void]$lines.Add((([regex]::Unescape('- CRM \u64cd\u4f5c {0:N0} \u6b21\uff0c\u7d2f\u8a08 {1:N0} \u6beb\u79d2\uff1b\u5931\u6557 {2:N0} \u6b21')) -f $dataverse.CrmOpCount, $dataverse.CrmMsTotal, $dataverse.CrmFailures))
    [void]$lines.Add((([regex]::Unescape('- \u5168\u90e8\u8acb\u6c42\u8017\u6642 {0:N0} \u6beb\u79d2\uff0c\u5176\u4e2d CRM {1:N0} \u6beb\u79d2\u3001\u61c9\u7528\u7a0b\u5f0f\u81ea\u8eab {2:N0} \u6beb\u79d2')) -f $dataverse.RequestDurationSum, $dataverse.RequestCrmMsSum, $appMs))
    [void]$lines.Add(([regex]::Unescape('- \u5224\u8b80\uff1aCRM \u4f54\u6bd4\u9ad8\u4ee3\u8868\u5f80\u8fd4\u6b21\u6578\u6216\u67e5\u8a62\u6210\u672c\u662f\u74f6\u9838\uff1b\u61c9\u7528\u7a0b\u5f0f\u81ea\u8eab\u4f54\u6bd4\u9ad8\u5247\u4ee3\u8868\u74f6\u9838\u5728 CRM \u4e4b\u5916\uff08\u5e8f\u5217\u5316\u3001\u52a0\u5bc6\u3001Session \u6216\u672c\u6a5f\u904b\u7b97\uff09\u3002')))
    [void]$lines.Add('')

    if ($dataverse.EntityStats.Count -gt 0) {
        [void]$lines.Add((([regex]::Unescape('#### Entity \u67e5\u8a62\u5206\u4f48\uff08\u524d {0} \u540d\uff09')) -f $Top))
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('| Entity | \u6b21\u6578 | \u7d2f\u8a08\u6beb\u79d2 | \u5e73\u5747\u6beb\u79d2 | \u6700\u9577\u6beb\u79d2 |')))
        [void]$lines.Add('|---|---:|---:|---:|---:|')
        foreach ($entry in ($dataverse.EntityStats.GetEnumerator() | Sort-Object -Property { $_.Value.Ms } -Descending | Select-Object -First $Top)) {
            [void]$lines.Add(('| `{0}` | {1:N0} | {2:N0} | {3} | {4:N0} |' -f $entry.Key, $entry.Value.Count, $entry.Value.Ms, (Format-Average $entry.Value.Ms $entry.Value.Count), $entry.Value.Max))
        }
        if ($dataverse.EntityOverflow -gt 0) {
            [void]$lines.Add(('' ))
            [void]$lines.Add((([regex]::Unescape('- Entity \u7a2e\u985e\u8d85\u904e\u805a\u5408\u4e0a\u9650\uff0c\u53e6\u6709 {0:N0} \u6b21\u672a\u8a08\u5165\u3002')) -f $dataverse.EntityOverflow))
        }
        [void]$lines.Add('')
    }

    if ($dataverse.NPlusOneRequests.Count -gt 0) {
        [void]$lines.Add((([regex]::Unescape('#### N+1 \u5fb5\u5146\uff08\u540c\u4e00 entity \u5728\u55ae\u4e00\u8acb\u6c42\u5167\u67e5\u8a62 \u2265 {0} \u6b21\uff09')) -f $NPlusOneThreshold))
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('| TraceId | \u91cd\u8907\u6700\u591a\u7684 Entity | \u8a72 Entity \u6b21\u6578 | \u8acb\u6c42\u5167 CRM \u6b21\u6578 | CRM \u6beb\u79d2 | \u8acb\u6c42\u6beb\u79d2 |')))
        [void]$lines.Add('|---|---|---:|---:|---:|---:|')
        foreach ($item in ($dataverse.NPlusOneRequests | Sort-Object -Property TopCount -Descending)) {
            [void]$lines.Add(('| `{0}` | `{1}` | {2:N0} | {3:N0} | {4:N0} | {5:N0} |' -f $item.TraceId, $item.Entity, $item.TopCount, $item.CrmCount, $item.CrmMs, $item.DurationMs))
        }
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('- \u5224\u8b80\uff1a\u540c\u4e00\u5f35\u8868\u88ab\u91cd\u8907\u67e5\u8a62\uff0c\u901a\u5e38\u662f\u8ff4\u5708\u5167\u9010\u7b46\u67e5\u8a62\u6240\u81f4\u3002\u5408\u4f75\u70ba\u55ae\u6b21\u6279\u6b21\u67e5\u8a62\u7684\u6548\u76ca\uff0c\u7d04\u7b49\u65bc\u300c\u6b21\u6578 \u00d7 \u55ae\u6b21\u5f80\u8fd4\u6210\u672c\u300d\u3002\u512a\u5316\u55ae\u4e00\u67e5\u8a62\u5c0d\u6b64\u7121\u6548\u3002')))
        [void]$lines.Add('')
    }

    if ($dataverse.SlowRequests.Count -gt 0) {
        [void]$lines.Add((([regex]::Unescape('#### \u6162\u8acb\u6c42\u6b78\u56e0\uff08\u2265 {0} \u6beb\u79d2\uff09')) -f $SlowRequestThresholdMs))
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('| TraceId | \u7e3d\u6beb\u79d2 | CRM \u6beb\u79d2 | \u61c9\u7528\u7a0b\u5f0f\u6beb\u79d2 | CRM \u6b21\u6578 | \u91cd\u8907\u6700\u591a\u7684 Entity | \u6b21\u6578 |')))
        [void]$lines.Add('|---|---:|---:|---:|---:|---|---:|')
        foreach ($item in ($dataverse.SlowRequests | Sort-Object -Property DurationMs -Descending)) {
            [void]$lines.Add(('| `{0}` | {1:N0} | {2:N0} | {3:N0} | {4:N0} | `{5}` | {6:N0} |' -f $item.TraceId, $item.DurationMs, $item.CrmMs, $item.AppMs, $item.CrmCount, $item.TopEntity, $item.TopCount))
        }
        [void]$lines.Add('')
    }

    # ===================== Session Leakage =====================
    [void]$lines.Add(([regex]::Unescape('### Session Leakage \u5224\u5b9a')))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u5224\u6e96 | \u89c0\u6e2c\u503c | \u8aaa\u660e |')))
    [void]$lines.Add('|---|---:|---|')
    [void]$lines.Add((([regex]::Unescape('| \u540c\u4e00 client \u7684\u79df\u7d04\u5340\u9593\u91cd\u758a | {0:N0} | \u7d42\u6975\u5224\u6e96\uff1b\u975e\u96f6\u4ee3\u8868\u5169\u500b\u8acb\u6c42\u540c\u6642\u6301\u6709\u540c\u4e00\u689d\u5be6\u9ad4\u9023\u7dda |')) -f $dataverse.LeaseOverlaps))
    [void]$lines.Add((([regex]::Unescape('| \u8acb\u6c42\u7d50\u675f\u6642\u672a\u6b78\u9084\u7684\u79df\u7d04 | {0:N0} \u500b\u8acb\u6c42 | \u975e\u96f6\u4ee3\u8868\u79df\u7d04\u6d29\u6f0f\u5230\u8acb\u6c42\u908a\u754c\u4e4b\u5916 |')) -f $dataverse.LeaseOutstandingRequests))
    [void]$lines.Add((([regex]::Unescape('| \u5e73\u884c\u9032\u5165\u540c\u4e00 Gateway | {0:N0} \u6b21 / {1:N0} \u500b\u8acb\u6c42 | Gateway \u7684 depth \u8207 lease \u6b04\u4f4d\u7121\u540c\u6b65\u4fdd\u8b77\uff0c\u5e73\u884c\u9032\u5165\u53ef\u80fd\u9020\u6210\u9023\u7dda\u5171\u7528 |')) -f $dataverse.ConcurrentGatewayEvents, $dataverse.ConcurrentGatewayRequests))
    [void]$lines.Add((([regex]::Unescape('| Gateway \u91cb\u653e\u6642\u4ecd\u6301\u6709\u79df\u7d04 | {0:N0} \u6b21 | \u79df\u7d04\u9760 DI \u56de\u6536 scope \u624d\u88ab\u6551\u56de\uff0c\u800c\u975e\u6b63\u5e38\u57f7\u884c\u8def\u5f91 |')) -f $dataverse.ScopeEndLeaseHeld))
    [void]$lines.Add((([regex]::Unescape('| \u6b78\u9084\u6642\u4ecd\u5e36\u547c\u53eb\u7aef\u8eab\u5206 | {0:N0} \u6b21 | \u975e\u96f6\u4ee3\u8868 impersonation \u72c0\u614b\u8de8\u8acb\u6c42\u6b98\u7559 |')) -f $dataverse.CallerStateViolations))
    [void]$lines.Add((([regex]::Unescape('| \u89c0\u6e2c\u5230\u7684\u6700\u5927 reentrant \u6df1\u5ea6 | {0:N0} | \u70ba 1 \u4ee3\u8868\u5de2\u72c0\u8def\u5f91\u672a\u88ab\u89f8\u767c\uff0creentrant \u9632\u7dda\u672c\u6b21\u672a\u53d7\u9a57\u8b49 |')) -f $dataverse.MaxDepthObserved))
    [void]$lines.Add('')

    # ===================== 資源趨勢 =====================
    [void]$lines.Add(([regex]::Unescape('### \u8cc7\u6e90\u8da8\u52e2\uff08\u8a18\u61b6\u9ad4\u8207\u9023\u7dda\u6d29\u6f0f\uff09')))
    [void]$lines.Add('')
    if ($dataverse.ProcSamples -lt 2) {
        [void]$lines.Add((([regex]::Unescape('- \u7a0b\u5e8f\u5feb\u7167\u50c5 {0:N0} \u7b46\uff0c\u4e0d\u8db3\u4ee5\u6aa2\u5b9a\u8da8\u52e2\u3002\u6d29\u6f0f\u5224\u5b9a\u9700\u8981\u81f3\u5c11\u5169\u500b\u6642\u9593\u9ede\uff1b\u8acb\u5ef6\u9577\u91cd\u73fe\u6642\u9593\u3002')) -f $dataverse.ProcSamples))
    }
    else {
        [void]$lines.Add((([regex]::Unescape('- \u7a0b\u5e8f\u5feb\u7167 {0:N0} \u7b46')) -f $dataverse.ProcSamples))
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('| \u6307\u6a19 | \u8d77\u59cb | \u7d50\u675f | \u8b8a\u5316 | \u5224\u8b80 |')))
        [void]$lines.Add('|---|---:|---:|---:|---|')
        [void]$lines.Add((([regex]::Unescape('| Managed \u8a18\u61b6\u9ad4 (MB) | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u6301\u7e8c\u4e0a\u5347\u4ee3\u8868\u53d7\u63a7\u7269\u4ef6\u672a\u91cb\u653e |')) -f $dataverse.ProcFirst.ManagedMb, $dataverse.ProcLast.ManagedMb, ($dataverse.ProcLast.ManagedMb - $dataverse.ProcFirst.ManagedMb)))
        [void]$lines.Add((([regex]::Unescape('| \u79c1\u6709\u8a18\u61b6\u9ad4 (MB) | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | Managed \u5e73\u7a69\u4f46\u6b64\u503c\u4e0a\u5347\uff0c\u6307\u5411\u975e\u53d7\u63a7\u8cc7\u6e90 |')) -f $dataverse.ProcFirst.PrivateMb, $dataverse.ProcLast.PrivateMb, ($dataverse.ProcLast.PrivateMb - $dataverse.ProcFirst.PrivateMb)))
        [void]$lines.Add((([regex]::Unescape('| \u63a7\u5236\u4ee3\u78bc\u6578 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u9023\u7dda\u3001\u6a94\u6848\u6216 socket \u672a\u91cb\u653e\u7684\u6700\u76f4\u63a5\u6307\u6a19 |')) -f $dataverse.ProcFirst.Handles, $dataverse.ProcLast.Handles, ($dataverse.ProcLast.Handles - $dataverse.ProcFirst.Handles)))
        [void]$lines.Add((([regex]::Unescape('| \u57f7\u884c\u7dd2\u6578 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u4e0a\u5347\u4ee3\u8868\u57f7\u884c\u7dd2\u6d29\u6f0f\u6216\u98e2\u9913 |')) -f $dataverse.ProcFirst.Threads, $dataverse.ProcLast.Threads, ($dataverse.ProcLast.Threads - $dataverse.ProcFirst.Threads)))
        [void]$lines.Add((([regex]::Unescape('| Gen2 \u56de\u6536\u6b21\u6578 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u6301\u7e8c\u4e0a\u5347\u4ee3\u8868\u7269\u4ef6\u4e0d\u65b7\u6649\u5347\u5230\u9577\u547d\u4e16\u4ee3 |')) -f $dataverse.ProcFirst.Gen2, $dataverse.ProcLast.Gen2, ($dataverse.ProcLast.Gen2 - $dataverse.ProcFirst.Gen2)))
    }
    [void]$lines.Add('')
    if ($dataverse.PoolSamples -lt 2) {
        [void]$lines.Add((([regex]::Unescape('- \u9023\u7dda\u6c60\u5feb\u7167\u50c5 {0:N0} \u7b46\uff0c\u4e0d\u8db3\u4ee5\u6aa2\u5b9a\u9023\u7dda\u6578\u8da8\u52e2\u3002')) -f $dataverse.PoolSamples))
    }
    else {
        [void]$lines.Add((([regex]::Unescape('- \u9023\u7dda\u6c60\u5feb\u7167 {0:N0} \u7b46\uff1b\u5b58\u6d3b\u9023\u7dda\u5cf0\u503c {1:N0}\u3001\u5b50\u6c60\u6578\u5cf0\u503c {2:N0}')) -f $dataverse.PoolSamples, $dataverse.PoolAliveMax, $dataverse.SubPoolsMax))
        [void]$lines.Add('')
        [void]$lines.Add(([regex]::Unescape('| \u6307\u6a19 | \u8d77\u59cb | \u7d50\u675f | \u8b8a\u5316 | \u5224\u8b80 |')))
        [void]$lines.Add('|---|---:|---:|---:|---|')
        [void]$lines.Add((([regex]::Unescape('| \u5b58\u6d3b\u9023\u7dda | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u7a69\u5b9a\u904b\u884c\u6642\u61c9\u5728 MinSize \u9644\u8fd1\u9707\u76ea |')) -f $dataverse.PoolFirst.Alive, $dataverse.PoolLast.Alive, ($dataverse.PoolLast.Alive - $dataverse.PoolFirst.Alive)))
        [void]$lines.Add((([regex]::Unescape('| \u7d2f\u8a08\u5efa\u7acb | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u8207\u6dd8\u6c70\u6578\u7684\u5dee\u503c\u61c9\u7d04\u7b49\u65bc\u5b58\u6d3b\u6578 |')) -f $dataverse.PoolFirst.Created, $dataverse.PoolLast.Created, ($dataverse.PoolLast.Created - $dataverse.PoolFirst.Created)))
        [void]$lines.Add((([regex]::Unescape('| \u7d2f\u8a08\u6dd8\u6c70 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u5efa\u7acb\u9060\u591a\u65bc\u6dd8\u6c70\u4ee3\u8868\u9023\u7dda\u6296\u52d5 |')) -f $dataverse.PoolFirst.Discarded, $dataverse.PoolLast.Discarded, ($dataverse.PoolLast.Discarded - $dataverse.PoolFirst.Discarded)))
        [void]$lines.Add((([regex]::Unescape('| \u79df\u501f\u6e1b\u6b78\u9084 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u975e\u96f6\u4ee3\u8868\u6709\u79df\u7d04\u5c1a\u672a\u6b78\u9084 |')) -f ($dataverse.PoolFirst.Acquires - $dataverse.PoolFirst.Releases), ($dataverse.PoolLast.Acquires - $dataverse.PoolLast.Releases), (($dataverse.PoolLast.Acquires - $dataverse.PoolLast.Releases) - ($dataverse.PoolFirst.Acquires - $dataverse.PoolFirst.Releases))))
        [void]$lines.Add((([regex]::Unescape('| \u5b50\u6c60\u6578 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | \u5b50\u6c60\u7121\u56de\u6536\u8def\u5f91\uff0c\u555f\u7528 per-user \u9694\u96e2\u5f8c\u6703\u55ae\u8abf\u7d2f\u7a4d |')) -f $dataverse.PoolFirst.SubPools, $dataverse.PoolLast.SubPools, ($dataverse.PoolLast.SubPools - $dataverse.PoolFirst.SubPools)))
    }
    [void]$lines.Add('')

    Add-ReasonsToReport -Lines $lines -Result $dataverse
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $dataverse.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('## \u61c9\u7528\u7a0b\u5f0f\u8207\u6548\u80fd Trace.log\uff08{0}\uff09')) -f $application.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- `[Perf]` {0:N0}, `[Perf-N+1]` {1:N0}, `[Perf-Gap]` {2:N0}, `[Perf-Startup]` {3:N0}' -f $application.PerfCount, $application.NPlusOneCount, $application.GapCount, $application.StartupCount))
    [void]$lines.Add((([regex]::Unescape('- \u6162\u8acb\u6c42 {0:N0} \u6b21\u3001\u555f\u52d5\u6700\u5927\u8017\u6642 {1:N0} \u6beb\u79d2\u3001\u932f\u8aa4\uff0f\u4f8b\u5916\uff0f\u81f4\u547d\u932f\u8aa4 {2:N0} \u6b21\u3001\u8b66\u544a {3:N0} \u6b21')) -f $application.SlowCount, $application.StartupMax, $application.ErrorCount, $application.WarningCount))
    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('### \u6700\u6162\u7aef\u9ede\uff08\u524d {0} \u540d\uff1b\u67e5\u8a62\u3001GUID \u8207\u9577\u6578\u5b57\u5df2\u906e\u7f69\uff09')) -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u7aef\u9ede | \u6b21\u6578 | \u5e73\u5747\u7e3d\u8017\u6642\uff08\u6beb\u79d2\uff09 | \u6700\u5927\u7e3d\u8017\u6642\uff08\u6beb\u79d2\uff09 | CRM \u547c\u53eb\u6b21\u6578 | CRM \u8017\u6642\uff08\u6beb\u79d2\uff09 | \u6700\u5927 crm.n | \u5e73\u5747\u9593\u9694\uff08\u6beb\u79d2\uff09 | \u6700\u5927\u9593\u9694\uff08\u6beb\u79d2\uff09 |')))
    [void]$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($endpoint in ($application.Endpoints.Values | Sort-Object MaxTotal -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |' -f $endpoint.Path, $endpoint.Hits, (Format-Average $endpoint.TotalSum $endpoint.Hits), $endpoint.MaxTotal, $endpoint.CrmCountSum, $endpoint.CrmMsSum, $endpoint.MaxCrmN, (Format-Average $endpoint.GapSum $endpoint.Hits), $endpoint.MaxGap))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $application
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $application.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('## \u820a\u7248 ToolUtility Trace\uff08{0}\uff09')) -f $toolUtility.Status))
    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('- \u7de8\u78bc\uff1a{0}\uff1b{1:N0} \u884c\uff1b{2:N0} \u500b StringToProcess \u9805\u76ee\uff1b{3:N0} \u500b\u932f\u8aa4\u6307\u6a19')) -f $toolUtility.EncodingUsed, $toolUtility.Lines, $toolUtility.EntryCount, $toolUtility.ErrorCount))
    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('### \u5e38\u898b\u5b89\u5168\u985e\u5225\uff08\u524d {0} \u540d\uff1b\u5df2\u7701\u7565\u8a0a\u606f\u5167\u5bb9\uff09')) -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('| \u985e\u5225 | \u6b21\u6578 |')))
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($toolUtility.Categories.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $toolUtility
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $toolUtility.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add((([regex]::Unescape('## \u8de8\u6a94\u6848\u95dc\u806f\uff08{0}\uff09')) -f $crossStatus))
    [void]$lines.Add('')
    foreach ($note in $crossNotes) { [void]$lines.Add(('- {0}' -f $note)) }
    [void]$lines.Add(([regex]::Unescape('- \u5206\u6790\u5668\u4e0d\u6703\u6839\u64da\u6a21\u7cca\u6587\u5b57\u731c\u6e2c traceId\uff0f\u7aef\u9ede\u95dc\u4fc2\u3002\u82e5\u6c92\u6709\u5171\u7528\u7684\u95dc\u806f\u8b58\u5225\u78bc\uff0c\u53ea\u80fd\u9032\u884c\u6642\u9593\u7bc4\u570d\u8207\u805a\u5408\u7d50\u679c\u7684\u95dc\u806f\u3002')))

    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('## \u5efa\u8b70\u8207\u9650\u5236')))
    [void]$lines.Add('')
    [void]$lines.Add(([regex]::Unescape('- FAIL\uff1a\u91cd\u65b0\u6536\u96c6 Trace \u524d\uff0c\u8acb\u5148\u4fee\u6b63\u914d\u5c0d\u3001Pool \u9694\u96e2\u3001\u89e3\u6790\u6216\u654f\u611f\u8cc7\u6599\u554f\u984c\u3002')))
    [void]$lines.Add(([regex]::Unescape('- WARN\uff1a\u8acb\u5f9e\u540c\u4e00\u6b21 Debug \u91cd\u73fe\u6536\u96c6\u4e09\u500b\u6a94\u6848\uff0c\u4e26\u6aa2\u67e5\u6162\u7aef\u9ede\u3001N+1\u3001Gap\u3001\u903e\u6642\u8207\u4e1f\u68c4\u4e8b\u4ef6\u6307\u6a19\u3002')))
    [void]$lines.Add(([regex]::Unescape('- \u672c\u5831\u544a\u672c\u8eab\u7121\u6cd5\u8b49\u660e\u4e0d\u5b58\u5728\u8a18\u61b6\u9ad4\uff0f\u5de5\u4f5c\u968e\u6bb5\uff08Session\uff09\u6d29\u6f0f\uff1b\u6b63\u5f0f\u7d44\u614b\uff08Release\uff09\u9a57\u8b49\u4ecd\u9700\u8981\u4e26\u884c A/B \u9694\u96e2\u3001\u63a7\u5236\u4ee3\u78bc\u91cb\u653e\u3001\u9577\u6642\u9593\u7a69\u5b9a\u6027\u8207\u8cc7\u6e90\u57fa\u6e96\u6aa2\u67e5\u3002')))
    [void]$lines.Add(([regex]::Unescape('- \u5206\u6790\u671f\u9593\u6a94\u6848\u53ef\u80fd\u4ecd\u5728\u8ffd\u52a0\u5167\u5bb9\uff1b\u672c\u5831\u544a\u662f\u7576\u4e0b\u53ef\u8b80\u53d6\u7684\u5feb\u7167\uff0c\u53ef\u80fd\u4e0d\u5305\u542b\u4e4b\u5f8c\u7522\u751f\u7684\u4e8b\u4ef6\u3002')))
    [void]$lines.Add(([regex]::Unescape('- \u654f\u611f\u8cc7\u6599\u6a21\u5f0f\u6383\u63cf\u63a1\u4fdd\u5b88\u7b56\u7565\uff1b\u8acb\u5728\u4f86\u6e90\u74b0\u5883\u78ba\u8a8d\u547d\u4e2d\u7d50\u679c\uff0c\u539f\u59cb\u547d\u4e2d\u6587\u5b57\u523b\u610f\u4e0d\u4e88\u4fdd\u7559\u3002')))

    $reportDirectory = [System.IO.Path]::GetDirectoryName($reportPathFull)
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [void][System.IO.Directory]::CreateDirectory($reportDirectory)
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($reportPathFull, (($lines -join "`r`n") + "`r`n"), $utf8NoBom)

    Write-Output (([regex]::Unescape('\u6574\u9ad4\u72c0\u614b\uff1a{0}')) -f $overall)
    Write-Output (([regex]::Unescape('\u5831\u544a\u8def\u5f91\uff1a{0}')) -f $reportPathFull)
    Write-Output (([regex]::Unescape('Dataverse={0}\uff1bTrace.log={1}\uff1bToolUtility={2}\uff1b\u8de8\u6a94\u6848={3}')) -f $dataverse.Status, $application.Status, $toolUtility.Status, $crossStatus)

    if ($overall -eq 'FAIL') { exit 2 }
    exit 0
}
catch {
    Write-Error (([regex]::Unescape('Trace \u5206\u6790\u5668\u57f7\u884c\u5931\u6557\uff1a{0}
{1}')) -f $_.Exception.Message, $_.InvocationInfo.PositionMessage)
    exit 1
}
