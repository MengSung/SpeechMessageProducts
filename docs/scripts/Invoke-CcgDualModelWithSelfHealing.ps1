param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ })]
    [string]$TaskFile,

    [ValidateSet("analyzer", "architect", "reviewer", "debugger", "tester", "optimizer", "builder")]
    [string]$Role = "reviewer",

    [string]$RepositoryPath = (Get-Location).Path,
    [string]$OutputDirectory = ".\.ccg\dual-model-runs",
    [int]$TimeoutSeconds = 900,
    [int]$MaxAttempts = 2,
    [ValidateSet("dual", "claude")]
    [string]$BackendMode = "dual",
    [switch]$RunHealthBackendSmoke,
    [switch]$AllowSingleModelWhenQuotaBlocked
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Get-CcgToolPathEntries {
    return @(
        "C:\Users\Administrator\AppData\Roaming\npm",
        "C:\Users\Administrator\.claude\bin",
        "C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts",
        "C:\Users\Administrator\AppData\Local\Programs\Python\Python314",
        "C:\Users\Administrator\AppData\Local\Programs\Python\Launcher"
    ) | Where-Object { Test-Path -LiteralPath $_ }
}

function Add-UniquePathEntries {
    param(
        [string]$OriginalPath,
        [string[]]$Entries,
        [switch]$Prepend
    )

    $pathEntries = @()
    if ($OriginalPath) {
        $pathEntries = @($OriginalPath -split ";" | Where-Object { $_ -and $_.Trim() -ne "" })
    }

    $changed = $false
    if ($Prepend) {
        $newPathEntries = @($Entries + $pathEntries | Where-Object { $_ -and $_.Trim() -ne "" } | Select-Object -Unique)
        $changed = (($newPathEntries -join ";") -ne ($pathEntries -join ";"))
        return [pscustomobject]@{
            Path = ($newPathEntries -join ";")
            Changed = $changed
        }
    }

    foreach ($entry in $Entries) {
        if (-not ($pathEntries -contains $entry)) {
            $pathEntries += $entry
            $changed = $true
        }
    }

    return [pscustomobject]@{
        Path = (($pathEntries | Select-Object -Unique) -join ";")
        Changed = $changed
    }
}

function Resolve-CcgRealClaudeCommand {
    foreach ($path in @(
        "C:\Users\Administrator\AppData\Roaming\npm\claude.cmd",
        "C:\Users\Administrator\.claude\bin\claude.cmd"
    )) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return $null
}

