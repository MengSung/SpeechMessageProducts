<#
檔案整體說明

目的與定位：將分析／審查需求保存為UTF-8提示詞後交給雙模型自癒runner，依本次run身分讀取摘要，區分完整雙模型、指定單模型與額度降級。

主要功能與流程：
操作入口包含ConvertTo-SafeFileName、New-DirectoryIfMissing；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("analyzer", "architect", "reviewer", "debugger", "tester", "optimizer", "builder")]
    [string]$Role,

    [Parameter(Mandatory = $true)]
    [string]$Title,

    [string]$Prompt,
    [string]$PromptFile,
    [string]$RepositoryPath = (Get-Location).Path,
    [string]$OutputDirectory = ".\.ccg\dual-model-runs",
    [int]$TimeoutSeconds = 900,
    [int]$MaxAttempts = 2,
    [switch]$AllowSingleModelWhenQuotaBlocked,
    [switch]$RunHealthBackendSmoke,
    # 使用者明確選擇時只執行 Claude；預設仍保留原有雙模型流程。
    [switch]$ClaudeOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function ConvertTo-SafeFileName {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.Trim().ToLowerInvariant()
    $normalized = [regex]::Replace($normalized, '[^a-z0-9\u4e00-\u9fff]+', '-')
    $normalized = $normalized.Trim('-')
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return 'ccg-task'
    }
    if ($normalized.Length -gt 80) {
        return $normalized.Substring(0, 80).Trim('-')
    }
    return $normalized
}

function New-DirectoryIfMissing {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

$repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
Set-Location -LiteralPath $repositoryFullPath

if ([string]::IsNullOrWhiteSpace($Prompt) -and [string]::IsNullOrWhiteSpace($PromptFile)) {
    throw 'You must provide either -Prompt or -PromptFile.'
}

$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryFullPath $OutputDirectory
}
New-DirectoryIfMissing -Path $resolvedOutputDirectory

$promptText = if (-not [string]::IsNullOrWhiteSpace($PromptFile)) {
    $resolvedPromptFile = (Resolve-Path -LiteralPath $PromptFile).Path
    [System.IO.File]::ReadAllText($resolvedPromptFile, [System.Text.UTF8Encoding]::new($false))
} else {
    $Prompt
}

$safeTitle = ConvertTo-SafeFileName -Value $Title
$taskFileName = "$safeTitle-$Role.md"
$taskFile = Join-Path $resolvedOutputDirectory $taskFileName
$taskFileStem = [System.IO.Path]::GetFileNameWithoutExtension($taskFileName)

$taskBody = @"
# CCG $Role Task: $Title

## Repository
$repositoryFullPath

## Request
$promptText

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when `-AllowSingleModelWhenQuotaBlocked` is enabled and the other backend produced usable output.
"@

[System.IO.File]::WriteAllText($taskFile, $taskBody, [System.Text.UTF8Encoding]::new($false))

$runnerPath = Join-Path $repositoryFullPath 'docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1'
if (-not (Test-Path -LiteralPath $runnerPath)) {
    throw "Missing self-healing runner: $runnerPath"
}

$runnerArgs = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', $runnerPath,
    '-TaskFile', $taskFile,
    '-Role', $Role,
    '-RepositoryPath', $repositoryFullPath,
    '-OutputDirectory', $resolvedOutputDirectory,
    '-TimeoutSeconds', $TimeoutSeconds,
    '-MaxAttempts', $MaxAttempts
)

if ($AllowSingleModelWhenQuotaBlocked) {
    $runnerArgs += '-AllowSingleModelWhenQuotaBlocked'
}

if ($RunHealthBackendSmoke) {
    $runnerArgs += '-RunHealthBackendSmoke'
}

if ($ClaudeOnly) {
    $runnerArgs += '-ClaudeOnly'
}

& powershell.exe @runnerArgs
$runnerExitCode = $LASTEXITCODE

# 保母級說明：
# 這個入口腳本允許「本機流程繼續」與「完整雙模型 REVIEW 成功」分開判斷。
# 底層 runner 在 `-AllowSingleModelWhenQuotaBlocked` 啟用時，若一個模型成功、
# 另一個模型只是 quota/session 被擋，會以 exit code 0 讓任務不中斷。
# 但是 exit code 0 不代表一定是完整雙模型成功；呼叫端仍必須看 summary.json 的
# `dualModelComplete`、`reviewMode`、`degradedFallback`、`quotaBlocked` 欄位。
#
# 這裡刻意用本次 task 檔名 stem 比對 run folder，而不是單純抓最新 summary.json。
# 原因是 CCG run 可能連續執行，若只依 LastWriteTime 取最新資料夾，未來在並行或
# 手動搬移舊資料夾時可能讀到別次 review 的 summary。底層 runner 的 runId 格式是：
#   yyyyMMdd-HHmmss-<taskFileStem>
# 因此用 "*-$taskFileStem" 可以穩定找到本次 Start 腳本剛委派出去的 run。
$matchingSummaryDirectories = Get-ChildItem -LiteralPath $resolvedOutputDirectory -Directory -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "*-$taskFileStem" -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'summary.json'))
    } |
    Sort-Object LastWriteTime -Descending

$latestSummaryDirectory = $matchingSummaryDirectories | Select-Object -First 1

if ($latestSummaryDirectory) {
    $summaryPath = Join-Path $latestSummaryDirectory.FullName 'summary.json'
    try {
        $summary = Get-Content -LiteralPath $summaryPath -Encoding UTF8 -Raw | ConvertFrom-Json
        if ($summary.degradedFallback) {
            Write-Warning "CCG completed with DEGRADED FALLBACK. At least one backend succeeded, but this is not a completed dual-model review. Read: $summaryPath"
        } elseif ($summary.ok -and $summary.reviewMode -eq 'claude-only') {
            Write-Host "CCG completed with requested Claude-only success. Read: $summaryPath"
        } elseif ($summary.dualModelComplete) {
            Write-Host "CCG completed with full dual-model success. Read: $summaryPath"
        } elseif ($summary.quotaBlocked) {
            Write-Warning "CCG stopped on provider quota/session state and did not produce an accepted fallback. Read: $summaryPath"
        } else {
            Write-Warning "CCG did not complete successfully. Read: $summaryPath"
        }
    } catch {
        Write-Warning "CCG runner finished, but summary.json could not be parsed: $summaryPath. $($_.Exception.Message)"
    }
} else {
    Write-Warning "CCG runner finished, but no summary.json matching task '$taskFileStem' was found under $resolvedOutputDirectory."
}

exit $runnerExitCode
