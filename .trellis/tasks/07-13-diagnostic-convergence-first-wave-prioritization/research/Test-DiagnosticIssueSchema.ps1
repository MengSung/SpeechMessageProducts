param(
    [string]$DiagnosticsRoot = "docs/project-modular-diagnostics",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

$requiredFields = @(
    "Category",
    "Severity",
    "Priority",
    "Priority score",
    "Confirmed",
    "Evidence confidence",
    "Impact score",
    "Likelihood/frequency score",
    "Security urgency score",
    "Performance gain score",
    "Loop leverage score",
    "Ease/reversibility score",
    "Effort",
    "Primary owner",
    "Cross-module",
    "Gate blocked",
    "Files",
    "Evidence",
    "Control/data/lifetime flow",
    "Impact",
    "Why this is necessary",
    "Recommended action",
    "Validation",
    "Rollback boundary",
    "Extraction contract",
    "CCG round history"
)
$fieldAlternation = ($requiredFields | ForEach-Object { [regex]::Escape($_) }) -join "|"

function Get-FieldValue {
    param(
        [string]$Body,
        [string]$Name
    )

    $escapedName = [regex]::Escape($Name)
    $pattern = '(?ms)^- {0}:[ \t]*(?<inline>[^\r\n]*)(?<continuation>(?:\r?\n(?!- [^:\r\n]+:|### |## )[^\r\n]*)*)' -f $escapedName
    $match = [regex]::Match($Body, $pattern)
    if (-not $match.Success) {
        return $null
    }

    return ($match.Groups["inline"].Value + "`n" + $match.Groups["continuation"].Value).Trim()
}

$results = @()
$issueFiles = Get-ChildItem -LiteralPath $DiagnosticsRoot -Directory |
    ForEach-Object { Join-Path $_.FullName "issue.md" } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object

foreach ($issueFile in $issueFiles) {
    $content = [System.IO.File]::ReadAllText($issueFile)
    $moduleMatch = [regex]::Match($content, "(?m)^Module:\s*(?<module>[^\r\n]+)$")
    $module = if ($moduleMatch.Success) { $moduleMatch.Groups["module"].Value.Trim() } else { "UNKNOWN" }
    $workspace = Split-Path (Split-Path $issueFile -Parent) -Leaf
    $defects = [System.Collections.Generic.List[string]]::new()
    $issueIds = [System.Collections.Generic.List[string]]::new()

    $rankedMatch = [regex]::Match(
        $content,
        "(?ms)^## Ranked Confirmed Issues\s*\r?\n(?<body>.*?)(?=^## |\z)"
    )
    if (-not $rankedMatch.Success) {
        $defects.Add("missing canonical `## Ranked Confirmed Issues` section")
    }
    else {
        $rankedBody = $rankedMatch.Groups["body"].Value
        $headingMatches = [regex]::Matches($rankedBody, "(?m)^###\s+(?<heading>[^\r\n]+)$")
        if ($headingMatches.Count -eq 0 -and
            -not [string]::IsNullOrWhiteSpace($rankedBody) -and
            $rankedBody.Trim() -notmatch '^No .+ confirmed') {
            $defects.Add("ranked section contains content but no level-3 issue headings")
        }

        for ($index = 0; $index -lt $headingMatches.Count; $index++) {
            $headingMatch = $headingMatches[$index]
            $heading = $headingMatch.Groups["heading"].Value.Trim()
            $bodyStart = $headingMatch.Index + $headingMatch.Length
            $bodyEnd = if ($index + 1 -lt $headingMatches.Count) {
                $headingMatches[$index + 1].Index
            }
            else {
                $rankedBody.Length
            }
            $body = $rankedBody.Substring($bodyStart, $bodyEnd - $bodyStart)
            $idMatch = [regex]::Match(
                $heading,
                "^(?<id>$([regex]::Escape($module))-(?:SEC|PERF|EXT)-\d{3})\s+\S"
            )
            if (-not $idMatch.Success) {
                $defects.Add("non-canonical issue heading: '$heading'")
                continue
            }

            $issueId = $idMatch.Groups["id"].Value
            $issueIds.Add($issueId)
            foreach ($field in $requiredFields) {
                $value = Get-FieldValue -Body $body -Name $field
                if ($null -eq $value) {
                    $defects.Add("$issueId missing field: $field")
                }
                elseif ([string]::IsNullOrWhiteSpace($value)) {
                    $defects.Add("$issueId empty field: $field")
                }
            }

            $confirmed = Get-FieldValue -Body $body -Name "Confirmed"
            if ($null -ne $confirmed -and $confirmed -ne "true") {
                $defects.Add("$issueId Confirmed must be exactly true (found: $confirmed)")
            }

            $files = Get-FieldValue -Body $body -Name "Files"
            if ($null -ne $files) {
                $fileEntries = @($files -split '\r?\n' | Where-Object { $_ -match '^\s*-\s+' })
                $missingLineEntries = @($fileEntries | Where-Object { $_ -notmatch ':\d+(?:\b|[-,])' })
                if ($fileEntries.Count -eq 0 -or $missingLineEntries.Count -gt 0) {
                    $defects.Add("$issueId Files contains an entry without path:line evidence")
                }
            }

            $category = Get-FieldValue -Body $body -Name "Category"
            $expectedCategory = if ($issueId -match '-SEC-') {
                "Security"
            }
            elseif ($issueId -match '-PERF-') {
                "Performance"
            }
            else {
                "Extraction"
            }
            if ($null -ne $category -and $category -ne $expectedCategory) {
                $defects.Add("$issueId category mismatch: expected $expectedCategory, found $category")
            }

            $owner = Get-FieldValue -Body $body -Name "Primary owner"
            if ($null -ne $owner -and $owner -ne $module) {
                $defects.Add("$issueId Primary owner must be exactly $module (found: $owner)")
            }

            $scoreRanges = [ordered]@{
                "Priority score" = 100
                "Evidence confidence" = 20
                "Impact score" = 25
                "Likelihood/frequency score" = 15
                "Security urgency score" = 15
                "Performance gain score" = 10
                "Loop leverage score" = 10
                "Ease/reversibility score" = 5
            }
            $parsedScores = @{}
            foreach ($scoreField in $scoreRanges.Keys) {
                $scoreValue = Get-FieldValue -Body $body -Name $scoreField
                if ($null -eq $scoreValue) {
                    continue
                }
                if ($scoreValue -notmatch '^\d+$') {
                    $defects.Add("$issueId $scoreField must be a plain integer (found: $scoreValue)")
                    continue
                }
                $scoreNumber = [int]$scoreValue
                if ($scoreNumber -lt 0 -or $scoreNumber -gt $scoreRanges[$scoreField]) {
                    $defects.Add("$issueId $scoreField outside 0-$($scoreRanges[$scoreField]): $scoreNumber")
                }
                $parsedScores[$scoreField] = $scoreNumber
            }

            $componentFields = @(
                "Evidence confidence",
                "Impact score",
                "Likelihood/frequency score",
                "Security urgency score",
                "Performance gain score",
                "Loop leverage score",
                "Ease/reversibility score"
            )
            if ($parsedScores.ContainsKey("Priority score") -and
                @($componentFields | Where-Object { -not $parsedScores.ContainsKey($_) }).Count -eq 0) {
                $componentSum = ($componentFields | ForEach-Object { $parsedScores[$_] } | Measure-Object -Sum).Sum
                if ($componentSum -ne $parsedScores["Priority score"]) {
                    $defects.Add("$issueId Priority score $($parsedScores['Priority score']) does not equal component sum $componentSum")
                }
            }

            $priority = Get-FieldValue -Body $body -Name "Priority"
            if ($parsedScores.ContainsKey("Priority score") -and $null -ne $priority) {
                $score = $parsedScores["Priority score"]
                $severity = Get-FieldValue -Body $body -Name "Severity"
                $expectedPriority = if ($severity -eq "Critical") { "P0" } elseif ($score -ge 85) { "P0" } elseif ($score -ge 70) { "P1" } elseif ($score -ge 50) { "P2" } else { "P3" }
                if ($priority -ne $expectedPriority) {
                    $defects.Add("$issueId Priority band mismatch: score $score requires $expectedPriority, found $priority")
                }
            }

            $ccgHistory = Get-FieldValue -Body $body -Name "CCG round history"
            if ($null -ne $ccgHistory -and $ccgHistory -match '(?i)\bpending\b') {
                $defects.Add("$issueId CCG round history contains a pending placeholder")
            }
        }

        $duplicates = $issueIds | Group-Object | Where-Object Count -gt 1
        foreach ($duplicate in $duplicates) {
            $defects.Add("duplicate issue ID: $($duplicate.Name)")
        }
    }

    $results += [pscustomobject]@{
        Module = $module
        Workspace = $workspace
        IssueCount = $issueIds.Count
        Pass = $defects.Count -eq 0
        Defects = @($defects)
    }
}

if ($AsJson) {
    $results | ConvertTo-Json -Depth 5
    exit
}

$results | ForEach-Object {
    $state = if ($_.Pass) { "PASS" } else { "FAIL" }
    "{0} {1} issues={2} defects={3}" -f $state, $_.Module, $_.IssueCount, $_.Defects.Count
    $_.Defects | ForEach-Object { "  - $_" }
}

$passed = @($results | Where-Object Pass).Count
$failed = $results.Count - $passed
"SUMMARY workspaces=$($results.Count) passed=$passed failed=$failed"
