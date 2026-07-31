[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SchemaFile,

    [string] $RuntimeWindowsIdentity = 'NT AUTHORITY\NETWORK SERVICE',
    [string] $WorkingDirectory = 'C:\ProgramData\SpeechMessage\DynamicsControlPlane'
)

$ErrorActionPreference = 'Stop'
$databaseName = 'SpeechMessageDynamicsControlPlane'
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$instanceId = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').MSSQLSERVER
$parameterPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer\Parameters"
$singleUserArgumentName = 'SQLArg3'
$singleUserArgumentValue = '-mSQLCMD'
$schemaPath = [IO.Path]::GetFullPath($SchemaFile)
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)

if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "sqlcmd not found: $sqlcmd" }
if (-not (Test-Path -LiteralPath $schemaPath)) { throw "Schema file not found: $schemaPath" }
if (-not $workingPath.StartsWith('C:\ProgramData\SpeechMessage\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'WorkingDirectory must stay under C:\ProgramData\SpeechMessage.'
}
if ([string]::Equals($databaseName, 'MSCRM_CONFIG', [StringComparison]::OrdinalIgnoreCase) -or
    $databaseName.EndsWith('_MSCRM', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'CRM databases are forbidden provisioning targets.'
}

$parameterState = Get-ItemProperty -LiteralPath $parameterPath
if ($parameterState.PSObject.Properties[$singleUserArgumentName]) {
    throw "Refusing to overwrite existing $singleUserArgumentName."
}

$managedServiceNames = @(
    'MSCRMAsyncService',
    'MSCRMAsyncService$maintenance',
    'MSCRMMonitoringService',
    'MSCRMSandboxService',
    'MSCRMUnzipService',
    'MSCRMVssWriterService',
    'SQLSERVERAGENT',
    'SQLServerReportingServices',
    'SQLTELEMETRY',
    'SQLWriter',
    'W3SVC'
)
$initiallyRunning = @(Get-Service -Name $managedServiceNames -ErrorAction SilentlyContinue |
    Where-Object Status -eq 'Running' | Select-Object -ExpandProperty Name)
$sqlWasRunning = (Get-Service -Name 'MSSQLSERVER').Status -eq 'Running'

New-Item -ItemType Directory -Path $workingPath -Force | Out-Null
$bootstrapPath = Join-Path $workingPath 'recovery-bootstrap.sql'
$resultPath = Join-Path $workingPath 'recovery-provision-result.txt'
$schema = [IO.File]::ReadAllText($schemaPath, [Text.UTF8Encoding]::new($false))
$escapedIdentity = $RuntimeWindowsIdentity.Replace(']', ']]').Replace('''', '''''')
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
GO
IF SUSER_ID(N'$escapedIdentity') IS NULL CREATE LOGIN [$escapedIdentity] FROM WINDOWS;
GO
USE [$databaseName];
GO
IF USER_ID(N'$escapedIdentity') IS NULL CREATE USER [$escapedIdentity] FOR LOGIN [$escapedIdentity];
IF DATABASE_PRINCIPAL_ID(N'DynamicsCoordinatorRuntime') IS NULL CREATE ROLE DynamicsCoordinatorRuntime;
-- runtime 只取得 lease／epoch／canonical binding 所需的最小資料權限；
-- 不授與 DDL、DELETE、CRM 資料庫或任何可讓程序重綁既有 namespace 的廣泛權限。
GRANT SELECT, INSERT, UPDATE ON dbo.RuntimeHostSlotLease TO DynamicsCoordinatorRuntime;
GRANT SELECT, INSERT ON dbo.RuntimeHostAdmissionEpoch TO DynamicsCoordinatorRuntime;
GRANT SELECT, INSERT ON dbo.RuntimeHostOrganizationBinding TO DynamicsCoordinatorRuntime;
GRANT UPDATE ON OBJECT::dbo.RuntimeHostFencingSequence TO DynamicsCoordinatorRuntime;
ALTER ROLE DynamicsCoordinatorRuntime ADD MEMBER [$escapedIdentity];
SELECT DB_NAME() DatabaseName, SYSTEM_USER ProvisionedBy, N'$escapedIdentity' RuntimeIdentity, SYSUTCDATETIME() ServerUtc;
GO
"@
[IO.File]::WriteAllText($bootstrapPath, $bootstrap, [Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue

$singleUserConfigured = $false
try {
    foreach ($serviceName in $managedServiceNames) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne 'Stopped') {
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
        }
    }

    if ((Get-Service -Name 'MSSQLSERVER').Status -ne 'Stopped') {
        Stop-Service -Name 'MSSQLSERVER' -Force -ErrorAction Stop
    }
    (Get-Service -Name 'MSSQLSERVER').WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

    New-ItemProperty -LiteralPath $parameterPath -Name $singleUserArgumentName -Value $singleUserArgumentValue -PropertyType String -Force | Out-Null
    $singleUserConfigured = $true
    Start-Service -Name 'MSSQLSERVER' -ErrorAction Stop
    (Get-Service -Name 'MSSQLSERVER').WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

    & $sqlcmd -S localhost -E -b -i $bootstrapPath -o $resultPath -W
    if ($LASTEXITCODE -ne 0) {
        $detail = if (Test-Path -LiteralPath $resultPath) { Get-Content -Raw -LiteralPath $resultPath } else { '(no output)' }
        throw "Single-user SQL provisioning failed: $detail"
    }
}
finally {
    $sqlService = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
    if ($sqlService -and $sqlService.Status -ne 'Stopped') {
        Stop-Service -Name 'MSSQLSERVER' -Force -ErrorAction Continue
        $sqlService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }

    if ($singleUserConfigured) {
        Remove-ItemProperty -LiteralPath $parameterPath -Name $singleUserArgumentName -ErrorAction Continue
    }

    if ($sqlWasRunning) {
        Start-Service -Name 'MSSQLSERVER' -ErrorAction Continue
        (Get-Service -Name 'MSSQLSERVER').WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
    }

    foreach ($serviceName in $initiallyRunning) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne 'Running') {
            Start-Service -Name $serviceName -ErrorAction Continue
        }
    }

    Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
}

$failedServices = @(Get-Service -Name $initiallyRunning -ErrorAction SilentlyContinue |
    Where-Object Status -ne 'Running' | Select-Object -ExpandProperty Name)
if ($failedServices.Count -gt 0) {
    throw "Services failed to return to Running: $($failedServices -join ', ')"
}

[pscustomobject]@{
    DatabaseName = $databaseName
    RuntimeIdentity = $RuntimeWindowsIdentity
    Result = Get-Content -Raw -LiteralPath $resultPath
    RestoredServices = $initiallyRunning
    SqlArguments = @(
        (Get-ItemProperty -LiteralPath $parameterPath).PSObject.Properties |
            Where-Object Name -like 'SQLArg*' |
            Sort-Object Name |
            ForEach-Object { "$($_.Name)=$($_.Value)" }
    )
}
