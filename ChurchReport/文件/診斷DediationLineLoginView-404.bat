@echo off
chcp 65001 >nul
echo ========================================
echo DediationLineLoginView 404 快速診斷
echo ========================================
echo.

echo [1/10] 檢查 IIS 服務狀態...
sc query W3SVC | findstr "STATE"
echo.

echo [2/10] 檢查應用程式池狀態...
powershell -Command "Import-Module WebAdministration; Get-WebAppPoolState -Name 'ChurchReport' 2>$null || Write-Host '找不到 ChurchReport 應用程式池'"
echo.

echo [3/10] 檢查 port 479 監聽狀態...
netstat -ano | findstr ":479"
if errorlevel 1 (
    echo ? Port 479 沒有被監聽
) else (
    echo ? Port 479 正在監聽
)
echo.

echo [4/10] 檢查 SSL 憑證綁定...
netsh http show sslcert ipport=0.0.0.0:479
if errorlevel 1 (
    echo ? Port 479 沒有 SSL 憑證綁定
) else (
    echo ? Port 479 有 SSL 憑證綁定
)
echo.

echo [5/10] 檢查 IIS 網站狀態...
powershell -Command "Import-Module WebAdministration; Get-Website | Where-Object { $_.Bindings -like '*:479:*' } | Select-Object Name, State, Bindings | Format-Table -AutoSize"
echo.

echo [6/10] 檢查應用程式進程...
tasklist /FI "IMAGENAME eq dotnet.exe" /FO TABLE
echo.

echo [7/10] 檢查最近的錯誤日誌 (stdout)...
if exist "Logs\stdout*.log" (
    powershell -Command "Get-ChildItem 'Logs\stdout*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName -Tail 10 }"
) else (
    echo ?? 找不到 stdout 日誌檔案
)
echo.

echo [8/10] 檢查最近的 Trace 日誌...
if exist "Logs\Trace.log" (
    powershell -Command "Get-Content 'Logs\Trace.log' -Tail 10"
) else (
    echo ?? 找不到 Trace.log
)
echo.

echo [9/10] 檢查 DNS 解析...
nslookup sunnyvalechback.speechmessage.com.tw
echo.

echo [10/10] 測試本機連線...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'https://localhost:479/' -UseBasicParsing -TimeoutSec 5; Write-Host '? 本機連線成功 - 狀態碼:' $response.StatusCode } catch { Write-Host '? 本機連線失敗:' $_.Exception.Message }"
echo.

echo ========================================
echo 診斷完成
echo ========================================
echo.
echo 根據以上結果判斷問題：
echo.
echo 如果 IIS 服務未運行 → 執行: net start W3SVC
echo 如果應用程式池未運行 → 執行修復腳本 1
echo 如果 Port 479 沒有監聽 → 檢查 IIS 綁定配置
echo 如果沒有 SSL 憑證 → 執行修復腳本 2
echo 如果本機連線失敗 → 檢查應用程式日誌
echo.

pause
