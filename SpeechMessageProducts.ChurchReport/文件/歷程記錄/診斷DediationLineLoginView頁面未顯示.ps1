# 診斷DediationLineLoginView頁面未顯示.ps1
# 完整診斷腳本

param(
    [string]$OutputFile = "DediationLineLoginView診斷結果.txt"
)

$ErrorActionPreference = "Continue"

# 輸出函數
function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    
    $color = switch ($Type) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        default { "White" }
    }
    
    $icon = switch ($Type) {
        "Success" { "?" }
        "Error" { "?" }
        "Warning" { "??" }
        default { "??" }
    }
    
    $msg = "$icon $Message"
    Write-Host $msg -ForegroundColor $color
    Add-Content -Path $OutputFile -Value $msg
}

function Write-Section {
    param([string]$Title)
    
    $separator = "?" * 70
    Write-Host "`n$separator" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "$separator`n" -ForegroundColor Cyan
    
    Add-Content -Path $OutputFile -Value "`n$separator`n  $Title`n$separator`n"
}

# 清除舊的輸出檔案
if (Test-Path $OutputFile) {
    Remove-Item $OutputFile -Force
}

# 開始診斷
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║    DediationLineLoginView 頁面未顯示 - 完整診斷              ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

Add-Content -Path $OutputFile -Value @"
╔══════════════════════════════════════════════════════════════╗
║    DediationLineLoginView 頁面未顯示 - 完整診斷              ║
╚══════════════════════════════════════════════════════════════╝

診斷時間: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

"@

# 1. 檢查 IIS 服務
Write-Section "1. IIS 服務狀態"

try {
    $iisService = Get-Service W3SVC -ErrorAction Stop
    if ($iisService.Status -eq "Running") {
        Write-Status "IIS 服務正在運行" "Success"
    } else {
        Write-Status "IIS 服務狀態: $($iisService.Status)" "Error"
        Write-Status "請執行: net start W3SVC" "Warning"
    }
} catch {
    Write-Status "找不到 IIS 服務: $($_.Exception.Message)" "Error"
}

# 2. 檢查應用程式池
Write-Section "2. 應用程式池狀態"

try {
    Import-Module WebAdministration -ErrorAction Stop
    
    $pool = Get-WebAppPoolState "ChurchReport" -ErrorAction Stop
    if ($pool.Value -eq "Started") {
        Write-Status "應用程式池正在運行" "Success"
        
        # 取得詳細資訊
        $poolInfo = Get-Item "IIS:\AppPools\ChurchReport"
        Write-Status "    .NET CLR 版本: $($poolInfo.managedRuntimeVersion)" "Info"
        Write-Status "    管道模式: $($poolInfo.managedPipelineMode)" "Info"
        Write-Status "    啟用 32 位元: $($poolInfo.enable32BitAppOnWin64)" "Info"
    } else {
        Write-Status "應用程式池狀態: $($pool.Value)" "Error"
        Write-Status "請執行: Start-WebAppPool 'ChurchReport'" "Warning"
    }
} catch {
    Write-Status "無法檢查應用程式池: $($_.Exception.Message)" "Error"
}

# 3. 檢查網站綁定
Write-Section "3. IIS 網站綁定"

try {
    $bindings = Get-WebBinding -Name "ChurchReport" -ErrorAction Stop
    if ($bindings) {
        Write-Status "找到 $($bindings.Count) 個綁定" "Success"
        foreach ($binding in $bindings) {
            Write-Status "    協定: $($binding.protocol), Port: $($binding.bindingInformation)" "Info"
        }
        
        # 檢查 Port 479
        $port479 = $bindings | Where-Object { $_.bindingInformation -like "*:479:*" }
        if ($port479) {
            Write-Status "Port 479 已綁定" "Success"
        } else {
            Write-Status "Port 479 未綁定" "Warning"
        }
    } else {
        Write-Status "找不到網站綁定" "Error"
    }
} catch {
    Write-Status "無法檢查網站綁定: $($_.Exception.Message)" "Error"
}

# 4. 檢查 Port 監聽
Write-Section "4. Port 479 監聽狀態"

$portListening = netstat -ano | Select-String ":479.*LISTENING"
if ($portListening) {
    Write-Status "Port 479 正在監聽" "Success"
    $portListening | ForEach-Object {
        Write-Status "    $_" "Info"
    }
} else {
    Write-Status "Port 479 沒有被監聽" "Error"
}

# 5. 檢查視圖檔案
Write-Section "5. 視圖檔案檢查"

$viewPaths = @(
    "Views\Dedication\DediationLineLoginView.cshtml",
    "Views\Home\DediationLineLoginView.cshtml",
    "ChurchReport\Views\Dedication\DediationLineLoginView.cshtml",
    "ChurchReport\Views\Home\DediationLineLoginView.cshtml"
)

