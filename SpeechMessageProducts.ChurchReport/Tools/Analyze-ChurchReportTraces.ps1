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
        '敏感欄位值' = '(?i)\b(?:password|passwd|pwd|token|secret|authorization|cookie|credential|username|email|phone|mobile|address)\b[^:=\r\n]{0,3}[:=]\s*(?!"?(?:null|none|"))\S+'
        'Bearer／JWT' = '(?i)\bBearer\s+[A-Za-z0-9._~+/-]{12,}|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        '電子郵件' = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b'
        '臺灣身分證字號格式' = '(?i)\b[A-Z][12]\d{8}\b'
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
        return '(空白)'
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
        Add-Reason -Result $result -Severity WARN -Message '找不到檔案；三檔證據集合不完整。'
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
                $eventName = '(缺少 ev)'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message '檔案無法完整解碼或讀取。' }
    if ($result.Parsed -eq 0) { Add-Reason -Result $result -Severity WARN -Message '沒有可解析的 JSONL 事件；證據不足。' }
    if ($result.ParseErrors -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ("{0} 行 JSONL 無法解析。" -f $result.ParseErrors) }
    if ($result.PairOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message '配對追蹤超過記憶體上限；配對結果僅是部分證據。' }
    if (($result.MissingRequestEnds + $result.OrphanRequestEnds) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'request.begin／request.end 事件未完整配對。' }
    if (($result.MissingReturns + $result.OrphanReturns) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '租約取得／歸還事件未完整配對。' }
    if ($result.HealthFailures -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '偵測到 Pool 健康檢查失敗。' }
    if ($result.CallerStateViolations -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '歸還的租約仍保留呼叫端狀態。' }
    if ($result.InvalidPseudonyms -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '使用者欄位值不符合短期虛擬識別碼格式。' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '偵測到疑似敏感資料模式；報告已省略原始值。' }
    if ($result.Timeouts -gt 0) { Add-Reason -Result $result -Severity WARN -Message '偵測到 Pool 取得逾時。' }

    # ================= Session Leakage：由重到輕 =================
    if ($result.LeaseOverlaps -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message ('同一條實體連線出現 {0:N0} 次租約區間重疊；曾有兩個 request 同時持有同一條連線。' -f $result.LeaseOverlaps)
    }
    if ($result.LeaseOutstandingRequests -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message ('{0:N0} 個 request 結束時仍有未歸還租約（最多 {1:N0} 條）；租約洩漏。' -f $result.LeaseOutstandingRequests, $result.LeaseOutstandingMax)
    }
    if ($result.ConcurrentGatewayEvents -gt 0) {
        Add-Reason -Result $result -Severity FAIL -Message ('偵測到 {0:N0} 次平行進入同一個 scoped Gateway（最高同時 {1:N0} 條）；Gateway 的 depth 與 lease 欄位無同步保護，此路徑可能造成跨 request 共用連線。' -f $result.ConcurrentGatewayEvents, $result.ConcurrentGatewayMax)
    }
    if ($result.ScopeEndLeaseHeld -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message ('{0:N0} 次 Gateway 釋放時仍持有租約；租約是靠 DI 回收 scope 才被救回，而非正常執行路徑。' -f $result.ScopeEndLeaseHeld)
    }

    # ================= 建線可觀測性不變量 =================
    $acquireResults = $result.AcquireHits + $result.AcquireMisses + $result.AcquireFails
    if ($result.AcquireWaitCount -ne $acquireResults) {
        Add-Reason -Result $result -Severity WARN -Message ('取得等待 {0:N0} 筆無法被結果完整解釋（hit + miss + fail = {1:N0}）；有 acquire 未留下結果事件。' -f $result.AcquireWaitCount, $acquireResults)
    }
    if ($result.CreateFailed -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message ('建立連線失敗 {0:N0} 次；請檢視錯誤種類分佈。' -f $result.CreateFailed)
    }

    # ================= 故障淘汰比率 =================
    $returnTotal = $result.ReturnHealthy + $result.ReturnFaulted
    if ($returnTotal -gt 0) {
        $faultRate = [double]$result.ReturnFaulted / [double]$returnTotal
        if ($faultRate -ge 0.01) {
            Add-Reason -Result $result -Severity WARN -Message ('租約以 faulted 歸還的比率為 {0:P1}（{1:N0}/{2:N0}）；每一次都會銷毀並重建一條連線。' -f $faultRate, $result.ReturnFaulted, $returnTotal)
        }
    }

    # ================= 效能 =================
    if ($result.NPlusOneRequests.Count -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message ('偵測到 N+1 徵兆：有 request 對同一張 entity 查詢達 {0:N0} 次以上。' -f $NPlusOneThreshold)
    }
    if ($result.LockWaitCount -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message ('偵測到 {0:N0} 次子池鎖等待（最長 {1:N0} 毫秒）；建線移出鎖之後此事件不應出現，屬回歸徵兆。' -f $result.LockWaitCount, $result.LockWaitMax)
    }
    if ($result.CrmFailures -gt 0) {
        Add-Reason -Result $result -Severity WARN -Message ('{0:N0} 次 CRM 操作以失敗結束。' -f $result.CrmFailures)
    }

    # ================= 資源趨勢 =================
    if ($result.ProcSamples -lt 2) {
        Add-Reason -Result $result -Severity WARN -Message '程序資源快照少於兩筆；無法檢定記憶體或控制代碼是否單調成長，本次執行不構成無洩漏的證據。'
    }
    else {
        $handleGrowth = $result.ProcLast.Handles - $result.ProcFirst.Handles
        $privateGrowth = $result.ProcLast.PrivateMb - $result.ProcFirst.PrivateMb
        if ($handleGrowth -ge 500) {
            Add-Reason -Result $result -Severity WARN -Message ('控制代碼數成長 {0:N0}（{1:N0} 至 {2:N0}）；非受控資源可能未釋放。' -f $handleGrowth, $result.ProcFirst.Handles, $result.ProcLast.Handles)
        }
        if ($privateGrowth -ge 256) {
            Add-Reason -Result $result -Severity WARN -Message ('私有記憶體成長 {0:N0} MB（{1:N0} 至 {2:N0}）。' -f $privateGrowth, $result.ProcFirst.PrivateMb, $result.ProcLast.PrivateMb)
        }
    }
    if ($result.PoolSamples -ge 2) {
        $aliveGrowth = $result.PoolLast.Alive - $result.PoolFirst.Alive
        if ($aliveGrowth -ge 5) {
            Add-Reason -Result $result -Severity WARN -Message ('存活連線數成長 {0:N0}（{1:N0} 至 {2:N0}）；連線可能只進不出。' -f $aliveGrowth, $result.PoolFirst.Alive, $result.PoolLast.Alive)
        }
        $unreturned = $result.PoolLast.Acquires - $result.PoolLast.Releases
        if ($unreturned -gt 0) {
            Add-Reason -Result $result -Severity WARN -Message ('最後一筆連線池快照顯示累計租借比歸還多 {0:N0} 次；可能有租約未歸還。' -f $unreturned)
        }
        if ($result.PoolLast.SubPools -gt $result.PoolFirst.SubPools) {
            Add-Reason -Result $result -Severity WARN -Message ('子池數量由 {0:N0} 成長為 {1:N0}；子池目前沒有回收路徑，啟用 per-user 隔離後會單調累積。' -f $result.PoolFirst.SubPools, $result.PoolLast.SubPools)
        }
    }
    if ($result.DroppedEvents -gt 0) { Add-Reason -Result $result -Severity WARN -Message '有 Trace 事件被丟棄；證據可能不完整。' }

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
        Add-Reason -Result $result -Severity WARN -Message '找不到檔案；三檔證據集合不完整。'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message '檔案無法以 UTF-8 完整解碼或讀取。' }
    if ($result.PerfCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message '沒有 [Perf] 事件；無法評估端點效能。' }
    if ($result.EndpointOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message '端點種類數超過有限聚合上限。' }
    if ($result.SlowCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ("{0} 個請求達到 {1} 毫秒慢請求門檻。" -f $result.SlowCount, $SlowThreshold) }
    if ($result.NPlusOneCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '偵測到 [Perf-N+1] 指標。' }
    if ($result.GapCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '偵測到 [Perf-Gap] 指標。' }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '偵測到錯誤／例外／致命錯誤關鍵字。' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '偵測到疑似敏感資料模式；報告已省略原始值。' }

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
    $result | Add-Member NoteProperty EncodingUsed 'Big5（950 編碼頁）'
    $result | Add-Member NoteProperty Categories (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::OrdinalIgnoreCase))

    if (-not [System.IO.File]::Exists($Path)) {
        Add-Reason -Result $result -Severity WARN -Message '找不到檔案；三檔證據集合不完整。'
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
            $category = '(未分類)'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message '檔案無法以 Big5 完整解碼或讀取。' }
    if ($result.EntryCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message '沒有 StringToProcess 項目；證據不足或格式不受支援。' }
    if ($result.CategoryOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message '類別種類數超過有限聚合上限。' }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '偵測到英文或繁體中文錯誤指標。' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message '偵測到疑似敏感資料模式；報告已省略原始值。' }

    return $result
}

