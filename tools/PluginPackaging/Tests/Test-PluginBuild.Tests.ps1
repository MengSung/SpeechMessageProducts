<#
故障注入只改測試程序的環境變數，finally 恢復；不讀取、複製或輸出正式金鑰內容。
驗證一般 Debug／Release 不打包；明確要求打包才檢查金鑰、警告或輸出簽章 DLL。
CI 明確傳入 UseEphemeralTestKey 才生成一次性測試金鑰；不依賴正式 secret，不作正式部署。
所有輸出隔離於本次暫存資料夾，結束時檢查絕對路徑邊界後刪除，不改寫正式 artifacts。
#>
[CmdletBinding()]
param([switch]$UseEphemeralTestKey)
$ErrorActionPreference = 'Stop'
$originalKey = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE')
if (-not $UseEphemeralTestKey -and [string]::IsNullOrWhiteSpace($originalKey)) {
    throw 'Formal key environment is unavailable; restart Codex before running this suite.'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectPath = Join-Path $repoRoot 'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj'
$checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-build-' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$failures = 0
try {
    $signingKey = $originalKey
    if ($UseEphemeralTestKey) {
        # 金鑰只存在本次測試專屬暫存目錄；RSA 與檔案各有確定的釋放／刪除路徑。
        $signingKey = Join-Path $fixtureRoot 'ephemeral.snk'
        $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
        try {
            $rsa.PersistKeyInCsp = $false
            [IO.File]::WriteAllBytes($signingKey, $rsa.ExportCspBlob($true))
        } finally { $rsa.Dispose() }
    }
    foreach ($sample in @('release-unset-no-package', 'debug-unset-no-package', 'release-unset', 'debug-unset', 'release-signed', 'release-invalid')) {
        $configuration = if ($sample.StartsWith('debug-', [StringComparison]::Ordinal)) { 'Debug' } else { 'Release' }
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $(if ($sample -eq 'release-signed') { $signingKey } elseif ($sample -eq 'release-invalid') { Join-Path $fixtureRoot 'missing.snk' } else { $null }))
        $outputDirectory = Join-Path $fixtureRoot $sample
        # 未要求打包的案例不得進入合併目標；即使沒有金鑰，也不能建立 artifacts 目錄。
        $packageArguments = @()
        if (-not $sample.EndsWith('-no-package', [StringComparison]::Ordinal)) { $packageArguments = @('-p:SpeechMessagePackagePlugin=true') }
        $output = @(& dotnet build $projectPath -c $configuration @packageArguments --no-restore "-p:SpeechMessagePluginOutputDirectory=$outputDirectory" -p:SpeechMessageBuildNumber=37 --nologo -v minimal 2>&1)
        $code = $LASTEXITCODE
        $assemblyPath = Join-Path $outputDirectory 'SpeechMessage.Dynamics.Plugins.dll'
        $text = $output -join "`n"
        if ($sample.EndsWith('-no-package', [StringComparison]::Ordinal)) {
            $passed = $code -eq 0 -and -not (Test-Path -LiteralPath $outputDirectory) -and
                -not $text.Contains('SMP0001') -and -not $text.Contains('SMP0002') -and -not $text.Contains('SMP0003')
        } elseif ($sample -eq 'release-invalid') {
            $passed = $code -ne 0 -and $text.Contains('SMP0003') -and -not (Test-Path -LiteralPath $outputDirectory)
        } elseif ($sample -eq 'release-unset') {
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
Write-Output ('Plugin build samples: total=6 failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