$found = $false
foreach ($path in $viewPaths) {
    if (Test-Path $path) {
        Write-Status "視圖存在: $path" "Success"
        $found = $true
        
        $fileInfo = Get-Item $path
        Write-Status "    大小: $($fileInfo.Length) bytes" "Info"
        Write-Status "    修改時間: $($fileInfo.LastWriteTime)" "Info"
        
        if ($fileInfo.Length -eq 0) {
            Write-Status "    警告: 檔案為空！" "Warning"
        }
        
        # 檢查視圖內容
        try {
            $content = Get-Content $path -Raw -ErrorAction Stop
            if ($content -like "*@model*") {
                Write-Status "    包含 @model 宣告" "Success"
            }
            if ($content -like "*liff.init*") {
                Write-Status "    包含 LIFF 初始化" "Success"
            }
            if ($content -like "*UpdateLineUserId*") {
                Write-Status "    包含 UpdateLineUserId 函數" "Success"
            }
        } catch {
            Write-Status "    無法讀取檔案內容" "Warning"
        }
    }
}

if (-not $found) {
    Write-Status "找不到視圖檔案" "Error"
    Write-Status "請確認視圖檔案是否存在於以下任一位置:" "Warning"
    $viewPaths | ForEach-Object { Write-Status "    - $_" "Info" }
}

# 6. 檢查 Controller
Write-Section "6. DedicationController 檢查"

$controllerPaths = @(
    "Controllers\DedicationController.cs",
    "ChurchReport\Controllers\DedicationController.cs"
)

$found = $false
foreach ($path in $controllerPaths) {
    if (Test-Path $path) {
        Write-Status "Controller 存在: $path" "Success"
        $found = $true
        
        $content = Get-Content $path -Raw
        
        # 檢查方法
        if ($content -like "*DediationLineLoginView*") {
            Write-Status "    包含 DediationLineLoginView 方法" "Success"
            
            # 檢查路由屬性
            if ($content -like '*Route*DediationLineLoginView*') {
                Write-Status "    包含 Route 屬性" "Success"
            } else {
                Write-Status "    可能缺少 Route 屬性" "Warning"
            }
            
            # 檢查錯誤處理
            if ($content -like "*try*catch*") {
                Write-Status "    包含錯誤處理" "Success"
            } else {
                Write-Status "    可能缺少錯誤處理" "Warning"
            }
        } else {
            Write-Status "    不包含 DediationLineLoginView 方法" "Error"
        }
        
        break
    }
}

if (-not $found) {
    Write-Status "找不到 DedicationController" "Error"
}

# 7. 檢查 Startup.cs 路由配置
Write-Section "7. Startup.cs 路由配置"

$startupPaths = @(
    "Startup.cs",
    "ChurchReport\Startup.cs"
)

$found = $false
foreach ($path in $startupPaths) {
    if (Test-Path $path) {
        Write-Status "Startup.cs 存在: $path" "Success"
        $found = $true
        
        $content = Get-Content $path -Raw
        
        if ($content -like "*DediationLineLoginView*") {
            Write-Status "    包含 DediationLineLoginView 路由配置" "Success"
        } else {
            Write-Status "    可能缺少專門的路由配置（使用預設路由）" "Info"
        }
        
        if ($content -like "*UseMvc*") {
            Write-Status "    使用傳統 MVC 路由" "Success"
        } elseif ($content -like "*MapControllers*") {
            Write-Status "    使用端點路由" "Success"
        } else {
            Write-Status "    路由配置可能不正確" "Warning"
        }
        
        break
    }
}

if (-not $found) {
    Write-Status "找不到 Startup.cs" "Error"
}

# 8. 檢查編譯輸出
Write-Section "8. 編譯輸出檢查"

$dllPaths = @(
    "bin\Debug\netcoreapp2.1\ChurchReport.dll",
    "bin\Release\netcoreapp2.1\ChurchReport.dll",
    "ChurchReport\bin\Debug\netcoreapp2.1\ChurchReport.dll",
    "ChurchReport\bin\Release\netcoreapp2.1\ChurchReport.dll",
    "bin\ChurchReport.dll"
)

$found = $false
foreach ($path in $dllPaths) {
    if (Test-Path $path) {
        Write-Status "編譯輸出存在: $path" "Success"
        $found = $true
        
        $fileInfo = Get-Item $path
        $lastWrite = $fileInfo.LastWriteTime
        $timeDiff = (Get-Date) - $lastWrite
        
        Write-Status "    最後編譯: $lastWrite" "Info"
        Write-Status "    距今: $([math]::Round($timeDiff.TotalMinutes, 1)) 分鐘" "Info"
        
        if ($timeDiff.TotalHours -gt 24) {
            Write-Status "    警告: 編譯檔案超過 24 小時未更新" "Warning"
        }
        
        break
    }
}

