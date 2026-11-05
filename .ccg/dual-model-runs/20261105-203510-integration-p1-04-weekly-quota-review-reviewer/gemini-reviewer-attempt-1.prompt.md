ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-04-weekly-quota-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀reviewer，僅審這次因P1附件審查而發現的CCG週額度分類修復，不探索其他檔案/設定或憑證。原202614 reviewer只有Gemini成功；健康檢查兩次明確診斷You've hit your weekly limit，resets Nov 7,4pm Asia/Taipei。regex未認weekly，造成quotaBlocked=false。僅兩行改(session|weekly)保持既有分類，7例AST離線測試先6/7紅→7/7綠；既有summary6/6綠。修後203320兩模型實際嘗試，Gemini成功Claude被weekly quota擋；summary quotaBlocked=true,degradedFallback=true,dualModelComplete=false,ok=false，出口0僅代表授權fallback，不是雙模型。不是繞過或擴額。
請審正確分類、避免誤判普通weekly report/任意exit1、是否削弱錯誤守門或洩漏機密；若沒有可重現缺陷請明確無Critical/High/Warning。

```diff
diff --git a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1 b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
index dc59ced0..c88e9913 100644
--- a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
+++ b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
@@ -332,7 +332,7 @@ OUTPUT:
 
 function Test-QuotaBlockedText {
     param([string]$Text)
-    return ($Text -match "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)")
+    return ($Text -match "(?i)(you'?ve hit your (session|weekly) limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)")
 }
 
 function Test-BackendQuotaBlocked {
diff --git a/docs/scripts/Test-CcgDualModelHealth.ps1 b/docs/scripts/Test-CcgDualModelHealth.ps1
index aa2ebcf3..b8a705f7 100644
--- a/docs/scripts/Test-CcgDualModelHealth.ps1
+++ b/docs/scripts/Test-CcgDualModelHealth.ps1
@@ -206,7 +206,7 @@ OUTPUT: one line only
         -WorkingDirectory $RepositoryPath
 
     $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
-    $quotaBlockedPattern = "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)"
+    $quotaBlockedPattern = "(?i)(you'?ve hit your (session|weekly) limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)"
     $errorCombined = (($result.StdErr) -replace "`r", "")
     $quotaBlocked = $errorCombined -match $quotaBlockedPattern
     $ok = ($result.ExitCode -eq 0 -and $combined -match [regex]::Escape($ExpectedText))

```

## 離線測試
```powershell
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

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.