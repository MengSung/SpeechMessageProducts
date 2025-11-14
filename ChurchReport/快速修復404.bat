@echo off
echo =====================================
echo 根目錄 404 錯誤 - 快速修復腳本
echo =====================================
echo.

echo [1/5] 檢查 AuthenticationController...
if exist "Controllers\AuthenticationController.cs" (
    echo    ? AuthenticationController.cs 存在
) else (
    echo    ? AuthenticationController.cs 不存在！
    pause
    exit
)

echo.
echo [2/5] 檢查 Login 視圖...
if exist "Views\Authentication\Login.cshtml" (
    echo    ? Views\Authentication\Login.cshtml 存在
) else (
    echo    ? Views\Authentication\Login.cshtml 不存在！
    echo    正在從 Home 複製...
    if not exist "Views\Authentication" mkdir "Views\Authentication"
    copy "Views\Home\Login.cshtml" "Views\Authentication\Login.cshtml" >nul
    if exist "Views\Home\LineIdLoginView.cshtml" (
        copy "Views\Home\LineIdLoginView.cshtml" "Views\Authentication\LineIdLoginView.cshtml" >nul
    )
    echo    ? 視圖已複製
)

echo.
echo [3/5] 清理專案...
echo    執行 dotnet clean...
dotnet clean --verbosity quiet
if %errorlevel% equ 0 (
    echo    ? 清理完成
) else (
    echo    ? 清理警告（可忽略）
)

echo.
echo [4/5] 重建專案...
echo    執行 dotnet build...
dotnet build --no-incremental --verbosity quiet
if %errorlevel% equ 0 (
    echo    ? 建置成功
) else (
    echo    ? 建置失敗！
    echo    請檢查編譯錯誤
    pause
    exit
)

echo.
echo [5/5] 完成診斷
echo.
echo =====================================
echo 修復完成！
echo =====================================
echo.
echo 下一步：
echo 1. 在 Visual Studio 中停止除錯（如果正在運行）
echo 2. 按 F5 重新啟動應用程式
echo 3. 訪問 http://localhost:43371/
echo.
echo 如果仍然出現 404：
echo - 嘗試訪問 http://localhost:43371/Login
echo - 嘗試訪問 http://localhost:43371/Authentication/Login
echo - 按 Ctrl+F5 在瀏覽器中強制刷新
echo.

pause
