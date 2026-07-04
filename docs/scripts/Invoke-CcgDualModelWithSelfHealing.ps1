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
        [string[]]$Entries
    )

    $pathEntries = @()
    if ($OriginalPath) {
        $pathEntries = @($OriginalPath -split ";" | Where-Object { $_ -and $_.Trim() -ne "" })
    }

    $changed = $false
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

function Initialize-CcgToolchainEnvironment {
    $toolPathEntries = @(Get-CcgToolPathEntries)

    $processPath = Add-UniquePathEntries -OriginalPath $env:Path -Entries $toolPathEntries
    if ($processPath.Changed) {
        $env:Path = $processPath.Path
    }

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $userPathResult = Add-UniquePathEntries -OriginalPath $userPath -Entries $toolPathEntries
    if ($userPathResult.Changed) {
        [Environment]::SetEnvironmentVariable("Path", $userPathResult.Path, "User")
    }

    $env:GEMINI_CLI_TRUST_WORKSPACE = "true"
    $env:CODEAGENT_LITE_MODE = "true"
    $env:PYTHONIOENCODING = "utf-8"

    return [pscustomobject]@{
        ToolPathEntries = $toolPathEntries
        ChangedProcessPath = $processPath.Changed
        ChangedUserPath = $userPathResult.Changed
        GEMINI_CLI_TRUST_WORKSPACE = $env:GEMINI_CLI_TRUST_WORKSPACE
        CODEAGENT_LITE_MODE = $env:CODEAGENT_LITE_MODE
        PYTHONIOENCODING = $env:PYTHONIOENCODING
    }
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
    return ($Text -match "(?i)(you'?ve hit your session limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b)")
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

function Invoke-ClaudeDirectQuotaProbe {
    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)

    $claudeCandidates = @(
        "C:\Users\Administrator\AppData\Roaming\npm\claude.cmd",
        "C:\Users\Administrator\.claude\bin\claude.cmd"
    )

    $claudePath = $claudeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $claudePath) {
        return [pscustomobject]@{
            Ran = $false
            QuotaBlocked = $false
            Output = "claude.cmd not found for direct quota probe."
        }
    }

    $probe = Invoke-ProcessCapture `
        -FilePath $claudePath `
        -Arguments @("-p", "Smoke test only. Reply with exactly: CLAUDE_DIRECT_QUOTA_PROBE_OK", "--dangerously-skip-permissions", "--output-format", "text") `
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

$toolchainEnvironment = Initialize-CcgToolchainEnvironment

$repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
Set-Location -LiteralPath $repositoryFullPath

$resolvedTaskFile = (Resolve-Path -LiteralPath $TaskFile).Path
$taskText = [System.IO.File]::ReadAllText($resolvedTaskFile, [System.Text.UTF8Encoding]::new($false))

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
    $healthArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $healthScript, "-RepositoryPath", $repositoryFullPath, "-OutputDirectory", $runDirectory)
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

    foreach ($backend in @("gemini", "claude")) {
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

        if ($backend -eq "claude" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
            # Claude Code may expose the real provider/session-limit error only
            # when invoked directly. If wrapper stderr only says exit 1, run a
            # direct probe so the pipeline can stop as "external quota blocked"
            # instead of repeatedly trying local repairs that cannot help.
            $directProbe = Invoke-ClaudeDirectQuotaProbe -WorkingDirectory $repositoryFullPath
            $diagnostic = $directProbe.Output
            if ($directProbe.QuotaBlocked) {
                $quotaBlocked = $true
            }
        }

        $producedOutput = Test-BackendProducedOutput -Result $result -Role $Role
        $backendOk = ($result.ExitCode -eq 0 -and -not $result.TimedOut -and -not $quotaBlocked -and $producedOutput)

        $attemptRecord.backends += [ordered]@{
            backend = $backend
            ok = $backendOk
            exitCode = $result.ExitCode
            timedOut = $result.TimedOut
            quotaBlocked = $quotaBlocked
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
        $summary.completedBackends = @("gemini", "claude")
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
