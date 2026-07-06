# 記憶體監測腳本
# 持續監測 .NET 應用程式的記憶體使用情況

param(
    [Parameter(Mandatory=$false)]
    [int]$ProcessId,
    
    [Parameter(Mandatory=$false)]
    [string]$ProcessName = "ChurchReport",
    
    [int]$DurationMinutes = 60,
    [int]$IntervalSeconds = 10,
    [string]$OutputPath = "."
)

# 顏色設定
$ErrorColor = "Red"
$WarningColor = "Yellow"
$InfoColor = "Cyan"
$SuccessColor = "Green"

function Write-Banner {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor $SuccessColor
    Write-Host "║  ChurchReport 記憶體監測工具                              ║" -ForegroundColor $SuccessColor
    Write-Host "║  Memory Monitoring Tool                                    ║" -ForegroundColor $SuccessColor
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor $SuccessColor
    Write-Host ""
}

function Get-TargetProcess {
    param(
        [int]$ProcessId,
        [string]$ProcessName
    )
    
    if ($ProcessId) {
        try {
            return Get-Process -Id $ProcessId -ErrorAction Stop
        }
        catch {
            Write-Host "? 找不到進程 ID: $ProcessId" -ForegroundColor $ErrorColor
            return $null
        }
    }
    
    $processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    
    if ($processes.Count -eq 0) {
        Write-Host "? 找不到進程: $ProcessName" -ForegroundColor $ErrorColor
        return $null
    }
    elseif ($processes.Count -eq 1) {
        return $processes[0]
    }
    else {
        Write-Host "??  發現多個 $ProcessName 進程:" -ForegroundColor $WarningColor
        for ($i = 0; $i -lt $processes.Count; $i++) {
            $proc = $processes[$i]
            Write-Host "  [$i] PID: $($proc.Id) - WS: $([math]::Round($proc.WorkingSet64/1MB, 2)) MB" -ForegroundColor $InfoColor
        }
        
        $selection = Read-Host "請選擇進程編號 (0-$($processes.Count-1))"
        return $processes[[int]$selection]
    }
}

function Format-Bytes {
    param([long]$Bytes)
    
    if ($Bytes -ge 1GB) {
        return "$([math]::Round($Bytes/1GB, 2)) GB"
    }
    elseif ($Bytes -ge 1MB) {
        return "$([math]::Round($Bytes/1MB, 2)) MB"
    }
    elseif ($Bytes -ge 1KB) {
        return "$([math]::Round($Bytes/1KB, 2)) KB"
    }
    else {
        return "$Bytes B"
    }
}

function Get-MemoryTrend {
    param([array]$Data)
    
    if ($Data.Count -lt 2) {
        return "N/A"
    }
    
    $first = $Data[0]
    $last = $Data[-1]
    $change = $last - $first
    $percentChange = ($change / $first) * 100
    
    if ($percentChange -gt 5) {
        return "↗? +$([math]::Round($percentChange, 1))%"
    }
    elseif ($percentChange -lt -5) {
        return "↘? $([math]::Round($percentChange, 1))%"
    }
    else {
        return "→ $([math]::Round($percentChange, 1))%"
    }
}

# ============================================================
# 主程式
# ============================================================

Write-Banner

# 取得目標進程
$process = Get-TargetProcess -ProcessId $ProcessId -ProcessName $ProcessName

if (-not $process) {
    Write-Host "請先啟動應用程式，然後重新執行此腳本" -ForegroundColor $WarningColor
    exit 1
}

Write-Host "? 找到目標進程:" -ForegroundColor $SuccessColor
Write-Host "   進程名稱: $($process.ProcessName)" -ForegroundColor White
Write-Host "   進程 ID: $($process.Id)" -ForegroundColor White
Write-Host "   啟動時間: $($process.StartTime)" -ForegroundColor White
Write-Host ""

# 設定監測參數
$iterations = [math]::Ceiling(($DurationMinutes * 60) / $IntervalSeconds)
$logFile = Join-Path $OutputPath "memory-monitor-$($process.ProcessName)-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"
$summaryFile = Join-Path $OutputPath "memory-summary-$($process.ProcessName)-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

Write-Host "監測設定:" -ForegroundColor $InfoColor
Write-Host "   持續時間: $DurationMinutes 分鐘" -ForegroundColor White
Write-Host "   採樣間隔: $IntervalSeconds 秒" -ForegroundColor White
Write-Host "   總採樣數: $iterations 次" -ForegroundColor White
Write-Host "   CSV 日誌: $logFile" -ForegroundColor White
Write-Host "   摘要報告: $summaryFile" -ForegroundColor White
Write-Host ""

