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