function New-CcgClaudeModelShim {
    param([Parameter(Mandatory = $true)][string]$ShimDirectory)

    $realClaudePath = Resolve-CcgRealClaudeCommand
    if (-not $realClaudePath) {
        return $null
    }

    New-DirectoryIfMissing -Path $ShimDirectory
    $shimPath = Join-Path $ShimDirectory "claude.cmd"
    $tempShimPath = Join-Path $ShimDirectory ("claude-" + [Guid]::NewGuid().ToString("N") + ".cmd.tmp")
    $content = @"
@echo off
setlocal
set "CCG_REAL_CLAUDE_CMD=$realClaudePath"
set "CCG_HAS_MODEL=0"
for %%A in (%*) do (
  if /I "%%~A"=="--model" set "CCG_HAS_MODEL=1"
)
if "%CLAUDE_MODEL%"=="" set "CCG_HAS_MODEL=1"
if "%CCG_HAS_MODEL%"=="1" (
  call "%CCG_REAL_CLAUDE_CMD%" %*
) else (
  call "%CCG_REAL_CLAUDE_CMD%" --model "%CLAUDE_MODEL%" %*
)
exit /b %ERRORLEVEL%
"@

    [System.IO.File]::WriteAllText($tempShimPath, $content, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempShimPath -Destination $shimPath -Force

    return [pscustomobject]@{
        Directory = $ShimDirectory
        Script = $shimPath
        RealClaude = $realClaudePath
    }
}

function Initialize-CcgToolchainEnvironment {
    if ([string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
        $env:CLAUDE_MODEL = "sonnet"
    }

    if ([string]::IsNullOrWhiteSpace($env:CCG_CLAUDE_MODEL_SHIM_DIR)) {
        $env:CCG_CLAUDE_MODEL_SHIM_DIR = Join-Path ([System.IO.Path]::GetTempPath()) ("ccg-claude-model-shim-" + $PID + "-" + [Guid]::NewGuid().ToString("N"))
    }

    $claudeShim = New-CcgClaudeModelShim -ShimDirectory $env:CCG_CLAUDE_MODEL_SHIM_DIR
    if ($claudeShim) {
        $env:CLAUDE_MODEL_SHIM = $claudeShim.Script
        $env:CCG_CLAUDE_MODEL_SHIM_DIR = $claudeShim.Directory
    }

    $toolPathEntries = @(Get-CcgToolPathEntries)
    $processToolPathEntries = @()
    if ($claudeShim) {
        $processToolPathEntries += $claudeShim.Directory
    }
    $processToolPathEntries += $toolPathEntries

    $processPath = Add-UniquePathEntries -OriginalPath $env:Path -Entries $processToolPathEntries -Prepend
    if ($processPath.Changed) {
        $env:Path = $processPath.Path
    }

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $userPathResult = Add-UniquePathEntries -OriginalPath $userPath -Entries $toolPathEntries
    if ($userPathResult.Changed) {
        [Environment]::SetEnvironmentVariable("Path", $userPathResult.Path, "User")
    }

    if ($BackendMode -eq 'dual') {
        $env:GEMINI_CLI_TRUST_WORKSPACE = "true"
    }
    $env:CODEAGENT_LITE_MODE = "true"
    $env:PYTHONIOENCODING = "utf-8"

    $environmentSummary = [ordered]@{
        ToolPathEntries = $toolPathEntries
        ChangedProcessPath = $processPath.Changed
        ChangedUserPath = $userPathResult.Changed
        CODEAGENT_LITE_MODE = $env:CODEAGENT_LITE_MODE
        PYTHONIOENCODING = $env:PYTHONIOENCODING
        CLAUDE_MODEL = $env:CLAUDE_MODEL
        CLAUDE_MODEL_SHIM = $env:CLAUDE_MODEL_SHIM
        CCG_CLAUDE_MODEL_SHIM_DIR = $env:CCG_CLAUDE_MODEL_SHIM_DIR
        CLAUDE_REAL_COMMAND = if ($claudeShim) { $claudeShim.RealClaude } else { $null }
    }

    if ($BackendMode -eq 'dual') {
        $environmentSummary.GEMINI_CLI_TRUST_WORKSPACE = $env:GEMINI_CLI_TRUST_WORKSPACE
    }

    return [pscustomobject]$environmentSummary
}

function Resolve-ExecutablePath {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$FallbackPaths = @()
    )

    $command = Get-Command -Name $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $FallbackPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return $null
}

function New-DirectoryIfMissing {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Join-ProcessArguments {
    param([string[]]$Arguments = @())

    return (($Arguments | ForEach-Object {
        if ($null -eq $_ -or $_.Length -eq 0) {
            '""'
        }
        elseif ($_ -notmatch '[\s"]') {
            $_
        }
        else {
            '"' + ($_.Replace('"', '\"')) + '"'
        }
    }) -join ' ')
}

function Invoke-ProcessCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$InputText,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds = 900
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)

    $startInfo.Arguments = Join-ProcessArguments -Arguments $Arguments

    $startInfo.Environment["GEMINI_CLI_TRUST_WORKSPACE"] = "true"
    $startInfo.Environment["CODEAGENT_LITE_MODE"] = "true"
    $startInfo.Environment["PYTHONIOENCODING"] = "utf-8"
    if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
        $startInfo.Environment["CLAUDE_MODEL"] = $env:CLAUDE_MODEL
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL_SHIM)) {
        $startInfo.Environment["CLAUDE_MODEL_SHIM"] = $env:CLAUDE_MODEL_SHIM
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CCG_CLAUDE_MODEL_SHIM_DIR)) {
        $startInfo.Environment["CCG_CLAUDE_MODEL_SHIM_DIR"] = $env:CCG_CLAUDE_MODEL_SHIM_DIR
    }
    $startInfo.Environment["Path"] = $env:Path

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    [void]$process.Start()

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $stdin = [System.IO.StreamWriter]::new($process.StandardInput.BaseStream, [System.Text.UTF8Encoding]::new($false))
    $stdin.Write($InputText)
    $stdin.Close()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }
        return [pscustomobject]@{
            ExitCode = 124
            TimedOut = $true
            StdOut = $stdoutTask.GetAwaiter().GetResult()
            StdErr = $stderrTask.GetAwaiter().GetResult()
        }
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        TimedOut = $false
        StdOut = $stdoutTask.GetAwaiter().GetResult()
        StdErr = $stderrTask.GetAwaiter().GetResult()
    }
}