# 初始化資料儲存
$workingSetData = @()
$privateMemoryData = @()
$gen0Collections = @()
$gen1Collections = @()
$gen2Collections = @()
$threadCountData = @()
$handleCountData = @()

# 寫入 CSV 標頭
$csvHeader = "Timestamp,Elapsed_Seconds,WorkingSet_MB,PrivateMemory_MB,VirtualMemory_MB,PagedMemory_MB,ThreadCount,HandleCount,Gen0_Collections,Gen1_Collections,Gen2_Collections"
$csvHeader | Out-File $logFile -Encoding UTF8

Write-Host "開始監測... (按 Ctrl+C 可提前停止)" -ForegroundColor $InfoColor
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor $InfoColor
Write-Host ""

# 監測迴圈
$startTime = Get-Date
$lastGen0 = 0
$lastGen1 = 0
$lastGen2 = 0

for ($i = 0; $i -lt $iterations; $i++) {
    try {
        # 刷新進程資訊
        $process.Refresh()
        
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        $elapsed = [math]::Round((Get-Date).Subtract($startTime).TotalSeconds, 0)
        
        # 收集記憶體指標
        $workingSet = [math]::Round($process.WorkingSet64 / 1MB, 2)
        $privateMemory = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        $virtualMemory = [math]::Round($process.VirtualMemorySize64 / 1MB, 2)
        $pagedMemory = [math]::Round($process.PagedMemorySize64 / 1MB, 2)
        $threadCount = $process.Threads.Count
        $handleCount = $process.HandleCount
        
        # 嘗試取得 GC 資訊（需要 .NET runtime metrics）
        $gen0 = 0
        $gen1 = 0
        $gen2 = 0
        
        try {
            # 這需要進程有啟用診斷
            $perfCounters = Get-Counter "\Process($($process.ProcessName))\*" -ErrorAction SilentlyContinue
            # GC 統計需要從 ETW 或其他來源取得
        }
        catch {
            # 無法取得 GC 統計，使用預設值
        }
        
        # 儲存資料
        $workingSetData += $workingSet
        $privateMemoryData += $privateMemory
        $gen0Collections += $gen0
        $gen1Collections += $gen1
        $gen2Collections += $gen2
        $threadCountData += $threadCount
        $handleCountData += $handleCount
        
        # 寫入 CSV
        $csvLine = "$timestamp,$elapsed,$workingSet,$privateMemory,$virtualMemory,$pagedMemory,$threadCount,$handleCount,$gen0,$gen1,$gen2"
        $csvLine | Out-File $logFile -Append -Encoding UTF8
        
        # 計算趨勢
        $wsTrend = Get-MemoryTrend -Data $workingSetData
        $pmTrend = Get-MemoryTrend -Data $privateMemoryData
        
        # 輸出當前狀態
        $progress = [math]::Round(($i + 1) / $iterations * 100, 1)
        $remainingMinutes = [math]::Round((($iterations - $i - 1) * $IntervalSeconds) / 60, 1)
        
        Write-Host "[$timestamp] 進度: $progress% | 剩餘: $remainingMinutes 分鐘" -ForegroundColor $InfoColor
        Write-Host "  工作集: $(Format-Bytes ($workingSet * 1MB)) $wsTrend | 私有記憶體: $(Format-Bytes ($privateMemory * 1MB)) $pmTrend" -ForegroundColor White
        Write-Host "  執行緒: $threadCount | 控制代碼: $handleCount" -ForegroundColor Gray
        
        # 記憶體警告
        if ($workingSetData.Count -ge 10) {
            $recentGrowth = $workingSetData[-1] - $workingSetData[-10]
            if ($recentGrowth -gt ($workingSetData[-10] * 0.1)) {
                Write-Host "  ??  最近 10 次採樣記憶體增長超過 10%" -ForegroundColor $WarningColor
            }
        }
        
        Write-Host ""
        
        # 等待下一次採樣
        if ($i -lt $iterations - 1) {
            Start-Sleep -Seconds $IntervalSeconds
        }
    }
    catch [System.InvalidOperationException] {
        Write-Host "? 進程已結束" -ForegroundColor $ErrorColor
        break
    }
    catch {
        Write-Host "??  監測錯誤: $($_.Exception.Message)" -ForegroundColor $WarningColor
        continue
    }
}

# ============================================================
# 生成摘要報告
# ============================================================

Write-Host ""
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor $InfoColor
Write-Host "生成摘要報告..." -ForegroundColor $InfoColor
Write-Host ""

$totalDuration = (Get-Date).Subtract($startTime)
$actualSamples = $workingSetData.Count

