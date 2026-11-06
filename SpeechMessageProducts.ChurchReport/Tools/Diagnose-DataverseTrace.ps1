<#
檔案整體說明

目的與定位：唯讀診斷 Dataverse Gateway 的 JSONL 執行軌跡。

主要功能與流程：
操作入口包含Add-DiagnosticResult、Get-EventIds、Write-DiagnosticReport；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
    唯讀診斷 Dataverse Gateway 的 JSONL 執行軌跡。

.DESCRIPTION
    本腳本不會修改 Trace 檔案、網站設定或任何原始碼，僅讀取一行一筆 JSON
    的 dataverse-trace.jsonl，並輸出可供人工稽核的結論。

    檢查項目：
      1. 檔案存在、大小與 JSONL 每行可解析性。
      2. 每筆事件是否具備 ts 與 ev 欄位，以及事件種類統計。
      3. request.begin 與 request.end 是否依 traceId 成對。
      4. pool.acquire.* 與 pool.return 是否依 leaseId 成對。
      5. user 欄位是否為 u_ 開頭的程序內假名，而非原始身分。
      6. pool.return 的 callerIdAtReturn 是否已清空（Run F）。
      7. pool.cleanup 的 idleAfter 是否低於 minSize（Run F）。
      8. 是否有 trace.dropped 或疑似密碼、Token、Email 等敏感資料。

    本腳本刻意不代替下列測試：
      - dotnet build 與三個 .NET 測試專案的品質門檻。
      - ToolUtility 不依賴 ASP.NET Core、FrameworkReference 與 DEBUG-only 靜態掃描。
      - H1 的 identityName/sessionId/anon 單元測試與 T7 完整 JSONL schema 斷言。
      - Trace Enabled=false 的關閉路徑、程序重啟 flush/檔案輪替與保留數測試。
      - 兩個使用者的並行隔離、登入授權正確性、真實 Dataverse timeout/cancel/fault 演練。
      - Controllers 零 diff、UTF-8/CRLF 與 git 白名單稽核。
    腳本結尾會列出這些補測項目與建議指令；本腳本的 PASS 只代表執行軌跡
    檢查通過，不代表整個 Run H 的所有品質門檻已自動通過。

    預設路徑與 ChurchReport 的 appsettings.Development.json 一致：
      D:\dataverse-trace\dataverse-trace.jsonl

    這是診斷工具，不是 Run G/Run H 的切換器。網站必須先啟動並實際操作，
    才會有 request、CRM 與 pool 事件；若檔案只有 pool.cleanup，request 檢查
    會顯示 WARN，而不是捏造通過或失敗結果。

.PARAMETER TracePath
    JSONL 檔案的絕對或相對路徑。

.PARAMETER Watch
    完成一次稽核後，以 -Tail 行數持續監看檔案新增內容。監看模式不會重跑稽核。

.PARAMETER Tail
    -Watch 模式顯示的初始行數，預設 20。

.PARAMETER ReportPath
    結論報告的輸出路徑。省略時會在 Trace 同一資料夾產生
    dataverse-trace.diagnostic-report.md。

.EXAMPLE
    .\Diagnose-DataverseTrace.ps1

.EXAMPLE
    .\Diagnose-DataverseTrace.ps1 -TracePath 'D:\dataverse-trace\dataverse-trace.jsonl' -Watch

.NOTES
    此檔案不輸出 Trace 原文，避免把可能含有敏感資料的行再次顯示到主控台。
    若結論為 FAIL，應先處理失敗項目，再重新啟動網站並重新產生一批 Trace。
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$TracePath = 'D:\dataverse-trace\dataverse-trace.jsonl',

    [switch]$Watch,

    [ValidateRange(1, 1000)]
    [int]$Tail = 20,

    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$script:Results = New-Object System.Collections.Generic.List[object]