function Get-RoleFile {
    param(
        [Parameter(Mandatory = $true)][string]$Backend,
        [Parameter(Mandatory = $true)][string]$Role
    )

    return "C:\Users\Administrator\.claude\.ccg\prompts\$Backend\$Role.md"
}

function New-BackendPrompt {
    param(
        [Parameter(Mandatory = $true)][string]$Backend,
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$TaskText
    )

    $roleFile = Get-RoleFile -Backend $Backend -Role $Role
    return @"
ROLE_FILE: $roleFile
<TASK>
$TaskText
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
"@
}

function Test-QuotaBlockedText {
    param([string]$Text)
    return ($Text -match "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)")
}

function Test-BackendQuotaBlocked {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [string]$Diagnostic
    )

    if ($Result.ExitCode -eq 0 -and -not $Result.TimedOut) {
        return $false
    }

    $errorText = (($Result.StdErr + "`n" + $Diagnostic) -replace "`r", "")
    return (Test-QuotaBlockedText -Text $errorText)
}

function Get-ModelResponseText {
    param([string]$StdOut)

    if (-not $StdOut) {
        return ""
    }

    $text = $StdOut -replace "`r", ""
    $taskEnd = $text.LastIndexOf("</TASK>", [StringComparison]::OrdinalIgnoreCase)
    if ($taskEnd -ge 0) {
        $text = $text.Substring($taskEnd + "</TASK>".Length)
    }

    return $text.Trim()
}

function Test-BackendProducedOutput {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Role
    )

    $modelOutput = Get-ModelResponseText -StdOut $Result.StdOut
    if ([string]::IsNullOrWhiteSpace($modelOutput)) {
        return $false
    }

    if ($Role -eq "reviewer") {
        return ($modelOutput -match "(?i)Critical|Warning|Info")
    }

    return ($modelOutput.Length -ge 20)
}

function Get-BackendFailureReason {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [bool]$BackendOk,
        [bool]$QuotaBlocked,
        [bool]$ProducedOutput
    )

    if ($BackendOk) {
        return "ok"
    }
    if ($QuotaBlocked) {
        return "provider-quota-or-billing-blocked"
    }
    if ($Result.TimedOut) {
        return "timeout"
    }
    if (-not $ProducedOutput) {
        return "no-usable-output"
    }
    return "backend-exit-$($Result.ExitCode)"
}

function Get-ShortDiagnostic {
    param(
        [string]$Text,
        [string]$Fallback
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Fallback
    }

    $lines = @(
        ($Text -replace "`r", "") -split "`n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $priorityLines = @(
        $lines | Where-Object {
            $_ -match "(?i)(error when talking to gemini api|_apierror|api error|api key not valid|invalid_argument|resource_exhausted|insufficient_quota|quota|billing|payment|required|status\s*:?\s*(400|403|429)|exited with status\s+(400|403|429))"
        }
    )
    $normalized = if ($priorityLines.Count -gt 0) {
        ($priorityLines | Select-Object -First 3) -join " "
    } else {
        $lines -join " "
    }
    if ($normalized.Length -gt 500) {
        return $normalized.Substring(0, 500)
    }
    return $normalized
}

