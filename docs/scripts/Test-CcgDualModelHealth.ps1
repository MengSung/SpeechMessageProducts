param(
    [string]$RepositoryPath = (Get-Location).Path,
    [string]$OutputDirectory = ".\.ccg\dual-model-runs",
    [switch]$SkipBackendSmoke
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function New-DirectoryIfMissing {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
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

function Invoke-CommandCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$InputText = $null,
        [int]$TimeoutSeconds = 180,
        [string]$WorkingDirectory = $RepositoryPath
    )

    $tempBase = Join-Path ([System.IO.Path]::GetTempPath()) ("ccg-health-" + [Guid]::NewGuid().ToString("N"))
    $stdoutPath = "$tempBase.out"
    $stderrPath = "$tempBase.err"
    $stdinPath = "$tempBase.in"

    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $FilePath
        $startInfo.WorkingDirectory = $WorkingDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardInput = ($null -ne $InputText)
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

        if ($null -ne $InputText) {
            [System.IO.File]::WriteAllText($stdinPath, $InputText, [System.Text.UTF8Encoding]::new($false))
            $stdin = [System.IO.StreamWriter]::new($process.StandardInput.BaseStream, [System.Text.UTF8Encoding]::new($false))
            $stdin.Write($InputText)
            $stdin.Close()
        }

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
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath, $stdinPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-BackendSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$Backend,
        [Parameter(Mandatory = $true)][string]$ExpectedText,
        [Parameter(Mandatory = $true)][string]$WrapperPath,
        [Parameter(Mandatory = $true)][string]$RoleFile
    )

    $prompt = @"
