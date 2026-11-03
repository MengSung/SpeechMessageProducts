param([string]$RepositoryPath = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $RepositoryPath
# 直接驗證 SDK 選取與 Git 忽略行為，不掃描或建立真實金鑰、舊方案及建置產物。
$cases = [ordered]@{
    SdkSettings_SelectInstalledNet10WithLatestFeature = {
        $path = Join-Path $RepositoryPath 'global.json'
        if (-not (Test-Path -LiteralPath $path)) { throw 'global.json is missing.' }
        $config = [IO.File]::ReadAllText($path, (New-Object Text.UTF8Encoding($false))) | ConvertFrom-Json
        if ($config.sdk.version -notmatch '^10\.0\.\d+$' -or $config.sdk.rollForward -ne 'latestFeature') { throw 'SDK settings do not satisfy the phase contract.' }
        $installed = @(& dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] })
        if ($installed -notcontains $config.sdk.version) { throw 'Pinned SDK is not installed.' }
        $selected = (& dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $selected -notmatch '^10\.0\.\d+$') { throw 'Repository did not select a .NET 10 SDK.' }
    }
    IgnoreRules_ExcludeReferenceArtifactsKeysAndRawTrx = {
        # 以字碼產生非 ASCII 目錄資料，保持 PowerShell 可執行內容為 ASCII。
        $legacyName = '.' + (-join (@(0x5176,0x4ed6,0x89e3,0x6c7a,0x65b9,0x6848) | ForEach-Object { [char]$_ }))
        $probes = @(($legacyName + '/p0-ignore-probe.txt'), 'artifacts/p0-ignore-probe.txt', 'p0-ignore-probe.snk', 'p0-ignore-probe.trx', 'sixlabors.lic', 'nested/sixlabors.lic')
        if ($probes.Count -ne 6) { throw 'The test fixture must exercise all six independent paths.' }
        $missing = @()
        foreach ($probe in $probes) {
            $null = & git -c core.longpaths=true check-ignore --no-index -- $probe
            if ($LASTEXITCODE -ne 0) { $missing += $probe }
        }
        if ($missing.Count -gt 0) { throw ('Missing ignore coverage: ' + ($missing -join ', ')) }
    }
}
$failed = 0
foreach ($entry in $cases.GetEnumerator()) {
    try { & $entry.Value; Write-Output ('PASS ' + $entry.Key) }
    catch { $failed++; Write-Output ('FAIL ' + $entry.Key + ': ' + $_.Exception.Message) }
}
Write-Output ('P0-02 settings tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