function Invoke-ClaudeDirectQuotaProbe {
    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)

    $claudePath = Resolve-ExecutablePath `
        -Name "claude.cmd" `
        -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\claude.cmd", "C:\Users\Administrator\.claude\bin\claude.cmd")
    if (-not $claudePath) {
        return [pscustomobject]@{
            Ran = $false
            QuotaBlocked = $false
            Output = "claude.cmd not found for direct quota probe."
        }
    }

    $probeArguments = @("-p", "Smoke test only. Reply with exactly: CLAUDE_DIRECT_QUOTA_PROBE_OK")
    if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
        $probeArguments += @("--model", $env:CLAUDE_MODEL)
    }
    $probeArguments += @("--dangerously-skip-permissions", "--output-format", "text")

    $probe = Invoke-ProcessCapture `
        -FilePath $claudePath `
        -Arguments $probeArguments `
        -InputText "" `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 120

    $combined = (($probe.StdOut + "`n" + $probe.StdErr) -replace "`r", "").Trim()

    return [pscustomobject]@{
        Ran = $true
        QuotaBlocked = (Test-QuotaBlockedText -Text $combined)
        Output = $combined
    }
}

function Invoke-GeminiDirectQuotaProbe {
    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)

    $geminiPath = Resolve-ExecutablePath `
        -Name "gemini.cmd" `
        -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd", "C:\Users\Administrator\.claude\bin\gemini.cmd")
    if (-not $geminiPath) {
        return [pscustomobject]@{
            Ran = $false
            QuotaBlocked = $false
            Output = "gemini.cmd not found for direct quota probe."
        }
    }

    $probe = Invoke-ProcessCapture `
        -FilePath $geminiPath `
        -Arguments @("-o", "stream-json", "-y") `
        -InputText "Smoke test only. Reply with exactly: GEMINI_DIRECT_QUOTA_PROBE_OK" `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 120

    $combined = (($probe.StdOut + "`n" + $probe.StdErr) -replace "`r", "").Trim()

    return [pscustomobject]@{
        Ran = $true
        QuotaBlocked = (Test-QuotaBlockedText -Text $combined)
        Output = $combined
    }
}

$toolchainEnvironment = Initialize-CcgToolchainEnvironment

$repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
Set-Location -LiteralPath $repositoryFullPath

$resolvedTaskFile = (Resolve-Path -LiteralPath $TaskFile).Path
$taskText = [System.IO.File]::ReadAllText($resolvedTaskFile, [System.Text.UTF8Encoding]::new($false))
$selectedBackends = if ($BackendMode -eq 'claude') { @('claude') } else { @('gemini', 'claude') }

$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryFullPath $OutputDirectory
}
New-DirectoryIfMissing -Path $resolvedOutputDirectory

$runId = (Get-Date).ToString("yyyyMMdd-HHmmss") + "-" + [System.IO.Path]::GetFileNameWithoutExtension($resolvedTaskFile)
$runDirectory = Join-Path $resolvedOutputDirectory $runId
New-DirectoryIfMissing -Path $runDirectory

$wrapperPath = Resolve-ExecutablePath `
    -Name "codeagent-wrapper.exe" `
    -FallbackPaths @("C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe")
$healthScript = Join-Path $repositoryFullPath "docs\scripts\Test-CcgDualModelHealth.ps1"
if (-not (Test-Path -LiteralPath $healthScript)) {
    throw "Missing health script: $healthScript"
}