ROLE_FILE: $RoleFile
<TASK>
Smoke test only. Reply with exactly: $ExpectedText
</TASK>
OUTPUT: one line only
"@

    $result = Invoke-CommandCapture `
        -FilePath $WrapperPath `
        -Arguments @("--lite", "--backend", $Backend, "-", $RepositoryPath) `
        -InputText $prompt `
        -TimeoutSeconds 180 `
        -WorkingDirectory $RepositoryPath

    $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
    $quotaBlockedPattern = "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)"
    $errorCombined = (($result.StdErr) -replace "`r", "")
    $quotaBlocked = $errorCombined -match $quotaBlockedPattern
    $ok = ($result.ExitCode -eq 0 -and $combined -match [regex]::Escape($ExpectedText))
    $diagnostic = $null

    if ($Backend -eq "gemini" -and -not $ok -and -not $quotaBlocked -and ($result.ExitCode -ne 0 -or $result.TimedOut)) {
        # Gemini sometimes reports only a generic wrapper failure while direct
        # CLI stderr includes the provider reason, such as exhausted balance.
        $geminiPath = Resolve-ExecutablePath `
            -Name "gemini.cmd" `
            -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd", "C:\Users\Administrator\.claude\bin\gemini.cmd")

        if ($geminiPath) {
            $directProbe = Invoke-CommandCapture `
                -FilePath $geminiPath `
                -Arguments @("-o", "stream-json", "-y") `
                -InputText "Smoke test only. Reply with exactly: GEMINI_DIRECT_HEALTH_OK" `
                -TimeoutSeconds 120 `
                -WorkingDirectory $RepositoryPath

            $diagnostic = (($directProbe.StdOut + "`n" + $directProbe.StdErr) -replace "`r", "").Trim()
            if ($diagnostic -match $quotaBlockedPattern) {
                $quotaBlocked = $true
            }
        }
    }

    if ($Backend -eq "claude" -and -not $ok -and -not $quotaBlocked) {
        # codeagent-wrapper sometimes collapses Claude provider errors into only
        # "claude exited with status 1". Probe Claude directly so this script can
        # distinguish a local wrapper/toolchain problem from a provider quota block.
        $claudePath = Resolve-ExecutablePath `
            -Name "claude.cmd" `
            -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\claude.cmd", "C:\Users\Administrator\.claude\bin\claude.cmd")

        if ($claudePath) {
            $directProbeArguments = @("-p", "Smoke test only. Reply with exactly: CLAUDE_DIRECT_HEALTH_OK")
            if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
                $directProbeArguments += @("--model", $env:CLAUDE_MODEL)
            }
            $directProbeArguments += @("--dangerously-skip-permissions", "--output-format", "text")

            $directProbe = Invoke-CommandCapture `
                -FilePath $claudePath `
                -Arguments $directProbeArguments `
                -TimeoutSeconds 120 `
                -WorkingDirectory $RepositoryPath

            $diagnostic = (($directProbe.StdOut + "`n" + $directProbe.StdErr) -replace "`r", "").Trim()
            if ($diagnostic -match $quotaBlockedPattern) {
                $quotaBlocked = $true
            }
        }
    }

    $failureReason = "ok"
    if (-not $ok) {
        if ($quotaBlocked) {
            $failureReason = "provider-quota-or-billing-blocked"
        }
        elseif ($result.TimedOut) {
            $failureReason = "timeout"
        }
        elseif ($result.ExitCode -eq 0) {
            $failureReason = "output-mismatch"
        }
        else {
            $failureReason = "backend-exit-$($result.ExitCode)"
        }
    }

    if ($quotaBlocked -and [string]::IsNullOrWhiteSpace($diagnostic)) {
        $lines = @($combined -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $priorityLines = @(
            $lines | Where-Object {
                $_ -match "(?i)(error when talking to gemini api|_apierror|api error|api key not valid|invalid_argument|resource_exhausted|insufficient_quota|quota|billing|payment|required|status\s*:?\s*(400|403|429)|exited with status\s+(400|403|429))"
            }
        )
        $diagnostic = if ($priorityLines.Count -gt 0) {
            ($priorityLines | Select-Object -First 3) -join " "
        } else {
            ($lines | Select-Object -First 6) -join " "
        }
    }

    [pscustomobject]@{
        Backend = $Backend
        Ok = $ok
        ExitCode = $result.ExitCode
        TimedOut = $result.TimedOut
        QuotaBlocked = $quotaBlocked
        FailureReason = $failureReason
        Diagnostic = $diagnostic
        Output = $combined.Trim()
    }
}

$repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
Set-Location -LiteralPath $repositoryFullPath

$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryFullPath $OutputDirectory
}
New-DirectoryIfMissing -Path $resolvedOutputDirectory

$env:GEMINI_CLI_TRUST_WORKSPACE = "true"
$env:CODEAGENT_LITE_MODE = "true"
$env:PYTHONIOENCODING = "utf-8"
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

$wantedPathEntries = @(
    "C:\Users\Administrator\AppData\Roaming\npm",
    "C:\Users\Administrator\.claude\bin",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python314",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Launcher"
)

$currentProcessPath = @($env:Path -split ";" | Where-Object { $_ -and $_.Trim() -ne "" })
$priorityPathEntries = @()
if ($claudeShim) {
    $priorityPathEntries += $claudeShim.Directory
}
$priorityPathEntries += $wantedPathEntries
foreach ($entry in $priorityPathEntries) {
    if ((Test-Path -LiteralPath $entry) -and -not ($currentProcessPath -contains $entry)) {
        $currentProcessPath += $entry
    }
}
$env:Path = (@($priorityPathEntries + $currentProcessPath | Where-Object { $_ -and $_.Trim() -ne "" }) | Select-Object -Unique) -join ";"

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$userPathEntries = @()
if ($userPath) {
    $userPathEntries = @($userPath -split ";" | Where-Object { $_ -and $_.Trim() -ne "" })
}
$changedUserPath = $false
foreach ($entry in $wantedPathEntries) {
    if ((Test-Path -LiteralPath $entry) -and -not ($userPathEntries -contains $entry)) {
        $userPathEntries += $entry
        $changedUserPath = $true
    }
}
if ($changedUserPath) {
    [Environment]::SetEnvironmentVariable("Path", (($userPathEntries | Select-Object -Unique) -join ";"), "User")
}

$wrapperPath = Resolve-ExecutablePath `
    -Name "codeagent-wrapper.exe" `
    -FallbackPaths @("C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe")
$geminiPath = Resolve-ExecutablePath `
    -Name "gemini.cmd" `
    -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd", "C:\Users\Administrator\.claude\bin\gemini.cmd")
$claudePath = Resolve-ExecutablePath `
    -Name "claude.cmd" `
    -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\claude.cmd", "C:\Users\Administrator\.claude\bin\claude.cmd")
$pythonPath = Resolve-ExecutablePath `
    -Name "python.exe" `
    -FallbackPaths @("C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe")

$summary = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    repositoryPath = $repositoryFullPath
    changedUserPath = $changedUserPath
    environment = [ordered]@{
        GEMINI_CLI_TRUST_WORKSPACE = $env:GEMINI_CLI_TRUST_WORKSPACE
        CODEAGENT_LITE_MODE = $env:CODEAGENT_LITE_MODE
        PYTHONIOENCODING = $env:PYTHONIOENCODING
        CLAUDE_MODEL = $env:CLAUDE_MODEL
        CLAUDE_MODEL_SHIM = $env:CLAUDE_MODEL_SHIM
        CCG_CLAUDE_MODEL_SHIM_DIR = $env:CCG_CLAUDE_MODEL_SHIM_DIR
        CLAUDE_REAL_COMMAND = if ($claudeShim) { $claudeShim.RealClaude } else { $null }
    }
    executables = [ordered]@{
        wrapper = $wrapperPath
        gemini = $geminiPath
        claude = $claudePath
        python = $pythonPath
    }
    smoke = @()
    ok = $false
    repairable = $true
    notes = @()
}

if (-not $wrapperPath) { $summary.notes += "codeagent-wrapper.exe not found." }
if (-not $geminiPath) { $summary.notes += "gemini.cmd not found." }
if (-not $claudePath) { $summary.notes += "claude.cmd not found." }
if (-not $pythonPath) { $summary.notes += "python.exe not found." }

if ($wrapperPath) {
    $wrapperVersion = Invoke-CommandCapture -FilePath $wrapperPath -Arguments @("--version") -TimeoutSeconds 30
    $summary.wrapperVersion = (($wrapperVersion.StdOut + "`n" + $wrapperVersion.StdErr) -replace "`r", "").Trim()
}

if (-not $SkipBackendSmoke -and $wrapperPath -and $geminiPath -and $claudePath -and $pythonPath) {
    $geminiRoleFile = "C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md"
    $claudeRoleFile = "C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md"

    $summary.smoke += Test-BackendSmoke `
        -Backend "gemini" `
        -ExpectedText "GEMINI_BACKEND_OK" `
        -WrapperPath $wrapperPath `
        -RoleFile $geminiRoleFile

    $summary.smoke += Test-BackendSmoke `
        -Backend "claude" `
        -ExpectedText "CLAUDE_BACKEND_OK" `
        -WrapperPath $wrapperPath `
        -RoleFile $claudeRoleFile
}

$summary.ok = (
    $wrapperPath -and
    $geminiPath -and
    $claudePath -and
    $pythonPath -and
    (
        $SkipBackendSmoke -or
        (
            @($summary.smoke).Count -eq 2 -and
            -not (@($summary.smoke) | Where-Object { -not $_.Ok })
        )
    )
)

if (@($summary.smoke) | Where-Object { $_.QuotaBlocked }) {
    $summary.repairable = $false
    $summary.notes += "At least one backend is blocked by provider quota or session limit. This cannot be repaired locally."
}

$healthPath = Join-Path $resolvedOutputDirectory ("ccg-health-" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".json")
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $healthPath -Encoding UTF8

$summary.healthReport = $healthPath
$summary | ConvertTo-Json -Depth 8

if ($summary.ok) {
    exit 0
}

if ($summary.repairable) {
    exit 2
}

exit 3
