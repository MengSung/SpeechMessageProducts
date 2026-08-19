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
        'Sensitive field value' = '(?i)\b(?:password|passwd|pwd|token|secret|authorization|cookie|credential|username|email|phone|mobile|address)\b[^:=\r\n]{0,3}[:=]\s*(?!"?(?:null|none|"))\S+'
        'Bearer/JWT' = '(?i)\bBearer\s+[A-Za-z0-9._~+/-]{12,}|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        'Email' = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b'
        'Taiwan identity-number pattern' = '(?i)\b[A-Z][12]\d{8}\b'
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
        return '(blank)'
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
        Add-Reason -Result $result -Severity WARN -Message 'File is missing; the three-file evidence set is incomplete.'
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
                $eventName = '(missing ev)'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message 'The file could not be decoded or read completely.' }
    if ($result.Parsed -eq 0) { Add-Reason -Result $result -Severity WARN -Message 'No parseable JSONL events; evidence is insufficient.' }
    if ($result.ParseErrors -gt 0) { Add-Reason -Result $result -Severity FAIL -Message ("{0} JSONL lines could not be parsed." -f $result.ParseErrors) }
    if ($result.PairOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'Pair tracking exceeded the memory bound; pairing is partial evidence only.' }
    if (($result.MissingRequestEnds + $result.OrphanRequestEnds) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'request.begin/request.end events are not fully paired.' }
    if (($result.MissingReturns + $result.OrphanReturns) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'lease acquire/return events are not fully paired.' }
    if ($result.HealthFailures -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'Pool health failures were detected.' }
    if ($result.CallerStateViolations -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'A returned lease retained caller state.' }
    if ($result.InvalidPseudonyms -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'A user value did not match the short-lived pseudonym format.' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'Potential sensitive-data patterns were found; raw values are omitted.' }
    if ($result.Timeouts -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'Pool acquire timeouts were detected.' }
    if ($result.DroppedEvents -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'Trace events were dropped; evidence may be incomplete.' }

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
        Add-Reason -Result $result -Severity WARN -Message 'File is missing; the three-file evidence set is incomplete.'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message 'The file could not be decoded as UTF-8 or read completely.' }
    if ($result.PerfCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message 'No [Perf] events; endpoint performance cannot be assessed.' }
    if ($result.EndpointOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'Endpoint cardinality exceeded the bounded aggregation limit.' }
    if ($result.SlowCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message ("{0} requests reached the {1}ms slow threshold." -f $result.SlowCount, $SlowThreshold) }
    if ($result.NPlusOneCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '[Perf-N+1] indicators were detected.' }
    if ($result.GapCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message '[Perf-Gap] indicators were detected.' }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'error/exception/fatal keywords were detected.' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'Potential sensitive-data patterns were found; raw values are omitted.' }

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
    $result | Add-Member NoteProperty EncodingUsed 'Big5 (code page 950)'
    $result | Add-Member NoteProperty Categories (New-Object 'System.Collections.Generic.Dictionary[string,long]' ([StringComparer]::OrdinalIgnoreCase))

    if (-not [System.IO.File]::Exists($Path)) {
        Add-Reason -Result $result -Severity WARN -Message 'File is missing; the three-file evidence set is incomplete.'
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
            $category = '(uncategorized)'
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

    if ($null -ne $result.ReadError) { Add-Reason -Result $result -Severity FAIL -Message 'The file could not be decoded as Big5 or read completely.' }
    if ($result.EntryCount -eq 0) { Add-Reason -Result $result -Severity WARN -Message 'No StringToProcess entries; evidence is insufficient or the format is unsupported.' }
    if ($result.CategoryOverflow -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'Category cardinality exceeded the bounded aggregation limit.' }
    if ($result.ErrorCount -gt 0) { Add-Reason -Result $result -Severity WARN -Message 'English or Traditional Chinese error indicators were detected.' }
    if ((Get-SensitiveTotal $result.SensitiveCounts) -gt 0) { Add-Reason -Result $result -Severity FAIL -Message 'Potential sensitive-data patterns were found; raw values are omitted.' }

    return $result
}

function Format-NullableTime {
    param($Value)
    if ($null -eq $Value) { return 'n/a' }
    return ([DateTime]$Value).ToString('yyyy-MM-dd HH:mm:ss.fff')
}

