# LineMessagingProcessor 升級至 .NET 10 的替換腳本

Write-Host "LineMessagingProcessor 專案升級至 .NET 10" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

$projectDir = "LineMessagingProcessor"
$oldProjectFile = "$projectDir\LineMessagingProcessor.csproj"
$newProjectFile = "$projectDir\LineMessagingProcessor_Net10.csproj"
$backupFile = "$projectDir\LineMessagingProcessor.csproj.backup"

# 檢查檔案是否存在
if (-not (Test-Path $newProjectFile)) {
    Write-Host "錯誤: 找不到新專案檔案 $newProjectFile" -ForegroundColor Red
    exit 1
}

Write-Host "步驟 1: 備份舊專案檔案..." -ForegroundColor Yellow
if (Test-Path $oldProjectFile) {
    Copy-Item $oldProjectFile $backupFile -Force
    Write-Host "  ? 已備份至: $backupFile" -ForegroundColor Green
}

Write-Host "步驟 2: 替換專案檔案..." -ForegroundColor Yellow
Remove-Item $oldProjectFile -Force
Copy-Item $newProjectFile $oldProjectFile -Force
Write-Host "  ? 已使用 .NET 10 專案檔案替換" -ForegroundColor Green

Write-Host ""
Write-Host "升級完成!" -ForegroundColor Green
Write-Host ""
Write-Host "請執行以下步驟:" -ForegroundColor Cyan
Write-Host "1. 重新載入 Visual Studio 方案" -ForegroundColor White
Write-Host "2. 還原 NuGet 套件" -ForegroundColor White
Write-Host "3. 重新建置專案" -ForegroundColor White
Write-Host ""
Write-Host "如需還原舊版本，請執行:" -ForegroundColor Yellow
Write-Host "  Copy-Item $backupFile $oldProjectFile -Force" -ForegroundColor White
