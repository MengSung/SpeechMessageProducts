<#
直接執行 workflow 的授權準備與清理區塊，以合成文字驗證內容完整性、缺 secret 失敗、
發布路徑失敗時回收、下游故障後清理，以及不刪除借用檔案。測試不讀真實授權檔，
各案例只在唯一暫存目錄內建立假檔；finally 恢復程序環境並逐檔清理，不修改使用者設定。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workflowPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.github/workflows/solution-ci.yml'))
$workflow = [IO.File]::ReadAllText($workflowPath)
$utf8 = New-Object Text.UTF8Encoding($false)
$fakeLicense = "synthetic-license-only`nline-two ::warning:: not-a-command`n"
$failures = 0
$checks = 0

# 只擷取明確命名步驟的 run 區塊；缺步驟時回傳空動作，讓契約斷言產生功能紅燈。
function Get-WorkflowBlock([string]$Name) {
    $lines = $workflow -split '\r?\n'
    $step = [Array]::IndexOf($lines, ('      - name: ' + $Name))
    if ($step -lt 0) { return [scriptblock]::Create('') }
    $index = $step + 1
    while ($index -lt $lines.Length -and $lines[$index] -ne '        run: |') {
        if ($lines[$index].StartsWith('      - name: ', [StringComparison]::Ordinal)) { return [scriptblock]::Create('') }
        $index++
    }
    $body = New-Object 'Collections.Generic.List[string]'
    for ($index++; $index -lt $lines.Length; $index++) {
        if ($lines[$index].Length -eq 0) { $body.Add(''); continue }
        if (-not $lines[$index].StartsWith('          ', [StringComparison]::Ordinal)) { break }
        $body.Add($lines[$index].Substring(10))
    }
    return [scriptblock]::Create(($body -join "`n"))
}

# 失敗只印固定案例名稱，從不輸出捕捉到的例外、secret 或 GITHUB_ENV 內容。
function Assert-Contract([bool]$Condition, [string]$Name) {
    $script:checks++
    if ($Condition) { Write-Output ('PASS ' + $Name) }
    else { $script:failures++; Write-Output ('FAIL ' + $Name) }
}

