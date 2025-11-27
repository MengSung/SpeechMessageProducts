# 記憶體洩漏快速掃描腳本
# 檢查 ChurchReport 解決方案中可能導致記憶體洩漏的模式

param(
    [string]$ProjectPath = ".",
    [switch]$Detailed
)

# 設定輸出顏色
$script:IssueCount = 0

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host $Title -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Issue {
    param([string]$Message, [string]$Severity = "Warning")
    
    $script:IssueCount++
    
    switch ($Severity) {
        "Critical" { Write-Host "  ?? $Message" -ForegroundColor Red }
        "Warning"  { Write-Host "  ??  $Message" -ForegroundColor Yellow }
        "Info"     { Write-Host "  ??  $Message" -ForegroundColor Cyan }
        "Success"  { Write-Host "  ? $Message" -ForegroundColor Green }
    }
}

function Write-FileLocation {
    param([string]$Path, [int]$LineNumber)
    Write-Host "      ?? $Path`:$LineNumber" -ForegroundColor Gray
}

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ChurchReport 記憶體洩漏檢查工具                          ║" -ForegroundColor Green
Write-Host "║  Memory Leak Detection Scanner                             ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "掃描路徑: $ProjectPath" -ForegroundColor White
Write-Host "開始時間: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host ""

# ============================================================
# 1. 檢查 HttpClient 使用
# ============================================================
Write-Section "1. 檢查 HttpClient 使用"

$httpClientPatterns = @(
    "new HttpClient\(",
    "new RestClient\(",
    "new WebClient\("
)

$httpClientIssues = @()
foreach ($pattern in $httpClientPatterns) {
    $results = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
        Select-String -Pattern $pattern |
        Where-Object { 
            $_.Line -notmatch "//.*$pattern" -and 
            $_.Line -notmatch "/\*.*$pattern.*\*/" 
        }
    
    $httpClientIssues += $results
}

if ($httpClientIssues.Count -gt 0) {
    Write-Issue "發現 $($httpClientIssues.Count) 處 HttpClient/RestClient 實例化" "Critical"
    Write-Host "      ??  應該使用 IHttpClientFactory 或注入單例" -ForegroundColor Yellow
    
    if ($Detailed) {
        $httpClientIssues | Select-Object -First 10 | ForEach-Object {
            Write-FileLocation $_.Path $_.LineNumber
            Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
        }
        if ($httpClientIssues.Count -gt 10) {
            Write-Host "      ... 還有 $($httpClientIssues.Count - 10) 處 ..." -ForegroundColor DarkGray
        }
    }
} else {
    Write-Issue "未發現 HttpClient 問題" "Success"
}

# ============================================================
# 2. 檢查事件訂閱與取消訂閱
# ============================================================
Write-Section "2. 檢查事件訂閱"

$eventSubscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "\+=" |
    Where-Object { 
        $_.Line -notmatch "//.*\+=" -and 
        $_.Line -notmatch "/\*.*\+=.*\*/" 
    }

$eventUnsubscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "-=" |
    Where-Object { 
        $_.Line -notmatch "//.*-=" -and 
        $_.Line -notmatch "/\*.*-=.*\*/" 
    }

Write-Issue "發現 $($eventSubscriptions.Count) 處事件訂閱 (+=)" "Info"
Write-Issue "發現 $($eventUnsubscriptions.Count) 處事件取消訂閱 (-=)" "Info"

$subscribeFiles = $eventSubscriptions | Select-Object -ExpandProperty Path -Unique
$unsubscribeFiles = $eventUnsubscriptions | Select-Object -ExpandProperty Path -Unique
$filesWithoutUnsubscribe = $subscribeFiles | Where-Object { $_ -notin $unsubscribeFiles }

