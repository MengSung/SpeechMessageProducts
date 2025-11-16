@echo off
chcp 65001 >nul
echo ============================================
echo IntegrateView 日期更新功能檢測工具
echo ============================================
echo.

echo [檢查 1/5] 檢查 Controller Action 是否存在...
findstr /C:"UpdateIntegrateDate" "ChurchReport\Controllers\SmallGroupController.cs" >nul
if %errorlevel%==0 (
    echo ? UpdateIntegrateDate action 已存在
) else (
    echo ? 錯誤: UpdateIntegrateDate action 不存在
    goto :error
)
echo.

echo [檢查 2/5] 檢查 System.Globalization using 語句...
findstr /C:"using System.Globalization" "ChurchReport\Controllers\SmallGroupController.cs" >nul
if %errorlevel%==0 (
    echo ? System.Globalization using 語句已添加
) else (
    echo ? 警告: 可能缺少 System.Globalization using 語句
)
echo.

echo [檢查 3/5] 檢查 View 的 JavaScript 更新...
findstr /C:"response.success" "ChurchReport\Views\Home\IntegrateView.cshtml" >nul
if %errorlevel%==0 (
    echo ? JavaScript 已更新為檢查 response.success
) else (
    echo ? 警告: JavaScript 可能未正確更新
)
echo.

echo [檢查 4/5] 檢查專案是否可編譯...
dotnet build ChurchReport\ChurchReport.csproj --no-restore --verbosity quiet >nul 2>&1
if %errorlevel%==0 (
    echo ? 專案編譯成功
) else (
    echo ? 錯誤: 專案編譯失敗
    echo 請執行: dotnet build ChurchReport\ChurchReport.csproj 查看詳細錯誤
    goto :error
)
echo.

echo [檢查 5/5] 驗證文件完整性...
if exist "ChurchReport\文件\IntegrateView日期更新除錯指南.md" (
    echo ? 除錯指南文件已創建
) else (
    echo ? 警告: 除錯指南文件不存在
)
echo.

echo ============================================
echo ? 所有檢查通過!
echo ============================================
echo.
echo 修復內容摘要:
echo 1. 新增 UpdateIntegrateDate action 方法
echo 2. 添加 System.Globalization using 語句
echo 3. 更新 View 的 JavaScript 邏輯
echo 4. 創建完整的除錯指南
echo.
echo 下一步測試建議:
echo 1. 啟動應用程式: dotnet run --project ChurchReport\ChurchReport.csproj
echo 2. 登入系統並進入 IntegrateView 頁面
echo 3. 嘗試更改小組日期
echo 4. 開啟瀏覽器開發者工具 (F12) 監控 Network 請求
echo 5. 確認頁面正確重新載入新日期的資料
echo.
echo 如遇問題,請參考: ChurchReport\文件\IntegrateView日期更新除錯指南.md
echo.
goto :end

:error
echo.
echo ============================================
echo ? 檢測發現問題,請檢查上述錯誤訊息
echo ============================================
echo.
exit /b 1

:end
pause
