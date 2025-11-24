@echo off
chcp 65001 > nul
echo ==========================================
echo    Mermaid 流程圖匯出工具
echo ==========================================
echo.

REM 檢查 Mermaid CLI 是否已安裝
where mmdc >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [錯誤] 未安裝 Mermaid CLI
    echo.
    echo 請執行以下命令安裝:
    echo npm install -g @mermaid-js/mermaid-cli
    echo.
    pause
    exit /b 1
)

echo [1/4] 開始匯出流程圖...
echo.

REM 設定輸入輸出路徑
set INPUT_FILE=整個系統的流程圖.md
set OUTPUT_PNG=整個系統的流程圖.png
set OUTPUT_SVG=整個系統的流程圖.svg

REM 匯出 PNG (白色背景, 2400px 寬)
echo [2/4] 匯出 PNG 格式 (白色背景, 2400px)...
mmdc -i "%INPUT_FILE%" -o "%OUTPUT_PNG%" -b white -w 2400
if %ERRORLEVEL% EQU 0 (
    echo      ✓ PNG 匯出成功: %OUTPUT_PNG%
) else (
    echo      ✗ PNG 匯出失敗
)
echo.

REM 匯出 SVG
echo [3/4] 匯出 SVG 格式...
mmdc -i "%INPUT_FILE%" -o "%OUTPUT_SVG%" -b white
if %ERRORLEVEL% EQU 0 (
    echo      ✓ SVG 匯出成功: %OUTPUT_SVG%
) else (
    echo      ✗ SVG 匯出失敗
)
echo.

REM 匯出深色主題版本
echo [4/4] 匯出深色主題版本...
mmdc -i "%INPUT_FILE%" -o "整個系統的流程圖_深色.png" -t dark -w 2400
if %ERRORLEVEL% EQU 0 (
    echo      ✓ 深色版本匯出成功
) else (
    echo      ✗ 深色版本匯出失敗
)
echo.

echo ==========================================
echo    匯出完成!
echo ==========================================
echo.
echo 已生成的檔案:
echo   - %OUTPUT_PNG%
echo   - %OUTPUT_SVG%
echo   - 整個系統的流程圖_深色.png
echo.

REM 開啟檔案總管顯示結果
explorer /select,"%OUTPUT_PNG%"

pause