if ($filesWithoutUnsubscribe.Count -gt 0) {
    Write-Issue "發現 $($filesWithoutUnsubscribe.Count) 個檔案有訂閱但沒有取消訂閱" "Warning"
    
    if ($Detailed) {
        $filesWithoutUnsubscribe | Select-Object -First 5 | ForEach-Object {
            Write-Host "      ?? $_" -ForegroundColor Gray
        }
        if ($filesWithoutUnsubscribe.Count -gt 5) {
            Write-Host "      ... 還有 $($filesWithoutUnsubscribe.Count - 5) 個檔案 ..." -ForegroundColor DarkGray
        }
    }
} else {
    Write-Issue "事件訂閱與取消訂閱看起來平衡" "Success"
}

# ============================================================
# 3. 檢查 Timer 使用
# ============================================================
Write-Section "3. 檢查 Timer 使用"

$timerUsages = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "new (System\.Threading\.)?Timer\(" |
    Where-Object { 
        $_.Line -notmatch "//.*new.*Timer" -and 
        $_.Line -notmatch "/\*.*new.*Timer.*\*/" 
    }

if ($timerUsages.Count -gt 0) {
    Write-Issue "發現 $($timerUsages.Count) 處 Timer 實例化" "Warning"
    Write-Host "      ??  請確認 Timer 在 Dispose 中正確釋放" -ForegroundColor Yellow
    
    if ($Detailed) {
        $timerUsages | ForEach-Object {
            Write-FileLocation $_.Path $_.LineNumber
        }
    }
} else {
    Write-Issue "未發現 Timer 使用" "Success"
}

# ============================================================
# 4. 檢查靜態集合
# ============================================================
Write-Section "4. 檢查靜態集合"

$staticCollectionPatterns = @(
    "static.*Dictionary<",
    "static.*List<",
    "static.*ConcurrentBag<",
    "static.*ConcurrentDictionary<",
    "static.*HashSet<"
)

$staticCollections = @()
foreach ($pattern in $staticCollectionPatterns) {
    $results = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
        Select-String -Pattern $pattern |
        Where-Object { 
            $_.Line -notmatch "//.*$pattern" -and 
            $_.Line -notmatch "/\*.*$pattern.*\*/" 
        }
    
    $staticCollections += $results
}

if ($staticCollections.Count -gt 0) {
    Write-Issue "發現 $($staticCollections.Count) 處靜態集合" "Warning"
    Write-Host "      ??  靜態集合可能導致記憶體無法釋放" -ForegroundColor Yellow
    
    if ($Detailed) {
        $staticCollections | Select-Object -First 10 | ForEach-Object {
            Write-FileLocation $_.Path $_.LineNumber
            Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
        }
        if ($staticCollections.Count -gt 10) {
            Write-Host "      ... 還有 $($staticCollections.Count - 10) 處 ..." -ForegroundColor DarkGray
        }
    }
} else {
    Write-Issue "未發現靜態集合" "Success"
}

# ============================================================
# 5. 檢查 IDisposable 實現
# ============================================================
Write-Section "5. 檢查 IDisposable 實現"

$disposableClasses = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "class.*:.*IDisposable" |
    Where-Object { 
        $_.Line -notmatch "//.*class.*:.*IDisposable" 
    }

$disposeImplementations = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "public void Dispose\(\)" |
    Where-Object { 
        $_.Line -notmatch "//.*public void Dispose" 
    }

Write-Issue "發現 $($disposableClasses.Count) 個實現 IDisposable 的類別" "Info"
Write-Issue "發現 $($disposeImplementations.Count) 個 Dispose() 實現" "Info"

# ============================================================
# 6. 檢查 using 語句使用
# ============================================================
Write-Section "6. 檢查資源管理"

$fileStreamUsages = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "new FileStream\(" |
    Where-Object { 
        $_.Line -notmatch "//.*new FileStream" -and
        $_.Line -notmatch "using.*new FileStream" 
    }

if ($fileStreamUsages.Count -gt 0) {
    Write-Issue "發現 $($fileStreamUsages.Count) 處 FileStream 可能未使用 using" "Warning"
    
    if ($Detailed) {
        $fileStreamUsages | Select-Object -First 5 | ForEach-Object {
            Write-FileLocation $_.Path $_.LineNumber
        }
    }
} else {
    Write-Issue "FileStream 使用看起來正常" "Success"
}

