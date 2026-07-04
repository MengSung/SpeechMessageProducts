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
    $quotaBlocked = $combined -match "session limit|rate limit|quota|429|usage limit|You've hit your session limit"
    $ok = ($result.ExitCode -eq 0 -and $combined -match [regex]::Escape($ExpectedText))
    $diagnostic = $null

    if ($Backend -eq "claude" -and -not $ok -and -not $quotaBlocked) {
        # codeagent-wrapper sometimes collapses Claude provider errors into only
        # "claude exited with status 1". Probe Claude directly so this script can
        # distinguish a local wrapper/toolchain problem from a provider quota block.
        $claudePath = Resolve-ExecutablePath `
            -Name "claude.cmd" `
            -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\claude.cmd", "C:\Users\Administrator\.claude\bin\claude.cmd")

        if ($claudePath) {
            $directProbe = Invoke-CommandCapture `
                -FilePath $claudePath `
                -Arguments @("-p", "Smoke test only. Reply with exactly: CLAUDE_DIRECT_HEALTH_OK", "--dangerously-skip-permissions", "--output-format", "text") `
                -TimeoutSeconds 120 `
                -WorkingDirectory $RepositoryPath

            $diagnostic = (($directProbe.StdOut + "`n" + $directProbe.StdErr) -replace "`r", "").Trim()
            if ($diagnostic -match "session limit|rate limit|quota|429|usage limit|You've hit your session limit") {
                $quotaBlocked = $true
            }
        }
    }

    [pscustomobject]@{
        Backend = $Backend
        Ok = $ok
        ExitCode = $result.ExitCode
        TimedOut = $result.TimedOut
        QuotaBlocked = $quotaBlocked
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

$wantedPathEntries = @(
    "C:\Users\Administrator\AppData\Roaming\npm",
    "C:\Users\Administrator\.claude\bin",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Python314",
    "C:\Users\Administrator\AppData\Local\Programs\Python\Launcher"
)

$currentProcessPath = @($env:Path -split ";" | Where-Object { $_ -and $_.Trim() -ne "" })
foreach ($entry in $wantedPathEntries) {
    if ((Test-Path -LiteralPath $entry) -and -not ($currentProcessPath -contains $entry)) {
        $currentProcessPath += $entry
    }
}
$env:Path = ($currentProcessPath | Select-Object -Unique) -join ";"

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
