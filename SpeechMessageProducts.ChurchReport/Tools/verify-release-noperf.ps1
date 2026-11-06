<#
檔案整體說明

目的與定位：檢查Release組件沒有Debug剖析輸出標記，保護正式版本不因診斷程式碼產生額外輸出；此檢查不能取代實際執行效能或所有敏感資料審查。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
# 驗證 Release 組件「無痕」：不得含任何 [Perf 字串（剖析輸出全在 #if DEBUG）。
# 用法：powershell -File Tools\verify-release-noperf.ps1
$ErrorActionPreference = "Stop"
$proj = "ChurchReport\ChurchReport.csproj"

Write-Host "==> dotnet build -c Release" -ForegroundColor Cyan
dotnet build $proj -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

$dll = Get-ChildItem -Path "ChurchReport\bin\Release" -Recurse -Filter "ChurchReport.dll" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "ChurchReport.dll (Release) not found." }

$bytes = [IO.File]::ReadAllBytes($dll.FullName)
$u = [Text.Encoding]::Unicode.GetString($bytes)
$a = [Text.Encoding]::ASCII.GetString($bytes)
$hit = $u.Contains("[Perf") -or $a.Contains("[Perf")

Write-Host ("DLL: " + $dll.FullName)
if ($hit) {
    Write-Host "`[FAIL`] Release DLL contains '`[Perf`' — 剖析字串外洩到 Release！" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`[PASS`] Release DLL 無 '`[Perf`' 字串（無痕通過）。" -ForegroundColor Green
}
