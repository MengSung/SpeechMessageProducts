param(
    [switch]$AllowOpenConvergenceSteps,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path ".").Path
$diagnosticsRoot = Join-Path $repositoryRoot "docs/project-modular-diagnostics"
$taskRoot = Join-Path $repositoryRoot ".trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization"
$schemaAudit = Join-Path $taskRoot "research/Test-DiagnosticIssueSchema.ps1"
$requiredRelativeFiles = @(
    "issue.md",
    "review-log.md",
    "evidence/scope-manifest.md",
    "evidence/security-analysis.md",
    "evidence/performance-analysis.md",
    "evidence/extraction-analysis.md",
    "evidence/runtime-validation-plan.md"
)
$allowedStatuses = @(
    "APPROVED",
    "APPROVED_DEGRADED",
    "NO_ACTION_REQUIRED",
    "DEGRADED_REVIEW_PENDING",
    "RUNTIME_VALIDATION_PENDING",
    "HUMAN_DECISION_REQUIRED"
)
$allowedChangedPathPatterns = @(
    '^docs/project-modular-diagnostics/',
    '^\.ccg/dual-model-runs/',
    '^\.ccg/tasks/(?:project-modular-analysis-diagnosis-optimization|diagnostic-convergence-first-wave-prioritization|archive/2026-07/line-richmenu-word-manual)/',
    '^\.ccg/tasks/line-richmenu-word-manual/',
    '^\.trellis/tasks/07-(?:10|13)-'
)
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [string]$Name,
        [bool]$Pass,
        [string]$Evidence
    )
    $checks.Add([pscustomobject]@{
        Name = $Name
        Pass = $Pass
        Evidence = $Evidence
    })
}

$workspaceRows = @()
$directories = @(Get-ChildItem -LiteralPath $diagnosticsRoot -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName "issue.md")
} | Sort-Object Name)
Add-Check -Name "workspace-count" -Pass ($directories.Count -eq 35) -Evidence "actual=$($directories.Count) expected=35"

$missingPackages = @()
foreach ($directory in $directories) {
    foreach ($relativePath in $requiredRelativeFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName $relativePath) -PathType Leaf)) {
            $missingPackages += "$($directory.Name)/$relativePath"
        }
    }
    $issueText = [IO.File]::ReadAllText((Join-Path $directory.FullName "issue.md"))
    $workspaceRows += [pscustomobject]@{
        Module = [regex]::Match($issueText, "(?m)^Module:\s*([^\r\n]+)$").Groups[1].Value.Trim()
        Workspace = $directory.Name
        Status = [regex]::Match($issueText, "(?m)^Status:\s*([^\r\n]+)$").Groups[1].Value.Trim()
        Gate = [regex]::Match($issueText, "(?m)^Gate status:\s*([^\r\n]+)$").Groups[1].Value.Trim()
        Hash = [regex]::Match($issueText, "(?m)^Issue document SHA-256:\s*([0-9a-f]{64})$").Groups[1].Value
    }
}
Add-Check -Name "seven-file-packages" -Pass ($missingPackages.Count -eq 0) -Evidence "present=$($directories.Count * 7 - $missingPackages.Count)/245 missing=$($missingPackages -join ',')"

$headerErrors = @()
foreach ($directory in $directories) {
    $issueText = [IO.File]::ReadAllText((Join-Path $directory.FullName "issue.md"))
    foreach ($name in @("Status", "Module", "Workspace", "Map source", "Mode", "Gate status", "Issue document SHA-256")) {
        $count = [regex]::Matches($issueText, "(?m)^$([regex]::Escape($name)):\s*(.*)$").Count
        if ($count -ne 1) { $headerErrors += "$($directory.Name):$name=$count" }
    }
}
Add-Check -Name "metadata-uniqueness" -Pass ($headerErrors.Count -eq 0) -Evidence "valid=$($directories.Count - $headerErrors.Count)/35 errors=$($headerErrors -join ',')"

$sha = [Security.Cryptography.SHA256]::Create()
$hashErrors = @()
try {
    foreach ($directory in $directories) {
        $issuePath = Join-Path $directory.FullName "issue.md"
        $text = [IO.File]::ReadAllText($issuePath).Replace("`r`n", "`n").Replace("`r", "`n")
        $stored = [regex]::Match($text, "(?m)^Issue document SHA-256:\s*([0-9a-f]{64})$").Groups[1].Value
        $canonical = [regex]::Replace($text, "(?m)^Issue document SHA-256:.*$", "Issue document SHA-256:", 1)
        $actual = [BitConverter]::ToString(
            $sha.ComputeHash([Text.UTF8Encoding]::new($false).GetBytes($canonical))
        ).Replace("-", "").ToLowerInvariant()
        if ($stored -ne $actual) { $hashErrors += "$($directory.Name):$stored!=$actual" }
    }
}
finally {
    $sha.Dispose()
}
Add-Check -Name "canonical-hashes" -Pass ($hashErrors.Count -eq 0) -Evidence "valid=$($directories.Count - $hashErrors.Count)/35 errors=$($hashErrors -join ',')"

