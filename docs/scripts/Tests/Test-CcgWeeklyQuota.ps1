<#
檔案整體說明

目的與定位：以合成供應商回應驗證Claude週額度與相關quota分類，避免外部額度被當成本機PATH或環境故障反覆修復。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
param(
    [string]$RunnerPath = (Join-Path $PSScriptRoot '..\Invoke-CcgDualModelWithSelfHealing.ps1'),
    [string]$HealthPath = (Join-Path $PSScriptRoot '..\Test-CcgDualModelHealth.ps1')
)

$ErrorActionPreference = 'Stop'
# 只擷取函式與正規式，不啟動模型、讀取憑證、改 PATH 或執行主流程。
$runnerTokens = $null
$runnerErrors = $null
$runner = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $RunnerPath).Path, [ref]$runnerTokens, [ref]$runnerErrors)
if ($runnerErrors.Count -gt 0) { throw 'Runner parsing failed.' }
$definition = $runner.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-QuotaBlockedText'
}, $false)
if (-not $definition) { throw 'Quota function missing.' }
. ([scriptblock]::Create($definition.Extent.Text))

$healthTokens = $null
$healthErrors = $null
$health = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $HealthPath).Path, [ref]$healthTokens, [ref]$healthErrors)
if ($healthErrors.Count -gt 0) { throw 'Health parsing failed.' }
$assignment = $health.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$quotaBlockedPattern'
}, $true)
if (-not $assignment) { throw 'Health quota pattern missing.' }
. ([scriptblock]::Create($assignment.Extent.Text))

$cases = @(
    @{ Text="You've hit your weekly limit"; Expected=$true },
    @{ Text="You've hit your session limit"; Expected=$true },
    @{ Text='HTTP 429'; Expected=$true },
    @{ Text='insufficient_quota'; Expected=$true },
    @{ Text='claude exited with status 1'; Expected=$false },
    @{ Text='A weekly report has a row limit.'; Expected=$false },
    @{ Text='All checks completed.'; Expected=$false }
)
$failed = 0
foreach ($case in $cases) {
    $runnerActual = Test-QuotaBlockedText -Text $case.Text
    $healthActual = $case.Text -match $quotaBlockedPattern
    if ($runnerActual -ne $case.Expected -or $healthActual -ne $case.Expected) {
        $failed++
        Write-Output ('FAIL quota fixture: ' + $case.Text)
    } else { Write-Output ('PASS quota fixture: ' + $case.Text) }
}
Write-Output ('Quota tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