$sqlReaderUsages = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "SqlDataReader" |
    Where-Object { 
        $_.Line -notmatch "//.*SqlDataReader" 
    }

if ($sqlReaderUsages.Count -gt 0) {
    Write-Issue "發現 $($sqlReaderUsages.Count) 處 SqlDataReader 使用，請確認正確釋放" "Info"
}

# ============================================================
# 7. 檢查大型物件配置
# ============================================================
Write-Section "7. 檢查大型物件配置"

$largeArrayAllocations = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "new byte\[.*\]" |
    Where-Object { 
        $_.Line -notmatch "//.*new byte" 
    }

if ($largeArrayAllocations.Count -gt 0) {
    Write-Issue "發現 $($largeArrayAllocations.Count) 處 byte[] 配置" "Info"
    Write-Host "      ??  建議使用 ArrayPool<byte> 來優化大型陣列" -ForegroundColor Cyan
}

# ============================================================
# 8. 檢查 Task 和 async 使用
# ============================================================
Write-Section "8. 檢查非同步模式"

$taskWithoutAwait = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "\.Result" |
    Where-Object { 
        $_.Line -notmatch "//.*\.Result" 
    }

if ($taskWithoutAwait.Count -gt 0) {
    Write-Issue "發現 $($taskWithoutAwait.Count) 處使用 .Result (可能造成死鎖)" "Warning"
    
    if ($Detailed) {
        $taskWithoutAwait | Select-Object -First 5 | ForEach-Object {
            Write-FileLocation $_.Path $_.LineNumber
        }
    }
}

$taskWithoutConfigureAwait = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -ErrorAction SilentlyContinue | 
    Select-String -Pattern "await.*(?<!ConfigureAwait\(false\))" |
    Where-Object { 
        $_.Line -match "await" -and 
        $_.Line -notmatch "ConfigureAwait" -and
        $_.Line -notmatch "//" 
    }

if ($taskWithoutConfigureAwait.Count -gt 50) {
    Write-Issue "大部分 await 未使用 ConfigureAwait(false)" "Info"
    Write-Host "      ??  在函式庫代碼中建議使用 ConfigureAwait(false)" -ForegroundColor Cyan
}

# ============================================================
# 總結報告
# ============================================================
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  掃描完成總結                                              ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$summary = @"
發現的問題統計:
  - HttpClient/RestClient 實例化: $($httpClientIssues.Count) 處
  - 事件訂閱: $($eventSubscriptions.Count) 處
  - 可能遺漏的取消訂閱: $($filesWithoutUnsubscribe.Count) 處
  - Timer 實例化: $($timerUsages.Count) 處
  - 靜態集合: $($staticCollections.Count) 處
  - IDisposable 實現: $($disposableClasses.Count) 個類別
  - FileStream 可能未正確釋放: $($fileStreamUsages.Count) 處
  - byte[] 配置: $($largeArrayAllocations.Count) 處
  - 使用 .Result (風險): $($taskWithoutAwait.Count) 處

總計發現 $script:IssueCount 個需要注意的項目
"@

Write-Host $summary -ForegroundColor White
Write-Host ""

# 建議
Write-Host "建議行動:" -ForegroundColor Yellow
Write-Host "  1. ?? 優先修復 HttpClient 實例化問題" -ForegroundColor Red
Write-Host "  2. ??  檢查事件訂閱是否正確取消" -ForegroundColor Yellow
Write-Host "  3. ??  確認 Timer 在 Dispose 中釋放" -ForegroundColor Yellow
Write-Host "  4. ??  審查靜態集合是否需要清理機制" -ForegroundColor Cyan
Write-Host "  5. ??  確認所有 IDisposable 資源正確釋放" -ForegroundColor Cyan
Write-Host ""

Write-Host "完成時間: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host ""

# 生成報告檔案
$reportFile = "memory-leak-scan-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
$summary | Out-File $reportFile -Encoding UTF8
Write-Host "詳細報告已儲存至: $reportFile" -ForegroundColor Green
