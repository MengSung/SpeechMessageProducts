@echo off
chcp 65001 >nul
echo ========================================
echo DediationLineLoginView 404 快速修復
echo ========================================
echo.
echo 此腳本將執行以下修復操作：
echo 1. 重啟 IIS 應用程式池
echo 2. 清除暫存檔案
echo 3. 重啟 IIS 服務
echo 4. 驗證服務狀態
echo.
echo ?? 警告: 這將導致網站短暫無法訪問
echo.

set /p confirm="確定要繼續嗎？(Y/N): "
if /i not "%confirm%"=="Y" (
    echo 取消操作
    pause
    exit /b
)

echo.
echo [步驟 1/5] 停止應用程式池...
powershell -Command "Import-Module WebAdministration; Stop-WebAppPool -Name 'ChurchReport' -ErrorAction SilentlyContinue"
timeout /t 5 /nobreak >nul
echo ? 應用程式池已停止
echo.

echo [步驟 2/5] 清除 ASP.NET 暫存檔案...
if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files\*" (
    del /s /q "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files\*" 2>nul
    echo ? 暫存檔案已清除
) else (
    echo ?? 找不到暫存檔案目錄
)
echo.

echo [步驟 3/5] 啟動應用程式池...
powershell -Command "Import-Module WebAdministration; Start-WebAppPool -Name 'ChurchReport' -ErrorAction SilentlyContinue"
timeout /t 3 /nobreak >nul
echo ? 應用程式池已啟動
echo.

echo [步驟 4/5] 重啟 IIS 服務...
iisreset /restart
echo.

echo [步驟 5/5] 等待服務穩定 (30 秒)...
timeout /t 30 /nobreak >nul
echo.

echo ========================================
echo 驗證修復結果
echo ========================================
echo.

echo 檢查 IIS 服務狀態...
sc query W3SVC | findstr "STATE"
echo.

echo 檢查應用程式池狀態...
powershell -Command "Import-Module WebAdministration; Get-WebAppPoolState -Name 'ChurchReport' 2>$null"
echo.

echo 檢查 port 479 監聽...
netstat -ano | findstr ":479"
echo.

echo 測試本機連線...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'https://localhost:479/' -UseBasicParsing -TimeoutSec 10 -SkipCertificateCheck; Write-Host '? 修復成功 - 狀態碼:' $response.StatusCode } catch { Write-Host '? 仍然失敗:' $_.Exception.Message; Write-Host '請執行進階診斷或聯絡技術支援' }"
echo.

echo ========================================
echo 修復完成
echo ========================================
echo.
echo 請測試以下 URL：
echo https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
echo.
echo 如果仍然失敗，請：
echo 1. 執行「診斷DediationLineLoginView-404.bat」進行詳細診斷
echo 2. 檢查「DediationLineLoginView-404錯誤診斷報告.md」
echo 3. 查看應用程式日誌: Logs\stdout*.log 和 Logs\Trace.log
echo.

pause
