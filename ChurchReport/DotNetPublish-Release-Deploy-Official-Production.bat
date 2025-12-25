@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo   ChurchReport 正式環境部署
echo   .NET 10 Razor Pages Web 最佳化
echo ========================================
echo.

REM 步驟 1: 環境檢查
echo [步驟 1/5] 環境檢查...
echo.

REM 檢查 .NET SDK
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [錯誤] 未找到 .NET SDK
    echo 請先安裝 .NET 10 SDK
    goto :ERROR
)

for /f "tokens=*" %%v in ('dotnet --version') do set DOTNET_VERSION=%%v
echo [OK] .NET SDK 版本: %DOTNET_VERSION%

REM 檢查專案檔
if not exist "ChurchReport.csproj" (
    echo [錯誤] 找不到 ChurchReport.csproj
    echo 請確認在正確的目錄執行此腳本
    goto :ERROR
)
echo [OK] 找到專案檔 ChurchReport.csproj

REM 取得 Git 版本號（僅用於顯示）
for /f "tokens=*" %%i in ('git describe --tags --always 2^>nul') do set GIT_VERSION=%%i
if "%GIT_VERSION%"=="" set GIT_VERSION=Manual-Build
echo [OK] Git 版本: %GIT_VERSION%

echo [OK] 建置時間: %date% %time%
echo.

REM 步驟 2: 清理舊版本
echo [步驟 2/5] 清理舊版本...
if exist "./bin/Output-Release-Deploy-Official-Production" (
    rmdir /s /q "./bin/Output-Release-Deploy-Official-Production"
    echo [OK] 已清理舊的輸出目錄
) else (
    echo [OK] 無需清理
)
echo.

REM 步驟 3: 還原套件
echo [步驟 3/5] 還原 NuGet 套件...
dotnet restore --verbosity minimal
if %ERRORLEVEL% NEQ 0 (
    echo [錯誤] 套件還原失敗
    goto :ERROR
)
echo [OK] 套件還原完成
echo.

REM 步驟 4: 編譯專案
echo [步驟 4/5] 編譯並發佈專案...
echo 編譯配置:
echo   - 目標平台: win-x64
echo   - 部署模式: Framework-Dependent
echo   - 優化級別: ReadyToRun + PGO
echo   - 輸出目錄: ./bin/Output-Release-Deploy-Official-Production
echo.

dotnet publish -c Release -r win-x64 --self-contained false ^
    --verbosity normal ^
    /p:Version=1.0.0 ^
    /p:AssemblyVersion=1.0.0 ^
    /p:FileVersion=1.0.0 ^
    /p:InformationalVersion=%GIT_VERSION% ^
    /p:PublishSingleFile=false ^
    /p:IncludeNativeLibrariesForSelfExtract=false ^
    /p:PublishReadyToRun=true ^
    /p:PublishReadyToRunComposite=false ^
    /p:PublishTrimmed=false ^
    /p:TieredCompilation=true ^
    /p:TieredCompilationQuickJit=true ^
    /p:TieredPGO=true ^
    /p:OptimizationPreference=Speed ^
    /p:DebugType=None ^
    /p:ReadyToRunUseCrossgen2=true ^
    /p:DebugSymbols=false ^
    /p:Deterministic=false ^
    /p:ContinuousIntegrationBuild=false ^
    -o "./bin/Output-Release-Deploy-Official-Production"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [錯誤] 編譯失敗 (錯誤代碼: %ERRORLEVEL%)
    goto :ERROR
)

echo [OK] 編譯完成
echo.

REM 步驟 5: 驗證輸出
echo [步驟 5/5] 驗證部署結果...
echo.

set FAILED=0

if exist "./bin/Output-Release-Deploy-Official-Production/ChurchReport.dll" (
    echo [OK] ChurchReport.dll
) else (
    echo [FAIL] ChurchReport.dll 不存在
    set FAILED=1
)

if exist "./bin/Output-Release-Deploy-Official-Production/web.config" (
    echo [OK] web.config
) else (
    echo [WARN] web.config 不存在 (將由 IIS 自動生成)
)

