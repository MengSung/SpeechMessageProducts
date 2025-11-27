# 事件訂閱記憶體洩漏掃描腳本
# 用於檢測 C# 代碼中的事件訂閱與取消訂閱

param(
    [string]$ProjectPath = ".",
    [switch]$Detailed = $false,
    [switch]$ExportCsv = $false
)

Write-Host "================================" -ForegroundColor Green
Write-Host "事件訂閱記憶體洩漏掃描" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""

# 計時開始
$startTime = Get-Date

# 1. 搜尋所有事件訂閱 (+=)
Write-Host "[1/6] 搜尋事件訂閱 (+=)..." -ForegroundColor Yellow
$subscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -Exclude *AssemblyInfo.cs,*Designer.cs | 
    Select-String -Pattern "(\w+)\s*\+=\s*" |
    Where-Object { 
        $_.Line -notmatch "//.*\+=" -and 
        $_.Line -notmatch "^\s*//" -and
        $_.Line -notmatch "^\s*\*" 
    }

Write-Host "  發現 $($subscriptions.Count) 處事件訂閱" -ForegroundColor Cyan
Write-Host ""

# 2. 搜尋取消訂閱 (-=)
Write-Host "[2/6] 搜尋取消訂閱 (-=)..." -ForegroundColor Yellow
$unsubscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -Exclude *AssemblyInfo.cs,*Designer.cs | 
    Select-String -Pattern "(\w+)\s*\-=\s*" |
    Where-Object { 
        $_.Line -notmatch "//.*\-=" -and 
        $_.Line -notmatch "^\s*//" -and
        $_.Line -notmatch "^\s*\*"
    }

Write-Host "  發現 $($unsubscriptions.Count) 處取消訂閱" -ForegroundColor Cyan
Write-Host ""

# 3. 搜尋 IDisposable 實現
Write-Host "[3/6] 檢查 IDisposable 實現..." -ForegroundColor Yellow
$disposableClasses = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -Exclude *AssemblyInfo.cs,*Designer.cs | 
    Select-String -Pattern "class\s+(\w+)\s*:\s*.*\bIDisposable\b" |
    Where-Object { 
        $_.Line -notmatch "//.*class" -and 
        $_.Line -notmatch "^\s*//" -and
        $_.Line -notmatch "^\s*\*"
    }

Write-Host "  發現 $($disposableClasses.Count) 個實現 IDisposable 的類別" -ForegroundColor Cyan
Write-Host ""

# 4. 搜尋 Timer 使用
Write-Host "[4/6] 檢查 Timer 使用..." -ForegroundColor Yellow
$timerUsages = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs -Exclude *AssemblyInfo.cs,*Designer.cs | 
    Select-String -Pattern "new\s+Timer\s*\(" |
    Where-Object { 
        $_.Line -notmatch "//.*new\s+Timer" -and 
        $_.Line -notmatch "^\s*//" -and
        $_.Line -notmatch "^\s*\*"
    }

Write-Host "  發現 $($timerUsages.Count) 處 Timer 實例化" -ForegroundColor Cyan
Write-Host ""

# 5. 搜尋常見事件模式
Write-Host "[5/6] 搜尋常見事件模式..." -ForegroundColor Yellow

$eventPatterns = @{
    "Timer.Elapsed" = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | Select-String -Pattern "\.Elapsed\s*\+=" | Where-Object { $_.Line -notmatch "//" }
    "EventHandler" = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | Select-String -Pattern "EventHandler.*\+=" | Where-Object { $_.Line -notmatch "//" }
    "PropertyChanged" = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | Select-String -Pattern "PropertyChanged\s*\+=" | Where-Object { $_.Line -notmatch "//" }
    "CollectionChanged" = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | Select-String -Pattern "CollectionChanged\s*\+=" | Where-Object { $_.Line -notmatch "//" }
}

foreach ($pattern in $eventPatterns.Keys) {
    $count = $eventPatterns[$pattern].Count
    if ($count -gt 0) {
        Write-Host "  - $pattern : $count 處" -ForegroundColor White
    }
}
Write-Host ""

# 6. 分析結果
Write-Host "[6/6] 分析結果..." -ForegroundColor Yellow
$potentialLeaks = $subscriptions.Count - $unsubscriptions.Count

# 統計摘要
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "統計摘要" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  事件訂閱總數:     $($subscriptions.Count) 處" -ForegroundColor White
Write-Host "  取消訂閱總數:     $($unsubscriptions.Count) 處" -ForegroundColor White
$leakColor = if($potentialLeaks -gt 50){"Red"}elseif($potentialLeaks -gt 20){"Yellow"}else{"Green"}
Write-Host "  潛在洩漏風險:     $potentialLeaks 處" -ForegroundColor $leakColor
Write-Host "  IDisposable 類別: $($disposableClasses.Count) 個" -ForegroundColor White
Write-Host "  Timer 實例化:     $($timerUsages.Count) 處" -ForegroundColor White
Write-Host ""

# 按文件分組 - Top 20
Write-Host "========================================" -ForegroundColor Green
Write-Host "事件訂閱最多的前 20 個文件" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$subscriptionsByFile = $subscriptions | Group-Object -Property Path | 
    Sort-Object -Property Count -Descending | 
    Select-Object -First 20