if (-not $found) {
    Write-Status "找不到編譯輸出" "Error"
    Write-Status "請先編譯專案" "Warning"
}

# 9. 檢查日誌檔案
Write-Section "9. 錯誤日誌檢查"

$logPaths = @(
    "Logs\Trace.log",
    "ChurchReport\Logs\Trace.log"
)

foreach ($pattern in $logPaths) {
    $logs = Get-ChildItem $pattern -ErrorAction SilentlyContinue | 
            Sort-Object LastWriteTime -Descending | 
            Select-Object -First 1
    
    if ($logs) {
        Write-Status "找到日誌: $($logs.FullName)" "Success"
        Write-Status "    大小: $($logs.Length) bytes" "Info"
        Write-Status "    修改時間: $($logs.LastWriteTime)" "Info"
        
        # 讀取最後 20 行
        $lastLines = Get-Content $logs.FullName -Tail 20 -ErrorAction SilentlyContinue
        if ($lastLines) {
            Write-Status "    最後 20 行日誌:" "Info"
            
            $hasError = $false
            foreach ($line in $lastLines) {
                if ($line -like "*error*" -or $line -like "*exception*" -or $line -like "*fail*") {
                    Write-Status "      $line" "Error"
                    $hasError = $true
                } else {
                    Write-Status "      $line" "Info"
                }
            }
            
            if (-not $hasError) {
                Write-Status "    沒有發現明顯錯誤" "Success"
            }
        }
    }
}

# 10. 網路連線測試
Write-Section "10. 網路連線測試"

try {
    $result = Test-NetConnection -ComputerName localhost -Port 479 -ErrorAction Stop
    if ($result.TcpTestSucceeded) {
        Write-Status "可以連線到 localhost:479" "Success"
    } else {
        Write-Status "無法連線到 localhost:479" "Error"
    }
} catch {
    Write-Status "網路測試失敗: $($_.Exception.Message)" "Error"
}

# 11. 事件檢視器錯誤
Write-Section "11. Windows 事件檢視器錯誤"

try {
    $events = Get-EventLog -LogName Application -Source "ASP.NET*" -EntryType Error -Newest 5 -ErrorAction Stop
    if ($events) {
        Write-Status "找到 $($events.Count) 個 ASP.NET 錯誤事件" "Warning"
        foreach ($event in $events) {
            Write-Status "    [$($event.TimeGenerated)] $($event.Message.Substring(0, [Math]::Min(100, $event.Message.Length)))..." "Error"
        }
    } else {
        Write-Status "沒有發現 ASP.NET 錯誤事件" "Success"
    }
} catch {
    Write-Status "無法讀取事件檢視器: $($_.Exception.Message)" "Warning"
}

# 總結
Write-Section "診斷總結"

Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║  診斷完成！                                                   ║
╚══════════════════════════════════════════════════════════════╝

診斷結果已儲存到: $OutputFile

【建議的下一步】
????????????????????????????????????????????????????????????

1. 如果 IIS 服務或應用程式池未運行:
   執行: 緊急修復DediationLineLoginView.bat

2. 如果視圖檔案不存在:
   檢查視圖是否在正確的位置

3. 如果找到錯誤日誌:
   查看日誌檔案的完整內容

4. 測試 URL:
   本機: https://localhost:479/Dedication/DediationLineLoginView/test
   實際: https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy

5. 使用瀏覽器開發者工具:
   - F12 → Console (查看 JavaScript 錯誤)
   - F12 → Network (查看請求狀態)

【測試檢查清單】
????????????????????????????????????????????????????????????

□ IIS 服務正在運行
□ 應用程式池正在運行
□ Port 479 正在監聽
□ 視圖檔案存在且不為空
□ Controller 包含 DediationLineLoginView 方法
□ 編譯輸出是最新的
□ 沒有明顯的日誌錯誤

"@ -ForegroundColor Cyan

Add-Content -Path $OutputFile -Value @"

【診斷總結】

請根據上述檢查結果採取相應的修復措施。

如果所有檢查都通過但問題仍然存在，請提供:
1. 瀏覽器顯示的內容（截圖）
2. Chrome DevTools Network 標籤截圖
3. Chrome DevTools Console 錯誤訊息
4. 本診斷報告

診斷完成時間: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

Write-Host "`n完成！請查看 $OutputFile 了解完整報告。" -ForegroundColor Green
