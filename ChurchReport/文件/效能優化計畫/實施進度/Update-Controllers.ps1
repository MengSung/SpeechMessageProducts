# PowerShell 腳本：批量更新 Controllers 以支援 CRM 連接池
# 版本: 2.0
# 日期: 2024-01-XX

# 設定控制器路徑
$controllersPath = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"

# 需要更新的控制器列表
$controllers = @(
    "AuthenticationController.cs",
    "DedicationAuditController.cs",
    "DedicationController.cs",
    "EquipmentController.cs",
    "HomeController.cs",
    "ListManagementController.cs",
    "NewPersonController.cs",
    "PersonalController.cs",
    "PhoneBindingController.cs",
    "QrCodeController.cs"
)

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "  批量更新 Controllers - CRM 連接池整合" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$skipCount = 0
$errorCount = 0
$updateLog = @()

foreach ($controller in $controllers) {
    $filePath = Join-Path $controllersPath $controller
    
    Write-Host "[$($controllers.IndexOf($controller) + 1)/$($controllers.Count)] 處理: $controller" -ForegroundColor Yellow
    
    if (-not (Test-Path $filePath)) {
        Write-Host "  [錯誤] 找不到檔案: $filePath" -ForegroundColor Red
        $errorCount++
        $updateLog += "? $controller - 檔案不存在"
        continue
    }
    
    try {
        # 讀取檔案內容（使用 UTF-8 編碼）
        $content = Get-Content $filePath -Raw -Encoding UTF8
        
        # 檢查是否已經更新過
        if ($content -match "ICrmConnectionPool\s+connectionPool") {
            Write-Host "  [跳過] 已包含 ICrmConnectionPool 參數" -ForegroundColor Gray
            $skipCount++
            $updateLog += "??  $controller - 已更新，跳過"
            continue
        }
        
        $modified = $false
        
        # 1. 添加 using 語句（如果不存在）
        if ($content -notmatch "using ToolUtilityNameSpace\.ConnectionOperations;") {
            Write-Host "  [步驟 1] 添加 using 語句" -ForegroundColor Green
            
            # 在 ToolUtilityNameSpace.DependencyInjection 之後添加
            if ($content -match "using ToolUtilityNameSpace\.DependencyInjection;") {
                $content = $content -replace `
                    "(using ToolUtilityNameSpace\.DependencyInjection;)", `
                    "`$1`r`nusing ToolUtilityNameSpace.ConnectionOperations;"
                $modified = $true
            }
            else {
                Write-Host "  [警告] 找不到 using ToolUtilityNameSpace.DependencyInjection; 語句" -ForegroundColor Yellow
            }
        }
        
        # 2. 更新建構式 - 處理多種可能的格式
        
        # 模式 1: 標準格式 (paymentService)
        if ($content -match "IToolUtilityProvider\s+toolUtilityProvider\)\s*:\s*base\(httpContextAccessor,\s*memoryCache,\s*paymentService,\s*toolUtilityProvider\)") {
            Write-Host "  [步驟 2] 更新建構式（標準格式 - paymentService）" -ForegroundColor Green
            
            $content = $content -replace `
                "(IToolUtilityProvider\s+toolUtilityProvider)\)\s*(:\s*base\(httpContextAccessor,\s*memoryCache,\s*paymentService,\s*toolUtilityProvider)\)", `
                "`$1,`r`n            ICrmConnectionPool connectionPool)`r`n        `$2, connectionPool)"
            $modified = $true
        }
        # 模式 2: HomeController 格式 (qpayService)
        elseif ($content -match "IToolUtilityProvider\s+toolUtilityProvider\)\s*:\s*base\(httpContextAccessor,\s*memoryCache,\s*qpayService,\s*toolUtilityProvider\)") {
            Write-Host "  [步驟 2] 更新建構式（qpayService 格式）" -ForegroundColor Green
            
            $content = $content -replace `
                "(IToolUtilityProvider\s+toolUtilityProvider)\)\s*(:\s*base\(httpContextAccessor,\s*memoryCache,\s*qpayService,\s*toolUtilityProvider)\)", `
                "`$1,`r`n            ICrmConnectionPool connectionPool)`r`n        `$2, connectionPool)"
            $modified = $true
        }
        # 模式 3: 更靈活的匹配（處理空白和換行）
        elseif ($content -match "IToolUtilityProvider\s+toolUtilityProvider\s*\)\s*:\s*base\s*\(\s*httpContextAccessor") {
            Write-Host "  [步驟 2] 更新建構式（靈活匹配）" -ForegroundColor Green
            
            # 使用正則表達式找到建構式的結束位置
            $pattern = "(IToolUtilityProvider\s+toolUtilityProvider)\s*\)\s*(:\s*base\s*\([^)]+\))"
            if ($content -match $pattern) {
                $baseCall = $matches[2]
                # 在 base 調用中添加 connectionPool
                $newBaseCall = $baseCall -replace "\)", ", connectionPool)"
                
                $content = $content -replace `
                    "(IToolUtilityProvider\s+toolUtilityProvider)\s*\)\s*(:\s*base\s*\([^)]+\))", `
                    "`$1,`r`n            ICrmConnectionPool connectionPool)`r`n        $newBaseCall"
                $modified = $true
            }
        }
        else {
            Write-Host "  [警告] 無法識別建構式格式，需要手動更新" -ForegroundColor Red
            $errorCount++
            $updateLog += "??  $controller - 無法識別建構式格式"
            continue
        }
        
        # 3. 如果有修改，儲存檔案
        if ($modified) {
            # 備份原始檔案
            $backupPath = "$filePath.backup"
            Copy-Item $filePath $backupPath -Force
            
            # 儲存更新後的內容
            Set-Content $filePath $content -NoNewline -Encoding UTF8
            
            Write-Host "  [完成] $controller 已成功更新（已建立備份）" -ForegroundColor Green
            $successCount++
            $updateLog += "? $controller - 更新成功"
        }
        else {
            Write-Host "  [跳過] 沒有需要修改的內容" -ForegroundColor Gray
            $skipCount++
            $updateLog += "??  $controller - 無需修改"
        }
    }
    catch {
        Write-Host "  [錯誤] 更新失敗: $_" -ForegroundColor Red
        $errorCount++
        $updateLog += "? $controller - 更新失敗: $_"
    }
    
    Write-Host ""
}

