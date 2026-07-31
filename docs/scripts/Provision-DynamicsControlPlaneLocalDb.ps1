[CmdletBinding()]
param(
    [ValidateSet('MSSQLLocalDB')]
    [string] $InstanceName = 'MSSQLLocalDB',

    [ValidateSet('SpeechMessageDynamicsControlPlane')]
    [string] $DatabaseName = 'SpeechMessageDynamicsControlPlane',

    [string] $SchemaFile = '',

    [switch] $RemoveDrainedUnboundEpochs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

<#
Windows PowerShell 5.1 在 param 預設值綁定階段尚未可靠提供 PSScriptRoot；
因此先完成參數綁定，再從 script 位置解析 checked-in schema，避免依賴呼叫端目前工作目錄。
#>
if ([string]::IsNullOrWhiteSpace($SchemaFile)) {
    $SchemaFile = Join-Path $PSScriptRoot '..\..\eng\dynamics-control-plane-schema.sql'
}

<#
此 script 是 Local Gateway Development control-plane schema 的唯一人工 provisioning owner。
LocalDB instance、database 與 schema 都限制為 repository 已審查的固定值；任何不同輸入都在啟動 native process 前失敗。
LocalDB 只允許建立它的同一個 Windows 使用者存取，適合單機 Development 與整合測試；
這項證據不代表 Central Gateway、多主機或跨服務帳號 production coordinator 已通過驗證。
Gateway startup 不得呼叫本 script，也不得自行建表；startup 的責任只有 VerifySchemaAsync 與 fail-closed readiness。

併行與生命週期：named mutex 是同一 Windows session 中唯一的 provisioning owner，避免兩個 shell 同時 CREATE DATABASE。
native process 以同步方式執行，結束後才進下一階段；不建立背景工作、timer、暫存 SQL 檔或未觀察 Task。
finally 一定釋放 mutex。SQL connection/process 由 sqlcmd 在每次命令結束時回收，不會把 handle 留給 Gateway。

效能取捨：provisioning 是低頻人工操作，故選擇多次短生命週期 sqlcmd 與完整驗證，而不是常駐連線或略過檢查。
重跑時只在 database/object 不存在才建立，checked-in schema 本身也以 OBJECT_ID/COL_LENGTH 保持 idempotent。
#>

$allowedInstanceName = 'MSSQLLocalDB'
$allowedServerName = '(localdb)\MSSQLLocalDB'
$allowedDatabaseName = 'SpeechMessageDynamicsControlPlane'
$expectedSchemaFile = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..\eng\dynamics-control-plane-schema.sql'))
$resolvedSchemaFile = [IO.Path]::GetFullPath($SchemaFile)

if (-not [string]::Equals($InstanceName, $allowedInstanceName, [StringComparison]::Ordinal)) {
    throw "Only the fixed LocalDB instance is allowed: $allowedInstanceName"
}
if (-not [string]::Equals($DatabaseName, $allowedDatabaseName, [StringComparison]::Ordinal)) {
    throw "Only the fixed Dynamics control-plane database is allowed: $allowedDatabaseName"
}
if (-not [string]::Equals($resolvedSchemaFile, $expectedSchemaFile, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SchemaFile must resolve to the checked-in schema: $expectedSchemaFile"
}
if (-not (Test-Path -LiteralPath $resolvedSchemaFile -PathType Leaf)) {
    throw "Schema file not found: $resolvedSchemaFile"
}

<#
.SYNOPSIS
尋找已安裝的 native executable，且只接受 Application command 或明確的固定安裝路徑。

.DESCRIPTION
此 helper 不修改 PATH、不啟動子程序，也不保存任何 credential。回傳值由目前 script 同步使用；
找不到工具時立即失敗，避免換用未審查的 PowerShell module 或遠端 SQL fallback。
#>
function Resolve-RequiredExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CommandName,

        [Parameter(Mandatory = $true)]
        [string[]] $CandidatePaths
    )

    foreach ($candidatePath in $CandidatePaths) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidatePath)
        }
    }

    $command = Get-Command $CommandName -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    throw "Required executable not found: $CommandName"
}

<#
.SYNOPSIS
將同步 native process 的失敗輸出限制在可診斷且有界的例外內容。

.DESCRIPTION
輸入只來自固定 LocalDB/sqlcmd 命令，沒有 caller connection string 或 credential；helper 仍將長度限制為 4096 字元，
避免異常物件保存無界 tool output。成功路徑不輸出這些文字，失敗路徑則保留真正 SQL/tool 訊息而非只有 exit code。
#>
function Get-NativeFailureDetail {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Output
    )

    $detail = (($Output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($detail)) {
        return '(no diagnostic output)'
    }
    if ($detail.Length -gt 4096) {
        return $detail.Substring(0, 4096) + [Environment]::NewLine + '(diagnostic output truncated)'
    }

    return $detail
}