# 計算統計資訊
$wsMin = ($workingSetData | Measure-Object -Minimum).Minimum
$wsMax = ($workingSetData | Measure-Object -Maximum).Maximum
$wsAvg = ($workingSetData | Measure-Object -Average).Average
$wsFirst = $workingSetData[0]
$wsLast = $workingSetData[-1]
$wsGrowth = $wsLast - $wsFirst
$wsGrowthPercent = ($wsGrowth / $wsFirst) * 100

$pmMin = ($privateMemoryData | Measure-Object -Minimum).Minimum
$pmMax = ($privateMemoryData | Measure-Object -Maximum).Maximum
$pmAvg = ($privateMemoryData | Measure-Object -Average).Average
$pmFirst = $privateMemoryData[0]
$pmLast = $privateMemoryData[-1]
$pmGrowth = $pmLast - $pmFirst
$pmGrowthPercent = ($pmGrowth / $pmFirst) * 100

$threadAvg = ($threadCountData | Measure-Object -Average).Average
$handleAvg = ($handleCountData | Measure-Object -Average).Average

# 生成報告文字
$report = @"
╔════════════════════════════════════════════════════════════╗
║  記憶體監測摘要報告                                        ║
║  Memory Monitoring Summary Report                          ║
╚════════════════════════════════════════════════════════════╝

監測資訊
========================================
進程名稱: $($process.ProcessName)
進程 ID: $($process.Id)
開始時間: $($startTime.ToString("yyyy-MM-dd HH:mm:ss"))
結束時間: $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
總時長: $([math]::Round($totalDuration.TotalMinutes, 1)) 分鐘
採樣數量: $actualSamples 次
採樣間隔: $IntervalSeconds 秒

工作集 (Working Set) 記憶體
========================================
初始值: $([math]::Round($wsFirst, 2)) MB
最終值: $([math]::Round($wsLast, 2)) MB
最小值: $([math]::Round($wsMin, 2)) MB
最大值: $([math]::Round($wsMax, 2)) MB
平均值: $([math]::Round($wsAvg, 2)) MB
增長量: $([math]::Round($wsGrowth, 2)) MB
增長率: $([math]::Round($wsGrowthPercent, 2))%

私有記憶體 (Private Memory)
========================================
初始值: $([math]::Round($pmFirst, 2)) MB
最終值: $([math]::Round($pmLast, 2)) MB
最小值: $([math]::Round($pmMin, 2)) MB
最大值: $([math]::Round($pmMax, 2)) MB
平均值: $([math]::Round($pmAvg, 2)) MB
增長量: $([math]::Round($pmGrowth, 2)) MB
增長率: $([math]::Round($pmGrowthPercent, 2))%

系統資源
========================================
平均執行緒數: $([math]::Round($threadAvg, 0))
平均控制代碼數: $([math]::Round($handleAvg, 0))

評估與建議
========================================
"@

# 評估結果
if ($wsGrowthPercent -lt 5) {
    $report += "? 記憶體使用穩定 (增長 < 5%)`n"
    $report += "   系統運行正常，未檢測到明顯的記憶體洩漏。`n"
}
elseif ($wsGrowthPercent -lt 10) {
    $report += "??  記憶體使用略有增長 (增長 5-10%)`n"
    $report += "   建議繼續監測，確認是否為正常業務增長。`n"
}
elseif ($wsGrowthPercent -lt 20) {
    $report += "??  記憶體使用明顯增長 (增長 10-20%)`n"
    $report += "   建議檢查是否有記憶體洩漏的跡象。`n"
    $report += "   請執行: Check-MemoryLeaks.ps1 進行代碼掃描`n"
}
else {
    $report += "?? 記憶體使用大幅增長 (增長 > 20%)`n"
    $report += "   疑似存在記憶體洩漏！`n"
    $report += "   建議立即執行以下操作：`n"
    $report += "   1. 執行 Check-MemoryLeaks.ps1 掃描代碼問題`n"
    $report += "   2. 使用 dotnet-dump 收集記憶體快照`n"
    $report += "   3. 使用 Visual Studio 診斷工具分析`n"
}

$report += "`n"
$report += "詳細資料請查看: $logFile`n"
$report += "報告生成時間: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n"

# 輸出並儲存報告
Write-Host $report -ForegroundColor White
$report | Out-File $summaryFile -Encoding UTF8

Write-Host ""
Write-Host "? 監測完成！" -ForegroundColor $SuccessColor
Write-Host "   CSV 日誌: $logFile" -ForegroundColor $InfoColor
Write-Host "   摘要報告: $summaryFile" -ForegroundColor $InfoColor
Write-Host ""

# 詢問是否開啟報告
$openReport = Read-Host "是否開啟摘要報告? (Y/N)"
if ($openReport -eq "Y" -or $openReport -eq "y") {
    Start-Process notepad.exe $summaryFile
}