$schemaOutput = @(& $schemaAudit)
$schemaSummary = @($schemaOutput | Where-Object { $_ -match '^SUMMARY ' })[-1]
Add-Check -Name "per-issue-schema" -Pass ($schemaSummary -eq "SUMMARY workspaces=35 passed=35 failed=0") -Evidence $schemaSummary

$invalidStatuses = @($workspaceRows | Where-Object Status -NotIn $allowedStatuses)
Add-Check -Name "status-vocabulary" -Pass ($invalidStatuses.Count -eq 0) -Evidence (($workspaceRows | Group-Object Status | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join '; ')

$step2FrozenPath = Join-Path $taskRoot "research/step2-frozen-inputs.csv"
$step2DispositionPath = Join-Path $taskRoot "research/step2-blocked-dispositions.csv"
$step2Errors = @()
$step2DirectCount = 0
$step2DeferredCount = 0
if (-not (Test-Path -LiteralPath $step2FrozenPath) -or
    -not (Test-Path -LiteralPath $step2DispositionPath)) {
    $step2Errors += "missing frozen input or disposition CSV"
}
else {
    $step2Frozen = @(Import-Csv -LiteralPath $step2FrozenPath)
    $step2Dispositions = @(Import-Csv -LiteralPath $step2DispositionPath)
    $step2DispositionMap = @{}
    foreach ($row in $step2Dispositions) { $step2DispositionMap[$row.Module] = $row }
    if ($step2Frozen.Count -ne 17 -or $step2Dispositions.Count -ne 17) {
        $step2Errors += "row-count:frozen=$($step2Frozen.Count),dispositions=$($step2Dispositions.Count)"
    }
    foreach ($item in $step2Frozen) {
        if (-not $step2DispositionMap.ContainsKey($item.Module)) {
            $step2Errors += "$($item.Module):missing-disposition"
            continue
        }
        $row = $step2DispositionMap[$item.Module]
        $workspaceRow = $workspaceRows | Where-Object Module -eq $item.Module
        $reviewLogPath = Join-Path (Join-Path $diagnosticsRoot $item.Folder) "review-log.md"
        $reviewLog = [IO.File]::ReadAllText($reviewLogPath)
        $expectedDisposition = if ($item.Module -eq "B02") {
            $step2DirectCount++
            "PROVIDER_BLOCKED_NO_USABLE_BACKEND"
        }
        else {
            $step2DeferredCount++
            "PROVIDER_BLOCKED_RETRY_DEFERRED"
        }
        if ($workspaceRow.Status -ne "DEGRADED_REVIEW_PENDING") {
            $step2Errors += "$($item.Module):status=$($workspaceRow.Status)"
        }
        if ($workspaceRow.Hash -ne $item.Hash -or $row.FrozenHash -ne $item.Hash) {
            $step2Errors += "$($item.Module):hash-drift"
        }
        if ($row.Disposition -ne $expectedDisposition -or $row.OptimizationEligible -ne "False") {
            $step2Errors += "$($item.Module):disposition=$($row.Disposition),eligible=$($row.OptimizationEligible)"
        }
        foreach ($requiredText in @(
            "## Step 2 Convergence Disposition - 2026-07-13",
            $item.Hash,
            $row.Prompt,
            $expectedDisposition,
            "optimization admission"
        )) {
            if (-not $reviewLog.Contains($requiredText)) {
                $step2Errors += "$($item.Module):missing=$requiredText"
            }
        }
        if ($reviewLog -match '\$\(|\$promptRelative|\$disposition') {
            $step2Errors += "$($item.Module):unexpanded-template-token"
        }
    }
}
Add-Check -Name "step2-pending-review-dispositions" -Pass ($step2Errors.Count -eq 0) -Evidence "modules=17 direct=$step2DirectCount deferred=$step2DeferredCount eligible=0 errors=$($step2Errors -join ';')"

$runtimeExpected = @("B06A", "B06B", "B06C", "X05Q")
$runtimeErrors = @($workspaceRows | Where-Object { $_.Module -in $runtimeExpected -and $_.Status -ne "RUNTIME_VALIDATION_PENDING" })
Add-Check -Name "runtime-dispositions" -Pass ($runtimeErrors.Count -eq 0) -Evidence "expected=B06A,B06B,B06C,X05Q errors=$($runtimeErrors.Module -join ',')"

$topologyModules = @("B04A", "B04C", "X04A", "X04B", "X05Q")
$topologyErrors = @()
foreach ($module in $topologyModules) {
    $row = $workspaceRows | Where-Object Module -eq $module
    $reviewLog = [IO.File]::ReadAllText((Join-Path (Join-Path $diagnosticsRoot $row.Workspace) "review-log.md"))
    if ($reviewLog -notmatch "RECOVERY_EXCEPTION_ACCEPTED" -or
        $reviewLog -notmatch "NO_OVERLAP" -or
        $reviewLog -notmatch "Nested (?:child sessions|agent count).*0") {
        $topologyErrors += $module
    }
}
Add-Check -Name "worker-topology" -Pass ($topologyErrors.Count -eq 0) -Evidence "checked=5 errors=$($topologyErrors -join ',')"

$f01ComparisonPath = Join-Path $taskRoot "research/f01a-write-scope-recovery-r1-disposition.json"
$f01Pass = $false
$f01Evidence = "disposition missing"
if (Test-Path -LiteralPath $f01ComparisonPath) {
    $f01Comparison = Get-Content -Raw -Encoding utf8 $f01ComparisonPath | ConvertFrom-Json
    $f01Pass = [bool]$f01Comparison.recoveryWriteScopeClean -and
        $f01Comparison.unexpectedDeltaCountAfterApprovedBoundary -eq 0 -and
        $f01Comparison.productDeltaCount -eq 0 -and
        $f01Comparison.finalDiagnosticStatus -eq "HUMAN_DECISION_REQUIRED" -and
        -not [bool]$f01Comparison.optimizationEligible
    $f01Evidence = "run=$($f01Comparison.runId) clean=$($f01Comparison.recoveryWriteScopeClean) rawUnexpected=$($f01Comparison.rawUnexpectedDeltaCount) finalUnexpected=$($f01Comparison.unexpectedDeltaCountAfterApprovedBoundary) product=$($f01Comparison.productDeltaCount) status=$($f01Comparison.finalDiagnosticStatus)"
}
if ($AllowOpenConvergenceSteps -and -not (Test-Path -LiteralPath $f01ComparisonPath)) {
    $f01Pass = $true
    $f01Evidence = "OPEN: disposition not run"
}
Add-Check -Name "f01a-recovery-write-scope" -Pass $f01Pass -Evidence $f01Evidence

$changedPaths = @(& git status --porcelain=v1 --untracked-files=all | ForEach-Object { $_.Substring(3).Replace("\", "/") })
$unexpectedChangedPaths = @($changedPaths | Where-Object {
    $path = $_
    -not ($allowedChangedPathPatterns | Where-Object { $path -match $_ })
})
Add-Check -Name "no-product-changes" -Pass ($unexpectedChangedPaths.Count -eq 0) -Evidence "changed=$($changedPaths.Count) unexpected=$($unexpectedChangedPaths -join ',')"

$ledgerPath = Join-Path $repositoryRoot ".trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/diagnostic-run-ledger.md"
$ledger = [IO.File]::ReadAllText($ledgerPath)
$ledgerRows = [regex]::Matches($ledger, '(?m)^\|\s*\d+\s*\|\s*([^|]+)\|\s*`([^`]+)`\s*\|\s*([^|]+)\|')
$ledgerMap = @{}
foreach ($match in $ledgerRows) {
    $ledgerMap[$match.Groups[1].Value.Trim()] = $match.Groups[3].Value.Trim()
}
$ledgerErrors = @()
foreach ($row in $workspaceRows) {
    if (-not $ledgerMap.ContainsKey($row.Module) -or $ledgerMap[$row.Module] -ne $row.Status) {
        $ledgerErrors += "$($row.Module):issue=$($row.Status),ledger=$($ledgerMap[$row.Module])"
    }
}
$ledgerPass = $ledgerErrors.Count -eq 0
if ($AllowOpenConvergenceSteps -and -not $ledgerPass) {
    $ledgerPass = $true
    $ledgerErrors = @("OPEN: ledger synchronization deferred to Step 6")
}
Add-Check -Name "ledger-status-consistency" -Pass $ledgerPass -Evidence "rows=$($ledgerRows.Count) errors=$($ledgerErrors -join ';')"

$currentStatePath = Join-Path $diagnosticsRoot "diagnostic-run-current-state.md"
$currentStateText = [IO.File]::ReadAllText($currentStatePath)
$currentStateErrors = @()
foreach ($group in ($workspaceRows | Group-Object Status)) {
    $expectedLine = "- $($group.Name): $($group.Count)"
    if (-not $currentStateText.Contains($expectedLine)) {
        $currentStateErrors += "missing:$expectedLine"
    }
}
if ($currentStateText -match '(?m)^- INVALID_WRITE_SCOPE:\s*\d+') {
    $currentStateErrors += "stale INVALID_WRITE_SCOPE count"
}
Add-Check -Name "current-state-count-consistency" -Pass ($currentStateErrors.Count -eq 0) -Evidence "total=$($workspaceRows.Count) errors=$($currentStateErrors -join ';')"

$governanceErrors = @()
$trellisChild = Get-Content -Raw -Encoding utf8 (Join-Path $taskRoot "task.json") | ConvertFrom-Json
$trellisParentPath = Join-Path $repositoryRoot ".trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/task.json"
$trellisParent = Get-Content -Raw -Encoding utf8 $trellisParentPath | ConvertFrom-Json
$ccgChildPath = Join-Path $repositoryRoot ".ccg/tasks/diagnostic-convergence-first-wave-prioritization/task.json"
$ccgParentPath = Join-Path $repositoryRoot ".ccg/tasks/project-modular-analysis-diagnosis-optimization/task.json"
$ccgChild = Get-Content -Raw -Encoding utf8 $ccgChildPath | ConvertFrom-Json
$ccgParent = Get-Content -Raw -Encoding utf8 $ccgParentPath | ConvertFrom-Json
$progressText = [IO.File]::ReadAllText((Join-Path $taskRoot "progress.md"))
$richMenuArchivePath = Join-Path $repositoryRoot ".ccg/tasks/archive/2026-07/line-richmenu-word-manual/task.json"
$richMenuActivePath = Join-Path $repositoryRoot ".ccg/tasks/line-richmenu-word-manual"
$richMenuTask = if (Test-Path -LiteralPath $richMenuArchivePath) {
    Get-Content -Raw -Encoding utf8 $richMenuArchivePath | ConvertFrom-Json
}
else {
    $null
}
if ($trellisChild.status -ne "in_progress") { $governanceErrors += "trellis-child=$($trellisChild.status)" }
if ($trellisParent.status -ne "in_progress") { $governanceErrors += "trellis-parent=$($trellisParent.status)" }
if ($ccgChild.status -ne "in_progress" -or $ccgChild.currentPhase -ne "planning" -or $ccgChild.nextAction -notmatch "owner") {
    $governanceErrors += "ccg-child=$($ccgChild.status)/$($ccgChild.currentPhase)"
}
if ($ccgParent.status -ne "in_progress" -or $ccgParent.currentPhase -ne "planning" -or $ccgParent.nextAction -notmatch "owner") {
    $governanceErrors += "ccg-parent=$($ccgParent.status)/$($ccgParent.currentPhase)"
}
if ($progressText -notmatch '\| 6\. Final compliance audit \| COMPLETED \|' -or
    $progressText -notmatch '\| 7\. Optimization map \| NOT_STARTED_OWNER_GATE \|') {
    $governanceErrors += "progress-step-gate"
}
if ($null -eq $richMenuTask -or $richMenuTask.status -ne "cancelled" -or $richMenuTask.currentPhase -ne "closed") {
    $governanceErrors += "richmenu-not-closed"
}
if (Test-Path -LiteralPath $richMenuActivePath) {
    $governanceErrors += "richmenu-active-directory-present"
}
Add-Check -Name "governance-task-consistency" -Pass ($governanceErrors.Count -eq 0) -Evidence "trellis=in_progress ccg=planning richmenu=cancelled/closed step7=owner-gate errors=$($governanceErrors -join ';')"

$failed = @($checks | Where-Object { -not $_.Pass })
$result = [ordered]@{
    checkedAt = (Get-Date).ToString("o")
    pass = $failed.Count -eq 0
    checkCount = $checks.Count
    failedCount = $failed.Count
    checks = @($checks)
}
$resultJson = $result | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath
    }
    else {
        Join-Path $repositoryRoot $OutputPath
    }
    [IO.File]::WriteAllText(
        $resolvedOutputPath,
        ($resultJson + "`n"),
        [Text.UTF8Encoding]::new($false)
    )
}
$resultJson
if ($failed.Count -gt 0) { exit 1 }
