param(
    [switch]$PrepareOnly
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path ".").Path
$researchRoot = Join-Path $repositoryRoot ".trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/research"
$outputRoot = Join-Path $repositoryRoot ".ccg/dual-model-runs"
$promptPath = Join-Path $outputRoot "f01a-write-scope-recovery-r1-input.md"
$runner = Join-Path $repositoryRoot "docs/scripts/Start-CcgDualModelRun.ps1"
$title = "f01a-write-scope-recovery-r1"
$expectedCanonicalHash = "312d6da27a3895aa8c6f4fd4dd9ba5ad16f6537407595c35d72fbff02d644c76"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$manifestPrefix = "f01a-write-scope-recovery-r1"

function Get-GitFileList {
    param(
        [ValidateSet("tracked", "untracked", "ignored")]
        [string]$Classification
    )

    $arguments = switch ($Classification) {
        "tracked" { @("-c", "core.quotepath=false", "ls-files") }
        "untracked" { @("-c", "core.quotepath=false", "ls-files", "--others", "--exclude-standard") }
        "ignored" { @("-c", "core.quotepath=false", "ls-files", "--others", "--ignored", "--exclude-standard") }
    }
    $lines = @(& git @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for $Classification"
    }
    return @($lines | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        $_ -notmatch '^(?:\.vs|[^/]+/\.vs)/'
    })
}

