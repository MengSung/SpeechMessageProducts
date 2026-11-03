<#
故障注入只改測試程序的環境變數，finally 恢復；不讀取、複製或輸出正式金鑰內容。
驗證 Release 缺金鑰失敗、Debug 缺金鑰警告且輸出未簽章 DLL、正式 Release 輸出可通過打包檢查。
所有輸出隔離於本次暫存資料夾，結束時檢查絕對路徑邊界後刪除，不改寫正式 artifacts。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectPath = Join-Path $repoRoot 'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj'
$checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-build-' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$originalKey = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE')
if ([string]::IsNullOrWhiteSpace($originalKey)) { throw 'Formal key environment is unavailable; restart Codex before running this suite.' }
$failures = 0
try {
    foreach ($sample in @('release-unset', 'debug-unset', 'release-signed')) {
        $configuration = if ($sample -eq 'debug-unset') { 'Debug' } else { 'Release' }
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $(if ($sample -eq 'release-signed') { $originalKey } else { $null }))
        $outputDirectory = Join-Path $fixtureRoot $sample
        $output = @(& dotnet build $projectPath -c $configuration --no-restore "-p:SpeechMessagePluginOutputDirectory=$outputDirectory" -p:SpeechMessageBuildNumber=37 --nologo -v minimal 2>&1)
        $code = $LASTEXITCODE
        $assemblyPath = Join-Path $outputDirectory 'SpeechMessage.Dynamics.Plugins.dll'
        $text = $output -join "`n"
        if ($sample -eq 'release-unset') {
            $passed = $code -ne 0 -and $text.Contains('SMP0001') -and -not (Test-Path -LiteralPath $assemblyPath)
        } elseif ($sample -eq 'debug-unset') {
            $passed = $code -eq 0 -and $text.Contains('SMP0002') -and (Test-Path -LiteralPath $assemblyPath)
            if ($passed) {
                $inspection = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
                $inspectCode = $LASTEXITCODE
                $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
                $result = $jsonLine.ToString() | ConvertFrom-Json
                $passed = $inspectCode -eq 1 -and $result.checks.strongName -eq $false -and $result.checks.targetFramework -eq $true
            }
        } else {
            $passed = $code -eq 0 -and (Test-Path -LiteralPath $assemblyPath)
            if ($passed) {
                $null = & powershell -NoProfile -File $checker -AssemblyPath $assemblyPath
                $passed = $LASTEXITCODE -eq 0
                # 版本資源的唯讀查詢不載入執行 DLL；建置序號不改 AssemblyVersion 的穩定身分。
                $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
                $passed = $passed -and $version.FileVersion -eq '1.0.0.37' -and
                    $version.ProductVersion.StartsWith('1.0.0-p0.37', [StringComparison]::Ordinal)
            }
        }
        if ($passed) { Write-Output ('PASS ' + $sample) }
        else { $failures++; Write-Output ('FAIL ' + $sample + ': packaging contract not satisfied') }
    }
} finally {
    [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $originalKey)
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('speechmessage-build-', [StringComparison]::Ordinal)) {
        throw 'Refused cleanup outside the owned build fixture directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Force -Recurse
}
Write-Output ('Plugin build samples: total=3 failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
