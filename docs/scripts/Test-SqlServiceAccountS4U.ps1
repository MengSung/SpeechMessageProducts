[CmdletBinding()]
param(
    [string] $SqlServiceAccount = 'SPEECHMESSAGE\svc_sql',
    [string] $WorkingDirectory = 'C:\ProgramData\SpeechMessage\DynamicsControlPlane'
)

$ErrorActionPreference = 'Stop'
$taskName = 'SpeechMessage-SqlSvc-S4U-Probe'
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)
if (-not $workingPath.StartsWith('C:\ProgramData\SpeechMessage\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'WorkingDirectory must stay under C:\ProgramData\SpeechMessage.'
}
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "sqlcmd not found: $sqlcmd" }

New-Item -ItemType Directory -Path $workingPath -Force | Out-Null
$commandPath = Join-Path $workingPath 's4u-probe.cmd'
$outputPath = Join-Path $workingPath 's4u-probe.txt'
$lines = @(
    '@echo off',
    "whoami > `"$outputPath`"",
    "`"$sqlcmd`" -S localhost -E -Q `"SET NOCOUNT ON; SELECT SYSTEM_USER LoginName, IS_SRVROLEMEMBER('sysadmin') IsSysAdmin`" -W >> `"$outputPath`" 2>&1",
    'exit /b %ERRORLEVEL%'
)
[IO.File]::WriteAllLines($commandPath, $lines, [Text.ASCIIEncoding]::new())
Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue

$start = (Get-Date).AddMinutes(1).ToString('HH:mm')
& schtasks.exe /Create /TN $taskName /TR $commandPath /SC ONCE /ST $start /RU $SqlServiceAccount /NP /RL HIGHEST /F | Out-Null
if ($LASTEXITCODE -ne 0) { throw "schtasks create failed: $LASTEXITCODE" }
try {
    & schtasks.exe /Run /TN $taskName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "schtasks run failed: $LASTEXITCODE" }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $state = (Get-ScheduledTask -TaskName $taskName -ErrorAction Stop).State
    } while ($state -eq 'Running' -and [DateTime]::UtcNow -lt $deadline)

    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw 'S4U probe produced no output.'
    }

    Get-Content -LiteralPath $outputPath
}
finally {
    & schtasks.exe /Delete /TN $taskName /F | Out-Null
    Remove-Item -LiteralPath $commandPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
}
