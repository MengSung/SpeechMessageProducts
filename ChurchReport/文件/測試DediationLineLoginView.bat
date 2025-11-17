@echo off
chcp 65001 >nul
echo ========================================
echo DediationLineLoginView 完整測試
echo ========================================
echo.

echo [測試 1/8] 檢查視圖文件...
if exist "Views\Home\DediationLineLoginView.cshtml" (
    echo ? 視圖文件存在
    findstr /C:"dxLoadPanel" "Views\Home\DediationLineLoginView.cshtml" >nul
    if errorlevel 1 (
        echo ? 視圖中缺少 LoadPanel 定義
    ) else (
        echo ? 視圖中包含 LoadPanel 定義
    )
) else (
    echo ? 視圖文件不存在
)
echo.

echo [測試 2/8] 檢查 Startup.cs 路由配置...
findstr /C:"DediationLineLoginView" "Startup.cs" >nul
if errorlevel 1 (
    echo ? Startup.cs 中缺少路由配置
) else (
    echo ? Startup.cs 中有路由配置
)
echo.

echo [測試 3/8] 檢查 DedicationController...
findstr /C:"public IActionResult DediationLineLoginView" "Controllers\DedicationController.cs" >nul
if errorlevel 1 (
    echo ? DedicationController 中缺少方法
) else (
    echo ? DedicationController 中有 DediationLineLoginView 方法
)
echo.

echo [測試 4/8] 檢查 SetupUserLineId 方法...
findstr /C:"public IActionResult SetupUserLineId" "Controllers\DedicationController.cs" >nul
if errorlevel 1 (
    echo ? DedicationController 中缺少 SetupUserLineId
) else (
    echo ? DedicationController 中有 SetupUserLineId 方法
)
echo.

echo [測試 5/8] 檢查 HomeController 向後相容路由...
findstr /C:"SetupUserLineIdRedirect" "Controllers\HomeController.cs" >nul
if errorlevel 1 (
    echo ? HomeController 中缺少向後相容路由
) else (
    echo ? HomeController 中有 SetupUserLineId 向後相容路由
)
echo.

echo [測試 6/8] 檢查 IIS 服務狀態...
sc query W3SVC | findstr "RUNNING" >nul
if errorlevel 1 (
    echo ? IIS 服務未運行
    echo    執行: net start W3SVC
) else (
    echo ? IIS 服務正在運行
)
echo.

echo [測試 7/8] 檢查應用程式池狀態...
powershell -Command "Import-Module WebAdministration; try { $state = (Get-WebAppPoolState 'ChurchReport').Value; if ($state -eq 'Started') { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo ?? 應用程式池未運行或不存在
    echo    執行修復腳本
) else (
    echo ? 應用程式池正在運行
)
echo.

echo [測試 8/8] 檢查 Port 479 監聽...
netstat -ano | findstr ":479.*LISTENING" >nul
if errorlevel 1 (
    echo ? Port 479 沒有被監聽
    echo    檢查 IIS 網站綁定
) else (
    echo ? Port 479 正在監聽
)
echo.

echo ========================================
echo 測試完成 - 請閱讀以下建議
echo ========================================
echo.

echo 【手動測試 URL】
echo.
echo 1. 在伺服器上測試 (本機):
echo    https://localhost:479/Dedication/DediationLineLoginView/test
echo.
echo 2. 實際 LIFF URL:
echo    https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
echo.
echo 3. 向後相容路徑:
echo    https://sunnyvalechback.speechmessage.com.tw:479/Home/DediationLineLoginView/2007156647-OYnN8BKy
echo.

echo 【瀏覽器測試步驟】
echo.
echo 1. 開啟 Chrome DevTools (F12)
echo 2. 切換到 Console 標籤 - 查看 JavaScript 錯誤
echo 3. 切換到 Network 標籤 - 查看請求狀態
echo 4. 在 LINE 應用程式中開啟 LIFF URL
echo 5. 觀察登入流程
echo.

echo 【常見問題排除】
echo.
echo 如果頁面顯示但 JavaScript 錯誤:
echo    → 檢查 Browser Console
echo    → 確認 LIFF SDK 載入
echo.
echo 如果 AJAX 請求失敗:
echo    → 檢查 Network 標籤中的 SetupUserLineId 請求
echo    → 確認返回狀態碼 200 和 JSON { "status": "1" }
echo.
echo 如果無法重導向到 QPayView:
echo    → 檢查成功回調中的 window.location.href
echo    → 確認 /Home/QPayView/{LineUserId} 路徑存在
echo.
echo 如果 LIFF 初始化失敗:
echo    → 確認 LIFF ID 正確: 2007156647-OYnN8BKy
echo    → 在 LINE Developers Console 檢查設定
echo    → 確認網域在白名單中
echo.

echo 【下一步】
echo.
echo □ 執行手動 URL 測試
echo □ 在 LINE 應用程式中測試
echo □ 檢查 Browser Console 錯誤
echo □ 檢查 Network 請求狀態
echo □ 如果仍失敗，查看應用程式日誌:
echo   - Logs\stdout*.log
echo   - Logs\Trace.log
echo.

pause
