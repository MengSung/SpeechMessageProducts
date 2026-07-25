[CmdletBinding()]
param(
    [string]$ManifestPath = "eng/no-sdk-source-roots.json",
    [switch]$FailOnFindings,
    [switch]$Json,
    [switch]$SummaryOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    param([string]$ScriptRoot)
    return (Resolve-Path -LiteralPath (Join-Path $ScriptRoot "..")).Path
}

function Convert-ToRelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $resolvedBasePath = [string](Resolve-Path -LiteralPath $BasePath).Path
    $resolvedPath = [string](Resolve-Path -LiteralPath $Path).Path
    $directorySeparator = [string][System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [string][System.IO.Path]::AltDirectorySeparatorChar
    $normalizedBasePath = $resolvedBasePath.TrimEnd($directorySeparator.ToCharArray() + $alternateSeparator.ToCharArray()) + $directorySeparator

    if ([string]::Equals($resolvedPath, $resolvedBasePath, [StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }

    if ($resolvedPath.StartsWith($normalizedBasePath, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedPath.Substring($normalizedBasePath.Length)
    }

    return $resolvedPath
}

function Test-IsExcluded {
    param(
        [string]$RelativePath,
        [object[]]$ExcludedRelativePaths,
        [object[]]$ExcludedDirectoryNames
    )

    $normalized = $RelativePath.Replace('\', '/')
    $trimCharacters = [char[]]@('/', '\')

    foreach ($excludedDirectoryName in $ExcludedDirectoryNames) {
        $directoryName = ([string]$excludedDirectoryName).Trim($trimCharacters)
        if ([string]::IsNullOrWhiteSpace($directoryName)) {
            continue
        }

        foreach ($segment in $normalized.Split('/')) {
            if ([string]::Equals($segment, $directoryName, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    foreach ($excluded in $ExcludedRelativePaths) {
        $prefix = ([string]$excluded).Trim($trimCharacters).Replace('\', '/')
        if ($normalized -eq $prefix -or $normalized.StartsWith($prefix + "/")) {
            return $true
        }
    }

    return $false
}

function Get-ScanFiles {
    param(
        [string]$RepoRoot,
        [object]$Manifest
    )

    $filesByPath = [ordered]@{}

    foreach ($solution in $Manifest.solutionFiles) {
        $full = Join-Path $RepoRoot ([string]$solution)
        if (Test-Path -LiteralPath $full) {
            $filesByPath[(Resolve-Path -LiteralPath $full).Path] = $true
        }
    }

    foreach ($root in $Manifest.projectRoots) {
        $fullRoot = Join-Path $RepoRoot ([string]$root)
        if (-not (Test-Path -LiteralPath $fullRoot)) {
            continue
        }

        Get-ChildItem -LiteralPath $fullRoot -Recurse -File | ForEach-Object {
            $relative = Convert-ToRelativePath -BasePath $RepoRoot -Path $_.FullName
            if (Test-IsExcluded -RelativePath $relative -ExcludedRelativePaths $Manifest.excludedRelativePaths -ExcludedDirectoryNames $Manifest.excludedDirectoryNames) {
                return
            }

            if ($Manifest.scannedFileExtensions -contains $_.Extension) {
                $filesByPath[$_.FullName] = $true
            }

            if ($_.Name -eq "packages.config") {
                $filesByPath[$_.FullName] = $true
            }
        }
    }

    return @($filesByPath.Keys)
}

function Find-BannedSdkReferences {
    param(
        [string]$RepoRoot,
        [object]$Manifest
    )

    $findings = New-Object System.Collections.Generic.List[object]
    $files = Get-ScanFiles -RepoRoot $RepoRoot -Manifest $Manifest

    foreach ($file in $files) {
        $relative = Convert-ToRelativePath -BasePath $RepoRoot -Path $file
        $lineNumber = 0

        foreach ($line in [System.IO.File]::ReadLines($file, [System.Text.Encoding]::UTF8)) {
            $lineNumber++

            foreach ($rule in $Manifest.bannedPatterns) {
                $pattern = [string]$rule.pattern
                if ($line.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $findings.Add([pscustomobject]@{
                        ruleId = [string]$rule.id
                        kind = [string]$rule.kind
                        path = $relative
                        line = $lineNumber
                        pattern = $pattern
                        text = $line.Trim()
                    })
                }
            }
        }
    }

    return $findings.ToArray()
}

$repoRoot = Resolve-RepoRoot -ScriptRoot $PSScriptRoot
$manifestFullPath = if ([IO.Path]::IsPathRooted($ManifestPath)) {
    $ManifestPath
} else {
    Join-Path $repoRoot $ManifestPath
}

if (-not (Test-Path -LiteralPath $manifestFullPath)) {
    throw "Manifest not found: $manifestFullPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$findings = Find-BannedSdkReferences -RepoRoot $repoRoot -Manifest $manifest

$result = [pscustomobject]@{
    schemaVersion = "2026-07-24.phase0.no-sdk-scan-result.v1"
    generatedAt = (Get-Date).ToString("o")
    mode = if ($FailOnFindings) { "failing-gate" } else { "report-only" }
    repositoryRoot = $repoRoot
    manifestPath = (Convert-ToRelativePath -BasePath $repoRoot -Path $manifestFullPath)
    findingCount = $findings.Count
    findings = $findings
}

if ($Json) {
    $result | ConvertTo-Json -Depth 8
} else {
    Write-Host "No-Dynamics-SDK scan mode: $($result.mode)"
    Write-Host "Findings: $($findings.Count)"
    foreach ($group in ($findings | Group-Object -Property ruleId | Sort-Object -Property Name)) {
        Write-Host ("{0}: {1}" -f $group.Name, $group.Count)
    }

    if ($SummaryOnly -and $findings.Count -gt 0) {
        Write-Host "SummaryOnly enabled. Run without -SummaryOnly or use -Json for full finding details."
    } else {
        foreach ($finding in $findings) {
            Write-Host ("{0}:{1}: [{2}/{3}] {4}" -f $finding.path, $finding.line, $finding.ruleId, $finding.kind, $finding.text)
        }
    }
}

if ($FailOnFindings -and $findings.Count -gt 0) {
    Write-Error "Banned Microsoft Dynamics CRM/Dataverse SDK references found."
    exit 1
}

exit 0
