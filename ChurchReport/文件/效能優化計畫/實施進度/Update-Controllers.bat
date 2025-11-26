@echo off
chcp 65001 >nul
echo =========================================
echo   批量更新 Controllers - CRM 連接池整合
echo =========================================
echo.

REM 設定 PowerShell 執行策略
echo [步驟 1] 設定 PowerShell 執行策略...
powershell -Command "Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force"
if %errorlevel% neq 0 (
    echo [錯誤] 無法設定執行策略，請以系統管理員身份執行
    pause
    exit /b 1
)
echo [完成] 執行策略已設定
echo.

REM 執行 PowerShell 腳本
echo [步驟 2] 執行批量更新腳本...
powershell -ExecutionPolicy Bypass -File "%~dp0Update-Controllers.ps1"
if %errorlevel% neq 0 (
    echo [錯誤] 腳本執行失敗
    pause
    exit /b 1
)
echo.

REM 提示下一步
echo =========================================
echo   批量更新完成！
echo =========================================
echo.
echo 下一步操作：
echo 1. 檢查上述輸出是否有錯誤
echo 2. 執行編譯測試：
echo    cd D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport
echo    dotnet build
echo.
pause
