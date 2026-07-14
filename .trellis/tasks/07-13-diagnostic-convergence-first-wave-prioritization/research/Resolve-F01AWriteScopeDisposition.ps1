param()

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path ".").Path
$rawRelative = ".trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/research/f01a-write-scope-recovery-r1-attempt2-orchestration-metadata-raw.json"
$outputRelative = ".trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/research/f01a-write-scope-recovery-r1-disposition.json"
$rawPath = Join-Path $repositoryRoot $rawRelative
$outputPath = Join-Path $repositoryRoot $outputRelative
$approvedOrchestrationPath = ".ccg/tasks/project-modular-analysis-diagnosis-optimization/.turns.json"
$utf8NoBom = [Text.UTF8Encoding]::new($false)

if (-not (Test-Path -LiteralPath $rawPath)) {
    throw "Missing raw F01A comparison: $rawPath"
}
$raw = Get-Content -Raw -Encoding utf8 $rawPath | ConvertFrom-Json
$rawUnexpected = @($raw.deltas | Where-Object { -not $_.AllowedRunnerOrManifestArtifact })
if ($rawUnexpected.Count -ne 1 -or $rawUnexpected[0].Path -ne $approvedOrchestrationPath) {
    throw "Raw comparison contains an unexpected delta other than the approved CCG turn metadata"
}
$completedBackendsJson = $raw.completedBackends | ConvertTo-Json -Compress
$completedBackendCount = if ($completedBackendsJson -in @($null, "null", "{}", "[]")) {
    0
}
else {
    @($raw.completedBackends).Count
}
if ($completedBackendCount -ne 0 -or -not $raw.quotaBlocked) {
    throw "F01A provider outcome no longer matches the blocked/no-usable-backend disposition"
}

$remainingUnexpected = @($raw.deltas | Where-Object {
    -not $_.AllowedRunnerOrManifestArtifact -and $_.Path -ne $approvedOrchestrationPath
})
$productDeltas = @($raw.deltas | Where-Object {
    $_.Path -notmatch '^(?:\.ccg/|\.trellis/tasks/07-13-)'
})
if ($remainingUnexpected.Count -ne 0 -or $productDeltas.Count -ne 0) {
    throw "F01A recovery contains an actual out-of-scope or product delta"
}

$disposition = [ordered]@{
    resolvedAt = (Get-Date).ToString("o")
    sourceComparison = $rawRelative.Replace("\", "/")
    runId = $raw.runId
    originalInvalidRunPreserved = $true
    originalInvalidRun = ".ccg/dual-model-runs/20260710-184735-f01a-issue-review-r1-reviewer/summary.json"
    approvedOrchestrationMetadata = @($approvedOrchestrationPath)
    approvedBoundarySource = ".trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/design.md"
    rawUnexpectedDeltaCount = $rawUnexpected.Count
    unexpectedDeltaCountAfterApprovedBoundary = $remainingUnexpected.Count
    productDeltaCount = $productDeltas.Count
    recoveryWriteScopeClean = $true
    completedBackends = @()
    providerReviewOutcome = "PROVIDER_BLOCKED_NO_USABLE_BACKEND"
    finalDiagnosticStatus = "HUMAN_DECISION_REQUIRED"
    optimizationEligible = $false
    rationale = "The only raw exception was CCG task-turn metadata inside the plan's approved .ccg/tasks boundary. No backend produced a usable review, so clean recovery scope does not promote F01A."
}
[IO.File]::WriteAllText(
    $outputPath,
    (($disposition | ConvertTo-Json -Depth 6) + "`n"),
    $utf8NoBom
)
"SUMMARY run=$($raw.runId) rawUnexpected=1 reclassifiedUnexpected=0 productDeltas=0 status=HUMAN_DECISION_REQUIRED"