$summary = [ordered]@{
    runId = $runId
    role = $Role
    backendMode = $BackendMode
    repositoryPath = $repositoryFullPath
    taskFile = $resolvedTaskFile
    runDirectory = $runDirectory
    wrapperPath = $wrapperPath
    toolchainEnvironment = $toolchainEnvironment
    healthBackendSmoke = [bool]$RunHealthBackendSmoke
    attempts = @()
    ok = $false
    degradedFallback = $false
    fallbackAccepted = [bool]$AllowSingleModelWhenQuotaBlocked
    quotaBlocked = $false
    completedBackends = @()
    failedBackends = @()
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    $toolchainEnvironment = Initialize-CcgToolchainEnvironment
    $summary.toolchainEnvironment = $toolchainEnvironment

    $healthOutputPath = Join-Path $runDirectory "health-attempt-$attempt.json"
    $healthArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $healthScript, "-RepositoryPath", $repositoryFullPath, "-OutputDirectory", $runDirectory, "-BackendMode", $BackendMode)
    if (-not $RunHealthBackendSmoke) {
        $healthArguments += "-SkipBackendSmoke"
    }

    $healthProcess = Invoke-ProcessCapture `
        -FilePath "powershell.exe" `
        -Arguments $healthArguments `
        -InputText "" `
        -WorkingDirectory $repositoryFullPath `
        -TimeoutSeconds 420

    $healthText = (($healthProcess.StdOut + "`n" + $healthProcess.StdErr) -replace "`r", "")
    [System.IO.File]::WriteAllText($healthOutputPath, $healthText, [System.Text.UTF8Encoding]::new($false))

    $attemptRecord = [ordered]@{
        attempt = $attempt
        healthExitCode = $healthProcess.ExitCode
        healthTimedOut = $healthProcess.TimedOut
        healthOutput = $healthOutputPath
        healthStatus = "passed"
        backends = @()
    }

    if (-not $wrapperPath -or -not (Test-Path -LiteralPath $wrapperPath)) {
        $wrapperPath = Resolve-ExecutablePath `
            -Name "codeagent-wrapper.exe" `
            -FallbackPaths @("C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe")
        $summary.wrapperPath = $wrapperPath
    }

    if (-not $wrapperPath -or -not (Test-Path -LiteralPath $wrapperPath)) {
        $attemptRecord.healthStatus = "missing-wrapper"
        $summary.attempts += $attemptRecord
        break
    }

    if ($healthProcess.ExitCode -eq 2 -or $healthProcess.TimedOut) {
        $attemptRecord.healthStatus = "repairable-health-check-failed"
        $summary.attempts += $attemptRecord

        if ($attempt -lt $MaxAttempts) {
            continue
        }

        break
    }

    if ($healthProcess.ExitCode -ne 0 -and $healthProcess.ExitCode -ne 3) {
        $attemptRecord.healthStatus = "failed-health-check"
        $summary.attempts += $attemptRecord
        break
    }

    if ($healthProcess.ExitCode -eq 3 -and -not $AllowSingleModelWhenQuotaBlocked) {
        $attemptRecord.healthStatus = "provider-quota-blocked"
        $summary.quotaBlocked = $true
        $summary.attempts += $attemptRecord
        break
    }

    foreach ($backend in $selectedBackends) {
        $prompt = New-BackendPrompt -Backend $backend -Role $Role -TaskText $taskText
        $promptPath = Join-Path $runDirectory "$backend-$Role-attempt-$attempt.prompt.md"
        $stdoutPath = Join-Path $runDirectory "$backend-$Role-attempt-$attempt.stdout.md"
        $stderrPath = Join-Path $runDirectory "$backend-$Role-attempt-$attempt.stderr.md"

        [System.IO.File]::WriteAllText($promptPath, $prompt, [System.Text.UTF8Encoding]::new($false))

        $result = Invoke-ProcessCapture `
            -FilePath $wrapperPath `
            -Arguments @("--lite", "--backend", $backend, "-", $repositoryFullPath) `
            -InputText $prompt `
            -WorkingDirectory $repositoryFullPath `
            -TimeoutSeconds $TimeoutSeconds

        [System.IO.File]::WriteAllText($stdoutPath, $result.StdOut, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($stderrPath, $result.StdErr, [System.Text.UTF8Encoding]::new($false))

        $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
        $diagnostic = $null
        $quotaBlocked = Test-BackendQuotaBlocked -Result $result -Diagnostic $diagnostic

        if ($backend -eq "gemini" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
            # Gemini CLI can return a generic 403 through the wrapper while the
            # direct CLI stderr exposes the provider message, such as balance
            # exhaustion. Probe directly so quota/billing blocks are not
            # misclassified as local toolchain failures.
            $directProbe = Invoke-GeminiDirectQuotaProbe -WorkingDirectory $repositoryFullPath
            if ($directProbe.QuotaBlocked) {
                $quotaBlocked = $true
                $diagnostic = $directProbe.Output
            }
        }

        if ($backend -eq "claude" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
            # Claude Code may expose the real provider/session-limit error only
            # when invoked directly. If wrapper stderr only says exit 1, run a
            # direct probe so the pipeline can stop as "external quota blocked"
            # instead of repeatedly trying local repairs that cannot help.
            $directProbe = Invoke-ClaudeDirectQuotaProbe -WorkingDirectory $repositoryFullPath
            if ($directProbe.QuotaBlocked) {
                $quotaBlocked = $true
                $diagnostic = $directProbe.Output
            }
        }

        $producedOutput = Test-BackendProducedOutput -Result $result -Role $Role
        $backendOk = ($result.ExitCode -eq 0 -and -not $result.TimedOut -and -not $quotaBlocked -and $producedOutput)
        $failureReason = Get-BackendFailureReason -Result $result -BackendOk $backendOk -QuotaBlocked $quotaBlocked -ProducedOutput $producedOutput
        if ($quotaBlocked -and [string]::IsNullOrWhiteSpace($diagnostic)) {
            $diagnostic = Get-ShortDiagnostic -Text ($result.StdErr + "`n" + $result.StdOut) -Fallback "Provider quota or billing block detected."
        }

        $attemptRecord.backends += [ordered]@{
            backend = $backend
            ok = $backendOk
            exitCode = $result.ExitCode
            timedOut = $result.TimedOut
            quotaBlocked = $quotaBlocked
            failureReason = $failureReason
            producedOutput = $producedOutput
            outputLength = (Get-ModelResponseText -StdOut $result.StdOut).Length
            diagnostic = $diagnostic
            prompt = $promptPath
            stdout = $stdoutPath
            stderr = $stderrPath
        }
    }

    $summary.attempts += $attemptRecord

    $failed = @($attemptRecord.backends | Where-Object { -not $_.ok })
    if ($failed.Count -eq 0) {
        $summary.ok = $true
        $summary.completedBackends = @($selectedBackends)
        break
    }

    if (@($failed | Where-Object { $_.quotaBlocked }).Count -gt 0) {
        $summary.quotaBlocked = $true
        if ($AllowSingleModelWhenQuotaBlocked) {
            $summary.completedBackends = @($attemptRecord.backends | Where-Object { $_.ok } | ForEach-Object { $_.backend })
            $summary.failedBackends = @($failed | ForEach-Object { $_.backend })
            if (@($summary.completedBackends).Count -gt 0) {
                $summary.degradedFallback = $true
            }
            break
        }
    }
}

