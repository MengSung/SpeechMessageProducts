param(
    [switch]$PrepareOnly,
    [string]$StartAt = "B02",
    [int]$StopAfter = 17,
    [switch]$ContinueAfterNoUsableBackend
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path ".").Path
$diagnosticsRoot = Join-Path $repositoryRoot "docs/project-modular-diagnostics"
$outputRoot = Join-Path $repositoryRoot ".ccg/dual-model-runs"
$runner = Join-Path $repositoryRoot "docs/scripts/Start-CcgDualModelRun.ps1"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$moduleOrder = @(
    "B02", "B03", "B04A", "B04B", "B04C", "B05", "B07",
    "F02", "F03A", "F03Q",
    "X01", "X02A", "X02B", "X02C", "X02Q", "X03", "X04B"
)

function Get-CanonicalIssueHash {
    param([string]$IssuePath)

    $text = [IO.File]::ReadAllText($IssuePath).Replace("`r`n", "`n").Replace("`r", "`n")
    $storedMatch = [regex]::Match($text, "(?m)^Issue document SHA-256:\s*([0-9a-f]{64})\s*$")
    if (-not $storedMatch.Success) {
        throw "Missing canonical hash in $IssuePath"
    }

    $canonical = [regex]::Replace(
        $text,
        "(?m)^Issue document SHA-256:.*$",
        "Issue document SHA-256:",
        1
    )
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $actual = [BitConverter]::ToString(
            $sha.ComputeHash($utf8NoBom.GetBytes($canonical))
        ).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }

    if ($actual -ne $storedMatch.Groups[1].Value) {
        throw "Hash mismatch in ${IssuePath}: stored=$($storedMatch.Groups[1].Value) actual=$actual"
    }
    return $actual
}

$workspaces = @{}
Get-ChildItem -LiteralPath $diagnosticsRoot -Directory | ForEach-Object {
    $issuePath = Join-Path $_.FullName "issue.md"
    if (-not (Test-Path -LiteralPath $issuePath)) {
        return
    }
    $header = [IO.File]::ReadAllText($issuePath)
    $moduleMatch = [regex]::Match($header, "(?m)^Module:\s*([^\r\n]+)$")
    $statusMatch = [regex]::Match($header, "(?m)^Status:\s*([^\r\n]+)$")
    if ($moduleMatch.Success) {
        $workspaces[$moduleMatch.Groups[1].Value.Trim()] = [pscustomobject]@{
            Module = $moduleMatch.Groups[1].Value.Trim()
            Status = $statusMatch.Groups[1].Value.Trim()
            Folder = $_.Name
            Path = $_.FullName
            IssuePath = $issuePath
        }
    }
}

$startIndex = [Array]::IndexOf($moduleOrder, $StartAt)
if ($startIndex -lt 0) {
    throw "StartAt must be one of: $($moduleOrder -join ', ')"
}

$selectedModules = @($moduleOrder[$startIndex..($moduleOrder.Count - 1)] | Select-Object -First $StopAfter)
$prepared = @()