function Format-Average {
    param([long]$Sum, [long]$Count)
    if ($Count -le 0) { return 'n/a' }
    return [Math]::Round($Sum / [double]$Count, 2).ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Add-ReasonsToReport {
    param(
        [Parameter(Mandatory = $true)]$Lines,
        [Parameter(Mandatory = $true)]$Result
    )
    if ($Result.Reasons.Count -eq 0) {
        [void]$Lines.Add('- No explicit violation was detected.')
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
        [void]$Lines.Add('- Potential sensitive-data pattern hits: 0')
        return
    }
    [void]$Lines.Add('| Pattern | Matched lines |')
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
        [void]$crossNotes.Add('At least one file has no recognizable time range; full event alignment is unavailable.')
    }
    else {
        $latestStart = ($timed | Sort-Object StartTime -Descending | Select-Object -First 1).StartTime
        $earliestEnd = ($timed | Sort-Object EndTime | Select-Object -First 1).EndTime
        if ($latestStart -gt $earliestEnd) {
            $crossStatus = 'WARN'
            [void]$crossNotes.Add('Recognizable time ranges do not strictly overlap; cross-file causality needs a single controlled reproduction.')
        }
        else {
            [void]$crossNotes.Add('Recognizable time ranges overlap and can support manual correlation from one reproduction.')
        }
    }
    if ($crossStatus -eq 'WARN' -and $overall -eq 'PASS') { $overall = 'WARN' }

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add('# ChurchReport Three-File Trace Analysis Report')
    [void]$lines.Add('')
    [void]$lines.Add(('- Generated: {0}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')))
    [void]$lines.Add(('- Overall status: **{0}**' -f $overall))
    [void]$lines.Add(('- Slow-request threshold: {0} ms' -f $SlowRequestThresholdMs))
    [void]$lines.Add(('- Pair/aggregation memory bound: {0:N0} entries. Overflow is WARN and cannot become a false PASS.' -f $MaxPairEntries))
    [void]$lines.Add('')
    [void]$lines.Add('## Executive Summary')
    [void]$lines.Add('')
    [void]$lines.Add('| File | Status | Lines | Size (bytes) | Time range |')
    [void]$lines.Add('|---|---|---:|---:|---|')
    foreach ($result in $results) {
        $range = if ($null -eq $result.StartTime) { 'n/a' } else { (Format-NullableTime $result.StartTime) + ' to ' + (Format-NullableTime $result.EndTime) }
        [void]$lines.Add(('| {0} | **{1}** | {2:N0} | {3:N0} | {4} |' -f $result.Name, $result.Status, $result.Lines, $result.Length, $range))
    }

    [void]$lines.Add('')
    [void]$lines.Add('## File Inventory and Read-Only Contract')
    [void]$lines.Add('')
    [void]$lines.Add('| File | Path | Exists | Last modified |')
    [void]$lines.Add('|---|---|---|---|')
    foreach ($result in $results) {
        $modified = if ($null -eq $result.LastWriteTime) { 'n/a' } else { $result.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') }
        [void]$lines.Add(('| {0} | `{1}` | {2} | {3} |' -f $result.Name, ($result.Path -replace '\|', '\|'), $result.Exists, $modified))
    }
    [void]$lines.Add('')
    [void]$lines.Add('All inputs are streamed with `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete`; the analyzer does not modify source traces.')

    [void]$lines.Add('')
    [void]$lines.Add(('## Dataverse Management and Isolation ({0})' -f $dataverse.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- JSONL: {0:N0} lines, {1:N0} parsed, {2:N0} parse errors' -f $dataverse.Lines, $dataverse.Parsed, $dataverse.ParseErrors))
    [void]$lines.Add(('- Request pairing: {0:N0} missing end, {1:N0} orphan end' -f $dataverse.MissingRequestEnds, $dataverse.OrphanRequestEnds))
    [void]$lines.Add(('- Lease pairing: {0:N0} missing return, {1:N0} orphan return' -f $dataverse.MissingReturns, $dataverse.OrphanReturns))
    [void]$lines.Add(('- Request duration: {0:N0} samples, {1} ms average, {2:N0} ms maximum' -f $dataverse.RequestDurationCount, (Format-Average $dataverse.RequestDurationSum $dataverse.RequestDurationCount), $dataverse.RequestDurationMax))
    [void]$lines.Add(('- Acquire wait: {0:N0} samples, {1} ms average, {2:N0} ms maximum, {3:N0} timeouts' -f $dataverse.AcquireWaitCount, (Format-Average $dataverse.AcquireWaitSum $dataverse.AcquireWaitCount), $dataverse.AcquireWaitMax, $dataverse.Timeouts))
    [void]$lines.Add(('- Lease held: {0:N0} samples, {1} ms average, {2:N0} ms maximum' -f $dataverse.HeldCount, (Format-Average $dataverse.HeldSum $dataverse.HeldCount), $dataverse.HeldMax))
    [void]$lines.Add(('- Pool: {0:N0} health failures, {1:N0} below-MinSize cleanup snapshots, {2:N0} uncleared caller states, {3:N0} dropped events' -f $dataverse.HealthFailures, $dataverse.CleanupBelowMinSnapshots, $dataverse.CallerStateViolations, $dataverse.DroppedEvents))
    [void]$lines.Add('- Cleanup interpretation: `idleAfter < minSize` is concurrency-sensitive because a request can lease an idle client after cleanup selection and before the trace snapshot. It is reported as an observation, not a violation, unless independent lease/total-count evidence proves cleanup removed too many live clients.')
    [void]$lines.Add(('- User isolation: {0:N0} valid pseudonyms, {1:N0} format violations' -f $dataverse.UniquePseudonyms, $dataverse.InvalidPseudonyms))
    [void]$lines.Add('')
    [void]$lines.Add('### Event Counts')
    [void]$lines.Add('')
    [void]$lines.Add('| Event | Count |')
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($dataverse.EventCounts.GetEnumerator() | Sort-Object Name)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $dataverse
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $dataverse.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## Application and Performance Trace.log ({0})' -f $application.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- `[Perf]` {0:N0}, `[Perf-N+1]` {1:N0}, `[Perf-Gap]` {2:N0}, `[Perf-Startup]` {3:N0}' -f $application.PerfCount, $application.NPlusOneCount, $application.GapCount, $application.StartupCount))
    [void]$lines.Add(('- Slow requests {0:N0}, startup maximum {1:N0} ms, error/exception {2:N0}, warning {3:N0}' -f $application.SlowCount, $application.StartupMax, $application.ErrorCount, $application.WarningCount))
    [void]$lines.Add('')
    [void]$lines.Add(('### Slowest Endpoints (Top {0}; query, GUID, and long numbers are masked)' -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add('| Endpoint | Hits | Avg total ms | Max total ms | CRM calls | CRM ms | Max crm.n | Avg gap ms | Max gap ms |')
    [void]$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($endpoint in ($application.Endpoints.Values | Sort-Object MaxTotal -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |' -f $endpoint.Path, $endpoint.Hits, (Format-Average $endpoint.TotalSum $endpoint.Hits), $endpoint.MaxTotal, $endpoint.CrmCountSum, $endpoint.CrmMsSum, $endpoint.MaxCrmN, (Format-Average $endpoint.GapSum $endpoint.Hits), $endpoint.MaxGap))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $application
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $application.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## Legacy ToolUtility Trace ({0})' -f $toolUtility.Status))
    [void]$lines.Add('')
    [void]$lines.Add(('- Encoding: {0}; {1:N0} lines; {2:N0} StringToProcess entries; {3:N0} error indicators' -f $toolUtility.EncodingUsed, $toolUtility.Lines, $toolUtility.EntryCount, $toolUtility.ErrorCount))
    [void]$lines.Add('')
    [void]$lines.Add(('### Common Safe Categories (Top {0}; message text is omitted)' -f $Top))
    [void]$lines.Add('')
    [void]$lines.Add('| Category | Count |')
    [void]$lines.Add('|---|---:|')
    foreach ($entry in ($toolUtility.Categories.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top)) {
        [void]$lines.Add(('| `{0}` | {1:N0} |' -f $entry.Key, $entry.Value))
    }
    [void]$lines.Add('')
    Add-ReasonsToReport -Lines $lines -Result $toolUtility
    [void]$lines.Add('')
    Add-SensitiveTable -Lines $lines -Counts $toolUtility.SensitiveCounts

    [void]$lines.Add('')
    [void]$lines.Add(('## Cross-File Correlation ({0})' -f $crossStatus))
    [void]$lines.Add('')
    foreach ($note in $crossNotes) { [void]$lines.Add(('- {0}' -f $note)) }
    [void]$lines.Add('- The analyzer does not guess traceId/endpoint relationships from fuzzy text. Without a shared correlation id, only time-range and aggregate correlation is possible.')

    [void]$lines.Add('')
    [void]$lines.Add('## Recommendations and Limitations')
    [void]$lines.Add('')
    [void]$lines.Add('- FAIL: repair pairing, pool isolation, parsing, or sensitive-data issues before collecting a new trace.')
    [void]$lines.Add('- WARN: collect all three files from one Debug reproduction and inspect slow endpoints, N+1, Gap, timeout, and dropped indicators.')
    [void]$lines.Add('- This report alone cannot prove absence of memory/session leakage. Release still requires concurrent A/B isolation, handle-release, soak, and resource-baseline checks.')
    [void]$lines.Add('- Files may be appended during analysis. The report is a readable snapshot and may not include later events.')
    [void]$lines.Add('- Sensitive-pattern scanning is conservative. Verify hits in the source environment; raw matching text is intentionally never retained.')

    $reportDirectory = [System.IO.Path]::GetDirectoryName($reportPathFull)
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [void][System.IO.Directory]::CreateDirectory($reportDirectory)
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($reportPathFull, (($lines -join "`r`n") + "`r`n"), $utf8NoBom)

    Write-Output ("Overall status: {0}" -f $overall)
    Write-Output ("Report path: {0}" -f $reportPathFull)
    Write-Output ("Dataverse={0}; Trace.log={1}; ToolUtility={2}; CrossFile={3}" -f $dataverse.Status, $application.Status, $toolUtility.Status, $crossStatus)

    if ($overall -eq 'FAIL') { exit 2 }
    exit 0
}
catch {
    Write-Error ("Trace analyzer failed: {0}`r`n{1}" -f $_.Exception.Message, $_.InvocationInfo.PositionMessage)
    exit 1
}
