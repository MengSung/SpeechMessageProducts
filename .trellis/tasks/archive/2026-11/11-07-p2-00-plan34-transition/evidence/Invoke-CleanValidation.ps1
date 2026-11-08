<#
檔案整體說明
目的與定位：在已提交的短路徑乾淨複本執行實作計畫 3.4 的離線守門，原始輸出只留本機。
主要功能與流程：由目前 HEAD 建立 shared clone，依序建置、測試與品質檢查，逐項記錄結束碼；
Release 建置失敗即停止測試，避免把不存在或舊產物誤當成通過。額外 focused 案例只證明沒有 Debug 產物也能驗 R9。
使用邊界與維護：不讀秘密、不連 D365、LINE 或部署；只有還原套件可能連套件來源。每個原生程序同步等待退出。
目錄限定 C:\smv\<提交前八碼>；本工具不刪資料，結果檢查後由主代理驗證完整路徑再清理。
#>
$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$repo = (Get-Location).Path
$commit = (& git rev-parse HEAD).Trim()
$clone = Join-Path 'C:\smv' $commit.Substring(0,8)
$evidence = if ($env:P2_VALIDATION_EVIDENCE) { Join-Path $repo ($env:P2_VALIDATION_EVIDENCE + '/clean-' + $commit.Substring(0,8)) } else { Join-Path $repo ('.trellis\tasks\11-07-p2-00-plan34-transition\evidence\clean-' + $commit.Substring(0,8)) }
if (Test-Path -LiteralPath $clone) { throw 'Clone path already exists; inspect it instead of overwriting.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
& git clone --quiet --no-checkout --shared $repo $clone
if ($LASTEXITCODE -ne 0) { throw 'Local clone failed.' }
& git -C $clone checkout --quiet --detach $commit
if ($LASTEXITCODE -ne 0) { throw 'Clean checkout failed.' }
$results = [System.Collections.Generic.List[object]]::new()
function Invoke-Gate([string]$name, [string]$exe, [string[]]$arguments) {
    $log = Join-Path $evidence ($name + '.log')
    $started = [DateTimeOffset]::Now
    & $exe @arguments *> $log
    $code = $LASTEXITCODE
    $results.Add([pscustomobject]@{name=$name;exitCode=$code;startedAt=$started.ToString('o');endedAt=[DateTimeOffset]::Now.ToString('o');log=[IO.Path]::GetFileName($log)})
    Write-Output ($name + ': exit=' + $code)
    [IO.File]::WriteAllText((Join-Path $evidence 'results.json'), (ConvertTo-Json -InputObject @($results.ToArray()) -Depth 4), [Text.UTF8Encoding]::new($false))
    return $code
}
Push-Location $clone
try {
    $r = Invoke-Gate '01-release-build' 'dotnet' @('build','SpeechMessageProducts.sln','-c','Release','-warnaserror:NU1605')
    if ($r[-1] -ne 0) { throw 'Release build failed; do not test stale binaries.' }
    if (Test-Path -LiteralPath (Join-Path $clone 'SpeechMessage.Dynamics.Plugins\bin\Debug\net462\SpeechMessage.Dynamics.Plugins.dll')) { throw 'Unexpected Debug artifact in fresh Release clone.' }
    $null = Invoke-Gate '02-plugin-current-config-without-debug' 'dotnet' @('test','SpeechMessage.Hosting.Tests','-c','Release','--no-build','--filter','FullyQualifiedName~BuiltPlugin_HasNoForbiddenAssemblyReferences','--logger','trx','--results-directory',(Join-Path $evidence 'plugin-only-trx'))
    $null = Invoke-Gate '03-plugin-debug-build' 'dotnet' @('build','SpeechMessage.Dynamics.Plugins','-c','Debug')
    $null = Invoke-Gate '04-solution-tests' 'dotnet' @('test','SpeechMessageProducts.sln','-c','Release','--no-build','--logger','trx','--results-directory',(Join-Path $evidence 'solution-trx'))
    $null = Invoke-Gate '05-debug-tracer' 'dotnet' @('test','ToolUtility.Dataverse.Tests','-c','Debug','--filter','FullyQualifiedName~FileToolUtilityTracerTests','--logger','trx','--results-directory',(Join-Path $evidence 'tracer-trx'))
    $null = Invoke-Gate '06-encoding' 'powershell' @('-NoProfile','-File','tools/RepoQuality/Test-TextEncoding.ps1','-Scope','Changed','-Base','62b409c8')
    $null = Invoke-Gate '07-duplicates' 'powershell' @('-NoProfile','-File','tools/RepoQuality/Find-DuplicateCode.ps1','-Baseline','tools/RepoQuality/duplicate-baseline.json')
    $null = Invoke-Gate '08-i18n' 'dotnet' @('run','--project','tools/I18nCheck','--','verify')
    $null = Invoke-Gate '09-file-docs' 'python' @('tools/RepoQuality/check_file_documentation.py')
    $null = Invoke-Gate '10-progress-legacy' 'node' @('--test','tools/RepoQuality/Tests/Test-ProgressPage.cjs')
    $null = Invoke-Gate '11-progress34' 'node' @('--test','tools/RepoQuality/Tests/Test-ProgressPage34.cjs')
} finally { Pop-Location }
$failed = @($results | Where-Object { $_.exitCode -ne 0 })
Write-Output ('Clean clone: '+$commit.Substring(0,8)+'; gates='+$results.Count+'; failed='+$failed.Count)
if ($failed.Count -gt 0) { exit 1 }