$index = 1
foreach ($group in $subscriptionsByFile) {
    $fileName = Split-Path $group.Name -Leaf
    $relativePath = $group.Name.Replace($ProjectPath, "").TrimStart("\")
    Write-Host "$index. $fileName" -ForegroundColor Cyan
    Write-Host "   路徑: $relativePath" -ForegroundColor Gray
    Write-Host "   訂閱數: $($group.Count)" -ForegroundColor White
    Write-Host ""
    $index++
}

# 詳細報告（可選）
if ($Detailed) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "詳細事件訂閱列表" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    
    foreach ($sub in $subscriptions | Select-Object -First 50) {
        $fileName = Split-Path $sub.Path -Leaf
        Write-Host "$fileName : Line $($sub.LineNumber)" -ForegroundColor Yellow
        Write-Host "  $($sub.Line.Trim())" -ForegroundColor Gray
    }
    
    if ($subscriptions.Count -gt 50) {
        Write-Host ""
        Write-Host "... 還有 $($subscriptions.Count - 50) 處訂閱未顯示" -ForegroundColor Gray
    }
}

# 導出報告
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "導出報告" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$reportFile = "Event-Subscription-Report-$timestamp.txt"

# 文本報告
@"
========================================
事件訂閱記憶體洩漏掃描報告
========================================

生成時間: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
掃描路徑: $ProjectPath

統計摘要:
  事件訂閱總數:     $($subscriptions.Count)
  取消訂閱總數:     $($unsubscriptions.Count)
  潛在洩漏風險:     $potentialLeaks
  IDisposable 類別: $($disposableClasses.Count)
  Timer 實例化:     $($timerUsages.Count)

常見事件模式:
$(foreach ($pattern in $eventPatterns.Keys) { if ($eventPatterns[$pattern].Count -gt 0) { "  - $pattern : $($eventPatterns[$pattern].Count) 處`r`n" } })

========================================
事件訂閱最多的前 20 個文件
========================================

$($subscriptionsByFile | ForEach-Object { $fileName = Split-Path $_.Name -Leaf; "$fileName : $($_.Count) 處訂閱`r`n" })

========================================
所有事件訂閱詳細列表
========================================

$($subscriptions | ForEach-Object { "$($_.Path):$($_.LineNumber) - $($_.Line.Trim())`r`n" })

========================================
所有取消訂閱詳細列表
========================================

$($unsubscriptions | ForEach-Object { "$($_.Path):$($_.LineNumber) - $($_.Line.Trim())`r`n" })

========================================
IDisposable 實現列表
========================================

$($disposableClasses | ForEach-Object { "$($_.Path):$($_.LineNumber) - $($_.Line.Trim())`r`n" })

========================================
Timer 使用列表
========================================

$($timerUsages | ForEach-Object { "$($_.Path):$($_.LineNumber) - $($_.Line.Trim())`r`n" })

========================================
報告結束
========================================
"@ | Out-File $reportFile -Encoding UTF8

Write-Host "  ? 文本報告已儲存: $reportFile" -ForegroundColor Green

# CSV 報告（可選）
if ($ExportCsv) {
    $csvFile = "Event-Subscription-Report-$timestamp.csv"
    
    $csvData = $subscriptions | ForEach-Object {
        [PSCustomObject]@{
            Type = "Subscription"
            File = Split-Path $_.Path -Leaf
            Path = $_.Path
            Line = $_.LineNumber
            Code = $_.Line.Trim()
        }
    }
    
    $csvData += $unsubscriptions | ForEach-Object {
        [PSCustomObject]@{
            Type = "Unsubscription"
            File = Split-Path $_.Path -Leaf
            Path = $_.Path
            Line = $_.LineNumber
            Code = $_.Line.Trim()
        }
    }
    
    $csvData | Export-Csv -Path $csvFile -NoTypeInformation -Encoding UTF8
    Write-Host "  ? CSV 報告已儲存: $csvFile" -ForegroundColor Green
}

# 計時結束
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "掃描完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  耗時: $($duration.TotalSeconds) 秒" -ForegroundColor White
Write-Host ""

# 風險評估
Write-Host "========================================" -ForegroundColor Green
Write-Host "風險評估" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

if ($potentialLeaks -gt 50) {
    Write-Host "  ?? 高風險: 潛在洩漏超過 50 處" -ForegroundColor Red
    Write-Host "  建議: 立即開始修復事件訂閱問題" -ForegroundColor Red
} elseif ($potentialLeaks -gt 20) {
    Write-Host "  ?? 中風險: 潛在洩漏超過 20 處" -ForegroundColor Yellow
    Write-Host "  建議: 優先修復高頻使用的類別" -ForegroundColor Yellow
} else {
    Write-Host "  ?? 低風險: 潛在洩漏較少" -ForegroundColor Green
    Write-Host "  建議: 定期監測即可" -ForegroundColor Green
}

Write-Host ""
Write-Host "下一步行動:" -ForegroundColor Cyan
Write-Host "  1. 審查報告文件: $reportFile" -ForegroundColor White
Write-Host "  2. 檢查前 20 個最多訂閱的文件" -ForegroundColor White
Write-Host "  3. 確認所有 IDisposable 類別正確實現 Dispose" -ForegroundColor White
Write-Host "  4. 驗證 Timer 在 Dispose 中正確釋放" -ForegroundColor White
Write-Host "  5. 執行修復並重新掃描驗證" -ForegroundColor White
Write-Host ""
