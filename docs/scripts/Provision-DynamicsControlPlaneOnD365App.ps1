[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SchemaFile,

    [string] $WorkingDirectory = 'C:\ProgramData\SpeechMessage\DynamicsControlPlane',

    [string] $SqlExecutionPrincipal = 'SYSTEM'
)

$ErrorActionPreference = 'Stop'
$databaseName = 'SpeechMessageDynamicsControlPlane'
$taskName = 'SpeechMessage-DynamicsControlPlane-Provision'
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$schemaPath = [IO.Path]::GetFullPath($SchemaFile)
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)

if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "sqlcmd not found: $sqlcmd" }
if (-not (Test-Path -LiteralPath $schemaPath)) { throw "Schema file not found: $schemaPath" }
if (-not $workingPath.StartsWith('C:\ProgramData\SpeechMessage\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "WorkingDirectory must stay under C:\ProgramData\SpeechMessage: $workingPath"
}

New-Item -ItemType Directory -Path $workingPath -Force | Out-Null
$bootstrapPath = Join-Path $workingPath 'bootstrap.sql'
$outputPath = Join-Path $workingPath 'provision-result.txt'
$commandPath = Join-Path $workingPath 'provision.cmd'

$schema = [IO.File]::ReadAllText($schemaPath, [Text.UTF8Encoding]::new($false))
$bootstrap = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_ID(N'$databaseName') IS NULL CREATE DATABASE [$databaseName];
GO
ALTER DATABASE [$databaseName] SET RECOVERY SIMPLE;
GO
USE [$databaseName];
GO
$schema
"@
[IO.File]::WriteAllText($bootstrapPath, $bootstrap, [Text.UTF8Encoding]::new($false))

$command = "@echo off`r`n`"$sqlcmd`" -S localhost -E -b -i `"$bootstrapPath`" -o `"$outputPath`" -W`r`nexit /b %ERRORLEVEL%`r`n"
[IO.File]::WriteAllText($commandPath, $command, [Text.ASCIIEncoding]::new())
Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue

$action = New-ScheduledTaskAction -Execute $commandPath
$logonType = if (
    [string]::Equals($SqlExecutionPrincipal, 'SYSTEM', [StringComparison]::OrdinalIgnoreCase) -or
    $SqlExecutionPrincipal.StartsWith('NT SERVICE\', [StringComparison]::OrdinalIgnoreCase)) {
    'ServiceAccount'
} else {
    'S4U'
}
$principal = New-ScheduledTaskPrincipal -UserId $SqlExecutionPrincipal -LogonType $logonType -RunLevel Highest
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force -ErrorAction Stop | Out-Null
try {
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 250
        $state = (Get-ScheduledTask -TaskName $taskName -ErrorAction Stop).State
    } while ($state -eq 'Running' -and [DateTime]::UtcNow -lt $deadline)

    if ($state -eq 'Running') { throw 'SQL provisioning task exceeded 60 seconds.' }
    $info = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction Stop
    if ($info.LastTaskResult -ne 0) {
        $detail = if (Test-Path -LiteralPath $outputPath) { Get-Content -Raw -LiteralPath $outputPath } else { '(no output)' }
        throw "SQL provisioning failed with task result $($info.LastTaskResult): $detail"
    }

    [pscustomobject]@{
        DatabaseName = $databaseName
        TaskResult = $info.LastTaskResult
        OutputPath = $outputPath
        Output = Get-Content -Raw -LiteralPath $outputPath
    }
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $commandPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
}
