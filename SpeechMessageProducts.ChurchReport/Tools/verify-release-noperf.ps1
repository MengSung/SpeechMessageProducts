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
