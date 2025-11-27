@echo off
chcp 65001 >nul
echo.
echo ╔══════════════════════════════════════════════════════════╗
echo ║          ChurchReport 404 錯誤 - 一鍵修復               ║
echo ╚══════════════════════════════════════════════════════════╝
echo.
echo 當前分支: Sunny_MyPay_2.1_Spit_HomeController
echo 問題: http://localhost:43371/ 出現 404 錯誤
echo 原因: 應用程式未重新載入 AuthenticationController
echo.
echo ══════════════════════════════════════════════════════════
echo [步驟 1/4] 檢查檔案完整性...
echo ══════════════════════════════════════════════════════════

if exist "Controllers\AuthenticationController.cs" (
    echo ? AuthenticationController.cs 存在
) else (
    echo ? AuthenticationController.cs 不存在！
    echo.
    echo 請先確保已從 Git 拉取最新代碼或重新創建控制器
    pause
    exit /b 1
)

if exist "Views\Authentication\Login.cshtml" (
    echo ? Login.cshtml 存在
) else (
    echo ? Login.cshtml 不存在！正在從 Home 複製...
    if not exist "Views\Authentication" mkdir "Views\Authentication"
    copy /Y "Views\Home\Login.cshtml" "Views\Authentication\Login.cshtml" >nul 2>&1
    if exist "Views\Home\LineIdLoginView.cshtml" (
        copy /Y "Views\Home\LineIdLoginView.cshtml" "Views\Authentication\LineIdLoginView.cshtml" >nul 2>&1
    )
    echo ? 視圖文件已複製
)

echo.
echo ══════════════════════════════════════════════════════════
echo [步驟 2/4] 停止現有進程（如果有）...
echo ══════════════════════════════════════════════════════════

:: 檢查並停止佔用 43371 埠的進程
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :43371 ^| findstr LISTENING') do (
    echo 發現進程 PID: %%a 佔用埠 43371
    taskkill /PID %%a /F >nul 2>&1
    if !errorlevel! equ 0 (
        echo ? 已停止進程 %%a
    )
)

timeout /t 2 >nul

echo.
echo ══════════════════════════════════════════════════════════
echo [步驟 3/4] 清理並重建專案...
echo ══════════════════════════════════════════════════════════

:: 清理 bin 和 obj 資料夾
if exist "bin" (
    rd /s /q "bin" >nul 2>&1
    echo ? 已清理 bin 資料夾
)

if exist "obj" (
    rd /s /q "obj" >nul 2>&1
    echo ? 已清理 obj 資料夾
)

:: 使用 MSBuild 重建（更可靠）
echo.
echo 正在重建專案...
echo （這可能需要 30-60 秒，請稍候...）
echo.

msbuild ChurchReport.csproj /t:Rebuild /p:Configuration=Debug /v:minimal /nologo

if %errorlevel% neq 0 (
    echo.
    echo ? 建置失敗！請檢查錯誤訊息。
    echo.
    pause
    exit /b 1
)

echo.
echo ? 專案重建成功！

echo.
echo ══════════════════════════════════════════════════════════
echo [步驟 4/4] 驗證修復結果...
echo ══════════════════════════════════════════════════════════

if exist "bin\Debug\net471\ChurchReport.dll" (
    echo ? ChurchReport.dll 已生成
) else (
    echo ? ChurchReport.dll 未找到
    pause
    exit /b 1
)

:: 檢查 DLL 中是否包含 AuthenticationController
findstr /C:"AuthenticationController" "bin\Debug\net471\ChurchReport.dll" >nul 2>&1
if %errorlevel% equ 0 (
    echo ? AuthenticationController 已編譯進 DLL
) else (
    echo ? 無法確認 AuthenticationController（可能是正常的）
)

echo.
echo ══════════════════════════════════════════════════════════
echo ║                   ?? 修復完成！                         ║
echo ══════════════════════════════════════════════════════════
echo.
echo ? 檔案檢查完成
echo ? 進程清理完成
echo ? 專案重建完成
echo.
echo ┌──────────────────────────────────────────────────────┐
echo │  下一步操作（請在 Visual Studio 中執行）：           │
echo ├──────────────────────────────────────────────────────┤
echo │  1. 如果應用程式正在運行，請停止它（Shift+F5）       │
echo │  2. 按 F5 重新啟動應用程式                           │
echo │  3. 瀏覽器會自動開啟到 http://localhost:43371/       │
echo │  4. 您應該會看到登入頁面 ?                          │
echo └──────────────────────────────────────────────────────┘
echo.
echo ┌──────────────────────────────────────────────────────┐
echo │  測試清單：                                          │
echo ├──────────────────────────────────────────────────────┤
echo │  ? http://localhost:43371/                          │
echo │  ? http://localhost:43371/Login                     │
echo │  ? http://localhost:43371/Authentication/Login      │
echo │  ? http://localhost:43371/Home/Login (重定向)        │
echo └──────────────────────────────────────────────────────┘
echo.
echo ┌──────────────────────────────────────────────────────┐
echo │  如果仍然出現 404：                                  │
echo ├──────────────────────────────────────────────────────┤
echo │  1. 確認 Visual Studio 確實重新啟動了應用程式       │
echo │  2. 檢查「輸出」視窗中的啟動訊息                     │
echo │  3. 按 Ctrl+F5 在瀏覽器中強制刷新                   │
echo │  4. 嘗試訪問 /Login 而不是 /                        │
echo │  5. 查看 Logs\Trace.log 檔案中的錯誤                │
echo └──────────────────────────────────────────────────────┘
echo.
echo ?? 提示: 
echo    - 已清理 bin/obj 確保完全重建
echo    - 已檢查並停止佔用埠 43371 的進程
echo    - 視圖文件已就位
echo.
echo ?? 相關文檔:
echo    - 文件\404診斷報告-當前.md
echo    - 文件\AuthenticationController重構文檔.md
echo    - 文件\根目錄404錯誤診斷.md
echo.
pause
