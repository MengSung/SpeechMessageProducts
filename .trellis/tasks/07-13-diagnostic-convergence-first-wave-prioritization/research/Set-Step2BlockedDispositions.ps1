param()

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path ".").Path
$researchRoot = $PSScriptRoot
$frozenPath = Join-Path $researchRoot "step2-frozen-inputs.csv"
$runResultsPath = Join-Path $researchRoot "step2-run-results.jsonl"
$outputPath = Join-Path $researchRoot "step2-blocked-dispositions.csv"
$heading = "## Step 2 Convergence Disposition - 2026-07-13"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$tick = [char]96

if (-not (Test-Path -LiteralPath $frozenPath)) {
    throw "Missing frozen inputs: $frozenPath"
}
if (-not (Test-Path -LiteralPath $runResultsPath)) {
    throw "Missing Step 2 run results: $runResultsPath"
}

$frozen = @(Import-Csv -LiteralPath $frozenPath)
$runResults = @(Get-Content -LiteralPath $runResultsPath -Encoding utf8 |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_ | ConvertFrom-Json })
if ($frozen.Count -ne 17) {
    throw "Expected 17 frozen modules, found $($frozen.Count)"
}
if ($runResults.Count -ne 1 -or $runResults[0].Module -ne "B02") {
    throw "Expected the controlled retry queue to stop after the B02 probe"
}

$probe = $runResults[0]
if (-not $probe.QuotaBlocked -or
    -not [string]::IsNullOrWhiteSpace([string]$probe.CompletedBackends)) {
    throw "B02 did not record the expected no-usable-backend provider block"
}
$summaryPath = Join-Path $repositoryRoot $probe.SummaryPath
if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw "Missing B02 provider-block summary: $summaryPath"
}
$summary = Get-Content -Raw -Encoding utf8 $summaryPath | ConvertFrom-Json
if (@($summary.completedBackends).Count -ne 0) {
    throw "B02 summary now contains a completed backend; blocked integration is invalid"
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($item in $frozen) {
    $workspacePath = Join-Path $repositoryRoot "docs/project-modular-diagnostics/$($item.Folder)"
    $issuePath = Join-Path $workspacePath "issue.md"
    $reviewLogPath = Join-Path $workspacePath "review-log.md"
    $issueText = [IO.File]::ReadAllText($issuePath)
    $reviewLog = [IO.File]::ReadAllText($reviewLogPath).Replace("`r`n", "`n").Replace("`r", "`n")
    $storedHash = [regex]::Match(
        $issueText,
        "(?m)^Issue document SHA-256:\s*([0-9a-f]{64})$"
    ).Groups[1].Value
    $status = [regex]::Match($issueText, "(?m)^Status:\s*([^\r\n]+)$").Groups[1].Value.Trim()

    if ($storedHash -ne $item.Hash) {
        throw "$($item.Module) frozen hash mismatch: frozen=$($item.Hash) current=$storedHash"
    }
    if ($status -ne "DEGRADED_REVIEW_PENDING") {
        throw "$($item.Module) expected DEGRADED_REVIEW_PENDING, found $status"
    }
    $headingIndex = $reviewLog.IndexOf($heading, [StringComparison]::Ordinal)
    if ($headingIndex -ge 0) {
        $reviewLog = $reviewLog.Substring(0, $headingIndex).TrimEnd()
    }

    $promptRelative = ".ccg/dual-model-runs/$($item.Title)-input.md"
    if ($item.Module -eq "B02") {
        $disposition = "PROVIDER_BLOCKED_NO_USABLE_BACKEND"
        $attemptEvidence = @"
- Module-specific self-healing review was invoked through
  ${tick}docs/scripts/Start-CcgDualModelRun.ps1${tick}.
- Run ID: $tick$($probe.RunId)$tick.
- Summary: $tick$($probe.SummaryPath)$tick.
- Runner exit code: $tick$($probe.ExitCode)$tick.
- Completed backends: none.
- Gemini: provider quota/billing block; no usable output.
- Claude: exited without usable output.
"@
    }
    else {
        $disposition = "PROVIDER_BLOCKED_RETRY_DEFERRED"
        $attemptEvidence = @"
- No module-specific provider invocation was made in this pass.
- The sequential queue stopped after B02 returned zero completed backends, as
  required by the controlled retry budget. Repeating the same unavailable
  provider/session state for the remaining queue was intentionally avoided.
- Blocking probe summary:
  $tick$($probe.SummaryPath)$tick.
"@
    }

    $section = @"

$heading

- Frozen canonical issue hash: $tick$($item.Hash)$tick.
- Prepared retry prompt: $tick$promptRelative$tick.
$($attemptEvidence.TrimEnd())
- Explicit disposition: $tick$disposition$tick.
- No per-issue CCG verdict was produced or inferred.
- The canonical ${tick}issue.md$tick was not changed by this disposition record.
- Module status remains ${tick}DEGRADED_REVIEW_PENDING$tick and the module is excluded
  from optimization admission until a later run produces usable reviewer
  output and every completed-backend verdict is resolved.
"@
    [IO.File]::WriteAllText(
        $reviewLogPath,
        ($reviewLog.TrimEnd() + "`n`n" + $section.Replace("`r`n", "`n").TrimStart() + "`n"),
        $utf8NoBom
    )

    $rows.Add([pscustomobject]@{
        Module = $item.Module
        Folder = $item.Folder
        FrozenHash = $item.Hash
        Prompt = $promptRelative
        ProviderInvoked = $item.Module -eq "B02"
        Disposition = $disposition
        BlockingRunId = $probe.RunId
        BlockingSummary = $probe.SummaryPath
        OptimizationEligible = $false
    })
}

$rows | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding UTF8
"SUMMARY modules=$($rows.Count) direct=1 deferred=$($rows.Count - 1) eligible=0"