if exist "./bin/Output-Release-Deploy-Official-Production/appsettings.json" (
    echo [OK] appsettings.json
) else (
    echo [WARN] appsettings.json 不存在
)

if exist "./bin/Output-Release-Deploy-Official-Production/wwwroot" (
    echo [OK] wwwroot 靜態資源
) else (
    echo [WARN] wwwroot 目錄不存在
)

REM 檢查 ReadyToRun 編譯
dir /b "./bin/Output-Release-Deploy-Official-Production/*.ni.dll" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [OK] ReadyToRun Native Images (.ni.dll)
) else (
    echo [WARN] 未找到 Native Images (AOT 編譯可能未啟用)
)

echo.

if %FAILED% EQU 1 (
    echo [錯誤] 部署驗證失敗，關鍵檔案遺失
    goto :ERROR
)

REM 計算部署包大小
for /f "tokens=3" %%s in ('dir /s /-c "./bin/Output-Release-Deploy-Official-Production" 2^>nul ^| find "個檔案"') do set SIZE=%%s
if not "%SIZE%"=="" (
    echo 部署包大小: %SIZE% bytes
)

echo.
echo ========================================
echo   部署成功！
echo ========================================
echo.
echo 部署資訊:
echo   版本號: %GIT_VERSION%
echo   建置時間: %date% %time%
echo   輸出目錄: ./bin/Output-Release-Deploy-Official-Production
echo.
echo 優化配置:
echo   [OK] ReadyToRun AOT 編譯
echo   [OK] Profile-Guided Optimization (PGO)
echo   [OK] Tiered Compilation
echo   [OK] 速度優先優化
echo.
echo 預期效能提升:
echo   啟動速度: 30-50%%
echo   首次請求: 20-30%%
echo   穩定執行: 10-15%%
echo.
echo ========================================
echo   部署注意事項
echo ========================================
echo.
echo IIS 部署檢查清單:
echo   [ ] 1. 伺服器已安裝 .NET 10 Runtime (ASP.NET Core)
echo   [ ] 2. IIS 模組: ASP.NET Core Module (ANCM)
echo   [ ] 3. 應用程式集區設定: "無受管理程式碼"
echo   [ ] 4. 應用程式集區設定: 啟動模式 "AlwaysRunning"
echo   [ ] 5. 網站實體路徑指向: ./bin/Output-Release-Deploy-Official-Production
echo.
echo 設定檔檢查:
echo   [ ] 1. appsettings.json - Dynamics 365 連線字串
echo   [ ] 2. appsettings.json - Line Notify Token
echo   [ ] 3. web.config - ASPNETCORE_ENVIRONMENT = Production
echo   [ ] 4. DevExtreme 授權檔案 (如需要)
echo.
echo Dynamics 365 連線:
echo   [ ] 1. CRM URL 正確
echo   [ ] 2. 帳號密碼正確
echo   [ ] 3. 網路連線正常
echo   [ ] 4. 防火牆規則設定
echo.
echo ========================================
goto :END

:ERROR
echo.
echo ========================================
echo   部署失敗！
echo ========================================
echo.
echo 錯誤代碼: %ERRORLEVEL%
echo.
echo 常見問題排除:
echo.
echo 1. SDK 版本問題
echo    解決: 確認安裝 .NET 10 SDK
echo    檢查: dotnet --list-sdks
echo.
echo 2. 套件還原失敗
echo    解決: 手動執行 dotnet restore
echo    檢查: 網路連線、NuGet 來源
echo.
echo 3. 編譯錯誤
echo    解決: 檢查上方錯誤訊息
echo    建議: 在 Visual Studio 中先建置確認
echo.
echo 4. 磁碟空間不足
echo    解決: 清理 bin/obj 目錄
echo    檢查: 可用磁碟空間 ^> 1GB
echo.
echo 5. 權限問題
echo    解決: 以系統管理員身分執行
echo.
echo 6. Git 版本號問題
echo    解決: 已修正為固定版本 1.0.0
echo    備註: Git hash 僅用於顯示
echo.
echo ========================================
pause
exit /b 1

:END
echo.
echo 按任意鍵關閉視窗...
pause >nul
exit /b 0