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
    [int]$SlowRequestThresholdMs = 1000
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
        [Parameter(Mandatory = $true)][int]$PairLimit
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
                }
                'pool.acquire.hit' {
                    if (-not [string]::IsNullOrWhiteSpace($leaseId)) {
                        if ($acquired.Count -lt $PairLimit) { [void]$acquired.Add($leaseId) } else { $result.PairOverflow++ }
                    }
                }
                'pool.acquire.miss' {
                    if (-not [string]::IsNullOrWhiteSpace($leaseId)) {
                        if ($acquired.Count -lt $PairLimit) { [void]$acquired.Add($leaseId) } else { $result.PairOverflow++ }
                    }
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
                    $idleAfter = Convert-ToLong (Get-RecordValue -Record $record -Name 'idleAfter')
                    $minSize = Convert-ToLong (Get-RecordValue -Record $record -Name 'minSize')
                    if ($idleAfter -lt $minSize) { $result.CleanupBelowMinSnapshots++ }
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

    $dataverse = Analyze-DataverseTrace -Path $dataversePath -PairLimit $MaxPairEntries
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
    [void]$lines.Add('- 清理判讀：`idleAfter < minSize` 會受並行執行影響，因為請求可能在清理選取後、Trace 快照前租用閒置用戶端。本項屬觀察結果，不直接視為違規；除非獨立的租約／總數證據證明清理移除了過多仍在使用的用戶端。')
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