if (-not $summary.ok -and -not $summary.failedBackends.Count) {
    $lastAttempt = @($summary.attempts)[@($summary.attempts).Count - 1]
    $summary.completedBackends = @($lastAttempt.backends | Where-Object { $_.ok } | ForEach-Object { $_.backend })
    $summary.failedBackends = @($lastAttempt.backends | Where-Object { -not $_.ok } | ForEach-Object { $_.backend })
}

$summaryPath = Join-Path $runDirectory "summary.json"
$markdownPath = Join-Path $runDirectory "summary.md"
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$markdown = @(
    "# CCG Dual Model Run Summary",
    "",
    "- RunId: $runId",
    "- Role: $Role",
    "- Repository: $repositoryFullPath",
    "- TaskFile: $resolvedTaskFile",
    "- OK: $($summary.ok)",
    "- DegradedFallback: $($summary.degradedFallback)",
    "- FallbackAccepted: $($summary.fallbackAccepted)",
    "- QuotaBlocked: $($summary.quotaBlocked)",
    "- CompletedBackends: $($summary.completedBackends -join ', ')",
    "- FailedBackends: $($summary.failedBackends -join ', ')",
    "",
    "Artifacts are stored in:",
    "",
    $runDirectory
) -join [Environment]::NewLine
[System.IO.File]::WriteAllText($markdownPath, $markdown, [System.Text.UTF8Encoding]::new($false))

$summary | ConvertTo-Json -Depth 10

if ($summary.ok) {
    exit 0
}

if ($summary.degradedFallback) {
    exit 0
}

if ($summary.quotaBlocked) {
    exit 3
}

exit 2