foreach ($module in $selectedModules) {
    if (-not $workspaces.ContainsKey($module)) {
        throw "No diagnostic workspace found for $module"
    }
    $workspace = $workspaces[$module]
    if ($workspace.Status -ne "DEGRADED_REVIEW_PENDING") {
        throw "$module is not DEGRADED_REVIEW_PENDING (found $($workspace.Status))"
    }

    $hash = Get-CanonicalIssueHash -IssuePath $workspace.IssuePath
    $slug = $module.ToLowerInvariant()
    $title = "$slug-convergence-step2-r1"
    $promptPath = Join-Path $outputRoot "$title-input.md"
    $relativeWorkspace = "docs/project-modular-diagnostics/$($workspace.Folder)"
    $prompt = @"
<TASK>
Perform a zero-trust diagnostic review for exactly one isolation zone.

Module: $module
Workspace: $relativeWorkspace
Reviewed canonical issue hash: $hash
Diagnostic mode: DIAGNOSIS_ONLY

Read:
- $relativeWorkspace/issue.md
- $relativeWorkspace/review-log.md
- all five files under $relativeWorkspace/evidence/
- docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md
- the $module ownership/dependency entries in
  docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md
- product source only as needed to verify cited evidence

Hard constraints:
- Do not edit or create repository files.
- Do not run build, test, restore, runtime, benchmark, package, format, migration,
  code-generation, external CRM, payment, or LINE commands.
- Review only the exact hash above. If the hash differs, return HASH_MISMATCH.
- For every item under `## Ranked Confirmed Issues`, return exactly one verdict:
  KEEP, REWRITE, DELETE, or NEEDS_RUNTIME_VALIDATION.
- Verify source reachability, ownership, severity, category, evidence, score inputs,
  action necessity, validation, rollback, and extraction boundaries.
- Do not promote hypotheses, positive controls, or blocked runtime claims.
- Report every unresolved Critical/Warning. No finding may be silently ignored.
- A provider/session/quota failure is not approval.

Output Markdown only:
1. Reviewed module and hash
2. Per-issue verdict table: ID | verdict | evidence check | required correction
3. Runtime-only and rejected-candidate audit
4. Critical / Warning / Info findings
5. Overall diagnostic verdict: APPROVE, APPROVE_DEGRADED,
   RUNTIME_VALIDATION_REQUIRED, REWRITE, or HUMAN_DECISION_REQUIRED
</TASK>
"@
    [IO.File]::WriteAllText($promptPath, $prompt.Replace("`r`n", "`n"), $utf8NoBom)
    $prepared += [pscustomobject]@{
        Module = $module
        Folder = $workspace.Folder
        Hash = $hash
        Title = $title
        PromptPath = $promptPath
    }
}

$prepared | Format-Table Module, Hash, Title -AutoSize
$prepared | Select-Object Module, Folder, Hash, Title, PromptPath |
    Export-Csv -LiteralPath (Join-Path $PSScriptRoot "step2-frozen-inputs.csv") `
        -NoTypeInformation -Encoding UTF8
if ($PrepareOnly) {
    exit 0
}

$runCount = 0
$resultPath = Join-Path $PSScriptRoot "step2-run-results.jsonl"
if ($StartAt -eq $moduleOrder[0]) {
    [IO.File]::WriteAllText($resultPath, "", $utf8NoBom)
}
foreach ($item in $prepared) {
    $runCount++
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
        -Role reviewer `
        -Title $item.Title `
        -PromptFile $item.PromptPath `
        -RepositoryPath $repositoryRoot `
        -OutputDirectory $outputRoot `
        -AllowSingleModelWhenQuotaBlocked
    $runnerExitCode = $LASTEXITCODE

    $runFolder = Get-ChildItem -LiteralPath $outputRoot -Directory |
        Where-Object Name -Like "*-$($item.Title)-reviewer" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $runFolder) {
        throw "Runner produced no run folder for $($item.Module)"
    }
    $summaryPath = Join-Path $runFolder.FullName "summary.json"
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        throw "Runner produced no summary for $($item.Module): $summaryPath"
    }
    $summary = Get-Content -Raw -Encoding utf8 $summaryPath | ConvertFrom-Json
    $completedCount = @($summary.completedBackends).Count
    $runResult = [pscustomobject]@{
        Module = $item.Module
        Hash = $item.Hash
        RunId = $summary.runId
        SummaryPath = $summaryPath.Substring($repositoryRoot.Length + 1).Replace("\", "/")
        ExitCode = $runnerExitCode
        Ok = $summary.ok
        CompletedBackends = @($summary.completedBackends) -join ","
        QuotaBlocked = $summary.quotaBlocked
        DegradedFallback = $summary.degradedFallback
    }
    $jsonLine = $runResult | ConvertTo-Json -Compress
    [IO.File]::AppendAllText($resultPath, $jsonLine + "`n", $utf8NoBom)
    $jsonLine

    if ($completedCount -eq 0 -and -not $ContinueAfterNoUsableBackend) {
        Write-Warning "Stopped after $($item.Module): no usable backend completed."
        break
    }
}