function Get-RepositoryManifest {
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($classification in @("tracked", "untracked", "ignored")) {
        foreach ($relativePath in Get-GitFileList -Classification $classification) {
            $absolutePath = Join-Path $repositoryRoot $relativePath
            if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
                $rows.Add([pscustomobject]@{
                    Classification = $classification
                    Path = $relativePath.Replace("\", "/")
                    Length = -1
                    LastWriteUtc = "MISSING"
                    Sha256 = "MISSING"
                })
                continue
            }
            $item = Get-Item -LiteralPath $absolutePath -Force
            try {
                $hash = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            catch {
                $hash = "UNREADABLE:$($_.Exception.GetType().Name)"
            }
            $rows.Add([pscustomobject]@{
                Classification = $classification
                Path = $relativePath.Replace("\", "/")
                Length = $item.Length
                LastWriteUtc = $item.LastWriteTimeUtc.ToString("o")
                Sha256 = $hash
            })
        }
    }
    return @($rows | Sort-Object Classification, Path)
}

function Get-CanonicalIssueHash {
    $issuePath = Join-Path $repositoryRoot "docs/project-modular-diagnostics/F01A-solution-build-ci-governance/issue.md"
    $text = [IO.File]::ReadAllText($issuePath).Replace("`r`n", "`n").Replace("`r", "`n")
    $canonical = [regex]::Replace($text, "(?m)^Issue document SHA-256:.*$", "Issue document SHA-256:", 1)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString(
            $sha.ComputeHash($utf8NoBom.GetBytes($canonical))
        ).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $promptPath)) {
    throw "Missing F01A recovery prompt: $promptPath"
}
$prompt = [IO.File]::ReadAllText($promptPath)
if ($prompt -notmatch [regex]::Escape($expectedCanonicalHash) -or
    $prompt -notmatch "REPOSITORY_COMMANDS: none" -or
    $prompt -notmatch "Execute no shell") {
    throw "F01A prompt does not preserve the frozen hash and command-free contract"
}
$actualCanonicalHash = Get-CanonicalIssueHash
if ($actualCanonicalHash -ne $expectedCanonicalHash) {
    throw "F01A canonical hash changed: expected=$expectedCanonicalHash actual=$actualCanonicalHash"
}

if ($PrepareOnly) {
    $counts = foreach ($classification in @("tracked", "untracked", "ignored")) {
        [pscustomobject]@{
            Classification = $classification
            Count = @(Get-GitFileList -Classification $classification).Count
        }
    }
    $counts | Format-Table -AutoSize
    "F01AHash=$actualCanonicalHash PromptReady=true VsExcluded=true"
    exit 0
}

$beforePath = Join-Path $researchRoot "$manifestPrefix-before.csv"
$afterPath = Join-Path $researchRoot "$manifestPrefix-after.csv"
$comparisonPath = Join-Path $researchRoot "$manifestPrefix-comparison.json"

$before = Get-RepositoryManifest
$before | Export-Csv -LiteralPath $beforePath -NoTypeInformation -Encoding UTF8

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
    -Role reviewer `
    -Title $title `
    -PromptFile $promptPath `
    -RepositoryPath $repositoryRoot `
    -OutputDirectory $outputRoot `
    -AllowSingleModelWhenQuotaBlocked
$runnerExitCode = $LASTEXITCODE

$after = Get-RepositoryManifest
$after | Export-Csv -LiteralPath $afterPath -NoTypeInformation -Encoding UTF8

$beforeMap = @{}
foreach ($row in $before) { $beforeMap["$($row.Classification)|$($row.Path)"] = $row }
$afterMap = @{}
foreach ($row in $after) { $afterMap["$($row.Classification)|$($row.Path)"] = $row }
$allKeys = @($beforeMap.Keys + $afterMap.Keys | Sort-Object -Unique)
$deltas = foreach ($key in $allKeys) {
    $old = $beforeMap[$key]
    $new = $afterMap[$key]
    $changed = $null -eq $old -or $null -eq $new -or
        $old.Length -ne $new.Length -or
        $old.LastWriteUtc -ne $new.LastWriteUtc -or
        $old.Sha256 -ne $new.Sha256
    if (-not $changed) { continue }
    $path = if ($null -ne $new) { $new.Path } else { $old.Path }
    $classification = if ($null -ne $new) { $new.Classification } else { $old.Classification }
    $allowed = $path -match '^\.ccg/dual-model-runs/(?:\d{8}-\d{6}-)?f01a-write-scope-recovery-r1(?:-reviewer)?(?:/|\.md$)' -or
        $path -eq '.ccg/tasks/project-modular-analysis-diagnosis-optimization/.turns.json' -or
        $path -match '^\.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/research/f01a-write-scope-recovery-r1-(?:before|after|comparison)\.(?:csv|json)$'
    [pscustomobject]@{
        Classification = $classification
        Path = $path
        Change = if ($null -eq $old) { "ADDED" } elseif ($null -eq $new) { "REMOVED" } else { "MODIFIED" }
        AllowedRunnerOrManifestArtifact = $allowed
        BeforeLength = if ($null -ne $old) { $old.Length } else { $null }
        AfterLength = if ($null -ne $new) { $new.Length } else { $null }
        BeforeSha256 = if ($null -ne $old) { $old.Sha256 } else { $null }
        AfterSha256 = if ($null -ne $new) { $new.Sha256 } else { $null }
    }
}

$unexpected = @($deltas | Where-Object { -not $_.AllowedRunnerOrManifestArtifact })
$runFolder = Get-ChildItem -LiteralPath $outputRoot -Directory |
    Where-Object Name -Like "*-$title-reviewer" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
$summary = if ($null -ne $runFolder -and (Test-Path (Join-Path $runFolder.FullName "summary.json"))) {
    Get-Content -Raw -Encoding utf8 (Join-Path $runFolder.FullName "summary.json") | ConvertFrom-Json
} else { $null }

$comparison = [ordered]@{
    comparedAt = (Get-Date).ToString("o")
    excluded = @(".vs/**")
    beforeCount = $before.Count
    afterCount = $after.Count
    deltaCount = @($deltas).Count
    unexpectedDeltaCount = $unexpected.Count
    writeScopeClean = $unexpected.Count -eq 0
    runnerExitCode = $runnerExitCode
    runId = if ($null -ne $summary) { $summary.runId } else { $null }
    completedBackends = if ($null -ne $summary) { @($summary.completedBackends) } else { @() }
    quotaBlocked = if ($null -ne $summary) { $summary.quotaBlocked } else { $null }
    degradedFallback = if ($null -ne $summary) { $summary.degradedFallback } else { $null }
    deltas = @($deltas)
}
[IO.File]::WriteAllText(
    $comparisonPath,
    (($comparison | ConvertTo-Json -Depth 7) + "`n"),
    $utf8NoBom
)

$comparison | ConvertTo-Json -Depth 4
if ($unexpected.Count -gt 0) {
    exit 4
}
