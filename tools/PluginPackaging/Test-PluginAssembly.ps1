<#
以 .NET 10 的 System.Reflection.Metadata 讀取外掛，不把待測組件載入 PowerShell。
Windows PowerShell 5.1 不具相容的 Metadata 執行環境，故薄入口交給專用工具；兩者皆離線。
串流／PE reader 由工具操作擁有；此入口沒有背景程序或機密來源。
#>
[CmdletBinding()]
param(
    [string]$AssemblyPath
)
$ErrorActionPreference = 'Stop'
# 在腳本主體才求預設路徑，避免 Windows PowerShell 5.1 參數繫結階段的 PSScriptRoot 空值。
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '../../artifacts/plugins/*.dll'
}
$projectPath = Join-Path $PSScriptRoot 'PluginPackaging.csproj'
& dotnet build $projectPath -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$toolPath = Join-Path $PSScriptRoot 'bin/Release/net10.0/PluginPackaging.dll'
# 明確檔名交由工具 fail closed；萬用字元只在本機單層展開，不走訪其他目錄或父 worktree。
$paths = if (Test-Path -LiteralPath $AssemblyPath -PathType Leaf) { @($AssemblyPath) }
    else { @(Get-ChildItem -Path $AssemblyPath -File -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -ExpandProperty FullName) }
if ($paths.Count -eq 0) {
    Write-Output '{"ok":false,"reason":"NoAssemblies"}'
    exit 1
}
$exitCode = 0
foreach ($path in $paths) {
    & dotnet $toolPath $path
    if ($LASTEXITCODE -ne 0) { $exitCode = 1 }
}
exit $exitCode
