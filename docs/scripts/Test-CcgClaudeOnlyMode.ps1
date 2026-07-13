param(
    [string]$RepositoryPath = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
$healthScript = Join-Path $repositoryFullPath 'docs\scripts\Test-CcgDualModelHealth.ps1'
$fixtureDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("ccg-claude-only-fixture-" + [Guid]::NewGuid().ToString('N'))

try {
    $healthOutput = & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $healthScript `
        -RepositoryPath $repositoryFullPath `
        -OutputDirectory $fixtureDirectory `
        -BackendMode claude `
        -SkipBackendSmoke 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Claude-only health fixture failed with exit code $LASTEXITCODE. $($healthOutput -join [Environment]::NewLine)"
    }

    $summary = ($healthOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($summary.backendMode -ne 'claude') {
        throw "Expected backendMode 'claude', got '$($summary.backendMode)'."
    }

    $summaryJson = $summary | ConvertTo-Json -Depth 10
    if ($summaryJson -match '(?i)gemini') {
        throw 'Claude-only health summary must not contain Gemini fields or values.'
    }

    $healthArtifacts = @(Get-ChildItem -LiteralPath $fixtureDirectory -File -Filter 'ccg-health-*.json')
    if ($healthArtifacts.Count -ne 1) {
        throw "Expected exactly one health artifact, found $($healthArtifacts.Count)."
    }

    $healthArtifactText = Get-Content -LiteralPath $healthArtifacts[0].FullName -Raw -Encoding UTF8
    if ($healthArtifactText -match '(?i)gemini') {
        throw 'Claude-only health artifact must not contain Gemini fields or values.'
    }

    Write-Host 'PASS: Claude-only health fixture emitted no Gemini fields or artifacts.'
}
finally {
    Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
