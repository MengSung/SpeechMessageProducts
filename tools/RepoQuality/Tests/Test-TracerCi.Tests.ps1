<#
檔案整體說明

目的與定位：執行真實 workflow 的 tracer 區塊，以合成 TRX 證明零測試不得假綠燈。

主要功能與流程：
操作入口包含dotnet；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
執行真實 workflow 的 tracer 區塊，以合成 TRX 證明零測試不得假綠燈。
.DESCRIPTION
只取代 dotnet 測試執行端，讓它回傳成功並產生指定假 TRX；真正的 CI 區塊與 TRX 守門照常執行。
各案例擁有自己的暫存工作樹，沒有產品、網路或憑證；所有固定路徑均限制在自有根內再清理。
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/solution-ci.yml'))
$match = [regex]::Match($workflow, '(?ms)^      - name: Debug tracer contracts\r?\n.*?        run: \|\r?\n(?<run>.*?)(?=^      - name:|\z)')
if (-not $match.Success) { throw 'The actual tracer workflow block is missing.' }
$block = [scriptblock]::Create(($match.Groups['run'].Value -replace '(?m)^          ', ''))
$base = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-tracer-ci-' + [Guid]::NewGuid().ToString('N'))))
$originalLocation = (Get-Location).Path
$utf8 = New-Object Text.UTF8Encoding($false)

function dotnet {
    <# 假測試端固定 exit=0，真正的拒絕必須由實際 CI 區塊讀取 TRX 得出，不能靠程序失敗。 #>
    $destination = Join-Path (Get-Location).Path 'artifacts/ci/debug-tracer/tracer.trx'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    [IO.File]::WriteAllText($destination, $script:syntheticTrx, $script:utf8)
    $global:LASTEXITCODE = 0
}

$cases = @(
    @{ Name='SevenExecuted_Passes'; Executed=7; Failed=0; Results=7; Expected=$true },
    @{ Name='ZeroExecuted_Fails'; Executed=0; Failed=0; Results=0; Expected=$false },
    @{ Name='FailedExecution_Fails'; Executed=7; Failed=1; Results=7; Expected=$false },
    @{ Name='CountersWithoutResults_Fails'; Executed=7; Failed=0; Results=0; Expected=$false },
    @{ Name='MalformedTrx_Fails'; Raw='<broken'; Expected=$false },
    @{ Name='ExternalEntity_Fails'; Raw='<!DOCTYPE TestRun [<!ENTITY x SYSTEM "file:///synthetic-never-read">]><TestRun>&x;</TestRun>'; Expected=$false }
)
$failed = 0
try {
    foreach ($case in $cases) {
        $fixture = Join-Path $base ([Guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'tools/RepoQuality'))
        $guardSource = Join-Path $root 'tools/RepoQuality/Test-TrxExecution.ps1'
        if (Test-Path -LiteralPath $guardSource) {
            Copy-Item -LiteralPath $guardSource -Destination (Join-Path $fixture 'tools/RepoQuality/Test-TrxExecution.ps1')
        }
        if ($case.ContainsKey('Raw')) { $script:syntheticTrx = $case.Raw }
        else {
            $results = ''
            for ($index = 0; $index -lt $case.Results; $index++) {
                $outcome = 'Passed'
                if ($index -lt $case.Failed) { $outcome = 'Failed' }
                $results += '<UnitTestResult outcome="' + $outcome + '" />'
            }
            $script:syntheticTrx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>' + $results + '</Results><ResultSummary><Counters total="' + $case.Executed + '" executed="' + $case.Executed + '" passed="' + ($case.Executed - $case.Failed) + '" failed="' + $case.Failed + '" /></ResultSummary></TestRun>'
        }
        Set-Location -LiteralPath $fixture
        $passed = $true
        try { $null = & $block 2>&1 } catch { $passed = $false }
        if ($passed -eq $case.Expected) { Write-Output ('PASS ' + $case.Name) }
        else { $failed++; Write-Output ('FAIL ' + $case.Name) }
    }
} finally {
    Set-Location -LiteralPath $originalLocation
    $actual = [IO.Path]::GetFullPath($base)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $actual.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($actual) -notlike 'speechmessage-tracer-ci-*') { throw 'Unsafe tracer fixture cleanup path.' }
    if (Test-Path -LiteralPath $actual) { Remove-Item -LiteralPath $actual -Recurse -Force }
}
Write-Output ('Tracer CI tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
exit 0
