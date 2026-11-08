<#
檔案整體說明
目的與定位：P2-11於.NET Framework隔離AppDomain執行實際合併Plugin完整鏈，不把一般bin單元測試當成部署閉包驗證。
主要功能與流程：只讀本工作樹artifacts內已建置DLL及net48測試入口，建立專屬Domain執行六個合成SDK／HTTP案例，全部完成才輸出固定計數。
使用邊界與維護：不讀正式金鑰、User Secrets或網路；測試案例自行Dispose連線、回應及key，runner以finally卸載Domain，再刪自有暫存測試閉包，不保存跨操作組件或租用。不建立背景程序，不以停程序取代資源釋放；產品路徑超出工作樹立即拒絕。
#>
[CmdletBinding()]
param(
    [switch]$Worker
)
$ErrorActionPreference = 'Stop'
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($Worker) {
    $assembly = [Environment]::GetEnvironmentVariable('P211_WORKER_ASSEMBLY')
    $test = [Environment]::GetEnvironmentVariable('P211_WORKER_TEST')
    foreach ($path in @($assembly, $test)) {
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not [IO.Path]::GetFullPath($path).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.File]::Exists($path)) {
            throw 'Worker input must be an existing file in its owned temporary fixture.'
        }
    }
    $probeType = [Reflection.Assembly]::LoadFrom($test).GetType(
        'SpeechMessage.Dynamics.Plugins.Tests.PackagedMemberProbe', $true)
    $probe = [Activator]::CreateInstance($probeType)
    $count = $probe.Verify($assembly)
    if ($count -ne 6) { throw 'Merged member chain did not complete all cases.' }
    Write-Output ('PASS merged-member-chain: total=' + $count + ' failed=0')
    exit 0
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assembly = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll'))
$test = [IO.Path]::GetFullPath((Join-Path $root 'SpeechMessage.Dynamics.Plugins.Tests/bin/Release/net48/SpeechMessage.Dynamics.Plugins.Tests.dll'))
$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($path in @($assembly, $test)) {
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($path)) {
        throw 'Expected local build output is unavailable.'
    }
}
$fixture = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('p211-chain-' + [Guid]::NewGuid().ToString('N'))))
$previousAssembly = [Environment]::GetEnvironmentVariable('P211_WORKER_ASSEMBLY')
$previousTest = [Environment]::GetEnvironmentVariable('P211_WORKER_TEST')
$child = $null
try {
    $null = New-Item -ItemType Directory -Path $fixture
    Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($test)) -File | Copy-Item -Destination $fixture
    # 同身分DLL必須在獨立閉包替換；否則LoadFrom會重用未合併Plugin而形成假驗證。
    $stagedAssembly = Join-Path $fixture 'SpeechMessage.Dynamics.Plugins.dll'
    Copy-Item -LiteralPath $assembly -Destination $stagedAssembly -Force
    $stagedTest = Join-Path $fixture ([IO.Path]::GetFileName($test))
    [Environment]::SetEnvironmentVariable('P211_WORKER_ASSEMBLY', $stagedAssembly)
    [Environment]::SetEnvironmentVariable('P211_WORKER_TEST', $stagedTest)
    $workerOutput = Join-Path $fixture 'worker.out'
    $workerError = Join-Path $fixture 'worker.err'
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Worker'
    ) -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $workerOutput -RedirectStandardError $workerError
    $workerText = if (Test-Path -LiteralPath $workerOutput) { [IO.File]::ReadAllText($workerOutput) } else { '' }
    if ($child.ExitCode -ne 0 -or -not $workerText.Contains('PASS merged-member-chain: total=6 failed=0')) {
        throw 'Merged member chain failed in the isolated worker process.'
    }
    Write-Output $workerText.Trim()
} finally {
    if ($null -ne $child) { $child.Dispose() }
    [Environment]::SetEnvironmentVariable('P211_WORKER_ASSEMBLY', $previousAssembly)
    [Environment]::SetEnvironmentVariable('P211_WORKER_TEST', $previousTest)
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('p211-chain-', [StringComparison]::Ordinal)) {
        throw 'Refused cleanup outside the owned fixture.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
