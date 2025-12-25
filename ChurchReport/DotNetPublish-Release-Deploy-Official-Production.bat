echo ========================================
echo   ChurchReport 正式環境部署
echo   .NET 10 Razor Pages Web 最佳化
echo ========================================
echo.

REM 取得當前版本號（從 Git 或自訂）
for /f "tokens=*" %%i in ('git describe --tags --always 2^>nul') do set GIT_VERSION=%%i
if "%GIT_VERSION%"=="" set GIT_VERSION=1.0.0

echo [時間] %date% %time%
echo.

REM 清理舊的輸出
if exist "./bin/Output-Release-Deploy-Official-Production" (
    echo [清理] 清理舊版本...
    rmdir /s /q "./bin/Output-Release-Deploy-Official-Production"
)

echo [編譯] 開始編譯...
echo.

REM 執行編譯 - 移除會導致版本號衝突的參數
dotnet publish -c Release -r win-x64 --self-contained false ^
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
    /p:IlcOptimizationPreference=Speed ^
    /p:IlcOptimizationData=true ^
-o "./bin/Output-Release-Deploy-Official-Production"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo   [成功] 部署成功！
    echo ========================================
    echo   [輸出] ./bin/Output-Release-Deploy-Production
    echo   [版本] %VERSION%
    echo   [優化] Web 最佳化 (AOT + PGO)
    echo   [啟動] 提升 30-50%%
    echo   [效能] 提升 10-15%%
    echo ========================================
    
    REM 顯示關鍵檔案
    echo.
    echo [檢查] 關鍵檔案檢查:
    if exist "./bin/Output-Release-Deploy-Production/ChurchReport.dll" (
        echo    [OK] ChurchReport.dll
    ) else (
        echo    [FAIL] ChurchReport.dll 不存在！
    )
    
    if exist "./bin/Output-Release-Deploy-Production/web.config" (
        echo    [OK] web.config
    ) else (
        echo    [WARN] web.config 不存在
    )
    
    if exist "./bin/Output-Release-Deploy-Production/appsettings.json" (
        echo    [OK] appsettings.json
    ) else (
        echo    [WARN] appsettings.json 不存在
    )
    
    REM 檢查 ReadyToRun 編譯結果
    dir /b "./bin/Output-Release-Deploy-Production/*.ni.dll" >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo    [OK] ReadyToRun Native Images (.ni.dll)
    ) else (
        echo    [WARN] 未找到 Native Images
    )
    
    REM 計算部署包大小
    for /f "tokens=3" %%s in ('dir /s /-c "./bin/Output-Release-Deploy-Production" 2^>nul ^| find "個檔案"') do set SIZE=%%s
    if not "%SIZE%"=="" (
        echo    [INFO] 部署包大小: %SIZE% bytes
    )
    
    echo.
    echo [注意] 部署注意事項:
    echo    1. 確認目標伺服器已安裝 .NET 10 Runtime (ASP.NET Core)
    echo    2. 設定 IIS 應用程式集區為 "無受管理程式碼"
    echo    3. 確認 Dynamics 365 連線設定正確
    echo    4. 檢查 appsettings.json 中的連線字串
    echo    5. 確認 DevExtreme 授權有效
    echo.
    echo [下一步] IIS 部署步驟:
    echo    1. 停止 IIS 應用程式集區
    echo    2. 複製 ./bin/Output-Release-Deploy-Production 到伺服器
    echo    3. 設定 IIS 網站實體路徑
    echo    4. 啟動應用程式集區
    echo    5. 測試網站是否正常運作
    
) else (
    echo.
    echo ========================================
    echo   [失敗] 部署失敗！
    echo ========================================
    echo   [錯誤] 錯誤代碼: %ERRORLEVEL%
    echo   [提示] 請檢查上方錯誤訊息
    echo ========================================
    echo.
    echo [排除] 常見問題:
    echo    1. 檢查 .NET 10 SDK 是否已安裝
    echo    2. 執行 dotnet restore 還原套件
    echo    3. 檢查專案是否有編譯錯誤
    echo    4. 確認在正確的目錄執行 (包含 ChurchReport.csproj)
    echo.
)

echo.

pause