# 顯示統計摘要
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "         批量更新完成統計" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "? 成功更新: $successCount 個檔案" -ForegroundColor Green
Write-Host "??  已跳過: $skipCount 個檔案" -ForegroundColor Gray
Write-Host "? 更新失敗: $errorCount 個檔案" -ForegroundColor Red
Write-Host "?? 總計: $($controllers.Count) 個檔案" -ForegroundColor White
Write-Host ""

# 顯示詳細記錄
Write-Host "詳細記錄:" -ForegroundColor Cyan
foreach ($log in $updateLog) {
    Write-Host "  $log"
}
Write-Host ""

# 下一步建議
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "         下一步操作建議" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan

if ($successCount -gt 0) {
    Write-Host "1. 驗證更新結果:" -ForegroundColor Yellow
    Write-Host "   cd D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport" -ForegroundColor White
    Write-Host "   dotnet build" -ForegroundColor White
    Write-Host ""
}

if ($errorCount -gt 0) {
    Write-Host "2. 手動檢查失敗的檔案:" -ForegroundColor Yellow
    Write-Host "   請檢查上述標記為 ? 的檔案" -ForegroundColor White
    Write-Host ""
}

Write-Host "3. 如果編譯成功，測試應用程式:" -ForegroundColor Yellow
Write-Host "   - 啟動應用程式" -ForegroundColor White
Write-Host "   - 測試登入功能" -ForegroundColor White
Write-Host "   - 測試主要功能" -ForegroundColor White
Write-Host ""

Write-Host "4. 還原備份（如果需要）:" -ForegroundColor Yellow
Write-Host "   備份檔案位於: *.backup" -ForegroundColor White
Write-Host ""

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "腳本執行完畢！" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