$script:UncoveredItems = @(
    ([regex]::Unescape('\u5efa\u7f6e\u8207\u55ae\u5143\u6e2c\u8a66\uff1adotnet build\uff0c\u4ee5\u53ca ToolUtility.Tests\u3001ToolUtility.Dataverse.Tests\u3001ChurchReport.MemberInfo.Tests\u3002')),
    ([regex]::Unescape('Run H \u975c\u614b\u76f8\u4f9d\u6aa2\u67e5\uff1aToolUtility \u7684 Microsoft.AspNetCore\u3001FrameworkReference\u3001DEBUG-only\u3001Trace.Listeners \u8207 AutoFlush \u6383\u63cf\u3002')),
    ([regex]::Unescape('H1 identityName \u2192 sessionId \u2192 anon fallback\u3001T7 \u5b8c\u6574 JSONL schema \u8207\u7aef\u5c0d\u7aef\u6b04\u4f4d\u503c\u65b7\u8a00\u3002')),
    ([regex]::Unescape('Trace Enabled=false \u95dc\u9589\u8def\u5f91\uff0c\u4ee5\u53ca\u7a0b\u5e8f\u505c\u6b62\u6642 writer flush\u3001\u6a94\u6848\u8f2a\u66ff\u8207 MaxRetainedFiles \u4fdd\u7559\u6578\u3002')),
    ([regex]::Unescape('A/B \u4f7f\u7528\u8005\u4e26\u884c\u9694\u96e2\u3001\u767b\u5165\u6388\u6b0a\u6b63\u78ba\u6027\u3001\u771f\u5be6 Dataverse timeout/cancel/fault \u6f14\u7df4\u3002')),
    ([regex]::Unescape('Controllers \u96f6 diff\u3001UTF-8/CRLF\u3001git \u767d\u540d\u55ae\u8207\u5176\u4ed6\u4ea4\u4ed8\u7a3d\u6838\u3002'))
)

function Add-DiagnosticResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [string]$Details
    )

    $script:Results.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Details = $Details
    })

    $colour = switch ($Status) {
        'PASS' { 'Green' }
        'WARN' { 'Yellow' }
        'FAIL' { 'Red' }
    }
    Write-Host ('[{0}] {1}: {2}' -f $Status, $Name, $Details) -ForegroundColor $colour
}

function Get-EventIds {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$InputRecords,

        [Parameter(Mandatory = $true)]
        [string[]]$EventNames,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    @(
        $InputRecords |
            Where-Object { $_.ev -in $EventNames } |
            ForEach-Object { $_.$PropertyName } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            Sort-Object -Unique
    )
}

