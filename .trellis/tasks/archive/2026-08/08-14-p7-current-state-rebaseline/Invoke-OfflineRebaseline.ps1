[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Build', 'Validate')]
    [string]$Mode,

    [string]$MatrixPath = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'The task-owned script root could not be resolved.'
    }

    $scriptRoot = Split-Path -Parent $scriptPath
}

$taskRoot = (Resolve-Path -LiteralPath $scriptRoot).Path
# 參數預設值在部分相對 -File 呼叫中早於 PSScriptRoot 可用；在解析 task 根目錄後才選擇預設輸出，避免共享路徑。
if ([string]::IsNullOrWhiteSpace($MatrixPath)) {
    $MatrixPath = (Join-Path $taskRoot 'authoritative-gap-matrix.json')
}

$repositoryRoot = (Resolve-Path (Join-Path $taskRoot '..\..\..')).Path
$archivedAnalyzer = Join-Path $repositoryRoot '.trellis\tasks\archive\2026-08\08-12-p7-remaining-work-rebaseline\build_rebaseline.py'
$resolvedMatrixPath = [System.IO.Path]::GetFullPath($MatrixPath)

if (-not (Test-Path -LiteralPath $archivedAnalyzer -PathType Leaf)) {
    throw 'The immutable archived offline analyzer is missing.'
}
if (-not $resolvedMatrixPath.StartsWith($taskRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Matrix output must remain inside the task-owned directory.'
}

function Invoke-Analyzer {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & python.exe $archivedAnalyzer @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Offline analyzer failed with exit code $LASTEXITCODE."
    }
}

function Get-CountMap {
    param([Parameter(Mandatory = $true)][object[]]$Values)

    $counts = [ordered]@{}
    foreach ($group in @($Values | Group-Object | Sort-Object Name)) {
        $counts[[string]$group.Name] = [int]$group.Count
    }
    return $counts
}

function Write-TaskJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Value
    )

    $content = $Value | ConvertTo-Json -Depth 5
    $normalized = ($content -replace "`r?`n", "`r`n") + "`r`n"
    [System.IO.File]::WriteAllText(
        $Path,
        $normalized,
        [System.Text.UTF8Encoding]::new($false))
}

function Write-MatrixSummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $matrix = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $rows = @($matrix.callSites)
    if ($rows.Count -ne 70) {
        throw 'Validated matrix did not contain the immutable seventy-row baseline.'
    }

    $summary = [ordered]@{
        schemaVersion = 'p7.current-state-rebaseline.summary.v1'
        callSiteCount = [int]$rows.Count
        registry = Get-CountMap @($rows | ForEach-Object { $_.registry.status })
        data8Executor = Get-CountMap @($rows | ForEach-Object { $_.data8Executor.status })
        productClient = Get-CountMap @($rows | ForEach-Object { $_.productClient.status })
        consumer = Get-CountMap @($rows | ForEach-Object { $_.consumer.status })
        ce91 = Get-CountMap @($rows | ForEach-Object { $_.ceEvidence.ce91 })
        temporaryLegacy = Get-CountMap @($rows | ForEach-Object { $_.temporaryLegacy })
        p75RemovalBlocker = Get-CountMap @($rows | ForEach-Object { $_.p75RemovalBlocker })
    }
    Write-TaskJson -Path (Join-Path (Split-Path -Parent $Path) 'matrix-summary.json') -Value $summary
}

if ($Mode -eq 'Build') {
    Invoke-Analyzer @('--output', $resolvedMatrixPath)
    Invoke-Analyzer @('--validate', $resolvedMatrixPath)
    Write-MatrixSummary -Path $resolvedMatrixPath
    exit 0
}

Invoke-Analyzer @('--validate', $resolvedMatrixPath)
exit 0
