<#
檔案整體說明
目的與定位：P2-11於獨立.NET Framework程序執行實際合併Plugin完整鏈，不把一般bin單元測試當成部署閉包驗證。
主要功能與流程：只讀本工作樹artifacts內已建置DLL及net48測試入口，將六個合成SDK／HTTP案例放進自有fixture，由隱藏worker執行，全部完成才輸出固定計數。
使用邊界與維護：不讀正式金鑰、User Secrets或網路；測試案例自行Dispose連線、回應及key。worker最多120秒，異常則終止並等待退出，finally釋放程序handle及環境參照，再刪自有fixture。輸出最多讀16KiB，失敗不轉印原始例外；路徑必須精確綁定本次fixture，不能只以整個Temp作信任範圍。
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
    $ownedFixture = [Environment]::GetEnvironmentVariable('P211_WORKER_FIXTURE')
    if ([string]::IsNullOrWhiteSpace($ownedFixture) -or
        -not [IO.Path]::GetFullPath($ownedFixture).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($ownedFixture) -notmatch '^p211-chain-[a-f0-9]{32}$') {
        throw 'Worker fixture identity is invalid.'
    }
    foreach ($path in @($assembly, $test)) {
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not [string]::Equals([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)), $ownedFixture, [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.File]::Exists($path)) {
            throw 'Worker input must be an existing file in its owned temporary fixture.'
        }
    }
    if ([IO.Path]::GetFileName($assembly) -ne 'packaged-member.dll' -or
        [IO.Path]::GetFileName($test) -ne 'SpeechMessage.Dynamics.Plugins.Tests.dll') {
        throw 'Worker filenames do not match its fixture contract.'
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
$previousFixture = [Environment]::GetEnvironmentVariable('P211_WORKER_FIXTURE')
$child = $null
try {
    $null = New-Item -ItemType Directory -Path $fixture
    Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($test)) -File | Copy-Item -Destination $fixture
    # 保留未合併測試相依供net48載入；合併簽章產物以不同檔名單獨LoadFrom，避免名稱繫結混用。
    $stagedAssembly = Join-Path $fixture 'packaged-member.dll'
    Copy-Item -LiteralPath $assembly -Destination $stagedAssembly
    $stagedTest = Join-Path $fixture ([IO.Path]::GetFileName($test))
    [Environment]::SetEnvironmentVariable('P211_WORKER_ASSEMBLY', $stagedAssembly)
    [Environment]::SetEnvironmentVariable('P211_WORKER_TEST', $stagedTest)
    [Environment]::SetEnvironmentVariable('P211_WORKER_FIXTURE', $fixture)
    $workerOutput = Join-Path $fixture 'worker.out'
    $workerError = Join-Path $fixture 'worker.err'
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Worker'
    ) -PassThru -WindowStyle Hidden -RedirectStandardOutput $workerOutput -RedirectStandardError $workerError
    # Windows PowerShell需在等待前取得handle，才能在子程序退出後可靠取得ExitCode。
    $null = $child.Handle
    if (-not $child.WaitForExit(120000)) { throw 'Merged member chain worker timed out.' }
    $child.WaitForExit()
    foreach ($output in @($workerOutput, $workerError)) {
        if ([IO.File]::Exists($output) -and (Get-Item -LiteralPath $output).Length -gt 16384) {
            throw 'Merged member chain worker exceeded its diagnostic budget.'
        }
    }
    $workerText = if (Test-Path -LiteralPath $workerOutput) { [IO.File]::ReadAllText($workerOutput) } else { '' }
    if ($child.ExitCode -ne 0 -or -not $workerText.Contains('PASS merged-member-chain: total=6 failed=0')) {
        throw ('Merged member chain worker failed: exit=' + $child.ExitCode)
    }
    Write-Output $workerText.Trim()
} finally {
    if ($null -ne $child) {
        try {
            if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
        } finally { $child.Dispose() }
    }
    [Environment]::SetEnvironmentVariable('P211_WORKER_ASSEMBLY', $previousAssembly)
    [Environment]::SetEnvironmentVariable('P211_WORKER_TEST', $previousTest)
    [Environment]::SetEnvironmentVariable('P211_WORKER_FIXTURE', $previousFixture)
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('p211-chain-', [StringComparison]::Ordinal)) {
        throw 'Refused cleanup outside the owned fixture.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