function Write-DiagnosticReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [string]$TraceFilePath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string]$ConclusionStatus,

        [Parameter(Mandatory = $true)]
        [string]$ConclusionText
    )

    $reportLines = New-Object System.Collections.Generic.List[string]
    $reportLines.Add(([regex]::Unescape('# Dataverse Trace \u8a3a\u65b7\u7d50\u8ad6\u5831\u544a')))
    $reportLines.Add('')
    $reportLines.Add((([regex]::Unescape('- \u7522\u751f\u6642\u9593\uff1a{0}')) -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
    $reportLines.Add((([regex]::Unescape('- Trace \u6a94\u6848\uff1a`{0}`')) -f $TraceFilePath))
    $reportLines.Add((([regex]::Unescape('- \u6700\u5f8c\u7d50\u8ad6\uff1a**{0}** \u2014 {1}')) -f $ConclusionStatus, $ConclusionText))
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('## \u6aa2\u67e5\u7d50\u679c')))
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('| \u7d50\u679c | \u6aa2\u67e5\u9805\u76ee | \u8a73\u60c5 |')))
    $reportLines.Add('|---|---|---|')
    foreach ($result in $script:Results) {
        $details = ([string]$result.Details).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
        $reportLines.Add(('| {0} | {1} | {2} |' -f $result.Status, $result.Name, $details))
    }
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('## \u5c1a\u672a\u7531\u672c\u8173\u672c\u6db5\u84cb\u7684\u88dc\u6e2c')))
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('\u672c\u5831\u544a\u53ea\u4ee3\u8868 JSONL \u57f7\u884c\u8ecc\u8de1\u8a3a\u65b7\u7d50\u679c\uff0c\u4e0d\u4ee3\u8868\u6574\u500b Run H \u54c1\u8cea\u9580\u6abb\u5df2\u5168\u90e8\u5b8c\u6210\u3002')))
    foreach ($item in $script:UncoveredItems) {
        $reportLines.Add('- {0}' -f $item)
    }
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('## \u5224\u8b80\u898f\u5247')))
    $reportLines.Add('')
    $reportLines.Add(([regex]::Unescape('- **PASS**\uff1a\u8a72\u9805\u6aa2\u67e5\u6709\u8db3\u5920\u7684 JSONL \u8b49\u64da\u4e14\u672a\u767c\u73fe\u9055\u898f\u3002')))
    $reportLines.Add(([regex]::Unescape('- **WARN**\uff1a\u6c92\u6709\u76f4\u63a5\u786c\u6027\u9055\u898f\uff0c\u4f46\u8cc7\u6599\u4e0d\u8db3\u6216\u9700\u8981\u53e6\u5916\u88dc\u6e2c\uff1b\u4e0d\u53ef\u7576\u6210\u5b8c\u6574\u901a\u904e\u3002')))
    $reportLines.Add(([regex]::Unescape('- **FAIL**\uff1a\u767c\u73fe\u53ef\u7531 JSONL \u8b49\u660e\u7684\u932f\u8aa4\uff0c\u6216 Trace \u6a94\u7121\u6cd5\u89e3\u6790/\u4e0d\u5b58\u5728\u3002')))

    try {
        $reportDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
        if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
            [System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllLines($OutputPath, $reportLines, $utf8NoBom)
        Write-Host (([regex]::Unescape('\u8a3a\u65b7\u5831\u544a\u5df2\u8f38\u51fa\uff1a{0}')) -f $OutputPath) -ForegroundColor Cyan
    }
    catch {
        Write-Host (([regex]::Unescape('\u8a3a\u65b7\u5831\u544a\u8f38\u51fa\u5931\u6557\uff1a{0}')) -f $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ([regex]::Unescape('Dataverse Trace \u552f\u8b80\u8a3a\u65b7')) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host (([regex]::Unescape('\u958b\u59cb\u6642\u9593: {0}')) -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))

try {
    $resolvedPath = [System.IO.Path]::GetFullPath($TracePath)
}
catch {
    Add-DiagnosticResult -Name ([regex]::Unescape('Trace \u8def\u5f91')) -Status 'FAIL' -Details (([regex]::Unescape('\u7121\u6cd5\u89e3\u6790\u8def\u5f91\uff1a{0}')) -f $_.Exception.Message)
    Write-Host ([regex]::Unescape('\u7d50\u8ad6\uff1aFAIL\uff08Trace \u8def\u5f91\u7121\u6cd5\u89e3\u6790\uff09')) -ForegroundColor Red
    return
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDirectory = [System.IO.Path]::GetDirectoryName($resolvedPath)
    if ([string]::IsNullOrWhiteSpace($reportDirectory)) {
        $reportDirectory = (Get-Location).Path
    }
    $resolvedReportPath = [System.IO.Path]::Combine(
        $reportDirectory,
        'dataverse-trace.diagnostic-report.md')
}
else {
    $resolvedReportPath = [System.IO.Path]::GetFullPath($ReportPath)
}

Write-Host (([regex]::Unescape('\u6a94\u6848: {0}')) -f $resolvedPath)

if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    Add-DiagnosticResult -Name ([regex]::Unescape('Trace \u6a94\u6848\u5b58\u5728')) -Status 'FAIL' -Details ([regex]::Unescape('\u627e\u4e0d\u5230\u6307\u5b9a\u7684 JSONL \u6a94\u6848\u3002\u8acb\u78ba\u8a8d\u7db2\u7ad9\u5df2\u555f\u52d5\u3001\u8a2d\u5b9a\u8def\u5f91\u6b63\u78ba\uff0c\u4e26\u91cd\u65b0\u64cd\u4f5c\u4e00\u6b21\u3002'))
    Write-Host ([regex]::Unescape('\u7d50\u8ad6\uff1aFAIL\uff08\u627e\u4e0d\u5230 Trace \u6a94\u6848\uff09')) -ForegroundColor Red
    Write-DiagnosticReport -OutputPath $resolvedReportPath -TraceFilePath $resolvedPath -ConclusionStatus 'FAIL' -ConclusionText ([regex]::Unescape('\u627e\u4e0d\u5230\u6307\u5b9a\u7684 JSONL \u6a94\u6848\u3002'))
    return
}

$fileInfo = Get-Item -LiteralPath $resolvedPath
Add-DiagnosticResult -Name ([regex]::Unescape('Trace \u6a94\u6848\u5b58\u5728')) -Status 'PASS' -Details (([regex]::Unescape('{0} bytes\uff0c\u6700\u5f8c\u5beb\u5165 {1}')) -f $fileInfo.Length, $fileInfo.LastWriteTime)

$records = New-Object System.Collections.Generic.List[object]
$parseErrors = New-Object System.Collections.Generic.List[string]
$lineNumber = 0

foreach ($line in [System.IO.File]::ReadLines($resolvedPath)) {
    $lineNumber++
    if ([string]::IsNullOrWhiteSpace($line)) {
        $parseErrors.Add((([regex]::Unescape('\u7b2c {0} \u884c\u70ba\u7a7a\u767d')) -f $lineNumber))
        continue
    }

    try {
        $records.Add(($line | ConvertFrom-Json -ErrorAction Stop))
    }
    catch {
        $parseErrors.Add((([regex]::Unescape('\u7b2c {0} \u884c JSON \u7121\u6cd5\u89e3\u6790\uff1a{1}')) -f $lineNumber, $_.Exception.Message))
    }
}

if ($parseErrors.Count -eq 0 -and $records.Count -gt 0) {
    Add-DiagnosticResult -Name ([regex]::Unescape('JSONL \u89e3\u6790')) -Status 'PASS' -Details (([regex]::Unescape('{0} \u884c\u5168\u90e8\u53ef\u89e3\u6790')) -f $records.Count)
}
elseif ($records.Count -eq 0) {
    Add-DiagnosticResult -Name ([regex]::Unescape('JSONL \u89e3\u6790')) -Status 'FAIL' -Details ([regex]::Unescape('\u6a94\u6848\u6c92\u6709\u4efb\u4f55\u53ef\u89e3\u6790\u7684\u4e8b\u4ef6\u3002\u8acb\u5148\u64cd\u4f5c\u7db2\u7ad9\uff0c\u518d\u91cd\u65b0\u57f7\u884c\u672c\u8173\u672c\u3002'))
}
else {
    Add-DiagnosticResult -Name ([regex]::Unescape('JSONL \u89e3\u6790')) -Status 'FAIL' -Details (([regex]::Unescape('\u5171 {0} \u884c\uff0c\u5176\u4e2d {1} \u884c\u7121\u6cd5\u89e3\u6790')) -f $lineNumber, $parseErrors.Count)
    foreach ($parseError in $parseErrors | Select-Object -First 10) {
        Write-Host ('  - {0}' -f $parseError) -ForegroundColor Red
    }
}

if ($records.Count -gt 0) {
    $missingCoreFields = @(
        $records | Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.ts) -or
            [string]::IsNullOrWhiteSpace([string]$_.ev)
        }
    )

    if ($missingCoreFields.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('\u4e8b\u4ef6\u57fa\u672c\u6b04\u4f4d')) -Status 'PASS' -Details ([regex]::Unescape('\u6bcf\u7b46\u4e8b\u4ef6\u5747\u542b ts \u8207 ev'))
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('\u4e8b\u4ef6\u57fa\u672c\u6b04\u4f4d')) -Status 'FAIL' -Details (([regex]::Unescape('{0} \u7b46\u4e8b\u4ef6\u7f3a\u5c11 ts \u6216 ev')) -f $missingCoreFields.Count)
    }

    Write-Host ''
    Write-Host ([regex]::Unescape('\u4e8b\u4ef6\u7d71\u8a08\uff1a')) -ForegroundColor Cyan
    $records |
        Group-Object -Property ev |
        Sort-Object Name |
        Format-Table Count, Name -AutoSize

    $beginIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('request.begin') -PropertyName 'traceId'
    $endIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('request.end') -PropertyName 'traceId'
    $beginCount = @($records | Where-Object { $_.ev -eq 'request.begin' }).Count
    $endCount = @($records | Where-Object { $_.ev -eq 'request.end' }).Count

    if ($beginCount -eq 0 -and $endCount -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('Request begin/end \u6210\u5c0d')) -Status 'WARN' -Details ([regex]::Unescape('\u76ee\u524d\u6c92\u6709 request.begin \u6216 request.end\uff1b\u8acb\u5728\u7db2\u7ad9\u57f7\u884c\u767b\u5165\u6216 CRM \u64cd\u4f5c\u5f8c\u91cd\u8dd1\u3002'))
    }
    else {
        $missingEnd = @($beginIds | Where-Object { $_ -notin $endIds })
        $orphanEnd = @($endIds | Where-Object { $_ -notin $beginIds })
        if ($beginCount -eq $endCount -and $missingEnd.Count -eq 0 -and $orphanEnd.Count -eq 0) {
            Add-DiagnosticResult -Name ([regex]::Unescape('Request begin/end \u6210\u5c0d')) -Status 'PASS' -Details (([regex]::Unescape('begin={0}\u3001end={1}\uff0ctraceId \u5168\u90e8\u914d\u5c0d')) -f $beginCount, $endCount)
        }
        else {
            Add-DiagnosticResult -Name ([regex]::Unescape('Request begin/end \u6210\u5c0d')) -Status 'FAIL' -Details (([regex]::Unescape('begin={0}\u3001end={1}\u3001\u7f3a\u5c11 end={2}\u3001\u5b64\u7acb end={3}')) -f $beginCount, $endCount, $missingEnd.Count, $orphanEnd.Count)
        }
    }

    $acquireNames = @('pool.acquire.hit', 'pool.acquire.miss')
    $acquireIds = Get-EventIds -InputRecords $records.ToArray() -EventNames $acquireNames -PropertyName 'leaseId'
    $returnIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('pool.return') -PropertyName 'leaseId'
    $acquireCount = @($records | Where-Object { $_.ev -in $acquireNames }).Count
    $returnCount = @($records | Where-Object { $_.ev -eq 'pool.return' }).Count

    if ($acquireCount -eq 0 -and $returnCount -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('Lease acquire/return \u6210\u5c0d')) -Status 'WARN' -Details ([regex]::Unescape('\u76ee\u524d\u6c92\u6709 pool \u501f\u9084\u4e8b\u4ef6\uff1b\u8acb\u57f7\u884c\u6703\u4f7f\u7528 Dataverse \u7684\u64cd\u4f5c\u5f8c\u91cd\u8dd1\u3002'))
    }
    else {
        $missingReturn = @($acquireIds | Where-Object { $_ -notin $returnIds })
        $orphanReturn = @($returnIds | Where-Object { $_ -notin $acquireIds })
        if ($missingReturn.Count -eq 0 -and $orphanReturn.Count -eq 0) {
            Add-DiagnosticResult -Name ([regex]::Unescape('Lease acquire/return \u6210\u5c0d')) -Status 'PASS' -Details (([regex]::Unescape('acquire={0}\u3001return={1}\uff0cleaseId \u5168\u90e8\u914d\u5c0d')) -f $acquireCount, $returnCount)
        }
        else {
            Add-DiagnosticResult -Name ([regex]::Unescape('Lease acquire/return \u6210\u5c0d')) -Status 'FAIL' -Details (([regex]::Unescape('acquire={0}\u3001return={1}\u3001\u7f3a\u5c11 return={2}\u3001\u5b64\u7acb return={3}')) -f $acquireCount, $returnCount, $missingReturn.Count, $orphanReturn.Count)
        }
    }

    $userRecords = @(
        $records | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.user)
        }
    )
    $invalidUsers = @(
        $userRecords | Where-Object {
            [string]$_.user -notmatch '^u_[0-9a-f]{8}$'
        }
    )

    if ($userRecords.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('User \u5047\u540d\u683c\u5f0f')) -Status 'WARN' -Details ([regex]::Unescape('\u76ee\u524d\u6c92\u6709\u542b user \u6b04\u4f4d\u7684\u4e8b\u4ef6\u3002'))
    }
    elseif ($invalidUsers.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('User \u5047\u540d\u683c\u5f0f')) -Status 'PASS' -Details (([regex]::Unescape('{0} \u500b user \u6b04\u4f4d\u5747\u70ba u_ \u958b\u982d\u7684\u5047\u540d')) -f $userRecords.Count)
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('User \u5047\u540d\u683c\u5f0f')) -Status 'FAIL' -Details (([regex]::Unescape('{0} \u500b user \u6b04\u4f4d\u4e0d\u7b26\u5408 u_XXXXXXXX \u683c\u5f0f')) -f $invalidUsers.Count)
    }

    $returnRecords = @($records | Where-Object { $_.ev -eq 'pool.return' })
    $nonEmptyCallerIds = @(
        $returnRecords | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.callerIdAtReturn)
        }
    )

    if ($returnRecords.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('CallerId \u6b78\u9084\u524d\u6e05\u9664')) -Status 'WARN' -Details ([regex]::Unescape('\u76ee\u524d\u6c92\u6709 pool.return \u4e8b\u4ef6\u3002'))
    }
    elseif ($nonEmptyCallerIds.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('CallerId \u6b78\u9084\u524d\u6e05\u9664')) -Status 'PASS' -Details (([regex]::Unescape('{0} \u7b46 pool.return \u7684 callerIdAtReturn \u5747\u70ba\u7a7a')) -f $returnRecords.Count)
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('CallerId \u6b78\u9084\u524d\u6e05\u9664')) -Status 'FAIL' -Details (([regex]::Unescape('{0} \u7b46 pool.return \u4ecd\u542b\u975e\u7a7a callerIdAtReturn')) -f $nonEmptyCallerIds.Count)
    }

    $cleanupRecords = @($records | Where-Object { $_.ev -eq 'pool.cleanup' })
    # 若 cleanup 開始時 idleBefore 已低於 MinSize，cleanup 本身不可能靠淘汰把數量補回來；
    # 這是容量/健康檢查的補建問題，不應被誤判成「淘汰穿透保底」。只有在開始時已達到
    # MinSize、但淘汰後低於 MinSize，才是 Run F3 的硬性失敗；原始不足另列 WARN。
    $minSizeViolations = @(
        $cleanupRecords | Where-Object {
            [long]$_.idleBefore -ge [long]$_.minSize -and
            [long]$_.idleAfter -lt [long]$_.minSize
        }
    )
    $alreadyBelowMinSize = @(
        $cleanupRecords | Where-Object {
            [long]$_.idleBefore -lt [long]$_.minSize
        }
    )

    if ($cleanupRecords.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('MinSize \u4fdd\u5e95')) -Status 'WARN' -Details ([regex]::Unescape('\u76ee\u524d\u6c92\u6709 pool.cleanup \u4e8b\u4ef6\u3002'))
    }
    elseif ($minSizeViolations.Count -eq 0 -and $alreadyBelowMinSize.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('MinSize \u4fdd\u5e95')) -Status 'PASS' -Details (([regex]::Unescape('{0} \u7b46 cleanup \u5747\u7dad\u6301 idleAfter >= minSize')) -f $cleanupRecords.Count)
    }
    elseif ($minSizeViolations.Count -gt 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('MinSize \u4fdd\u5e95')) -Status 'FAIL' -Details (([regex]::Unescape('{0} \u7b46 cleanup \u7684 idleAfter \u4f4e\u65bc minSize')) -f $minSizeViolations.Count)
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('MinSize \u4fdd\u5e95')) -Status 'WARN' -Details (([regex]::Unescape('{0} \u7b46 cleanup \u958b\u59cb\u6642 idleBefore \u5df2\u4f4e\u65bc minSize\uff1b\u672a\u89c0\u5bdf\u5230\u6dd8\u6c70\u7a7f\u900f\u4fdd\u5e95\uff0c\u4f46\u9700\u53e6\u67e5\u5065\u5eb7\u6dd8\u6c70\u5f8c\u7684\u88dc\u5efa')) -f $alreadyBelowMinSize.Count)
    }

    $droppedCount = @($records | Where-Object { $_.ev -eq 'trace.dropped' }).Count
    if ($droppedCount -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('Trace \u4f47\u5217\u4e1f\u68c4')) -Status 'PASS' -Details ([regex]::Unescape('\u6c92\u6709 trace.dropped \u4e8b\u4ef6'))
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('Trace \u4f47\u5217\u4e1f\u68c4')) -Status 'WARN' -Details (([regex]::Unescape('\u767c\u73fe {0} \u7b46 trace.dropped\uff1b\u90e8\u5206\u8a3a\u65b7\u4e8b\u4ef6\u53ef\u80fd\u4e0d\u5b8c\u6574')) -f $droppedCount)
    }

    $rawTrace = [System.IO.File]::ReadAllText($resolvedPath)
    $sensitivePattern = '(?i)(password\s*[:=]|access[_-]?token\s*[:=]|authorization\s*[:=]|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,})'
    $sensitiveMatches = [System.Text.RegularExpressions.Regex]::Matches($rawTrace, $sensitivePattern)
    if ($sensitiveMatches.Count -eq 0) {
        Add-DiagnosticResult -Name ([regex]::Unescape('\u654f\u611f\u8cc7\u6599\u8868\u9762\u6383\u63cf')) -Status 'PASS' -Details ([regex]::Unescape('\u672a\u767c\u73fe\u660e\u986f\u5bc6\u78bc\u3001Token\u3001Authorization \u6216 Email \u6587\u5b57\u6a21\u5f0f'))
    }
    else {
        Add-DiagnosticResult -Name ([regex]::Unescape('\u654f\u611f\u8cc7\u6599\u8868\u9762\u6383\u63cf')) -Status 'FAIL' -Details (([regex]::Unescape('\u767c\u73fe {0} \u500b\u7591\u4f3c\u654f\u611f\u8cc7\u6599\u6a21\u5f0f\uff1b\u8173\u672c\u4e0d\u8f38\u51fa\u539f\u6587\uff0c\u8acb\u4eba\u5de5\u6aa2\u67e5\u5c0d\u61c9\u4e8b\u4ef6')) -f $sensitiveMatches.Count)
    }
}

