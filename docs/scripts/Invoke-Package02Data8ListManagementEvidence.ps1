<#
.SYNOPSIS
    執行 P7.2 Slice C 的 CE 9.1 Data8 list-management 實機證據預檢與明確 opt-in runner。

.DESCRIPTION
    此 runner 專供 Lenovo 本機的 sunnyvalechback 部署設定使用。預設只驗證 coverage matrix、
    ChurchReport feature flag、既有 P6.2 profile input、Credential Manager target、Slice A
    contact descriptor，以及 Slice C task-owned fixture graph descriptor；預設絕不連線 CE、
    不讀取 password、不中斷 browser session，也不變更 CRM 或 feature flag。

    只有明確傳入 -ExecuteFixture 後，runner 才會以 Credential Manager 的既有 crm91 target
    將 password 放入單一短生命週期 child process，啟動一次受限的 dotnet test。child test
    先讀取所有五段流程的基線，再依序執行 add、remove、small-group、owner、transfer，每段
    都由既有 P72ListManagementFixtureBridge 完成一次 dispatch、read-back、restore 與
    restore read-back。timeout 或模糊結果絕不重試。

    明確傳入 -ReconcileFixture 時，runner 使用同一個 Credential Manager reference，但只啟動
    獨立的 read-only child lane。該 lane 僅執行 WhoAmI、Retrieve 與 RetrieveMultiple projection，
    正常路徑輸出 baseline-unprovable；若 child 的 store、runtime 或 logger cleanup 無法證明完成，
    則輸出優先序更高的 cleanup-failure 並把 readOnlyProbeExecuted 固定為 false。parent-owned
    safeToRetry 固定為 false。-ExecuteFixture 與
    -ReconcileFixture 為互斥 parameter set；同時指定會在讀取 credential 或建立 child 前由 binder 拒絕。

    所有 stdout 僅為一行 sanitized JSON。child 在清理自身 runtime 後只可寫入 parent 已建立的
    唯一 OS temporary evidence file；parent 以固定 schema、operation ID 與分類重新投影，並在
    寫出最終 JSON 前刪除整個 nonce directory。若 cleanup 無法證明成功，結果一律為 No-Go；永遠
    不輸出 password、token、cookie、endpoint、GUID、owner identity、baseline、CRM payload、
    temporary path 或原始例外。Credential native pointer、child process、temporary directory 與
    環境變數在 finally 有唯一且可預期的釋放／回復路徑。
#>
[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryPath,

    [Parameter(Mandatory = $false)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $false)]
    [string] $SourceFixtureDescriptorPath,

    [Parameter(Mandatory = $false)]
    [string] $FixtureDescriptorPath,

    [switch] $Json,

    [Parameter(ParameterSetName = 'Execute')]
    [switch] $ExecuteFixture,

    [Parameter(ParameterSetName = 'Reconcile')]
    [switch] $ReconcileFixture,

    [Parameter(ParameterSetName = 'Repair')]
    [switch] $RepairFixture,

    [Parameter(ParameterSetName = 'RepairProbe')]
    [switch] $RepairProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptExitCode = 1
$resultAlreadyWritten = $false
$completedHandoffOutcome = $null
$temporaryDirectory = $null
$temporaryDirectoryCreated = $false
$process = $null
$childProcessStarted = $false
$credentialPassword = $null
$isReconciliationMode = [bool]$ReconcileFixture
$isRepairMode = [bool]$RepairFixture
$isRepairProbeMode = [bool]$RepairProbe
$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe)
$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode)
$previousEnvironment = @{}
$inputEnvironmentNames = @(
    'CRM_PASSWORD',
    'SPEECHMESSAGE_P7_2_SLICE_C_LIVE',
    'SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE',
    'P7_2_SLICE_C_FIXTURE_OWNER',
    'P7_2_SLICE_C_FIXTURE_MARKER',
    'P7_2_SLICE_C_CONTACT_ID',
    'P7_2_SLICE_C_ADD_LIST_ID',
    'P7_2_SLICE_C_REMOVE_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID',
    'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_WEEK_START_UTC',
    'P7_2_SLICE_C_EVIDENCE_PATH',
    'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH'
)
$credentialTarget = 'speechmessage.crm91.p62'
$expectedProfileAlias = 'sunnyvalechback'
$expectedDeploymentProfileAlias = 'crm91'
$expectedFixtureMarker = 'p7.2-list-management'
$expectedOperationIds = @(
    'list.members.add.many',
    'list.members.remove.one',
    'listmanagement.smallgroup.update.fields',
    'contact.assign.owner',
    'newperson.contact.transfer.between.lists'
)

if ([string]::IsNullOrWhiteSpace($ProfileInputPath)) {
    $ProfileInputPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'
}

if ([string]::IsNullOrWhiteSpace($SourceFixtureDescriptorPath)) {
    $SourceFixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\contact-basic-info-fixture.json'
}

if ([string]::IsNullOrWhiteSpace($FixtureDescriptorPath)) {
    $FixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\list-management-fixture.json'
}

function New-HandoffResult {
    <#
    .SYNOPSIS
        建立唯一允許輸出到 operator console 的去識別化結果。

    .DESCRIPTION
        回傳物只含固定 operation ID、部署 alias、CE/connector、布林旗標與 allowlisted
        分類。它不接受 path、descriptor、GUID、identity、credential 或 exception 作為欄位，
        因而即使呼叫端失敗也不會把敏感 input 放入最後的一行 JSON。
    #>
    param(
        [string] $Outcome,
        [string] $Reason,
        [bool] $PreflightOnly,
        [bool] $OperationExecuted = $false,
        [object[]] $Operations = @(),
        [object[]] $Checks = @(),
        [Nullable[bool]] $ReadOnlyProbeExecuted = $null,
        [Nullable[bool]] $ReadBackConfirmed = $null,
        [string] $OwnerBinding = $null,
        [string] $ProbeStage = $null,
        [object] $States = $null,
        [object] $Probe = $null
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        reason = $Reason
        operationIds = $expectedOperationIds
        profileAlias = $expectedProfileAlias
        deploymentProfileAlias = $expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $PreflightOnly
        operationExecuted = $OperationExecuted
        featureFlagChanged = $false
    }
    $modeVariable = Get-Variable -Name isReconciliationMode -Scope Script -ErrorAction SilentlyContinue
    $repairVariable = Get-Variable -Name isRepairMode -Scope Script -ErrorAction SilentlyContinue
    $repairProbeVariable = Get-Variable -Name isRepairProbeMode -Scope Script -ErrorAction SilentlyContinue
    if (($null -ne $modeVariable -and [bool]$modeVariable.Value) -or
        ($null -ne $repairVariable -and [bool]$repairVariable.Value) -or
        ($null -ne $repairProbeVariable -and [bool]$repairProbeVariable.Value)) {
        # 這個欄位由 parent mode 自己產生；child evidence 若攜帶同名欄位會被 strict parser 拒絕。
        $result.safeToRetry = $false
    }
    if ($null -ne $ReadOnlyProbeExecuted) {
        $result.readOnlyProbeExecuted = [bool]$ReadOnlyProbeExecuted
    }
    if ($null -ne $ReadBackConfirmed) {
        $result.readBackConfirmed = [bool]$ReadBackConfirmed
    }
    if (-not [string]::IsNullOrWhiteSpace($OwnerBinding)) {
        $result.ownerBinding = $OwnerBinding
    }
    if (-not [string]::IsNullOrWhiteSpace($ProbeStage)) {
        $result.probeStage = $ProbeStage
    }
    if ($null -ne $States) {
        $result.states = $States
    }
    if ($null -ne $Probe) {
        $result.probe = $Probe
    }
    if ($Operations.Count -gt 0) {
        $result.operations = $Operations
    }
    if ($Checks.Count -gt 0) {
        $result.checks = $Checks
    }

    return $result
}