$prepare = Get-WorkflowBlock 'Prepare Six Labors license'
$cleanup = Get-WorkflowBlock 'Delete Six Labors license'
$keys = @('RUNNER_TEMP', 'GITHUB_ENV', 'SIXLABORS_LICENSE', 'SixLaborsLicenseFile')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempRoot ('speechmessage-license-test-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
try {
    $env:RUNNER_TEMP = $fixtureRoot
    $env:GITHUB_ENV = Join-Path $fixtureRoot 'github-env.txt'
    $env:SixLaborsLicenseFile = $null
    $env:SIXLABORS_LICENSE = $fakeLicense
    $output = @(& $prepare *>&1)
    $files = @(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File)
    Assert-Contract ($files.Count -eq 1) 'Configured_CreatesExactlyOneTemporaryFile'
    $published = if ([IO.File]::Exists($env:GITHUB_ENV)) { [IO.File]::ReadAllText($env:GITHUB_ENV) } else { '' }
    if ($files.Count -eq 1) {
        $bytes = [IO.File]::ReadAllBytes($files[0].FullName)
        Assert-Contract ([Text.Encoding]::UTF8.GetString($bytes) -ceq $fakeLicense) 'Configured_PreservesCompleteSyntheticContent'
        Assert-Contract (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) 'Configured_Utf8WithoutBom'
        Assert-Contract ($published.TrimEnd() -ceq ('SixLaborsLicenseFile=' + $files[0].FullName)) 'Configured_PublishesOnlyTemporaryPath'
        $env:SixLaborsLicenseFile = $files[0].FullName
    } else {
        Assert-Contract $false 'Configured_PreservesCompleteSyntheticContent'
        Assert-Contract $false 'Configured_Utf8WithoutBom'
        Assert-Contract $false 'Configured_PublishesOnlyTemporaryPath'
    }
    $visible = ($output -join "`n") + $published
    Assert-Contract (-not $visible.Contains('synthetic-license-only') -and -not $visible.Contains('line-two')) 'Configured_NeverPrintsLicense'
    Assert-Contract ([string]::IsNullOrEmpty($env:SIXLABORS_LICENSE)) 'Configured_ClearsSecretFromPreparationProcess'
    # 模擬下一步建置故障後，runner 仍會執行 always 清理；清理不依賴建置成功旗標。
    $output = @(& $cleanup *>&1)
    Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'DownstreamFailure_CleanupDeletesLicense'
    $null = & $cleanup
    Assert-Contract ([string]::IsNullOrEmpty($env:SixLaborsLicenseFile)) 'RepeatedCleanup_IsSafeAndClearsProcessPath'

    foreach ($secret in @($null, '   ')) {
        $env:SIXLABORS_LICENSE = $secret
        $caught = $null
        try { $output = @(& $prepare *>&1) } catch { $caught = $_.Exception }
        Assert-Contract ($null -ne $caught -and $caught.Message.Contains('SIXLABORS_LICENSE is required')) 'MissingOrWhitespaceSecret_FailsWithClearMessage'
        Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'MissingSecret_DoesNotCreateLicense'
    }

    # GITHUB_ENV 指向目錄，迫使「已寫檔但發布路徑失敗」；部分成功也不得留下授權檔。
    $env:SIXLABORS_LICENSE = $fakeLicense
    $env:GITHUB_ENV = $fixtureRoot
    $caught = $null
    try { $output = @(& $prepare *>&1) } catch { $caught = $_.Exception }
    Assert-Contract ($null -ne $caught -and $caught.Message.Contains('license preparation failed')) 'PublishFailure_FailsWithFixedMessage'
    Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'PublishFailure_RemovesPartiallyPreparedLicense'
    Assert-Contract ([string]::IsNullOrEmpty($env:SIXLABORS_LICENSE)) 'PublishFailure_ClearsSecret'
    $visible = ($output -join "`n") + $(if ($null -ne $caught) { $caught.Message } else { '' })
    Assert-Contract (-not $visible.Contains('synthetic-license-only') -and -not $visible.Contains('line-two')) 'PublishFailure_NeverPrintsLicense'

    # 不屬於此 workflow 的 runner 檔案不可被清理；不把任意環境路徑當成刪除權限。
    $borrowed = Join-Path $fixtureRoot 'unrelated.lic'
    [IO.File]::WriteAllText($borrowed, 'synthetic-borrowed', $utf8)
    $env:SixLaborsLicenseFile = $borrowed
    $caught = $null
    try { $output = @(& $cleanup *>&1) } catch { $caught = $_.Exception }
    Assert-Contract ($null -ne $caught -and [IO.File]::Exists($borrowed)) 'Cleanup_RejectsUnownedRunnerFile'
    $env:RUNNER_TEMP = Join-Path $fixtureRoot 'different-runner'
    $env:SixLaborsLicenseFile = $borrowed
    $caught = $null
    try { $output = @(& $cleanup *>&1) } catch { $caught = $_.Exception }
    Assert-Contract ($null -ne $caught -and [IO.File]::Exists($borrowed)) 'Cleanup_RejectsPathOutsideRunner'

    $prepareIndex = $workflow.IndexOf('      - name: Prepare Six Labors license', [StringComparison]::Ordinal)
    $buildIndex = $workflow.IndexOf('      - name: Build without signing key', [StringComparison]::Ordinal)
    Assert-Contract ($prepareIndex -ge 0 -and $prepareIndex -lt $buildIndex) 'Workflow_PreparesLicenseBeforeBuild'
    Assert-Contract ($workflow.Contains('SIXLABORS_LICENSE: ${{ secrets.SIXLABORS_LICENSE }}')) 'Workflow_SecretUsesStepEnvironment'
    Assert-Contract ([regex]::IsMatch($workflow, '(?m)^      - name: Delete Six Labors license\r?\n        if: always\(\)')) 'Workflow_CleanupAlwaysRuns'
    Assert-Contract (-not $prepare.ToString().Contains('${{')) 'Workflow_DoesNotInterpolateSecretIntoScript'
    Assert-Contract ($workflow.Contains('powershell -NoProfile -File tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1')) 'Workflow_RunsLicenseRegressionTests'
} finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $boundary = $tempRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('speechmessage-license-test-', [StringComparison]::Ordinal)) { throw 'Unsafe fixture cleanup.' }
    foreach ($file in @(Get-ChildItem -LiteralPath $resolved -File)) { Remove-Item -LiteralPath $file.FullName -Force }
    [IO.Directory]::Delete($resolved, $false)
}
Write-Output ('Six Labors CI license tests: total=' + $checks + ' failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
