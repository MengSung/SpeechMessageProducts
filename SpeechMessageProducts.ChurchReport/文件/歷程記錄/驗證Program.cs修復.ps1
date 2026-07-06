# Program.cs 版本衝突修復驗證工具 (PowerShell 版本)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Program.cs 版本衝突修復驗證工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 檢查專案檔案
Write-Host "[1/6] 檢查專案檔案..." -ForegroundColor Yellow
if (Test-Path "ChurchReport\ChurchReport.csproj") {
    Write-Host "? 專案檔案存在" -ForegroundColor Green
} else {
    Write-Host "? 錯誤: 找不到 ChurchReport.csproj" -ForegroundColor Red
    Write-Host "   請在方案根目錄執行此腳本" -ForegroundColor Red
    Read-Host "按 Enter 鍵退出"
    exit 1
}

# 2. 檢查 Program.cs 修改
Write-Host ""
Write-Host "[2/6] 檢查 Program.cs 修改..." -ForegroundColor Yellow

$programContent = Get-Content "ChurchReport\Program.cs" -Raw

if ($programContent -match "new WebHostBuilder\(\)") {
    Write-Host "? Program.cs 已使用手動建構" -ForegroundColor Green
} else {
    Write-Host "? 警告: Program.cs 可能仍使用 CreateDefaultBuilder" -ForegroundColor Red
    Write-Host "   請確認已套用修復" -ForegroundColor Red
}

if ($programContent -match "CreateDefaultBuilder") {
    Write-Host "? 警告: 發現 CreateDefaultBuilder，請移除" -ForegroundColor Red
} else {
    Write-Host "? 已移除 CreateDefaultBuilder" -ForegroundColor Green
}

if ($programContent -match "ConfigurationBuilder") {
    Write-Host "? 已添加手動 Configuration 建構" -ForegroundColor Green
} else {
    Write-Host "??  警告: 未找到 ConfigurationBuilder" -ForegroundColor Yellow
}

# 3. 檢查套件版本
Write-Host ""
Write-Host "[3/6] 檢查套件版本..." -ForegroundColor Yellow
Push-Location "ChurchReport"
$packages = dotnet list package --include-transitive 2>$null | Select-String "Microsoft.Extensions.Logging"
Pop-Location

if ($packages) {
    Write-Host "套件版本:" -ForegroundColor Cyan
    $packages | ForEach-Object {
        Write-Host "  $_" -ForegroundColor Gray
    }
} else {
    Write-Host "??  無法讀取套件資訊" -ForegroundColor Yellow
}

# 4. 清理專案
Write-Host ""
Write-Host "[4/6] 清理專案..." -ForegroundColor Yellow
Push-Location "ChurchReport"
$cleanResult = dotnet clean 2>&1
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "? 清理成功" -ForegroundColor Green
} else {
    Write-Host "??  清理時出現警告" -ForegroundColor Yellow
}

# 5. 還原套件
Write-Host ""
Write-Host "[5/6] 還原套件..." -ForegroundColor Yellow
Push-Location "ChurchReport"
$restoreResult = dotnet restore 2>&1
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "? 套件還原成功" -ForegroundColor Green
} else {
    Write-Host "? 套件還原失敗" -ForegroundColor Red
    Write-Host $restoreResult -ForegroundColor Red
    Read-Host "按 Enter 鍵退出"
    exit 1
}

# 6. 建置專案
Write-Host ""
Write-Host "[6/6] 建置專案..." -ForegroundColor Yellow
Write-Host "正在建置..." -ForegroundColor Gray
$buildResult = dotnet build "ChurchReport\ChurchReport.csproj" --no-restore 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "? 驗證成功！" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "修復已正確套用，應用程式可以正常建置。" -ForegroundColor White
    Write-Host ""
    Write-Host "下一步:" -ForegroundColor Cyan
    Write-Host "  1. 執行應用程式: " -NoNewline -ForegroundColor White
    Write-Host "dotnet run --project ChurchReport\ChurchReport.csproj" -ForegroundColor Yellow
    Write-Host "  2. 或在 Visual Studio 中按 " -NoNewline -ForegroundColor White
    Write-Host "F5" -NoNewline -ForegroundColor Yellow
    Write-Host " 啟動偵錯" -ForegroundColor White
    Write-Host ""
    Write-Host "相關文檔:" -ForegroundColor Cyan
    Write-Host "  - 完整修復報告: 文件\Program.cs版本衝突修復報告.md" -ForegroundColor Gray
    Write-Host "  - 快速參考卡: 文件\Program.cs版本衝突快速參考卡.md" -ForegroundColor Gray
    Write-Host "  - 修復總結: 文件\System.MethodAccessException完整修復總結.md" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "? 驗證失敗" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "建置輸出:" -ForegroundColor Yellow
    Write-Host $buildResult -ForegroundColor Gray
    Write-Host ""
    Write-Host "常見問題:" -ForegroundColor Cyan
    Write-Host "  1. 確認 Program.cs 已正確修改" -ForegroundColor White
    Write-Host "  2. 確認 Startup.cs 已移除過時的日誌配置" -ForegroundColor White
    Write-Host "  3. 刪除 bin 和 obj 資料夾後重試" -ForegroundColor White
    Write-Host ""
    Write-Host "快速修復命令:" -ForegroundColor Cyan
    Write-Host "  Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force" -ForegroundColor Yellow
    Write-Host "  dotnet clean" -ForegroundColor Yellow
    Write-Host "  dotnet restore" -ForegroundColor Yellow
    Write-Host "  dotnet build ChurchReport\ChurchReport.csproj" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host ""
Read-Host "按 Enter 鍵結束"
