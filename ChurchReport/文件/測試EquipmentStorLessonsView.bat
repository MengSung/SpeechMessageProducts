@echo off
chcp 65001 >nul
echo ====================================
echo EquipmentStorLessonsView 快速測試
echo ====================================
echo.

echo [1/5] 檢查檔案是否存在...
if exist "Views\Equipment\EquipmentStorLessonsView.cshtml" (
    echo ? EquipmentStorLessonsView.cshtml 存在
) else (
    echo ? EquipmentStorLessonsView.cshtml 不存在
    goto :error
)

echo.
echo [2/5] 檢查 Controller 是否存在...
if exist "Controllers\EquipmentController.cs" (
    echo ? EquipmentController.cs 存在
) else (
    echo ? EquipmentController.cs 不存在
    goto :error
)

echo.
echo [3/5] 檢查 Model 是否存在...
if exist "Models\EquipmentStorLessons.cs" (
    echo ? EquipmentStorLessons.cs 存在
) else (
    echo ? EquipmentStorLessons.cs 不存在
    goto :error
)

echo.
echo [4/5] 搜尋 DataSource 配置...
findstr /C:".Mvc()" "Views\Equipment\EquipmentStorLessonsView.cshtml" >nul
if %errorlevel% equ 0 (
    echo ? 使用 .Mvc^(^) 配置（正確）
) else (
    findstr /C:".WebApi()" "Views\Equipment\EquipmentStorLessonsView.cshtml" >nul
    if %errorlevel% equ 0 (
        echo ? 使用 .WebApi^(^) 配置（可能有問題）
        echo    建議: 改用 .Mvc^(^)
    ) else (
        echo ？ 無法確定 DataSource 配置
    )
)

echo.
echo [5/5] 搜尋 LoadEquipmentStorLessons 方法...
findstr /C:"LoadEquipmentStorLessons" "Controllers\EquipmentController.cs" >nul
if %errorlevel% equ 0 (
    echo ? LoadEquipmentStorLessons 方法存在
) else (
    echo ? LoadEquipmentStorLessons 方法不存在
    goto :error
)

echo.
echo ====================================
echo 測試完成！所有檢查都通過
echo ====================================
echo.
echo 下一步:
echo 1. 啟動應用程式
echo 2. 訪問 https://localhost:port/Equipment/EquipmentView
echo 3. 展開小組列表
echo 4. 展開聯絡人列表
echo 5. 檢查課程列表是否顯示
echo.
echo 如果仍有問題，請:
echo - 按 F12 開啟瀏覽器開發者工具
echo - 查看 Console 標籤的錯誤訊息
echo - 查看 Network 標籤的請求狀態
echo.
pause
exit /b 0

:error
echo.
echo ====================================
echo 測試失敗！請檢查上述錯誤
echo ====================================
echo.
pause
exit /b 1
