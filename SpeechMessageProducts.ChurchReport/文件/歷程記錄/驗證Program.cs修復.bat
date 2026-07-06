@echo off
chcp 65001 >nul
echo ========================================
echo Program.cs 版本衝突修復驗證工具
echo ========================================
echo.

echo [1/5] 檢查專案檔案...
if not exist "ChurchReport\ChurchReport.csproj" (
    echo ? 錯誤: 找不到 ChurchReport.csproj
    echo    請在方案根目錄執行此腳本
    pause
    exit /b 1
)
echo ? 專案檔案存在

echo.
echo [2/5] 檢查 Program.cs 修改...
findstr /C:"new WebHostBuilder()" "ChurchReport\Program.cs" >nul
if %errorlevel% equ 0 (
    echo ? Program.cs 已使用手動建構
) else (
    echo ? 警告: Program.cs 可能仍使用 CreateDefaultBuilder
    echo    請確認已套用修復
)

findstr /C:"CreateDefaultBuilder" "ChurchReport\Program.cs" >nul
if %errorlevel% equ 0 (
    echo ? 警告: 發現 CreateDefaultBuilder，請移除
) else (
    echo ? 已移除 CreateDefaultBuilder
)

echo.
echo [3/5] 清理專案...
cd ChurchReport
dotnet clean >nul 2>&1
if %errorlevel% equ 0 (
    echo ? 清理成功
) else (
    echo ?? 清理時出現警告
)
cd ..

echo.
echo [4/5] 還原套件...
cd ChurchReport
dotnet restore >nul 2>&1
if %errorlevel% equ 0 (
    echo ? 套件還原成功
) else (
    echo ? 套件還原失敗
    cd ..
    pause
    exit /b 1
)
cd ..

echo.
echo [5/5] 建置專案...
dotnet build ChurchReport\ChurchReport.csproj --no-restore
if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo ? 驗證成功！
    echo ========================================
    echo.
    echo 修復已正確套用，應用程式可以正常建置。
    echo.
    echo 下一步:
    echo   1. 執行應用程式: dotnet run --project ChurchReport\ChurchReport.csproj
    echo   2. 或在 Visual Studio 中按 F5 啟動偵錯
    echo.
) else (
    echo.
    echo ========================================
    echo ? 驗證失敗
    echo ========================================
    echo.
    echo 請檢查上方的錯誤訊息。
    echo.
    echo 常見問題:
    echo   1. 確認 Program.cs 已正確修改
    echo   2. 確認 Startup.cs 已移除過時的日誌配置
    echo   3. 執行 "dotnet clean" 後重試
    echo.
)

echo.
echo 按任意鍵結束...
pause >nul