function Write-HandoffResult {
    <#
    .SYNOPSIS
        輸出剛好一行 JSON 並鎖定後續 catch 不得再輸出第二筆結果。
    #>
    param([object] $Result)

    $script:resultAlreadyWritten = $true
    $Result | ConvertTo-Json -Compress -Depth 8
}

function New-TemporaryCleanupFailureResult {
    <#
    .SYNOPSIS
        將已計算的 child 結果保守轉為 temporary cleanup 的固定 No-Go。

    .DESCRIPTION
        evidence directory 是 child 執行後唯一仍可能保留去識別化資料的資源。即使五段 operation
        都已成功，parent 只要無法證明該 directory 已刪除，就不得輸出 Green；否則下一次 handoff
        可能讀到前次殘留證據，並讓資源生命週期與 CE mutation 狀態失去可稽核的一致性。

        此投影只保留既有 allowlisted operation 結果與是否可能已 dispatch 的布林值，固定覆寫為
        ``temporary-cleanup-failed``。它不保留或輸出 directory path、exception、credential 或 child
        payload，因此 cleanup 失敗不會擴大為本機資訊洩漏。
    #>
    param([object] $Result)

    $preflightOnly = $false
    $operationExecuted = $false
    $operations = @()
    if ($null -ne $Result) {
        if ($null -ne $Result.PSObject.Properties['preflightOnly'] -and $Result.preflightOnly -is [bool]) {
            $preflightOnly = [bool]$Result.preflightOnly
        }
        if ($null -ne $Result.PSObject.Properties['operationExecuted'] -and $Result.operationExecuted -is [bool]) {
            $operationExecuted = [bool]$Result.operationExecuted
        }
        if ($null -ne $Result.PSObject.Properties['operations'] -and $null -ne $Result.operations) {
            $operations = @($Result.operations)
        }
    }

    return New-HandoffResult `
        -Outcome 'no-go' `
        -Reason 'temporary-cleanup-failed' `
        -PreflightOnly $preflightOnly `
        -OperationExecuted $operationExecuted `
        -Operations $operations
}

function Read-StrictJsonFile {
    <#
    .SYNOPSIS
        讀取 UTF-8 no-BOM、CRLF-only、有限大小的 descriptor 或 profile JSON。

    .DESCRIPTION
        這個邊界一次讀入受限 bytes，驗證檔案長度與實際 bytes 一致，再用 UTF-8 strict
        decoder 與 ConvertFrom-Json 解析。任何路徑、encoding、換行或 JSON 問題都轉為呼叫端
        指定的固定 failure reason，避免 raw exception 把本機資料夾或內容帶到 stdout。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $FailureReason
    )

    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw $FailureReason
        }

        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) {
            throw $FailureReason
        }

        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
            throw $FailureReason
        }

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) {
            throw $FailureReason
        }

        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw $FailureReason
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function Read-StrictTextFile {
    <#
    .SYNOPSIS
        讀取 configuration text，但保留相同 encoding、大小與換行安全界線。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $FailureReason
    )

    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw $FailureReason
        }

        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) {
            throw $FailureReason
        }

        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
            throw $FailureReason
        }

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) {
            throw $FailureReason
        }

        return $text
    }
    catch {
        throw $FailureReason
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function Test-NonEmptyGuid {
    <#
    .SYNOPSIS
        驗證 descriptor 的 GUID 為固定 D 格式且非空值。
    #>
    param([object] $Value)

    $parsed = [Guid]::Empty
    return $Value -is [string] -and
        [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -and
        $parsed -ne [Guid]::Empty
}

function Test-SafeOwnerIdentity {
    <#
    .SYNOPSIS
        驗證 owner identity 可安全比對但不輸出。
    #>
    param([object] $Value)

    return $Value -is [string] -and
        -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 256 -and
        $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Test-CredentialTargetPresent {
    <#
    .SYNOPSIS
        只確認既有 Generic Credential target 存在，不取得其 secret。

    .DESCRIPTION
        Native credential pointer 由 C# helper 在 finally 呼叫 CredFree 釋放。預檢使用這個
        最小權限 probe，故不會在未傳入 -ExecuteFixture 時使 password 進入 managed memory。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72SliceC.CredentialPresenceReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72SliceC
{
    /// <summary>只確認 Generic Credential 存在，並在 finally 釋放 native pointer。</summary>
    public static class CredentialPresenceReader
    {
        private const uint GenericCredentialType = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        /// <summary>驗證 target 後立即釋放 native credential，不複製 blob。</summary>
        public static bool Exists(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0)
            {
                return false;
            }

            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out pointer) || pointer == IntPtr.Zero)
                {
                    return false;
                }

                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                return credential.Type == GenericCredentialType;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }
    }
}
'@ -ErrorAction Stop
        }

        return [SpeechMessage.P72SliceC.CredentialPresenceReader]::Exists($credentialTarget)
    }
    catch {
        return $false
    }
}

function Get-P72CredentialPassword {
    <#
    .SYNOPSIS
        僅在 ExecuteFixture 後以既有 crm91 Credential Manager target 讀取短暫 password。

    .DESCRIPTION
        指標、char buffer 與 native credential 都有單一 finally owner。回傳的 string 只在本
        script process 中設定為 child environment，finally 清除 reference 與環境變數；stdout、
        exception、evidence parser 和 result object 都不接收此值。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72SliceCLive.CredentialReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72SliceCLive
{
    /// <summary>以 bounded native read 取得 Generic Credential，且保證釋放 pointer 與 buffer。</summary>
    public static class CredentialReader
    {
        private const uint GenericCredentialType = 1;
        private const int MaximumBlobBytes = 8192;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        /// <summary>複製合法 bounded UTF-16 blob，並在 finally 清空 temporary char buffer。</summary>
        public static string ReadGenericSecret(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0)
            {
                return null;
            }

            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out pointer) || pointer == IntPtr.Zero)
                {
                    return null;
                }

                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.Type != GenericCredentialType ||
                    credential.CredentialBlob == IntPtr.Zero ||
                    credential.CredentialBlobSize == 0 ||
                    credential.CredentialBlobSize > MaximumBlobBytes ||
                    (credential.CredentialBlobSize & 1) != 0)
                {
                    return null;
                }

                var characterCount = checked((int)credential.CredentialBlobSize / 2);
                var characters = new char[characterCount];
                try
                {
                    Marshal.Copy(credential.CredentialBlob, characters, 0, characterCount);
                    var length = characterCount;
                    while (length > 0 && characters[length - 1] == '\0')
                    {
                        length--;
                    }

                    for (var index = 0; index < length; index++)
                    {
                        if (characters[index] == '\0')
                        {
                            return null;
                        }
                    }

                    return length == 0 ? null : new string(characters, 0, length);
                }
                finally
                {
                    Array.Clear(characters, 0, characters.Length);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }
    }
}
'@ -ErrorAction Stop
        }

        return [SpeechMessage.P72SliceCLive.CredentialReader]::ReadGenericSecret($credentialTarget)
    }
    catch {
        return $null
    }
}

function Test-Matrix {
    <#
    .SYNOPSIS
        驗證 Slice C coverage rows 仍是 CE 9.1/Data8 required-for-activation contract。
    #>
    param([object] $Matrix)

    if ($null -eq $Matrix -or
        $Matrix.schemaVersion -cne 'p7.2.fixture-activation.v1' -or
        $Matrix.defaultDispatch -cne 'fail-closed' -or
        $Matrix.allowedConnector -cne 'Data8' -or
        $Matrix.defaultCeSupport.ce82 -cne 'unsupported') {
        return $false
    }

    $expectations = @(
        @{ Id = 'list-membership-association'; Operations = @($expectedOperationIds[0], $expectedOperationIds[1]) },
        @{ Id = 'small-group-fixed-fields'; Operations = @($expectedOperationIds[2]) },
        @{ Id = 'contact-owner-assignment'; Operations = @($expectedOperationIds[3]) },
        @{ Id = 'contact-list-transfer-composite'; Operations = @($expectedOperationIds[4]) }
    )
    foreach ($expectation in $expectations) {
        $slice = @($Matrix.slices | Where-Object { $_.id -ceq $expectation.Id })
        if ($slice.Count -ne 1 -or
            $slice[0].status -cne 'required-for-activation' -or
            $slice[0].requiredCeVersion -cne '9.1' -or
            $slice[0].realCeEvidence.ce82 -cne 'unsupported' -or
            $slice[0].realCeEvidence.ce91 -cnotin @('pending', 'complete') -or
            @($slice[0].operationIds).Count -ne $expectation.Operations.Count) {
            return $false
        }

        for ($index = 0; $index -lt $expectation.Operations.Count; $index++) {
            if ($slice[0].operationIds[$index] -cne $expectation.Operations[$index]) {
                return $false
            }
        }
    }

    return $true
}

function Test-ProfileInput {
    <#
    .SYNOPSIS
        驗證既有 P6.2 profile 僅作為 crm91 Credential Manager reference 的來源。

    .DESCRIPTION
        profile 中的 Official Worker metadata 不會被啟動或傳入 child test；本 runner 固定使用
        ChurchReport Embedded + Data8。任何 profile identity/reference 不精確符合既有 target
        都 fail closed，避免以別的 deployment 或 credential 執行 CE operation。
    #>
    param([object] $Profile)

    if ($null -eq $Profile -or $Profile.schemaVersion -ne 1) {
        return $false
    }

    $profiles = @($Profile.profiles)
    $crm91 = @($profiles | Where-Object { $_.profileAlias -ceq 'crm91' })
    return $crm91.Count -eq 1 -and
        $crm91[0].workerKind -ceq 'OfficialCrm91Worker' -and
        $crm91[0].authentication -ceq 'Ifd' -and
        $crm91[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm91[0].identity.reference -ceq $credentialTarget
}

function Test-ChurchReportData8Configuration {
    <#
    .SYNOPSIS
        確認 deployment-owned sunnyvalechback CE 9.1 與所有既有 Package02 consumer flags 均為 false。
    #>
    param([string] $RepositoryRoot)

    try {
        $production = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.json') 512KB 'churchreport-config-invalid'
        $development = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') 128KB 'churchreport-config-invalid'
        $catalogPattern = '"sunnyvalechback"\s*:\s*\{[^\r\n\}]*"CeVersion"\s*:\s*"9\.1"[^\r\n\}]*"ServiceUri"\s*:\s*"https://sunnyvalechback\.speechmessage\.com\.tw/XRMServices/2011/Organization\.svc"'
        $profilePattern = '"ProfileAlias"\s*:\s*"sunnyvalechback"'
        $modePattern = '"ConnectionMode"\s*:\s*"Embedded"'
        $readFlagPattern = '"Package01FeeReadsEnabled"\s*:\s*false'
        $basicFlagPattern = '"Package02ContactBasicInfoUpdatesEnabled"\s*:\s*false'
        $profileFlagPattern = '"Package02ContactProfileOperationsEnabled"\s*:\s*false'
        return [Regex]::IsMatch($production, $catalogPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $profilePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $modePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $readFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $basicFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $profileFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    catch {
        return $false
    }
}

function Test-SourceFixtureDescriptor {
    <#
    .SYNOPSIS
        驗證並重用既有 Slice A contact descriptor，避免另行要求 operator 貼 contact GUID。
    #>
    param(
        [object] $Fixture,
        [string] $CurrentIdentity
    )

    return $null -ne $Fixture -and
        $Fixture.schemaVersion -eq 1 -and
        $Fixture.fixtureId -ceq 'p7.2-contact-basic-info' -and
        $Fixture.profileAlias -ceq $expectedProfileAlias -and
        $Fixture.ceVersion -ceq '9.1' -and
        $Fixture.connector -ceq 'Data8' -and
        $Fixture.marker -ceq 'p7.2-contact-basic-info' -and
        (Test-NonEmptyGuid $Fixture.contactId) -and
        (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -and
        [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)
}

function Test-SliceCFixtureDescriptor {
    <#
    .SYNOPSIS
        驗證 Slice C task-owned graph descriptor，並確認它只能搭配目前 operator 的 Slice A contact。

    .DESCRIPTION
        現有 bridge/store 沒有 generic discovery/provision API；因此 runner 不猜測 schema、不建立
        任意 CRM graph，也不要求 operator 在 command line 貼 GUID。smallGroupExpectedRelationshipListId
        必須是與所有 Slice C mutation list 都不同的專用 task-owned relationship list，避免 expected
        projection 與 add/remove/transfer rollback graph 共用 identity，或 bridge 退回以 leader 對全組織
        進行廣泛查詢。只有由已核准 provisioning 流程寫入的本機 descriptor 可通過，缺失或不完整時
        輸出 no-go 並且不啟動 child process。
    #>
    param(
        [object] $Fixture,
        [object] $SourceFixture,
        [string] $CurrentIdentity
    )

    if ($null -eq $Fixture -or
        $Fixture.schemaVersion -ne 1 -or
        $Fixture.fixtureId -cne 'p7.2-list-management' -or
        $Fixture.profileAlias -cne $expectedProfileAlias -or
        $Fixture.ceVersion -cne '9.1' -or
        $Fixture.connector -cne 'Data8' -or
        $Fixture.marker -cne $expectedFixtureMarker -or
        -not (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -or
        -not [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $expectedPropertyNames = @(
        'schemaVersion',
        'fixtureId',
        'profileAlias',
        'ceVersion',
        'connector',
        'marker',
        'ownerIdentity',
        'addListId',
        'removeListId',
        'smallGroupListId',
        'smallGroupTargetLeaderContactId',
        'smallGroupExpectedRelationshipListId',
        'transferSourceListId',
        'transferTargetListId',
        'transferWeekStartUtc'
    )
    $actualPropertyNames = @($Fixture.PSObject.Properties.Name)
    if ($actualPropertyNames.Count -ne $expectedPropertyNames.Count -or
        @($actualPropertyNames | Where-Object { $_ -cnotin $expectedPropertyNames }).Count -ne 0 -or
        @($expectedPropertyNames | Where-Object { $_ -cnotin $actualPropertyNames }).Count -ne 0) {
        return $false
    }

    $requiredGuidProperties = @(
        'addListId',
        'removeListId',
        'smallGroupListId',
        'smallGroupTargetLeaderContactId',
        'smallGroupExpectedRelationshipListId',
        'transferSourceListId',
        'transferTargetListId'
    )
    foreach ($name in $requiredGuidProperties) {
        if ($null -eq $Fixture.PSObject.Properties[$name] -or -not (Test-NonEmptyGuid $Fixture.$name)) {
            return $false
        }
    }

    $listIds = @(
        [string]$Fixture.addListId,
        [string]$Fixture.removeListId,
        [string]$Fixture.smallGroupListId,
        [string]$Fixture.smallGroupExpectedRelationshipListId,
        [string]$Fixture.transferSourceListId,
        [string]$Fixture.transferTargetListId
    )
    if (@($listIds | Select-Object -Unique).Count -ne $listIds.Count) {
        return $false
    }

    $weekStart = [DateTimeOffset]::MinValue
    if ($null -eq $Fixture.PSObject.Properties['transferWeekStartUtc'] -or
        -not [DateTimeOffset]::TryParseExact(
            [string]$Fixture.transferWeekStartUtc,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal,
            [ref]$weekStart) -or
        $weekStart.Offset -ne [TimeSpan]::Zero -or
        $weekStart.TimeOfDay -ne [TimeSpan]::Zero -or
        $weekStart.DayOfWeek -ne [DayOfWeek]::Sunday) {
        return $false
    }

    # source contact 不重複寫入 Slice C descriptor；它由已驗證 Slice A descriptor 帶入 child
    # process，減少 operator 需要處理的 identity 數量並避免兩份 descriptor 漂移。
    return $null -ne $SourceFixture -and (Test-NonEmptyGuid $SourceFixture.contactId)
}

function Get-StrictSliceCEvidenceFile {
    <#
    .SYNOPSIS
        讀取由 Slice C child 唯一寫入、parent 唯一擁有的去識別化 evidence 檔。

    .DESCRIPTION
        TRX stdout 會由 xUnit adapter 重複投影，不能作為單一 evidence record。此 parser 只接受
        parent 建立的 32 KiB UTF-8/CRLF temporary file，並以 exact top-level 與五筆 operation schema
        重新投影 allowlisted scalar，防止 child 夾帶 GUID、路徑、credential、exception 或額外 property
        到最終 console JSON。遺失、編碼錯誤、schema 不符或語意互相矛盾時固定丟出
        evidence-result-unavailable，caller 不得推論成功或重送 CRM mutation。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'featureFlagChanged',
        'operations'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $topReasons = @('', 'runtime-failure', 'cleanup-failure', 'fixture-precondition-failed', 'live-evidence-incomplete')
    $operationReasons = @(
        '', 'write-not-required', 'write-response-state-mismatch', 'write-not-committed',
        'write-ambiguous', 'reconciliation-failed', 'cleanup-reconciliation-failed',
        'cleanup-failed', 'cleanup-ambiguous-reconciled', 'write-ambiguous-reconciled',
        'write-result-invalid', 'prior-operation-no-go'
    )
    $reconciliationStates = @('baseline', 'expected', 'expected-after-fault', 'expected-after-invalid-response', 'partial-or-unknown', 'unknown', 'not-started')
    $cleanupStates = @('not-required', 'restored', 'restored-after-fault', 'manual-reconciliation-required', 'not-started')
    $operations = @($evidence.operations)
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -cnotin $topReasons -or
        $operations.Count -ne $expectedOperationIds.Count) {
        throw 'evidence-result-unavailable'
    }

    $operationPropertyNames = @(
        'operationId',
        'outcome',
        'reason',
        'operationExecuted',
        'reconciliationState',
        'cleanupState'
    )
    $projectedOperations = @()
    for ($index = 0; $index -lt $expectedOperationIds.Count; $index++) {
        $operation = $operations[$index]
        $actualOperationPropertyNames = if ($null -eq $operation) { @() } else { @($operation.PSObject.Properties.Name) }
        if ($null -eq $operation -or
            $actualOperationPropertyNames.Count -ne $operationPropertyNames.Count -or
            @($actualOperationPropertyNames | Where-Object { $_ -cnotin $operationPropertyNames }).Count -ne 0 -or
            @($operationPropertyNames | Where-Object { $_ -cnotin $actualOperationPropertyNames }).Count -ne 0 -or
            $operation.operationId -cne $expectedOperationIds[$index] -or
            $operation.outcome -cnotin @('go', 'no-go', 'not-run') -or
            $operation.reason -cnotin $operationReasons -or
            $operation.operationExecuted -isnot [bool] -or
            $operation.reconciliationState -cnotin $reconciliationStates -or
            $operation.cleanupState -cnotin $cleanupStates) {
            throw 'evidence-result-unavailable'
        }

        if ($operation.outcome -ceq 'go' -and
            ($operation.reason -cne '' -or
             -not $operation.operationExecuted -or
             $operation.reconciliationState -cne 'expected' -or
             $operation.cleanupState -cne 'restored')) {
            throw 'evidence-result-unavailable'
        }

        if ($operation.outcome -ceq 'not-run' -and
            ($operation.reason -cne 'prior-operation-no-go' -or
             $operation.operationExecuted -or
             $operation.reconciliationState -cne 'not-started' -or
             $operation.cleanupState -cne 'not-started')) {
            throw 'evidence-result-unavailable'
        }

        $projectedOperations += [pscustomobject]@{
            operationId = [string]$operation.operationId
            outcome = [string]$operation.outcome
            reason = [string]$operation.reason
            operationExecuted = [bool]$operation.operationExecuted
            reconciliationState = [string]$operation.reconciliationState
            cleanupState = [string]$operation.cleanupState
        }
    }

    $allGo = @($projectedOperations | Where-Object { $_.outcome -cne 'go' }).Count -eq 0
    $anyOperationExecuted = @($projectedOperations | Where-Object { $_.operationExecuted }).Count -gt 0
    if ($evidence.operationExecuted -ne $anyOperationExecuted -or
        ($evidence.outcome -ceq 'go' -and
         ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or -not $allGo)) -or
        ($evidence.outcome -ceq 'no-go' -and
         ($evidence.reason -ceq '' -or $allGo))) {
        throw 'evidence-result-unavailable'
    }

    return [pscustomobject]@{
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        operationExecuted = [bool]$evidence.operationExecuted
        operations = $projectedOperations
    }
}

function Get-StrictSliceCReconciliationEvidenceFile {
    <#
    .SYNOPSIS
        讀取只讀 reconciliation child 的封閉 evidence schema。

    .DESCRIPTION
        reconciliation 與 mutation evidence 使用不同檔名及不同環境變數。此 parser 只接受
        baseline-unprovable 或 cleanup-failure、五筆 not-run operation、readOnlyProbeExecuted、ownerBinding 與固定
        state categories。cleanup-failure 必須連同 readOnlyProbeExecuted=false，否則拒絕 evidence；它刻意拒絕
        child 自行傳入 safeToRetry，因為 retry 權限只能由 parent
        handoff 決定。所有 raw path、GUID、baseline、credential 與例外都在此邊界外消失。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'readOnlyProbeExecuted',
        'operationExecuted',
        'featureFlagChanged',
        'ownerBinding',
        'probeStage',
        'operations'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $stateNames = @('addMembership', 'removeMembership', 'smallGroup', 'contactOwner', 'transfer')
    $allowedStates = @{
        addMembership = @('baseline-absent', 'unexpected-present', 'unavailable')
        removeMembership = @('baseline-present', 'unexpected-absent', 'unavailable')
        smallGroup = @('not-expected-baseline-unproven', 'expected-baseline-unproven', 'unavailable')
        contactOwner = @('non-target-baseline-unproven', 'target-baseline-unproven', 'unavailable')
        transfer = @('baseline-shape-unproven', 'unexpected-shape-unproven', 'unavailable')
    }
    $operations = @($evidence.operations)
    # cleanup-failure 是唯一允許覆寫一般 baseline reason 的 release-blocking 類別；它只能
    # 與 readOnlyProbeExecuted=false 一起傳遞，避免 child 的未完成資源釋放被誤視為可用證據。
    $isCleanupFailure = $evidence.reason -ceq 'cleanup-failure'
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.readOnlyProbeExecuted -isnot [bool] -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.operationExecuted -ne $false -or
        $evidence.featureFlagChanged -ne $false -or
        $evidence.outcome -cne 'no-go' -or
        $evidence.reason -cnotin @('baseline-unprovable', 'cleanup-failure') -or
        ($isCleanupFailure -and $evidence.readOnlyProbeExecuted) -or
        $evidence.ownerBinding -cnotin @('matches-service-identity', 'unavailable') -or
        $evidence.probeStage -cnotin @(
            'not-started',
            'whoami-verified',
            'fixture-store-created',
            'add-membership-read',
            'remove-membership-read',
            'small-group-read',
            'small-group-expected-read',
            'contact-owner-read',
            'transfer-read',
            'classification-complete') -or
        $operations.Count -ne $expectedOperationIds.Count) {
        throw 'evidence-result-unavailable'
    }

    $operationPropertyNames = @(
        'operationId',
        'outcome',
        'reason',
        'operationExecuted',
        'reconciliationState',
        'cleanupState'
    )
    $projectedOperations = @()
    $projectedStates = [ordered]@{}
    for ($index = 0; $index -lt $expectedOperationIds.Count; $index++) {
        $operation = $operations[$index]
        $actualOperationPropertyNames = if ($null -eq $operation) { @() } else { @($operation.PSObject.Properties.Name) }
        $stateName = $stateNames[$index]
        if ($null -eq $operation -or
            $actualOperationPropertyNames.Count -ne $operationPropertyNames.Count -or
            @($actualOperationPropertyNames | Where-Object { $_ -cnotin $operationPropertyNames }).Count -ne 0 -or
            @($operationPropertyNames | Where-Object { $_ -cnotin $actualOperationPropertyNames }).Count -ne 0 -or
            $operation.operationId -cne $expectedOperationIds[$index] -or
            $operation.outcome -cne 'not-run' -or
            $operation.reason -cne 'baseline-unprovable' -or
            $operation.operationExecuted -isnot [bool] -or
            $operation.operationExecuted -ne $false -or
            $operation.reconciliationState -cnotin $allowedStates[$stateName] -or
            $operation.cleanupState -cne 'not-applicable') {
            throw 'evidence-result-unavailable'
        }

        $projectedOperations += [pscustomobject]@{
            operationId = [string]$operation.operationId
            outcome = 'not-run'
            reason = 'baseline-unprovable'
            operationExecuted = $false
            reconciliationState = [string]$operation.reconciliationState
            cleanupState = 'not-applicable'
        }
        $projectedStates[$stateName] = [string]$operation.reconciliationState
    }

    return [pscustomobject]@{
        outcome = 'no-go'
        reason = [string]$evidence.reason
        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
        ownerBinding = [string]$evidence.ownerBinding
        probeStage = [string]$evidence.probeStage
        states = $projectedStates
        operations = $projectedOperations
    }
}

function Get-StrictSliceCRepairEvidenceFile {
    <#
    .SYNOPSIS
        解析 relationship-list repair child 的最小 sanitized evidence schema。

    .DESCRIPTION
        repair 是唯一一個允許對 task-owned relationship list 送出一次 Update 的 lane；
        parser 只接受固定 profile、CE、connector、operation/read-back scalar 與 allowlisted
        reason。任何缺欄位、額外欄位、未預期 outcome 或 operationExecuted/readBackConfirmed
        組合都 fail closed，並由 parent 保持 safeToRetry=false。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'readBackConfirmed',
        'featureFlagChanged'
    )
    $actualPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedReasons = @(
        '',
        'already-repaired',
        'fixture-precondition-failed',
        'fixture-state-unexpected',
        'repair-readback-mismatch',
        'repair-ambiguous',
        'repair-precondition-failed',
        'cleanup-failure'
    )
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.readBackConfirmed -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false) {
        throw 'evidence-result-unavailable'
    }

    if ($evidence.outcome -ceq 'go') {
        if ($evidence.reason -ceq 'already-repaired') {
            if ($evidence.operationExecuted -or -not $evidence.readBackConfirmed) {
                throw 'evidence-result-unavailable'
            }
        }
        elseif ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or -not $evidence.readBackConfirmed) {
            throw 'evidence-result-unavailable'
        }
    }
    else {
        if ($evidence.reason -ceq '' -or
            ($evidence.reason -in @('fixture-precondition-failed', 'fixture-state-unexpected', 'repair-precondition-failed') -and $evidence.operationExecuted) -or
            ($evidence.reason -in @('repair-readback-mismatch', 'repair-ambiguous') -and -not $evidence.operationExecuted) -or
            $evidence.readBackConfirmed) {
            throw 'evidence-result-unavailable'
        }
    }

    return [pscustomobject]@{
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        operationExecuted = [bool]$evidence.operationExecuted
        readBackConfirmed = [bool]$evidence.readBackConfirmed
    }
}

function Get-StrictSliceCRepairProbeEvidenceFile {
    <#
    .SYNOPSIS
        解析 relationship-list repair 的唯讀 precondition probe evidence。

    .DESCRIPTION
        Probe 永遠不是 repair 授權；即使所有遠端 proof 成立，child 仍回傳 no-go，
        parent 只投影固定的 precondition 狀態。任何額外欄位、mutation 標記、原始例外、
        GUID 或不在 allowlist 的狀態都 fail closed。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'readOnlyProbeExecuted',
        'featureFlagChanged',
        'probe'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedReasons = @('repair-preconditions-proven', 'probe-precondition-failed', 'cleanup-failure')
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.outcome -cne 'no-go' -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -ne $false -or
        $evidence.readOnlyProbeExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false) {
        throw 'evidence-result-unavailable'
    }

    $probePropertyNames = @(
        'sourceContactMarkerValid',
        'smallGroupListValid',
        'expectedRelationshipListValid',
        'targetLeaderMarkerValid',
        'expectedRelationshipRaceLeaderMatches',
        'expectedRelationshipFieldsState',
        'preconditionState'
    )
    if ($null -eq $evidence.probe) {
        throw 'evidence-result-unavailable'
    }
    $actualProbePropertyNames = @($evidence.probe.PSObject.Properties.Name)
    if ($actualProbePropertyNames.Count -ne $probePropertyNames.Count -or
        @($actualProbePropertyNames | Where-Object { $_ -cnotin $probePropertyNames }).Count -ne 0 -or
        @($probePropertyNames | Where-Object { $_ -cnotin $actualProbePropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedFieldStates = @('blank', 'expected', 'partial', 'unexpected', 'unreadable')
    $allowedPreconditionStates = @('blank-repairable', 'already-repaired', 'partial-or-unexpected', 'provenance-invalid', 'fixture-precondition-failed', 'unavailable')
    if ($evidence.probe.sourceContactMarkerValid -isnot [bool] -or
        $evidence.probe.smallGroupListValid -isnot [bool] -or
        $evidence.probe.expectedRelationshipListValid -isnot [bool] -or
        $evidence.probe.targetLeaderMarkerValid -isnot [bool] -or
        $evidence.probe.expectedRelationshipRaceLeaderMatches -isnot [bool] -or
        $evidence.probe.expectedRelationshipFieldsState -cnotin $allowedFieldStates -or
        $evidence.probe.preconditionState -cnotin $allowedPreconditionStates) {
        throw 'evidence-result-unavailable'
    }

    if ($evidence.reason -ceq 'repair-preconditions-proven' -and -not $evidence.readOnlyProbeExecuted) {
        throw 'evidence-result-unavailable'
    }
    if ($evidence.reason -cne 'repair-preconditions-proven' -and $evidence.readOnlyProbeExecuted) {
        throw 'evidence-result-unavailable'
    }

    return [pscustomobject]@{
        outcome = 'no-go'
        reason = [string]$evidence.reason
        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
        preconditionState = [string]$evidence.probe.preconditionState
        probe = [pscustomobject]@{
            sourceContactMarkerValid = [bool]$evidence.probe.sourceContactMarkerValid
            smallGroupListValid = [bool]$evidence.probe.smallGroupListValid
            expectedRelationshipListValid = [bool]$evidence.probe.expectedRelationshipListValid
            targetLeaderMarkerValid = [bool]$evidence.probe.targetLeaderMarkerValid
            expectedRelationshipRaceLeaderMatches = [bool]$evidence.probe.expectedRelationshipRaceLeaderMatches
            expectedRelationshipFieldsState = [string]$evidence.probe.expectedRelationshipFieldsState
            preconditionState = [string]$evidence.probe.preconditionState
        }
    }
}

function Remove-OwnedSliceCTemporaryDirectory {
    <#
    .SYNOPSIS
        刪除本次 handoff 唯一擁有的 Slice C OS 暫存目錄。

    .DESCRIPTION
        temporary evidence 的唯一持有者是 parent runner；child 只獲得固定 evidence file path，不能
        指定或保留 parent directory。本 helper 因此只接受 OS temporary root 的直接子目錄，且名稱必須
        精確符合 ``speechmessage-p7-2-slice-c-`` 加上 32 位 GUID nonce。根目錄與所有已列舉的後代都
        不得是 reparse point，避免 cleanup 跟隨 junction／symlink 而刪除 owner 範圍以外的資料。

        回傳 ``$true`` 代表目錄已確實不存在；所有路徑、名稱、reparse、存取或刪除錯誤均回傳
        ``$false``。呼叫端必須把 ``$false`` 轉為 sanitized No-Go，而不是吞掉錯誤後報告 Green。
        此函式不輸出任何 path 或例外，因此不會把本機檔案系統資訊跨越 operator handoff 邊界。
    #>
    param([string] $TemporaryDirectory)

    if ([string]::IsNullOrWhiteSpace($TemporaryDirectory)) {
        return $false
    }

    try {
        $trimCharacters = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd($trimCharacters)
        $fullTemporaryDirectory = [IO.Path]::GetFullPath($TemporaryDirectory).TrimEnd($trimCharacters)
        $directoryName = [IO.Path]::GetFileName($fullTemporaryDirectory)
        $parentDirectory = [IO.Path]::GetDirectoryName($fullTemporaryDirectory)
        if (-not [string]::Equals($parentDirectory, $temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not [Regex]::IsMatch($directoryName, '^speechmessage-p7-2-slice-c-[0-9a-f]{32}$', [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
            -not (Test-Path -LiteralPath $fullTemporaryDirectory -PathType Container)) {
            return $false
        }

        $rootItem = Get-Item -LiteralPath $fullTemporaryDirectory -Force -ErrorAction Stop
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $false
        }

        # child 已結束後才進入 finally；仍逐一拒絕 descendant reparse point，確保遞迴刪除不會將
        # temporary evidence 的唯一 owner 擴張到連結目標。目錄很小（唯一 32 KiB evidence file），
        # 故完整列舉不會形成長生命週期記憶體或 I/O 成本。
        foreach ($item in @(Get-ChildItem -LiteralPath $fullTemporaryDirectory -Force -Recurse -ErrorAction Stop)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $false
            }
        }

        Remove-Item -LiteralPath $fullTemporaryDirectory -Force -Recurse -ErrorAction Stop
        return -not (Test-Path -LiteralPath $fullTemporaryDirectory)
    }
    catch {
        return $false
    }
}

function Complete-HandoffResult {
    <#
    .SYNOPSIS
        在輸出唯一 JSON 前完成 Slice C temporary evidence 的確定性 cleanup。

    .DESCRIPTION
        console JSON 是 operator 可據以判斷是否可繼續的最終狀態，故不能先輸出 Green、再在 finally
        靜默發現 evidence directory 刪除失敗。本函式是 parent 的唯一完成點：若本次確實建立過
        directory，會先執行受限 cleanup；成功才清除 owner state，失敗則將原本的 normal、timeout
        或 child-failure 結果統一轉成去識別化 No-Go。finally 仍會作 best-effort second cleanup，但
        絕不反向把已報告的 No-Go 改回 Green。
    #>
    param([object] $Result)

    $completedResult = $Result
    if ($script:temporaryDirectoryCreated) {
        if (Remove-OwnedSliceCTemporaryDirectory $script:temporaryDirectory) {
            $script:temporaryDirectory = $null
            $script:temporaryDirectoryCreated = $false
        }
        else {
            $completedResult = New-TemporaryCleanupFailureResult $Result
        }
    }

    # 唯一 console output 與 process exit code 必須由同一個 cleanup 後 verdict 決定；不可因 child
    # 原本成功而讓已轉成 No-Go 的 handoff 仍以 exit code 0 結束。
    $script:completedHandoffOutcome = [string]$completedResult.outcome
    Write-HandoffResult $completedResult
}

function Quote-Argument {
    <#
    .SYNOPSIS
        將受控 local path 封裝為 ProcessStartInfo argument，拒絕控制字元。
    #>
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -ge 0) {
        throw 'dotnet-start-failed'
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function New-NotStartedOperations {
    <#
    .SYNOPSIS
        產生無 dispatch 的五段預設摘要，用於 timeout 或 parser failure 的 sanitized JSON。
    #>
    $operations = @()
    foreach ($operationId in $expectedOperationIds) {
        $operations += [pscustomobject]@{
            operationId = $operationId
            outcome = 'not-run'
            reason = 'prior-operation-no-go'
            operationExecuted = $false
            reconciliationState = 'not-started'
            cleanupState = 'not-started'
        }
    }

    return $operations
}

try {
    # 先 snapshot 環境，所有 early exit 都會在 finally 恢復；這避免 parent process 的既有
    # credential/session 變數被 child execution 汙染或在失敗路徑遺留。
    foreach ($name in $inputEnvironmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    $resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    $matrixPath = Join-Path $resolvedRepositoryPath '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $testProjectPath = Join-Path $resolvedRepositoryPath 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj'
    if (-not (Test-Path -LiteralPath $resolvedRepositoryPath -PathType Container) -or
        -not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'repository-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 1
        throw 'result-written'
    }

    $matrix = Read-StrictJsonFile $matrixPath 256KB 'matrix-invalid'
    if (-not (Test-Matrix $matrix)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'matrix-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 1
        throw 'result-written'
    }

    if (-not (Test-ChurchReportData8Configuration $resolvedRepositoryPath)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'churchreport-config-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $ProfileInputPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'profile-input-required' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    $profile = Read-StrictJsonFile $ProfileInputPath 128KB 'profile-input-invalid'
    if (-not (Test-ProfileInput $profile)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'profile-input-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $SourceFixtureDescriptorPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $FixtureDescriptorPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fixture-input-required' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    $sourceFixture = Read-StrictJsonFile $SourceFixtureDescriptorPath 32KB 'fixture-input-invalid'
    $fixture = Read-StrictJsonFile $FixtureDescriptorPath 32KB 'fixture-input-invalid'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (-not (Test-SourceFixtureDescriptor $sourceFixture $identity) -or
        -not (Test-SliceCFixtureDescriptor $fixture $sourceFixture $identity)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fixture-input-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-CredentialTargetPresent)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'credential-unavailable' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not $liveModeRequested) {
        Write-HandoffResult (New-HandoffResult `
            -Outcome 'go' `
            -Reason '' `
            -PreflightOnly $true `
            -Checks @(
                'matrix-approved',
                'profile-crm91-present',
                'credential-target-present',
                'slice-a-contact-reused',
                'slice-c-fixture-owner-matches-operator',
                'feature-flags-remain-false'))
        $scriptExitCode = 0
        throw 'result-written'
    }

    $credentialPassword = Get-P72CredentialPassword
    if ([string]::IsNullOrWhiteSpace($credentialPassword)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'credential-unavailable' -PreflightOnly $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'dotnet-unavailable' -PreflightOnly $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    [Environment]::SetEnvironmentVariable('CRM_PASSWORD', $credentialPassword, 'Process')
    # 兩個 child lane 共用 fixture scalar，但 mode flag 與 evidence path 永遠互斥；先清掉另一
    # lane 的 process environment，避免從 parent 或前一次測試繼承後誤觸 mutation。
    if ($isReconciliationMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    elseif ($isRepairMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    elseif ($isRepairProbeMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', '1', 'Process')
    }
    else {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FIXTURE_OWNER', [string]$fixture.ownerIdentity, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FIXTURE_MARKER', [string]$fixture.marker, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_CONTACT_ID', [string]$sourceFixture.contactId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_ADD_LIST_ID', [string]$fixture.addListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REMOVE_LIST_ID', [string]$fixture.removeListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_LIST_ID', [string]$fixture.smallGroupListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID', [string]$fixture.smallGroupTargetLeaderContactId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID', [string]$fixture.smallGroupExpectedRelationshipListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID', [string]$fixture.transferSourceListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID', [string]$fixture.transferTargetListId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_WEEK_START_UTC', [string]$fixture.transferWeekStartUtc, 'Process')

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-slice-c-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    $temporaryDirectoryCreated = $true
    if ($isReconciliationMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementReconciliationEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Reconcile_package02_data8_list_management_emits_sanitized_reconciliation'
    }
    elseif ($isRepairMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Repair_package02_data8_relationship_fixture_emits_sanitized_evidence'
    }
    elseif ($isRepairProbeMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairProbeEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Probe_package02_data8_relationship_fixture_emits_sanitized_evidence'
    }
    else {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Live_package02_data8_list_management_emits_sanitized_evidence'
    }
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetCommand.Source
    $startInfo.Arguments = 'test ' + (Quote-Argument $testProjectPath) +
        ' --no-restore --filter ' +
        (Quote-Argument $testFilter) +
        ' --blame-hang-timeout 150s --verbosity quiet'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'dotnet-start-failed'
    }

    $childProcessStarted = $true
    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
        # timeout 後只終止同一 child tree，不重試或重新送出可能已 commit 的 operation。
        try {
            & taskkill.exe /PID $process.Id /T /F *> $null
        }
        catch {
            try { $process.Kill() } catch { }
        }

        $timeoutOperations = @()
        if ($operationMayHaveExecuted) {
            $timeoutOperations = New-NotStartedOperations
        }
        Complete-HandoffResult (New-HandoffResult `
            -Outcome 'no-go' `
            -Reason 'test-timeout' `
            -PreflightOnly $false `
            -OperationExecuted $operationMayHaveExecuted `
            -Operations $timeoutOperations)
        $scriptExitCode = 2
        throw 'result-written'
    }

    $process.WaitForExit()
    [void]$standardOutputTask.GetAwaiter().GetResult()
    [void]$standardErrorTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        # child 的 evidence 檔不是成功證明：非零結束可能代表 operation、runtime 或 cleanup
        # 在寫檔後仍失敗。先 drain 兩個 stream 才讀取穩定 ExitCode，接著完全拒絕 child
        # 留下的內容，避免格式正確但不可信的 JSON 越過 process lifecycle 邊界。Execute lane
        # 保守宣告可能已進入 operation 範圍，並以五筆 not-started projection 禁止任何重試；
        # reconciliation lane 則維持零 mutation 的空 operation 集合。
        $childFailureOperations = @()
        if ($operationMayHaveExecuted) {
            $childFailureOperations = New-NotStartedOperations
        }
        Complete-HandoffResult (New-HandoffResult `
            -Outcome 'no-go' `
            -Reason 'child-process-failed' `
            -PreflightOnly $false `
            -OperationExecuted $operationMayHaveExecuted `
            -Operations $childFailureOperations)
        $scriptExitCode = 2
        throw 'result-written'
    }
    if ($isReconciliationMode) {
        $strictEvidence = Get-StrictSliceCReconciliationEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $false `
            -ReadOnlyProbeExecuted $strictEvidence.readOnlyProbeExecuted `
            -OwnerBinding $strictEvidence.ownerBinding `
            -ProbeStage $strictEvidence.probeStage `
            -States $strictEvidence.states)
    }
    elseif ($isRepairMode) {
        $strictEvidence = Get-StrictSliceCRepairEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $strictEvidence.operationExecuted `
            -ReadBackConfirmed $strictEvidence.readBackConfirmed)
    }
    elseif ($isRepairProbeMode) {
        $strictEvidence = Get-StrictSliceCRepairProbeEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $false `
            -ReadOnlyProbeExecuted $strictEvidence.readOnlyProbeExecuted `
            -ProbeStage 'relationship-list-repair-preconditions' `
            -Probe $strictEvidence.probe)
    }
    else {
        $strictEvidence = Get-StrictSliceCEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $strictEvidence.operationExecuted `
            -Operations $strictEvidence.operations)
    }
    $scriptExitCode = if ($completedHandoffOutcome -ceq 'go') { 0 } else { 2 }
}
catch {
    if (-not $resultAlreadyWritten) {
        if ($childProcessStarted) {
            $reason = if ([string]$_.Exception.Message -eq 'evidence-result-unavailable') { 'evidence-result-unavailable' } else { 'handoff-failed' }
            $childFailureOperations = @()
            if ($operationMayHaveExecuted) {
                $childFailureOperations = New-NotStartedOperations
            }
            Complete-HandoffResult (New-HandoffResult `
                -Outcome 'no-go' `
                -Reason $reason `
                -PreflightOnly $false `
                -OperationExecuted $operationMayHaveExecuted `
                -Operations $childFailureOperations)
            $scriptExitCode = 2
        }
        else {
            $reason = if ([string]$_.Exception.Message -eq 'dotnet-start-failed') { 'dotnet-start-failed' } else { 'preflight-failed' }
            Complete-HandoffResult (New-HandoffResult -Outcome 'error' -Reason $reason -PreflightOnly (-not $liveModeRequested))
            $scriptExitCode = if ($completedHandoffOutcome -ceq 'no-go') { 2 } else { 1 }
        }
    }
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                & taskkill.exe /PID $process.Id /T /F *> $null
            }
        }
        catch {
            try { $process.Kill() } catch { }
        }
        finally {
            $process.Dispose()
        }
    }

    foreach ($name in $inputEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }

    if ($temporaryDirectoryCreated) {
        # Complete-HandoffResult 已在任何 JSON 輸出前處理第一次 cleanup 並於失敗時回報 No-Go。
        # 這裡只保留第二次 best-effort，避免錯誤後留下 evidence；無論結果如何都不得改寫已輸出的
        # sanitized verdict，也不得輸出 temporary path 或原始刪除例外。
        if (Remove-OwnedSliceCTemporaryDirectory $temporaryDirectory) {
            $temporaryDirectory = $null
            $temporaryDirectoryCreated = $false
        }
    }

    $credentialPassword = $null
}

exit $scriptExitCode
