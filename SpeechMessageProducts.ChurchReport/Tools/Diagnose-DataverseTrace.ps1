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
    '建置與單元測試：dotnet build，以及 ToolUtility.Tests、ToolUtility.Dataverse.Tests、ChurchReport.MemberInfo.Tests。',
    'Run H 靜態相依檢查：ToolUtility 的 Microsoft.AspNetCore、FrameworkReference、DEBUG-only、Trace.Listeners 與 AutoFlush 掃描。',
    'H1 identityName → sessionId → anon fallback、T7 完整 JSONL schema 與端對端欄位值斷言。',
    'Trace Enabled=false 關閉路徑，以及程序停止時 writer flush、檔案輪替與 MaxRetainedFiles 保留數。',
    'A/B 使用者並行隔離、登入授權正確性、真實 Dataverse timeout/cancel/fault 演練。',
    'Controllers 零 diff、UTF-8/CRLF、git 白名單與其他交付稽核。'
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
    $reportLines.Add('# Dataverse Trace 診斷結論報告')
    $reportLines.Add('')
    $reportLines.Add(('- 產生時間：{0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
    $reportLines.Add(('- Trace 檔案：`{0}`' -f $TraceFilePath))
    $reportLines.Add(('- 最後結論：**{0}** — {1}' -f $ConclusionStatus, $ConclusionText))
    $reportLines.Add('')
    $reportLines.Add('## 檢查結果')
    $reportLines.Add('')
    $reportLines.Add('| 結果 | 檢查項目 | 詳情 |')
    $reportLines.Add('|---|---|---|')
    foreach ($result in $script:Results) {
        $details = ([string]$result.Details).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
        $reportLines.Add(('| {0} | {1} | {2} |' -f $result.Status, $result.Name, $details))
    }
    $reportLines.Add('')
    $reportLines.Add('## 尚未由本腳本涵蓋的補測')
    $reportLines.Add('')
    $reportLines.Add('本報告只代表 JSONL 執行軌跡診斷結果，不代表整個 Run H 品質門檻已全部完成。')
    foreach ($item in $script:UncoveredItems) {
        $reportLines.Add('- {0}' -f $item)
    }
    $reportLines.Add('')
    $reportLines.Add('## 判讀規則')
    $reportLines.Add('')
    $reportLines.Add('- **PASS**：該項檢查有足夠的 JSONL 證據且未發現違規。')
    $reportLines.Add('- **WARN**：沒有直接硬性違規，但資料不足或需要另外補測；不可當成完整通過。')
    $reportLines.Add('- **FAIL**：發現可由 JSONL 證明的錯誤，或 Trace 檔無法解析/不存在。')

    try {
        $reportDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
        if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
            [System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllLines($OutputPath, $reportLines, $utf8NoBom)
        Write-Host ('診斷報告已輸出：{0}' -f $OutputPath) -ForegroundColor Cyan
    }
    catch {
        Write-Host ('診斷報告輸出失敗：{0}' -f $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host '============================================================' -ForegroundColor Cyan
Write-Host 'Dataverse Trace 唯讀診斷' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ('開始時間: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))

try {
    $resolvedPath = [System.IO.Path]::GetFullPath($TracePath)
}
catch {
    Add-DiagnosticResult -Name 'Trace 路徑' -Status 'FAIL' -Details ('無法解析路徑：{0}' -f $_.Exception.Message)
    Write-Host '結論：FAIL（Trace 路徑無法解析）' -ForegroundColor Red
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

Write-Host ('檔案: {0}' -f $resolvedPath)

if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    Add-DiagnosticResult -Name 'Trace 檔案存在' -Status 'FAIL' -Details '找不到指定的 JSONL 檔案。請確認網站已啟動、設定路徑正確，並重新操作一次。'
    Write-Host '結論：FAIL（找不到 Trace 檔案）' -ForegroundColor Red
    Write-DiagnosticReport -OutputPath $resolvedReportPath -TraceFilePath $resolvedPath -ConclusionStatus 'FAIL' -ConclusionText '找不到指定的 JSONL 檔案。'
    return
}

$fileInfo = Get-Item -LiteralPath $resolvedPath
Add-DiagnosticResult -Name 'Trace 檔案存在' -Status 'PASS' -Details ('{0} bytes，最後寫入 {1}' -f $fileInfo.Length, $fileInfo.LastWriteTime)

$records = New-Object System.Collections.Generic.List[object]
$parseErrors = New-Object System.Collections.Generic.List[string]
$lineNumber = 0

foreach ($line in [System.IO.File]::ReadLines($resolvedPath)) {
    $lineNumber++
    if ([string]::IsNullOrWhiteSpace($line)) {
        $parseErrors.Add(('第 {0} 行為空白' -f $lineNumber))
        continue
    }

    try {
        $records.Add(($line | ConvertFrom-Json -ErrorAction Stop))
    }
    catch {
        $parseErrors.Add(('第 {0} 行 JSON 無法解析：{1}' -f $lineNumber, $_.Exception.Message))
    }
}

if ($parseErrors.Count -eq 0 -and $records.Count -gt 0) {
    Add-DiagnosticResult -Name 'JSONL 解析' -Status 'PASS' -Details ('{0} 行全部可解析' -f $records.Count)
}
elseif ($records.Count -eq 0) {
    Add-DiagnosticResult -Name 'JSONL 解析' -Status 'FAIL' -Details '檔案沒有任何可解析的事件。請先操作網站，再重新執行本腳本。'
}
else {
    Add-DiagnosticResult -Name 'JSONL 解析' -Status 'FAIL' -Details ('共 {0} 行，其中 {1} 行無法解析' -f $lineNumber, $parseErrors.Count)
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
        Add-DiagnosticResult -Name '事件基本欄位' -Status 'PASS' -Details '每筆事件均含 ts 與 ev'
    }
    else {
        Add-DiagnosticResult -Name '事件基本欄位' -Status 'FAIL' -Details ('{0} 筆事件缺少 ts 或 ev' -f $missingCoreFields.Count)
    }

    Write-Host ''
    Write-Host '事件統計：' -ForegroundColor Cyan
    $records |
        Group-Object -Property ev |
        Sort-Object Name |
        Format-Table Count, Name -AutoSize

    $beginIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('request.begin') -PropertyName 'traceId'
    $endIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('request.end') -PropertyName 'traceId'
    $beginCount = @($records | Where-Object { $_.ev -eq 'request.begin' }).Count
    $endCount = @($records | Where-Object { $_.ev -eq 'request.end' }).Count

    if ($beginCount -eq 0 -and $endCount -eq 0) {
        Add-DiagnosticResult -Name 'Request begin/end 成對' -Status 'WARN' -Details '目前沒有 request.begin 或 request.end；請在網站執行登入或 CRM 操作後重跑。'
    }
    else {
        $missingEnd = @($beginIds | Where-Object { $_ -notin $endIds })
        $orphanEnd = @($endIds | Where-Object { $_ -notin $beginIds })
        if ($beginCount -eq $endCount -and $missingEnd.Count -eq 0 -and $orphanEnd.Count -eq 0) {
            Add-DiagnosticResult -Name 'Request begin/end 成對' -Status 'PASS' -Details ('begin={0}、end={1}，traceId 全部配對' -f $beginCount, $endCount)
        }
        else {
            Add-DiagnosticResult -Name 'Request begin/end 成對' -Status 'FAIL' -Details ('begin={0}、end={1}、缺少 end={2}、孤立 end={3}' -f $beginCount, $endCount, $missingEnd.Count, $orphanEnd.Count)
        }
    }

    $acquireNames = @('pool.acquire.hit', 'pool.acquire.miss')
    $acquireIds = Get-EventIds -InputRecords $records.ToArray() -EventNames $acquireNames -PropertyName 'leaseId'
    $returnIds = Get-EventIds -InputRecords $records.ToArray() -EventNames @('pool.return') -PropertyName 'leaseId'
    $acquireCount = @($records | Where-Object { $_.ev -in $acquireNames }).Count
    $returnCount = @($records | Where-Object { $_.ev -eq 'pool.return' }).Count

    if ($acquireCount -eq 0 -and $returnCount -eq 0) {
        Add-DiagnosticResult -Name 'Lease acquire/return 成對' -Status 'WARN' -Details '目前沒有 pool 借還事件；請執行會使用 Dataverse 的操作後重跑。'
    }
    else {
        $missingReturn = @($acquireIds | Where-Object { $_ -notin $returnIds })
        $orphanReturn = @($returnIds | Where-Object { $_ -notin $acquireIds })
        if ($missingReturn.Count -eq 0 -and $orphanReturn.Count -eq 0) {
            Add-DiagnosticResult -Name 'Lease acquire/return 成對' -Status 'PASS' -Details ('acquire={0}、return={1}，leaseId 全部配對' -f $acquireCount, $returnCount)
        }
        else {
            Add-DiagnosticResult -Name 'Lease acquire/return 成對' -Status 'FAIL' -Details ('acquire={0}、return={1}、缺少 return={2}、孤立 return={3}' -f $acquireCount, $returnCount, $missingReturn.Count, $orphanReturn.Count)
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
        Add-DiagnosticResult -Name 'User 假名格式' -Status 'WARN' -Details '目前沒有含 user 欄位的事件。'
    }
    elseif ($invalidUsers.Count -eq 0) {
        Add-DiagnosticResult -Name 'User 假名格式' -Status 'PASS' -Details ('{0} 個 user 欄位均為 u_ 開頭的假名' -f $userRecords.Count)
    }
    else {
        Add-DiagnosticResult -Name 'User 假名格式' -Status 'FAIL' -Details ('{0} 個 user 欄位不符合 u_XXXXXXXX 格式' -f $invalidUsers.Count)
    }

    $returnRecords = @($records | Where-Object { $_.ev -eq 'pool.return' })
    $nonEmptyCallerIds = @(
        $returnRecords | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.callerIdAtReturn)
        }
    )

    if ($returnRecords.Count -eq 0) {
        Add-DiagnosticResult -Name 'CallerId 歸還前清除' -Status 'WARN' -Details '目前沒有 pool.return 事件。'
    }
    elseif ($nonEmptyCallerIds.Count -eq 0) {
        Add-DiagnosticResult -Name 'CallerId 歸還前清除' -Status 'PASS' -Details ('{0} 筆 pool.return 的 callerIdAtReturn 均為空' -f $returnRecords.Count)
    }
    else {
        Add-DiagnosticResult -Name 'CallerId 歸還前清除' -Status 'FAIL' -Details ('{0} 筆 pool.return 仍含非空 callerIdAtReturn' -f $nonEmptyCallerIds.Count)
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
        Add-DiagnosticResult -Name 'MinSize 保底' -Status 'WARN' -Details '目前沒有 pool.cleanup 事件。'
    }
    elseif ($minSizeViolations.Count -eq 0 -and $alreadyBelowMinSize.Count -eq 0) {
        Add-DiagnosticResult -Name 'MinSize 保底' -Status 'PASS' -Details ('{0} 筆 cleanup 均維持 idleAfter >= minSize' -f $cleanupRecords.Count)
    }
    elseif ($minSizeViolations.Count -gt 0) {
        Add-DiagnosticResult -Name 'MinSize 保底' -Status 'FAIL' -Details ('{0} 筆 cleanup 的 idleAfter 低於 minSize' -f $minSizeViolations.Count)
    }
    else {
        Add-DiagnosticResult -Name 'MinSize 保底' -Status 'WARN' -Details ('{0} 筆 cleanup 開始時 idleBefore 已低於 minSize；未觀察到淘汰穿透保底，但需另查健康淘汰後的補建' -f $alreadyBelowMinSize.Count)
    }

    $droppedCount = @($records | Where-Object { $_.ev -eq 'trace.dropped' }).Count
    if ($droppedCount -eq 0) {
        Add-DiagnosticResult -Name 'Trace 佇列丟棄' -Status 'PASS' -Details '沒有 trace.dropped 事件'
    }
    else {
        Add-DiagnosticResult -Name 'Trace 佇列丟棄' -Status 'WARN' -Details ('發現 {0} 筆 trace.dropped；部分診斷事件可能不完整' -f $droppedCount)
    }

    $rawTrace = [System.IO.File]::ReadAllText($resolvedPath)
    $sensitivePattern = '(?i)(password\s*[:=]|access[_-]?token\s*[:=]|authorization\s*[:=]|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,})'
    $sensitiveMatches = [System.Text.RegularExpressions.Regex]::Matches($rawTrace, $sensitivePattern)
    if ($sensitiveMatches.Count -eq 0) {
        Add-DiagnosticResult -Name '敏感資料表面掃描' -Status 'PASS' -Details '未發現明顯密碼、Token、Authorization 或 Email 文字模式'
    }
    else {
        Add-DiagnosticResult -Name '敏感資料表面掃描' -Status 'FAIL' -Details ('發現 {0} 個疑似敏感資料模式；腳本不輸出原文，請人工檢查對應事件' -f $sensitiveMatches.Count)
    }
}

$failed = @($script:Results | Where-Object Status -eq 'FAIL')
$warnings = @($script:Results | Where-Object Status -eq 'WARN')
$conclusionStatus = if ($failed.Count -gt 0) { 'FAIL' } elseif ($warnings.Count -gt 0) { 'WARN' } else { 'PASS' }
$conclusionText = if ($conclusionStatus -eq 'FAIL') {
    '發現必須處理的 JSONL 診斷錯誤。'
}
elseif ($conclusionStatus -eq 'WARN') {
    '沒有直接硬性錯誤，但仍有警告或需要另外補測的項目。'
}
else {
    '本腳本涵蓋的 JSONL 執行軌跡檢查全部通過。'
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '最後結論' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if ($conclusionStatus -eq 'FAIL') {
    Write-Host ('結論：FAIL（{0} 個失敗、{1} 個警告）' -f $failed.Count, $warnings.Count) -ForegroundColor Red
    Write-Host '請先修正 FAIL 項目，重新啟動網站並重新產生 Trace 後再稽核。' -ForegroundColor Red
}
elseif ($conclusionStatus -eq 'WARN') {
    Write-Host ('結論：WARN（沒有硬性失敗，但有 {0} 個警告）' -f $warnings.Count) -ForegroundColor Yellow
    Write-Host '請依每個 WARN 的詳情補做操作或獨立測試；WARN 不等於完整通過。' -ForegroundColor Yellow
}
else {
    Write-Host '結論：PASS（本腳本涵蓋的 JSONL 執行軌跡檢查均通過）' -ForegroundColor Green
}

Write-Host ('結束時間: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '本腳本未涵蓋的補測項目' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '以下項目不能從 JSONL 單獨證明，請另外執行：' -ForegroundColor Yellow
for ($index = 0; $index -lt $script:UncoveredItems.Count; $index++) {
    Write-Host ('  {0}. {1}' -f ($index + 1), $script:UncoveredItems[$index])
}
Write-Host '上述補測未完成前，本腳本的 PASS 應解讀為「Trace 執行證據通過」，不是「Run H 全部結案」。' -ForegroundColor Yellow

Write-DiagnosticReport -OutputPath $resolvedReportPath -TraceFilePath $resolvedPath -ConclusionStatus $conclusionStatus -ConclusionText $conclusionText

if ($Watch -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    Write-Host ''
    Write-Host ('開始即時監看最後 {0} 行；按 Ctrl+C 結束。' -f $Tail) -ForegroundColor Cyan
    Get-Content -LiteralPath $resolvedPath -Tail $Tail -Wait
}