$failed = @($script:Results | Where-Object Status -eq 'FAIL')
$warnings = @($script:Results | Where-Object Status -eq 'WARN')
$conclusionStatus = if ($failed.Count -gt 0) { 'FAIL' } elseif ($warnings.Count -gt 0) { 'WARN' } else { 'PASS' }
$conclusionText = if ($conclusionStatus -eq 'FAIL') {
    ([regex]::Unescape('\u767c\u73fe\u5fc5\u9808\u8655\u7406\u7684 JSONL \u8a3a\u65b7\u932f\u8aa4\u3002'))
}
elseif ($conclusionStatus -eq 'WARN') {
    ([regex]::Unescape('\u6c92\u6709\u76f4\u63a5\u786c\u6027\u932f\u8aa4\uff0c\u4f46\u4ecd\u6709\u8b66\u544a\u6216\u9700\u8981\u53e6\u5916\u88dc\u6e2c\u7684\u9805\u76ee\u3002'))
}
else {
    ([regex]::Unescape('\u672c\u8173\u672c\u6db5\u84cb\u7684 JSONL \u57f7\u884c\u8ecc\u8de1\u6aa2\u67e5\u5168\u90e8\u901a\u904e\u3002'))
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ([regex]::Unescape('\u6700\u5f8c\u7d50\u8ad6')) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if ($conclusionStatus -eq 'FAIL') {
    Write-Host (([regex]::Unescape('\u7d50\u8ad6\uff1aFAIL\uff08{0} \u500b\u5931\u6557\u3001{1} \u500b\u8b66\u544a\uff09')) -f $failed.Count, $warnings.Count) -ForegroundColor Red
    Write-Host ([regex]::Unescape('\u8acb\u5148\u4fee\u6b63 FAIL \u9805\u76ee\uff0c\u91cd\u65b0\u555f\u52d5\u7db2\u7ad9\u4e26\u91cd\u65b0\u7522\u751f Trace \u5f8c\u518d\u7a3d\u6838\u3002')) -ForegroundColor Red
}
elseif ($conclusionStatus -eq 'WARN') {
    Write-Host (([regex]::Unescape('\u7d50\u8ad6\uff1aWARN\uff08\u6c92\u6709\u786c\u6027\u5931\u6557\uff0c\u4f46\u6709 {0} \u500b\u8b66\u544a\uff09')) -f $warnings.Count) -ForegroundColor Yellow
    Write-Host ([regex]::Unescape('\u8acb\u4f9d\u6bcf\u500b WARN \u7684\u8a73\u60c5\u88dc\u505a\u64cd\u4f5c\u6216\u7368\u7acb\u6e2c\u8a66\uff1bWARN \u4e0d\u7b49\u65bc\u5b8c\u6574\u901a\u904e\u3002')) -ForegroundColor Yellow
}
else {
    Write-Host ([regex]::Unescape('\u7d50\u8ad6\uff1aPASS\uff08\u672c\u8173\u672c\u6db5\u84cb\u7684 JSONL \u57f7\u884c\u8ecc\u8de1\u6aa2\u67e5\u5747\u901a\u904e\uff09')) -ForegroundColor Green
}

Write-Host (([regex]::Unescape('\u7d50\u675f\u6642\u9593: {0}')) -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ([regex]::Unescape('\u672c\u8173\u672c\u672a\u6db5\u84cb\u7684\u88dc\u6e2c\u9805\u76ee')) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ([regex]::Unescape('\u4ee5\u4e0b\u9805\u76ee\u4e0d\u80fd\u5f9e JSONL \u55ae\u7368\u8b49\u660e\uff0c\u8acb\u53e6\u5916\u57f7\u884c\uff1a')) -ForegroundColor Yellow
for ($index = 0; $index -lt $script:UncoveredItems.Count; $index++) {
    Write-Host ('  {0}. {1}' -f ($index + 1), $script:UncoveredItems[$index])
}
Write-Host ([regex]::Unescape('\u4e0a\u8ff0\u88dc\u6e2c\u672a\u5b8c\u6210\u524d\uff0c\u672c\u8173\u672c\u7684 PASS \u61c9\u89e3\u8b80\u70ba\u300cTrace \u57f7\u884c\u8b49\u64da\u901a\u904e\u300d\uff0c\u4e0d\u662f\u300cRun H \u5168\u90e8\u7d50\u6848\u300d\u3002')) -ForegroundColor Yellow

Write-DiagnosticReport -OutputPath $resolvedReportPath -TraceFilePath $resolvedPath -ConclusionStatus $conclusionStatus -ConclusionText $conclusionText

if ($Watch -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    Write-Host ''
    Write-Host (([regex]::Unescape('\u958b\u59cb\u5373\u6642\u76e3\u770b\u6700\u5f8c {0} \u884c\uff1b\u6309 Ctrl+C \u7d50\u675f\u3002')) -f $Tail) -ForegroundColor Cyan
    Get-Content -LiteralPath $resolvedPath -Tail $Tail -Wait
}
