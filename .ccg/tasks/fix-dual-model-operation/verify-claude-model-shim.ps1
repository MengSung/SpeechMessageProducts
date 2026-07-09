param(
    [string]$RepositoryPath = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$files = @(
    'docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1',
    'docs/scripts/Test-CcgDualModelHealth.ps1'
)

$failures = @()
foreach ($relativePath in $files) {
    $path = Join-Path $RepositoryPath $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        $failures += "Missing script: $relativePath"
        continue
    }

    $content = Get-Content -LiteralPath $path -Raw
    foreach ($requiredPattern in @(
        'function New-CcgClaudeModelShim',
        'CCG_REAL_CLAUDE_CMD',
        'CCG_CLAUDE_MODEL_SHIM_DIR',
        '--model',
        'CLAUDE_MODEL_SHIM',
        'Move-Item -LiteralPath'
    )) {
        if ($content -notmatch [regex]::Escape($requiredPattern)) {
            $failures += "$relativePath missing $requiredPattern"
        }
    }

    if ($content -match 'Join-Path \(\[System\.IO\.Path\]::GetTempPath\(\)\) "ccg-claude-model-shim"') {
        $failures += "$relativePath still uses a shared fixed Claude shim directory"
    }

    if ($content -match '\$Result\.StdOut \+ "`n" \+ \$Result\.StdErr \+ "`n" \+ \$Diagnostic') {
        $failures += "$relativePath still classifies provider quota from model stdout"
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'CLAUDE_MODEL_SHIM_GUARD_OK'