$sqlLocalDbPath = Resolve-RequiredExecutable -CommandName 'sqllocaldb.exe' -CandidatePaths @(
    'C:\Program Files\Microsoft SQL Server\170\Tools\Binn\SqlLocalDB.exe',
    'C:\Program Files\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe'
)
$sqlCmdPath = Resolve-RequiredExecutable -CommandName 'sqlcmd.exe' -CandidatePaths @(
    'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
)

$mutex = [Threading.Mutex]::new(
    $false,
    'Local\SpeechMessage.DynamicsControlPlane.LocalDb.Provision')
$ownsMutex = $false
$removedDrainedUnboundSlotRows = 0
$removedDrainedUnboundEpochRows = 0

try {
    $ownsMutex = $mutex.WaitOne([TimeSpan]::FromSeconds(60))
    if (-not $ownsMutex) {
        throw 'Timed out waiting for the LocalDB provisioning owner.'
    }

    <#
    先確認固定 instance 已存在，再啟動 user-owned LocalDB；任何 native exit code 都立即中止後續 DDL。
    #>
    $localDbInfoOutput = @(& $sqlLocalDbPath info $InstanceName 2>&1)
    $localDbInfoExitCode = $LASTEXITCODE
    if ($localDbInfoExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $localDbInfoOutput
        throw "LocalDB instance is not installed: $InstanceName (exit code $localDbInfoExitCode).$([Environment]::NewLine)$detail"
    }

    $localDbStartOutput = @(& $sqlLocalDbPath start $InstanceName 2>&1)
    $localDbStartExitCode = $LASTEXITCODE
    if ($localDbStartExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $localDbStartOutput
        throw "Failed to start LocalDB instance: $InstanceName (exit code $localDbStartExitCode).$([Environment]::NewLine)$detail"
    }

    <#
    Database 名稱已由固定 allowlist 驗證，因此 identifier 與 Unicode literal 不含 operator 輸入。
    IF DB_ID 使重跑不重建 database；named mutex 同時避免本機兩個 provisioning process 競爭此判斷。
    #>
    $createDatabaseSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_ID(N'$DatabaseName') IS NULL CREATE DATABASE [$DatabaseName];
"@
    $createDatabaseOutput = @(& $sqlCmdPath `
        -S $allowedServerName `
        -E `
        -b `
        -l 5 `
        -t 30 `
        -d master `
        -Q $createDatabaseSql 2>&1)
    $createDatabaseExitCode = $LASTEXITCODE
    if ($createDatabaseExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $createDatabaseOutput
        throw "Failed to create or locate the LocalDB control-plane database (exit code $createDatabaseExitCode).$([Environment]::NewLine)$detail"
    }

    <#
    舊版 LocalDB coordinator 尚未保存 canonical organization binding 時，無法從 epoch/slot 回推它原本指向的組織；
    因此正常 provisioning 一律由 schema fail-closed，絕不在預設路徑刪除任何 durable row。

    此 recovery 僅在 operator 明確帶入 RemoveDrainedUnboundEpochs 時執行，且仍受前方的固定 instance、固定 database、
    checked-in schema 與本機 named mutex 約束。SQL transaction 以 SERIALIZABLE 隔離，先建立單次 connection-owned
    temporary namespace 集合，再拒絕一小時內更新的 epoch、近期 touched／未到期／隔離中的 slot，以及沒有 expiry 的已指派 slot。
    只有所有證據都表示舊 host 已完成 drain 的無 binding row，才依 slot→epoch 順序在同一 transaction 刪除。

    執行後 sqlcmd process、temporary table、transaction 與 output 都在本區塊結束時釋放；PowerShell 只保存有界的計數，
    不保留 namespace、endpoint、connection string、credential、token、session 或背景工作。任何 precondition、native exit code
    或輸出格式不符合時立即失敗，不套用 schema，也不嘗試 DROP DATABASE、CRM SQL、遠端 SQL 或其他回退。
    #>
    if ($RemoveDrainedUnboundEpochs) {
        $removeDrainedUnboundEpochsSql = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> N'SpeechMessageDynamicsControlPlane'
    THROW 51001, 'Unexpected Dynamics control-plane database.', 1;
IF OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
BEGIN
    SELECT CAST(0 AS int) AS RemovedSlotRows,
           CAST(0 AS int) AS RemovedEpochRows;
    RETURN;
END;

DECLARE @drainedBeforeUtc datetime2(3) = DATEADD(hour, -1, SYSUTCDATETIME());
BEGIN TRANSACTION;
CREATE TABLE #UnboundEpoch
(
    LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL
        PRIMARY KEY
);

IF OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U') IS NULL
BEGIN
    INSERT INTO #UnboundEpoch (LeaseNamespaceId)
    SELECT epochRow.LeaseNamespaceId
    FROM dbo.RuntimeHostAdmissionEpoch AS epochRow;
END
ELSE
BEGIN
    INSERT INTO #UnboundEpoch (LeaseNamespaceId)
    SELECT epochRow.LeaseNamespaceId
    FROM dbo.RuntimeHostAdmissionEpoch AS epochRow
    LEFT JOIN dbo.RuntimeHostOrganizationBinding AS bindingRow
        ON bindingRow.LeaseNamespaceId = epochRow.LeaseNamespaceId
    WHERE bindingRow.LeaseNamespaceId IS NULL;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.RuntimeHostAdmissionEpoch AS epochRow
    INNER JOIN #UnboundEpoch AS unboundEpoch
        ON unboundEpoch.LeaseNamespaceId = epochRow.LeaseNamespaceId
    WHERE epochRow.LastUpdatedAtUtc > @drainedBeforeUtc
)
    THROW 51005, 'Refusing to remove recently updated unbound admission epochs.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.RuntimeHostSlotLease AS slotRow
    INNER JOIN #UnboundEpoch AS unboundEpoch
        ON unboundEpoch.LeaseNamespaceId = slotRow.LeaseNamespaceId
    WHERE slotRow.LastTouchedAtUtc > @drainedBeforeUtc
       OR slotRow.LeaseExpiresAtUtc > @drainedBeforeUtc
       OR slotRow.QuarantineUntilUtc > @drainedBeforeUtc
       OR (slotRow.HostInstanceId IS NOT NULL AND slotRow.LeaseExpiresAtUtc IS NULL)
)
    THROW 51006, 'Refusing to remove unbound admission epochs with undrained runtime-host slots.', 1;

DELETE slotRow
FROM dbo.RuntimeHostSlotLease AS slotRow
INNER JOIN #UnboundEpoch AS unboundEpoch
    ON unboundEpoch.LeaseNamespaceId = slotRow.LeaseNamespaceId;
DECLARE @removedSlotRows int = @@ROWCOUNT;

DELETE epochRow
FROM dbo.RuntimeHostAdmissionEpoch AS epochRow
INNER JOIN #UnboundEpoch AS unboundEpoch
    ON unboundEpoch.LeaseNamespaceId = epochRow.LeaseNamespaceId;
DECLARE @removedEpochRows int = @@ROWCOUNT;

COMMIT TRANSACTION;
SELECT @removedSlotRows AS RemovedSlotRows,
       @removedEpochRows AS RemovedEpochRows;
'@
        $removeDrainedUnboundEpochsOutput = @(& $sqlCmdPath `
            -S $allowedServerName `
            -E `
            -b `
            -l 5 `
            -t 30 `
            -d $DatabaseName `
            -Q $removeDrainedUnboundEpochsSql `
            -h -1 `
            -s '|' `
            -W 2>&1)
        $removeDrainedUnboundEpochsExitCode = $LASTEXITCODE
        if ($removeDrainedUnboundEpochsExitCode -ne 0) {
            $detail = Get-NativeFailureDetail -Output $removeDrainedUnboundEpochsOutput
            throw "Failed to remove only proven-drained unbound LocalDB epochs (exit code $removeDrainedUnboundEpochsExitCode).$([Environment]::NewLine)$detail"
        }

        $removeDrainedUnboundEpochsLine = $removeDrainedUnboundEpochsOutput |
            Where-Object { $_ -match '^\d+\|\d+$' } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($removeDrainedUnboundEpochsLine)) {
            throw 'Drained LocalDB epoch recovery returned no structured row counts.'
        }
        $removeDrainedUnboundEpochsFields = $removeDrainedUnboundEpochsLine.Split('|')
        if ($removeDrainedUnboundEpochsFields.Count -ne 2 -or
            $removeDrainedUnboundEpochsFields[0] -notmatch '^\d+$' -or
            $removeDrainedUnboundEpochsFields[1] -notmatch '^\d+$') {
            throw 'Drained LocalDB epoch recovery returned invalid row counts.'
        }
        $removedDrainedUnboundSlotRows = [int]$removeDrainedUnboundEpochsFields[0]
        $removedDrainedUnboundEpochRows = [int]$removeDrainedUnboundEpochsFields[1]
    }

    <#
    checked-in schema 自己驗證 DB_NAME 並以 OBJECT_ID/COL_LENGTH 實作重跑安全；-b 確保任何 THROW 皆回傳失敗。
    #>
    $applySchemaOutput = @(& $sqlCmdPath `
        -S $allowedServerName `
        -E `
        -b `
        -l 5 `
        -t 30 `
        -d $DatabaseName `
        -i $resolvedSchemaFile `
        -W 2>&1)
    $applySchemaExitCode = $LASTEXITCODE
    if ($applySchemaExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $applySchemaOutput
        throw "Failed to apply the checked-in LocalDB schema (exit code $applySchemaExitCode).$([Environment]::NewLine)$detail"
    }

    <#
    最後驗證 database 與三個必需物件；不建立 login/user/role，也不變更任何 server/database permission。
    #>
    $verifySql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'$DatabaseName'
    THROW 51001, 'Unexpected Dynamics control-plane database.', 1;
IF OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') IS NULL
    THROW 51002, 'Dynamics control-plane schema verification failed.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
      AND referenced_object_id = OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U')
      AND name = N'FK_RuntimeHostAdmissionEpoch_OrganizationBinding'
)
    THROW 51003, 'Dynamics control-plane canonical binding foreign key is missing.', 1;
IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE
        (
            (object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
             AND name IN (N'LeaseNamespaceId', N'ConfigurationDigest'))
            OR (object_id = OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U')
                AND name IN (N'LeaseNamespaceId', N'ConfigurationDigest', N'HostInstanceId'))
            OR (object_id = OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U')
                AND name IN (N'LeaseNamespaceId', N'NormalizedOrganizationBaseUri'))
        )
        AND collation_name <> N'Latin1_General_100_BIN2'
)
    THROW 51004, 'Dynamics control-plane string identity columns must use binary ordinal collation.', 1;
SELECT DB_NAME() AS DatabaseName,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
       SYSUTCDATETIME() AS ServerUtc;
"@
    $verificationOutput = @(& $sqlCmdPath `
        -S $allowedServerName `
        -E `
        -b `
        -l 5 `
        -t 30 `
        -d $DatabaseName `
        -Q $verifySql `
        -h -1 `
        -s '|' `
        -W 2>&1)
    $verificationExitCode = $LASTEXITCODE
    if ($verificationExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $verificationOutput
        throw "LocalDB schema verification failed (exit code $verificationExitCode).$([Environment]::NewLine)$detail"
    }

    $localDbStatusOutput = @(& $sqlLocalDbPath info $InstanceName 2>&1)
    $localDbStatusExitCode = $LASTEXITCODE
    if ($localDbStatusExitCode -ne 0) {
        $detail = Get-NativeFailureDetail -Output $localDbStatusOutput
        throw "LocalDB instance status verification failed (exit code $localDbStatusExitCode).$([Environment]::NewLine)$detail"
    }

    $verificationLine = $verificationOutput |
        Where-Object { $_ -match '^SpeechMessageDynamicsControlPlane\|' } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($verificationLine)) {
        throw 'LocalDB verification returned no structured database/version row.'
    }
    $verificationFields = $verificationLine.Split('|')
    if ($verificationFields.Count -ne 3) {
        throw 'LocalDB verification returned an unexpected structured row.'
    }

    <#
    輸出只包含固定 target、schema hash、已驗證狀態、SQL 版本與 server UTC；
    不轉送本地化 raw tool output，也不輸出 Windows owner、連線字串或任何 credential/token。
    #>
    [pscustomobject]@{
        InstanceName = $InstanceName
        ServerName = $allowedServerName
        InstanceState = 'Running'
        DatabaseName = $DatabaseName
        SchemaFile = $resolvedSchemaFile
        SchemaSha256 = (Get-FileHash -LiteralPath $resolvedSchemaFile -Algorithm SHA256).Hash
        ProductVersion = $verificationFields[1].Trim()
        ServerUtc = $verificationFields[2].Trim()
        DrainedUnboundRecoveryRequested = [bool]$RemoveDrainedUnboundEpochs
        RemovedDrainedUnboundSlotRows = $removedDrainedUnboundSlotRows
        RemovedDrainedUnboundEpochRows = $removedDrainedUnboundEpochRows
    }
}
finally {
    if ($ownsMutex) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