function Format-NullableTime {
    param($Value)
    if ($null -eq $Value) { return '無資料' }
    return ([DateTime]$Value).ToString('yyyy-MM-dd HH:mm:ss.fff')
}

function Format-Average {
    param([long]$Sum, [long]$Count)
    if ($Count -le 0) { return '無資料' }
    return [Math]::Round($Sum / [double]$Count, 2).ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Add-ReasonsToReport {
    param(
        [Parameter(Mandatory = $true)]$Lines,
        [Parameter(Mandatory = $true)]$Result
    )
    if ($Result.Reasons.Count -eq 0) {
        [void]$Lines.Add('- 未偵測到明確違規。')
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
        [void]$Lines.Add('- 潛在敏感資料模式命中：0')
        return
    }
    [void]$Lines.Add('| 模式 | 命中的行數 |')
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
        [void]$crossNotes.Add('至少有一個檔案沒有可辨識的時間範圍；無法進行完整事件對齊。')
    }
    else {
        $latestStart = ($timed | Sort-Object StartTime -Descending | Select-Object -First 1).StartTime
        $earliestEnd = ($timed | Sort-Object EndTime | Select-Object -First 1).EndTime
        if ($latestStart -gt $earliestEnd) {
            $crossStatus = 'WARN'
            [void]$crossNotes.Add('可辨識的時間範圍沒有明確重疊；跨檔案因果關聯需要一次受控的完整重現。')
        }
        else {
            [void]$crossNotes.Add('可辨識的時間範圍有重疊，可支援針對同一次重現進行人工關聯。')
        }
    }
    if ($crossStatus -eq 'WARN' -and $overall -eq 'PASS') { $overall = 'WARN' }

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add('# ChurchReport 三檔 Trace 分析報告')
    [void]$lines.Add('')
    [void]$lines.Add(('- 產生時間：{0}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')))
    [void]$lines.Add(('- 整體狀態：**{0}**' -f $overall))
    [void]$lines.Add(('- 慢請求門檻：{0} 毫秒' -f $SlowRequestThresholdMs))
    [void]$lines.Add(('- 配對／聚合記憶體上限：{0:N0} 筆。溢位會標記為 WARN，不會被誤判為 PASS。' -f $MaxPairEntries))
    [void]$lines.Add('')
    [void]$lines.Add('## 執行摘要')
    [void]$lines.Add('')
    [void]$lines.Add('| 檔案 | 狀態 | 行數 | 大小（位元組） | 時間範圍 |')
    [void]$lines.Add('|---|---|---:|---:|---|')
    foreach ($result in $results) {
        $range = if ($null -eq $result.StartTime) { '無資料' } else { (Format-NullableTime $result.StartTime) + ' 至 ' + (Format-NullableTime $result.EndTime) }
        [void]$lines.Add(('| {0} | **{1}** | {2:N0} | {3:N0} | {4} |' -f $result.Name, $result.Status, $result.Lines, $result.Length, $range))
    }

    [void]$lines.Add('')
    [void]$lines.Add('## 檔案清單與唯讀契約')
    [void]$lines.Add('')
    [void]$lines.Add('| 檔案 | 路徑 | 存在 | 最後修改時間 |')
    [void]$lines.Add('|---|---|---|---|')
    foreach ($result in $results) {
        $modified = if ($null -eq $result.LastWriteTime) { '無資料' } else { $result.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') }
        $existsLabel = if ($result.Exists) { '是' } else { '否' }
        [void]$lines.Add(('| {0} | `{1}` | {2} | {3} |' -f $result.Name, ($result.Path -replace '\|', '\|'), $existsLabel, $modified))
    }
    [void]$lines.Add('')
    [void]$lines.Add('所有輸入均以 `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete` 串流讀取；分析器不會修改原始 Trace。')

    [void]$lines.Add('')
    [void]$lines.Add(('## Dataverse 管理與隔離（{0}）' -f $dataverse.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- JSONL：{0:N0} 行，成功解析 {1:N0} 行，解析錯誤 {2:N0} 行' -f $dataverse.Lines, $dataverse.Parsed, $dataverse.ParseErrors))
    [void]$lines.Add(('- 請求配對：缺少結束 {0:N0}，多出的結束 {1:N0}' -f $dataverse.MissingRequestEnds, $dataverse.OrphanRequestEnds))
    [void]$lines.Add(('- 租約配對：缺少歸還 {0:N0}，多出的歸還 {1:N0}' -f $dataverse.MissingReturns, $dataverse.OrphanReturns))
    [void]$lines.Add(('- 請求耗時：{0:N0} 筆，平均 {1} 毫秒，最大 {2:N0} 毫秒' -f $dataverse.RequestDurationCount, (Format-Average $dataverse.RequestDurationSum $dataverse.RequestDurationCount), $dataverse.RequestDurationMax))
    [void]$lines.Add(('- 取得等待：{0:N0} 筆，平均 {1} 毫秒，最大 {2:N0} 毫秒，逾時 {3:N0} 次' -f $dataverse.AcquireWaitCount, (Format-Average $dataverse.AcquireWaitSum $dataverse.AcquireWaitCount), $dataverse.AcquireWaitMax, $dataverse.Timeouts))
    [void]$lines.Add(('- 租約持有：{0:N0} 筆，平均 {1} 毫秒，最大 {2:N0} 毫秒' -f $dataverse.HeldCount, (Format-Average $dataverse.HeldSum $dataverse.HeldCount), $dataverse.HeldMax))
    [void]$lines.Add(('- 連線池（Pool）：健康檢查失敗 {0:N0} 次、低於 MinSize 的清理快照 {1:N0} 次、未清除呼叫端狀態 {2:N0} 次、丟棄事件 {3:N0} 次' -f $dataverse.HealthFailures, $dataverse.CleanupBelowMinSnapshots, $dataverse.CallerStateViolations, $dataverse.DroppedEvents))
    [void]$lines.Add(('- 租約歸還狀態：healthy {0:N0}、faulted {1:N0}{2}' -f $dataverse.ReturnHealthy, $dataverse.ReturnFaulted, $(
        if (($dataverse.ReturnHealthy + $dataverse.ReturnFaulted) -gt 0) {
            '（faulted 比率 {0:P1}）' -f ([double]$dataverse.ReturnFaulted / [double]($dataverse.ReturnHealthy + $dataverse.ReturnFaulted))
        } else { '' })))
    [void]$lines.Add(('- 建立連線：成功 {0:N0}、失敗 {1:N0}、平均 {2} 毫秒、最長 {3:N0} 毫秒' -f $dataverse.CreateOk, $dataverse.CreateFailed, (Format-Average $dataverse.CreateMsSum ($dataverse.CreateOk + $dataverse.CreateFailed)), $dataverse.CreateMsMax))
    [void]$lines.Add(('- 取得結果配對：wait {0:N0} = hit {1:N0} + miss {2:N0} + fail {3:N0}{4}' -f $dataverse.AcquireWaitCount, $dataverse.AcquireHits, $dataverse.AcquireMisses, $dataverse.AcquireFails, $(
        if ($dataverse.AcquireWaitCount -eq ($dataverse.AcquireHits + $dataverse.AcquireMisses + $dataverse.AcquireFails)) { '　✔ 不變量成立' } else { '　✘ 不變量不成立' })))
    [void]$lines.Add(('- 清理淘汰：{0:N0} 條；越過保底的清理 {1:N0} 次' -f $dataverse.CleanupEvicted, $dataverse.CleanupBelowMinSnapshots))
    [void]$lines.Add('- 清理判讀：只有「淘汰前高於 MinSize、淘汰後低於 MinSize」才計為越線。舊規則單看 `idleAfter < minSize`，在池中沒有閒置連線（idle = 0）時恆為真，會產生大量假陽性。')
    if ($dataverse.DisposeByReason.Count -gt 0) {
        $disposeText = (($dataverse.DisposeByReason.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { '{0} {1:N0}' -f $_.Key, $_.Value }) -join '、')
        [void]$lines.Add(('- 連線淘汰原因：{0}' -f $disposeText))
    }
    if ($dataverse.ErrorKinds.Count -gt 0) {
        $kindText = (($dataverse.ErrorKinds.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { '{0} {1:N0}' -f $_.Key, $_.Value }) -join '、')
        [void]$lines.Add(('- 錯誤種類（僅型別名稱，不含訊息內容）：{0}' -f $kindText))
    }
    [void]$lines.Add(('- 使用者隔離：有效虛擬識別碼 {0:N0} 個，格式違規 {1:N0} 次' -f $dataverse.UniquePseudonyms, $dataverse.InvalidPseudonyms))
    [void]$lines.Add('')
    [void]$lines.Add('### 事件統計')
    [void]$lines.Add('')
    [void]$lines.Add('| 事件 | 次數 |')
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($dataverse.EventCounts.GetEnumerator() | Sort-Object Name)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')

    # ===================== 效能歸因 =====================
    [void]$lines.Add('### 效能歸因')
    [void]$lines.Add('')
    $appMs = [Math]::Max(0, $dataverse.RequestDurationSum - $dataverse.RequestCrmMsSum)
    [void]$lines.Add(('- CRM 操作 {0:N0} 次，累計 {1:N0} 毫秒；失敗 {2:N0} 次' -f $dataverse.CrmOpCount, $dataverse.CrmMsTotal, $dataverse.CrmFailures))
    [void]$lines.Add(('- 全部請求耗時 {0:N0} 毫秒，其中 CRM {1:N0} 毫秒、應用程式自身 {2:N0} 毫秒' -f $dataverse.RequestDurationSum, $dataverse.RequestCrmMsSum, $appMs))
    [void]$lines.Add('- 判讀：CRM 佔比高代表往返次數或查詢成本是瓶頸；應用程式自身佔比高則代表瓶頸在 CRM 之外（序列化、加密、Session 或本機運算）。')
    [void]$lines.Add('')

    if ($dataverse.EntityStats.Count -gt 0) {
        [void]$lines.Add(('#### Entity 查詢分佈（前 {0} 名）' -f $Top))
        [void]$lines.Add('')
        [void]$lines.Add('| Entity | 次數 | 累計毫秒 | 平均毫秒 | 最長毫秒 |')
        [void]$lines.Add('|---|---:|---:|---:|---:|')
        foreach ($entry in ($dataverse.EntityStats.GetEnumerator() | Sort-Object -Property { $_.Value.Ms } -Descending | Select-Object -First $Top)) {
            [void]$lines.Add(('| `{0}` | {1:N0} | {2:N0} | {3} | {4:N0} |' -f $entry.Key, $entry.Value.Count, $entry.Value.Ms, (Format-Average $entry.Value.Ms $entry.Value.Count), $entry.Value.Max))
        }
        if ($dataverse.EntityOverflow -gt 0) {
            [void]$lines.Add(('' ))
            [void]$lines.Add(('- Entity 種類超過聚合上限，另有 {0:N0} 次未計入。' -f $dataverse.EntityOverflow))
        }
        [void]$lines.Add('')
    }

    if ($dataverse.NPlusOneRequests.Count -gt 0) {
        [void]$lines.Add(('#### N+1 徵兆（同一 entity 在單一請求內查詢 ≥ {0} 次）' -f $NPlusOneThreshold))
        [void]$lines.Add('')
        [void]$lines.Add('| TraceId | 重複最多的 Entity | 該 Entity 次數 | 請求內 CRM 次數 | CRM 毫秒 | 請求毫秒 |')
        [void]$lines.Add('|---|---|---:|---:|---:|---:|')
        foreach ($item in ($dataverse.NPlusOneRequests | Sort-Object -Property TopCount -Descending)) {
            [void]$lines.Add(('| `{0}` | `{1}` | {2:N0} | {3:N0} | {4:N0} | {5:N0} |' -f $item.TraceId, $item.Entity, $item.TopCount, $item.CrmCount, $item.CrmMs, $item.DurationMs))
        }
        [void]$lines.Add('')
        [void]$lines.Add('- 判讀：同一張表被重複查詢，通常是迴圈內逐筆查詢所致。合併為單次批次查詢的效益，約等於「次數 × 單次往返成本」。優化單一查詢對此無效。')
        [void]$lines.Add('')
    }

    if ($dataverse.SlowRequests.Count -gt 0) {
        [void]$lines.Add(('#### 慢請求歸因（≥ {0} 毫秒）' -f $SlowRequestThresholdMs))
        [void]$lines.Add('')
        [void]$lines.Add('| TraceId | 總毫秒 | CRM 毫秒 | 應用程式毫秒 | CRM 次數 | 重複最多的 Entity | 次數 |')
        [void]$lines.Add('|---|---:|---:|---:|---:|---|---:|')
        foreach ($item in ($dataverse.SlowRequests | Sort-Object -Property DurationMs -Descending)) {
            [void]$lines.Add(('| `{0}` | {1:N0} | {2:N0} | {3:N0} | {4:N0} | `{5}` | {6:N0} |' -f $item.TraceId, $item.DurationMs, $item.CrmMs, $item.AppMs, $item.CrmCount, $item.TopEntity, $item.TopCount))
        }
        [void]$lines.Add('')
    }

    # ===================== Session Leakage =====================
    [void]$lines.Add('### Session Leakage 判定')
    [void]$lines.Add('')
    [void]$lines.Add('| 判準 | 觀測值 | 說明 |')
    [void]$lines.Add('|---|---:|---|')
    [void]$lines.Add(('| 同一 client 的租約區間重疊 | {0:N0} | 終極判準；非零代表兩個請求同時持有同一條實體連線 |' -f $dataverse.LeaseOverlaps))
    [void]$lines.Add(('| 請求結束時未歸還的租約 | {0:N0} 個請求 | 非零代表租約洩漏到請求邊界之外 |' -f $dataverse.LeaseOutstandingRequests))
    [void]$lines.Add(('| 平行進入同一 Gateway | {0:N0} 次 / {1:N0} 個請求 | Gateway 的 depth 與 lease 欄位無同步保護，平行進入可能造成連線共用 |' -f $dataverse.ConcurrentGatewayEvents, $dataverse.ConcurrentGatewayRequests))
    [void]$lines.Add(('| Gateway 釋放時仍持有租約 | {0:N0} 次 | 租約靠 DI 回收 scope 才被救回，而非正常執行路徑 |' -f $dataverse.ScopeEndLeaseHeld))
    [void]$lines.Add(('| 歸還時仍帶呼叫端身分 | {0:N0} 次 | 非零代表 impersonation 狀態跨請求殘留 |' -f $dataverse.CallerStateViolations))
    [void]$lines.Add(('| 觀測到的最大 reentrant 深度 | {0:N0} | 為 1 代表巢狀路徑未被觸發，reentrant 防線本次未受驗證 |' -f $dataverse.MaxDepthObserved))
    [void]$lines.Add('')

    # ===================== 資源趨勢 =====================
    [void]$lines.Add('### 資源趨勢（記憶體與連線洩漏）')
    [void]$lines.Add('')
    if ($dataverse.ProcSamples -lt 2) {
        [void]$lines.Add(('- 程序快照僅 {0:N0} 筆，不足以檢定趨勢。洩漏判定需要至少兩個時間點；請延長重現時間。' -f $dataverse.ProcSamples))
    }
    else {
        [void]$lines.Add(('- 程序快照 {0:N0} 筆' -f $dataverse.ProcSamples))
        [void]$lines.Add('')
        [void]$lines.Add('| 指標 | 起始 | 結束 | 變化 | 判讀 |')
        [void]$lines.Add('|---|---:|---:|---:|---|')
        [void]$lines.Add(('| Managed 記憶體 (MB) | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 持續上升代表受控物件未釋放 |' -f $dataverse.ProcFirst.ManagedMb, $dataverse.ProcLast.ManagedMb, ($dataverse.ProcLast.ManagedMb - $dataverse.ProcFirst.ManagedMb)))
        [void]$lines.Add(('| 私有記憶體 (MB) | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | Managed 平穩但此值上升，指向非受控資源 |' -f $dataverse.ProcFirst.PrivateMb, $dataverse.ProcLast.PrivateMb, ($dataverse.ProcLast.PrivateMb - $dataverse.ProcFirst.PrivateMb)))
        [void]$lines.Add(('| 控制代碼數 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 連線、檔案或 socket 未釋放的最直接指標 |' -f $dataverse.ProcFirst.Handles, $dataverse.ProcLast.Handles, ($dataverse.ProcLast.Handles - $dataverse.ProcFirst.Handles)))
        [void]$lines.Add(('| 執行緒數 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 上升代表執行緒洩漏或飢餓 |' -f $dataverse.ProcFirst.Threads, $dataverse.ProcLast.Threads, ($dataverse.ProcLast.Threads - $dataverse.ProcFirst.Threads)))
        [void]$lines.Add(('| Gen2 回收次數 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 持續上升代表物件不斷晉升到長命世代 |' -f $dataverse.ProcFirst.Gen2, $dataverse.ProcLast.Gen2, ($dataverse.ProcLast.Gen2 - $dataverse.ProcFirst.Gen2)))
    }
    [void]$lines.Add('')
    if ($dataverse.PoolSamples -lt 2) {
        [void]$lines.Add(('- 連線池快照僅 {0:N0} 筆，不足以檢定連線數趨勢。' -f $dataverse.PoolSamples))
    }
    else {
        [void]$lines.Add(('- 連線池快照 {0:N0} 筆；存活連線峰值 {1:N0}、子池數峰值 {2:N0}' -f $dataverse.PoolSamples, $dataverse.PoolAliveMax, $dataverse.SubPoolsMax))
        [void]$lines.Add('')
        [void]$lines.Add('| 指標 | 起始 | 結束 | 變化 | 判讀 |')
        [void]$lines.Add('|---|---:|---:|---:|---|')
        [void]$lines.Add(('| 存活連線 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 穩定運行時應在 MinSize 附近震盪 |' -f $dataverse.PoolFirst.Alive, $dataverse.PoolLast.Alive, ($dataverse.PoolLast.Alive - $dataverse.PoolFirst.Alive)))
        [void]$lines.Add(('| 累計建立 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 與淘汰數的差值應約等於存活數 |' -f $dataverse.PoolFirst.Created, $dataverse.PoolLast.Created, ($dataverse.PoolLast.Created - $dataverse.PoolFirst.Created)))
        [void]$lines.Add(('| 累計淘汰 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 建立遠多於淘汰代表連線抖動 |' -f $dataverse.PoolFirst.Discarded, $dataverse.PoolLast.Discarded, ($dataverse.PoolLast.Discarded - $dataverse.PoolFirst.Discarded)))
        [void]$lines.Add(('| 租借減歸還 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 非零代表有租約尚未歸還 |' -f ($dataverse.PoolFirst.Acquires - $dataverse.PoolFirst.Releases), ($dataverse.PoolLast.Acquires - $dataverse.PoolLast.Releases), (($dataverse.PoolLast.Acquires - $dataverse.PoolLast.Releases) - ($dataverse.PoolFirst.Acquires - $dataverse.PoolFirst.Releases))))
        [void]$lines.Add(('| 子池數 | {0:N0} | {1:N0} | {2:+#,##0;-#,##0;0} | 子池無回收路徑，啟用 per-user 隔離後會單調累積 |' -f $dataverse.PoolFirst.SubPools, $dataverse.PoolLast.SubPools, ($dataverse.PoolLast.SubPools - $dataverse.PoolFirst.SubPools)))
    }
    [void]$lines.Add('')

    Add-ReasonsToReport -Lines $lines -Result $dataverse
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $dataverse.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## 應用程式與效能 Trace.log（{0}）' -f $application.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- `[Perf]` {0:N0}, `[Perf-N+1]` {1:N0}, `[Perf-Gap]` {2:N0}, `[Perf-Startup]` {3:N0}' -f $application.PerfCount, $application.NPlusOneCount, $application.GapCount, $application.StartupCount))
    [void]$lines.Add(('- 慢請求 {0:N0} 次、啟動最大耗時 {1:N0} 毫秒、錯誤／例外／致命錯誤 {2:N0} 次、警告 {3:N0} 次' -f $application.SlowCount, $application.StartupMax, $application.ErrorCount, $application.WarningCount))
    [void]$lines.Add('')
    [void]$lines.Add(('### 最慢端點（前 {0} 名；查詢、GUID 與長數字已遮罩）' -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add('| 端點 | 次數 | 平均總耗時（毫秒） | 最大總耗時（毫秒） | CRM 呼叫次數 | CRM 耗時（毫秒） | 最大 crm.n | 平均間隔（毫秒） | 最大間隔（毫秒） |')
    [void]$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($endpoint in ($application.Endpoints.Values | Sort-Object MaxTotal -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |' -f $endpoint.Path, $endpoint.Hits, (Format-Average $endpoint.TotalSum $endpoint.Hits), $endpoint.MaxTotal, $endpoint.CrmCountSum, $endpoint.CrmMsSum, $endpoint.MaxCrmN, (Format-Average $endpoint.GapSum $endpoint.Hits), $endpoint.MaxGap))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $application
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $application.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## 舊版 ToolUtility Trace（{0}）' -f $toolUtility.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- 編碼：{0}；{1:N0} 行；{2:N0} 個 StringToProcess 項目；{3:N0} 個錯誤指標' -f $toolUtility.EncodingUsed, $toolUtility.Lines, $toolUtility.EntryCount, $toolUtility.ErrorCount))
    [void]$lines.Add('')
    [void]$lines.Add(('### 常見安全類別（前 {0} 名；已省略訊息內容）' -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add('| 類別 | 次數 |')
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($toolUtility.Categories.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $toolUtility
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $toolUtility.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## 跨檔案關聯（{0}）' -f $crossStatus))
    [void]$lines.Add('')
    foreach ($note in $crossNotes) { [void]$lines.Add(('- {0}' -f $note)) }
    [void]$lines.Add('- 分析器不會根據模糊文字猜測 traceId／端點關係。若沒有共用的關聯識別碼，只能進行時間範圍與聚合結果的關聯。')

    [void]$lines.Add('')
    [void]$lines.Add('## 建議與限制')
    [void]$lines.Add('')
    [void]$lines.Add('- FAIL：重新收集 Trace 前，請先修正配對、Pool 隔離、解析或敏感資料問題。')
    [void]$lines.Add('- WARN：請從同一次 Debug 重現收集三個檔案，並檢查慢端點、N+1、Gap、逾時與丟棄事件指標。')
    [void]$lines.Add('- 本報告本身無法證明不存在記憶體／工作階段（Session）洩漏；正式組態（Release）驗證仍需要並行 A/B 隔離、控制代碼釋放、長時間穩定性與資源基準檢查。')
    [void]$lines.Add('- 分析期間檔案可能仍在追加內容；本報告是當下可讀取的快照，可能不包含之後產生的事件。')
    [void]$lines.Add('- 敏感資料模式掃描採保守策略；請在來源環境確認命中結果，原始命中文字刻意不予保留。')

    $reportDirectory = [System.IO.Path]::GetDirectoryName($reportPathFull)
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [void][System.IO.Directory]::CreateDirectory($reportDirectory)
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($reportPathFull, (($lines -join "`r`n") + "`r`n"), $utf8NoBom)

    Write-Output ("整體狀態：{0}" -f $overall)
    Write-Output ("報告路徑：{0}" -f $reportPathFull)
    Write-Output ("Dataverse={0}；Trace.log={1}；ToolUtility={2}；跨檔案={3}" -f $dataverse.Status, $application.Status, $toolUtility.Status, $crossStatus)

    if ($overall -eq 'FAIL') { exit 2 }
    exit 0
}
catch {
    Write-Error ("Trace 分析器執行失敗：{0}`r`n{1}" -f $_.Exception.Message, $_.InvocationInfo.PositionMessage)
    exit 1
}
