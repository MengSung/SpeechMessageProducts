# ========================================
# Trace 專案快速修正腳本
# 修正 NETSDK1022 重複項目錯誤
# ========================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Trace 專案快速修正腳本" -ForegroundColor Cyan
Write-Host "修正 NETSDK1022 重複項目錯誤" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 設定路徑
$TraceProject = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\Trace"
$TraceCsproj = Join-Path $TraceProject "Trace.csproj"
$TraceFixed = Join-Path $TraceProject "Trace_Fixed.csproj"
$BackupFile = Join-Path $TraceProject "Trace.csproj.with-error"

# 檢查目錄
if (-not (Test-Path $TraceProject)) {
    Write-Host "? 錯誤: 找不到 Trace 專案目錄" -ForegroundColor Red
    exit 1
}

Write-Host "? 找到 Trace 專案目錄" -ForegroundColor Green
Write-Host ""

# 步驟 1: 備份有錯誤的檔案
Write-Host "[步驟 1/4] 備份有錯誤的專案檔案..." -ForegroundColor Cyan
if (Test-Path $TraceCsproj) {
    Copy-Item $TraceCsproj $BackupFile -Force
    Write-Host "? 已備份至: $BackupFile" -ForegroundColor Green
}
Write-Host ""

# 步驟 2: 使用修正後的檔案
Write-Host "[步驟 2/4] 使用修正後的專案檔案..." -ForegroundColor Cyan
if (Test-Path $TraceFixed) {
    Copy-Item $TraceFixed $TraceCsproj -Force
    Write-Host "? 已複製修正後的檔案" -ForegroundColor Green
} else {
    Write-Host "??  找不到 Trace_Fixed.csproj，跳過此步驟" -ForegroundColor Yellow
}
Write-Host ""

# 步驟 3: 清理編譯輸出
Write-Host "[步驟 3/4] 清理編譯輸出..." -ForegroundColor Cyan
Push-Location $TraceProject

$objDir = Join-Path $TraceProject "obj"
$binDir = Join-Path $TraceProject "bin"

if (Test-Path $objDir) {
    Remove-Item -Recurse -Force $objDir
    Write-Host "? 已清理 obj 目錄" -ForegroundColor Green
}

if (Test-Path $binDir) {
    Remove-Item -Recurse -Force $binDir
    Write-Host "? 已清理 bin 目錄" -ForegroundColor Green
}

Pop-Location
Write-Host ""

# 步驟 4: 重新編譯
Write-Host "[步驟 4/4] 重新編譯專案..." -ForegroundColor Cyan
Push-Location $TraceProject

$buildResult = dotnet build Trace.csproj 2>&1
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -eq 0) {
    Write-Host "? 編譯成功!" -ForegroundColor Green
    Write-Host ""
    
    # 檢查輸出檔案
    $outputDir = Join-Path $TraceProject "bin\Debug\net10.0"
    $traceDll = Join-Path $outputDir "Trace.dll"
    $traceXml = Join-Path $outputDir "Trace.xml"
    
    if (Test-Path $traceDll) {
        Write-Host "? Trace.dll 已產生" -ForegroundColor Green
        $fileInfo = Get-Item $traceDll
        Write-Host "   大小: $($fileInfo.Length) bytes" -ForegroundColor Gray
    }
    
    if (Test-Path $traceXml) {
        Write-Host "? Trace.xml 已產生" -ForegroundColor Green
    }
} else {
    Write-Host "? 編譯失敗!" -ForegroundColor Red
    Write-Host "錯誤訊息:" -ForegroundColor Yellow
    Write-Host $buildResult -ForegroundColor Yellow
    Pop-Location
    exit 1
}

Pop-Location
Write-Host ""

# 完成
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "? 修正完成!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "備份檔案位置: $BackupFile" -ForegroundColor Gray
Write-Host ""
Write-Host "下一步:" -ForegroundColor Cyan
Write-Host "1. 在 Visual Studio 中重新載入 Trace 專案" -ForegroundColor White
Write-Host "2. 編譯整個解決方案: dotnet build ChurchReport.sln" -ForegroundColor White
Write-Host ""
