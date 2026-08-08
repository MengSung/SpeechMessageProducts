<#
.SYNOPSIS
執行 P7.2 contact basic-info 固定 fixture preflight，或以明確 opt-in 取得一次 live evidence。

.DESCRIPTION
預設行為只驗證 P7.2 第一個 required-for-activation slice 是否具備可執行
的本機前置條件：版本化 coverage matrix、ChurchReport 的 sunnyvalechback
CE 9.1 Data8 組態、同一 Windows identity 建立的 P6.2 crm91 profile 與
Generic Credential，以及 task-owned contact fixture descriptor。預設不啟動
dotnet、Gateway、Official Worker 或任何 CE operation，也不修改產品設定、
feature flag 或 CRM 資料。

只有操作者明確加上 -ExecuteFixture，且全部 preflight 都通過後，腳本才從固定
Generic Credential 短暫讀取密碼，啟動單一 bounded dotnet test，依序執行
baseline read、sentinel update、read-back、restore 與 restore read-back。
fixture descriptor 只保存 contact GUID、固定 marker、owner identity 與 deployment
metadata；baseline／sentinel 不落檔。ambiguous timeout 不得盲目重試。

腳本永遠只輸出一行固定 JSON。Credential Manager native pointer 由
CredRead/CredFree 在 finally 釋放；密碼只存在於目前 PowerShell 與 child process
environment，執行後立即還原。TRX 以禁止 DTD 的 strict parser 只擷取固定 marker；
輸出不含 password、token、cookie、endpoint、OrganizationId、完整 GUID、帳號、
baseline、sentinel、路徑或原始例外。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryPath,

    [Parameter(Mandatory = $false)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $false)]
    [string] $FixtureDescriptorPath,

    [switch] $Json,

    [switch] $ExecuteFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptExitCode = 1
$resultAlreadyWritten = $false
$temporaryDirectory = $null
$process = $null
$childProcessStarted = $false
$credentialPassword = $null
$fixture = $null
$previousEnvironment = @{}
$inputEnvironmentNames = @(
    'CRM_PASSWORD',
    'SPEECHMESSAGE_P7_2_LIVE',
    'P7_2_CONTACT_ID',
    'P7_2_FIXTURE_OWNER',
    'P7_2_FIXTURE_MARKER'
)
$credentialTarget = 'speechmessage.crm91.p62'
$expectedProfileAlias = 'sunnyvalechback'
$expectedDeploymentProfileAlias = 'crm91'
$expectedOperationId = 'memberinfo.contact.update.basic.info'

if ([string]::IsNullOrWhiteSpace($ProfileInputPath)) {
    $ProfileInputPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'
}

if ([string]::IsNullOrWhiteSpace($FixtureDescriptorPath)) {
    $FixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\contact-basic-info-fixture.json'
}

function New-PreflightResult {
    param(
        [string] $Outcome,
        [string] $Reason,
        [object[]] $Checks = @(),
        [bool] $OperationExecuted = $false,
        [string] $SentinelState = '',
        [string] $CleanupState = ''
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        reason = $Reason
        operationId = $expectedOperationId
        profileAlias = $expectedProfileAlias
        deploymentProfileAlias = $expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = -not [bool]$ExecuteFixture
        operationExecuted = $OperationExecuted
        featureFlagChanged = $false
    }

    if ($Checks.Count -gt 0) {
        $result.checks = $Checks
    }
    if (-not [string]::IsNullOrWhiteSpace($SentinelState)) {
        $result.sentinelState = $SentinelState
    }
    if (-not [string]::IsNullOrWhiteSpace($CleanupState)) {
        $result.cleanupState = $CleanupState
    }

    return $result
}

function Write-PreflightResult {
    param([object] $Result)

    $script:resultAlreadyWritten = $true
    $Result | ConvertTo-Json -Compress -Depth 6
}

function Read-StrictJsonFile {
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
    param([object] $Value)

    $parsed = [Guid]::Empty
    return $Value -is [string] -and
        [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -and
        $parsed -ne [Guid]::Empty
}

function Test-SafeOwnerIdentity {
    param([object] $Value)

    return $Value -is [string] -and
        -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 256 -and
        $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Test-CredentialTargetPresent {
    <#
    只讀固定 Generic Credential 的存在性。native pointer 的唯一 owner 是本方法；
    CredFree 必須在 finally 執行，且不把 target、帳號或 blob 內容帶回 PowerShell。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72.CredentialPresenceReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72
{
    /// <summary>檢查固定 Generic Credential 是否存在；不讀取秘密 blob。</summary>
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

        /// <summary>讀取一次並立即釋放 native credential pointer，只回傳是否存在。</summary>
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

        return [SpeechMessage.P72.CredentialPresenceReader]::Exists($credentialTarget)
    }
    catch {
        return $false
    }
}

function Get-P72CredentialPassword {
    <#
    .SYNOPSIS
    從固定 Windows Generic Credential 讀取 P7.2 CE 9.1 測試密碼。

    .DESCRIPTION
    只有 ExecuteFixture 路徑才會呼叫本方法。native credential pointer 由 C# finally
    CredFree；managed char buffer 在 native helper 內清除。回傳值只交給 child process
    的短生命期 environment，不輸出、記錄、寫檔或序列化任何 credential 內容。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72Live.CredentialReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72Live
{
    /// <summary>讀取一次 Generic Credential blob 並在 native finally 釋放；不回傳帳號或 target。</summary>
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

        /// <summary>只讀固定 blob；離開 native scope 前清除暫存字元並釋放 pointer。</summary>
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

        return [SpeechMessage.P72Live.CredentialReader]::ReadGenericSecret($credentialTarget)
    }
    catch {
        return $null
    }
}

function Get-StrictEvidenceFromTrx {
    param([string] $TrxPath)

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        throw 'evidence-result-unavailable'
    }

    $fileInfo = Get-Item -LiteralPath $TrxPath -ErrorAction Stop
    if ($fileInfo.Length -le 0 -or $fileInfo.Length -gt 1048576) {
        throw 'evidence-result-unavailable'
    }

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($TrxPath, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        $matches = @()
        foreach ($node in @($document.SelectNodes('//*[local-name()="StdOut"]'))) {
            $matches += [Regex]::Matches(
                $node.InnerText,
                'P7_2_EVIDENCE_JSON=(\{[^\r\n]+\})')
        }
        if ($matches.Count -ne 1) {
            throw 'evidence-result-unavailable'
        }

        $evidence = $matches[0].Groups[1].Value | ConvertFrom-Json -ErrorAction Stop
        $allowedReasons = @(
            '', 'runtime-failure', 'cleanup-failure', 'reconciliation-failed',
            'write-response-state-mismatch', 'write-not-committed', 'write-ambiguous',
            'cleanup-reconciliation-failed', 'cleanup-failed', 'cleanup-ambiguous-reconciled',
            'write-ambiguous-reconciled', 'write-result-invalid')
        $allowedSentinelStates = @('baseline', 'confirmed', 'confirmed-after-fault', 'confirmed-after-invalid-response', 'unknown')
        $allowedCleanupStates = @('not-required', 'restored', 'restored-after-fault', 'manual-reconciliation-required')
        if ($evidence.schemaVersion -ne 1 -or
            $evidence.operationId -cne $expectedOperationId -or
            $evidence.profileAlias -cne $expectedProfileAlias -or
            $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
            $evidence.ceVersion -cne '9.1' -or
            $evidence.connector -cne 'Data8' -or
            $evidence.preflightOnly -ne $false -or
            $evidence.operationExecuted -isnot [bool] -or
            $evidence.featureFlagChanged -ne $false -or
            $evidence.outcome -cnotin @('go', 'no-go') -or
            $evidence.reason -cnotin $allowedReasons -or
            $evidence.sentinelState -cnotin $allowedSentinelStates -or
            $evidence.cleanupState -cnotin $allowedCleanupStates) {
            throw 'evidence-result-unavailable'
        }

        if ($evidence.outcome -ceq 'go' -and
            ($evidence.reason -cne '' -or
             -not $evidence.operationExecuted -or
             $evidence.sentinelState -cne 'confirmed' -or
             $evidence.cleanupState -cne 'restored')) {
            throw 'evidence-result-unavailable'
        }

        if ($evidence.outcome -ceq 'no-go' -and $evidence.reason -ceq '') {
            throw 'evidence-result-unavailable'
        }

        return [pscustomobject]@{
            outcome = [string]$evidence.outcome
            reason = [string]$evidence.reason
            operationExecuted = [bool]$evidence.operationExecuted
            sentinelState = [string]$evidence.sentinelState
            cleanupState = [string]$evidence.cleanupState
        }
    }
    catch {
        if ($_.Exception.Message -eq 'evidence-result-unavailable') {
            throw
        }

        throw 'evidence-result-unavailable'
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }
}

function Test-Matrix {
    param([object] $Matrix)

    if ($null -eq $Matrix -or
        $Matrix.schemaVersion -cne 'p7.2.fixture-activation.v1' -or
        $Matrix.defaultDispatch -cne 'fail-closed' -or
        $Matrix.allowedExecutionHost -cne 'Lenovo Legion local development environment' -or
        $Matrix.allowedConnector -cne 'Data8' -or
        $Matrix.defaultCeSupport.ce82 -cne 'unsupported' -or
        $Matrix.defaultCeSupport.ce91 -cne 'fixture-pending') {
        return $false
    }

    $slice = @($Matrix.slices | Where-Object { $_.id -ceq 'contact-basic-info' })
    if ($slice.Count -ne 1 -or
        $slice[0].status -cne 'required-for-activation' -or
        @($slice[0].operationIds).Count -ne 1 -or
        $slice[0].operationIds[0] -cne $expectedOperationId -or
        @($slice[0].allowedMutations).Count -ne 2 -or
        $slice[0].allowedMutations[0] -cne 'contact.mobilephone' -or
        $slice[0].allowedMutations[1] -cne 'contact.address2_line1' -or
        [string]::IsNullOrWhiteSpace([string]$slice[0].cleanup) -or
        [string]::IsNullOrWhiteSpace([string]$slice[0].reconciliation)) {
        return $false
    }

    return $true
}

function Test-ProfileInput {
    param([object] $Profile)

    if ($null -eq $Profile -or $Profile.schemaVersion -ne 1) {
        return $false
    }

    $profiles = @($Profile.profiles)
    if ($profiles.Count -ne 2) {
        return $false
    }

    $crm91 = @($profiles | Where-Object { $_.profileAlias -ceq 'crm91' })
    if ($crm91.Count -ne 1) {
        return $false
    }

    return $crm91[0].workerKind -ceq 'OfficialCrm91Worker' -and
        $crm91[0].authentication -ceq 'Ifd' -and
        $crm91[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm91[0].identity.reference -ceq $credentialTarget
}

function Test-ChurchReportData8Configuration {
    param(
        [string] $RepositoryRoot
    )

    $productionSettingsPath = Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.json'
    $developmentSettingsPath = Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.Development.json'
    try {
        $productionText = Read-StrictTextFile -Path $productionSettingsPath -MaximumBytes 512KB -FailureReason 'churchreport-config-invalid'
        $developmentText = Read-StrictTextFile -Path $developmentSettingsPath -MaximumBytes 128KB -FailureReason 'churchreport-config-invalid'

        # JSONC 不能由 Windows PowerShell 5.1 原生 ConvertFrom-Json 安全解析；這裡只比對
        # 版本控制下的固定 leaf values，且每個 regex 都限制在單一 object 行內，避免把
        # caller-supplied URL、credential 或 endpoint 當成 Data8 routing authority。
        $catalogPattern = '"sunnyvalechback"\s*:\s*\{[^\r\n\}]*"CeVersion"\s*:\s*"9\.1"[^\r\n\}]*"ServiceUri"\s*:\s*"https://sunnyvalechback\.speechmessage\.com\.tw/XRMServices/2011/Organization\.svc"'
        $profilePattern = '"ProfileAlias"\s*:\s*"sunnyvalechback"'
        $modePattern = '"ConnectionMode"\s*:\s*"Embedded"'
        $readFlagPattern = '"Package01FeeReadsEnabled"\s*:\s*false'
        $writeFlagPattern = '"Package02ContactBasicInfoUpdatesEnabled"\s*:\s*false'
        return [Regex]::IsMatch($productionText, $catalogPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($developmentText, $profilePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($developmentText, $modePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($developmentText, $readFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($developmentText, $writeFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    catch {
        return $false
    }
}

function Test-FixtureDescriptor {
    param(
        [object] $Fixture,
        [string] $CurrentIdentity
    )

    if ($null -eq $Fixture -or
        $Fixture.schemaVersion -ne 1 -or
        $Fixture.fixtureId -cne 'p7.2-contact-basic-info' -or
        $Fixture.profileAlias -cne $expectedProfileAlias -or
        $Fixture.ceVersion -cne '9.1' -or
        $Fixture.connector -cne 'Data8' -or
        $Fixture.marker -cne 'p7.2-contact-basic-info' -or
        -not (Test-NonEmptyGuid $Fixture.contactId) -or
        -not (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -or
        -not [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    return $true
}

try {
    foreach ($name in $inputEnvironmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    $resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    $matrixPath = Join-Path $resolvedRepositoryPath '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $testProjectPath = Join-Path $resolvedRepositoryPath 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj'
    if (-not (Test-Path -LiteralPath $resolvedRepositoryPath -PathType Container) -or
        -not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'error' -Reason 'repository-invalid')
        $scriptExitCode = 1
        throw 'result-written'
    }

    $matrix = Read-StrictJsonFile -Path $matrixPath -MaximumBytes 256KB -FailureReason 'matrix-invalid'
    if (-not (Test-Matrix $matrix)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'error' -Reason 'matrix-invalid')
        $scriptExitCode = 1
        throw 'result-written'
    }

    if (-not (Test-ChurchReportData8Configuration -RepositoryRoot $resolvedRepositoryPath)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'churchreport-config-invalid')
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $ProfileInputPath -PathType Leaf)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'profile-input-required')
        $scriptExitCode = 2
        throw 'result-written'
    }

    $profile = Read-StrictJsonFile -Path $ProfileInputPath -MaximumBytes 128KB -FailureReason 'profile-input-invalid'
    if (-not (Test-ProfileInput $profile)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'profile-input-invalid')
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $FixtureDescriptorPath -PathType Leaf)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'fixture-input-required')
        $scriptExitCode = 2
        throw 'result-written'
    }

    $fixture = Read-StrictJsonFile -Path $FixtureDescriptorPath -MaximumBytes 32KB -FailureReason 'fixture-input-invalid'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (-not (Test-FixtureDescriptor -Fixture $fixture -CurrentIdentity $identity)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'fixture-input-invalid')
        $scriptExitCode = 2
        throw 'result-written'
    }

    # 所有 non-secret、純本機 fixture contract 都必須先通過，才碰 Credential Manager；
    # 如此 owner／profile／CE／connector 不符時不會產生任何 credential access side effect。
    if (-not (Test-CredentialTargetPresent)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'credential-unavailable')
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not $ExecuteFixture) {
        Write-PreflightResult (New-PreflightResult -Outcome 'go' -Reason '' -Checks @(
            'matrix-approved',
            'profile-crm91-present',
            'credential-target-present',
            'fixture-owner-matches-operator'
        ))
        $scriptExitCode = 0
        throw 'result-written'
    }

    $credentialPassword = Get-P72CredentialPassword
    if ([string]::IsNullOrWhiteSpace($credentialPassword)) {
        Write-PreflightResult (New-PreflightResult -Outcome 'no-go' -Reason 'credential-unavailable')
        $scriptExitCode = 2
        throw 'result-written'
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        Write-PreflightResult (New-PreflightResult -Outcome 'error' -Reason 'dotnet-unavailable')
        $scriptExitCode = 1
        throw 'result-written'
    }

    [Environment]::SetEnvironmentVariable('CRM_PASSWORD', $credentialPassword, 'Process')
    [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_LIVE', '1', 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_CONTACT_ID', [string]$fixture.contactId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_FIXTURE_OWNER', [string]$fixture.ownerIdentity, 'Process')
    [Environment]::SetEnvironmentVariable('P7_2_FIXTURE_MARKER', [string]$fixture.marker, 'Process')

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-live-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    $trxPath = Join-Path $temporaryDirectory 'P72Data8ContactBasicInfoWriteEvidence.trx'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetCommand.Source
    $startInfo.Arguments = 'test "' + $testProjectPath + '" --no-restore --filter "FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ContactBasicInfoWriteEvidenceTests.Live_package02_data8_contact_basic_info_write_emits_sanitized_evidence" --logger "trx;LogFileName=P72Data8ContactBasicInfoWriteEvidence.trx" --results-directory "' + $temporaryDirectory + '" --blame-hang-timeout 150s --verbosity quiet'
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
        # Process 已可能送出 write；timeout 永遠採 manual reconciliation，且不得再啟動第二次 test。
        try {
            & taskkill.exe /PID $process.Id /T /F *> $null
        }
        catch {
            try {
                $process.Kill()
            }
            catch {
                # process 已退出或 OS 拒絕時仍維持固定 timeout evidence；finally 會 Dispose handle。
            }
        }

        Write-PreflightResult (New-PreflightResult `
            -Outcome 'no-go' `
            -Reason 'test-timeout' `
            -OperationExecuted $true `
            -SentinelState 'unknown' `
            -CleanupState 'manual-reconciliation-required')
        $scriptExitCode = 2
        throw 'result-written'
    }

    # WaitForExit(int) 不保證 redirected async stream 已完成；第二次無參數等待只負責 drain，
    # child 已確定退出，不會擴張 180 秒 operation deadline。
    $process.WaitForExit()
    [void]$standardOutputTask.GetAwaiter().GetResult()
    [void]$standardErrorTask.GetAwaiter().GetResult()
    $strictEvidence = Get-StrictEvidenceFromTrx -TrxPath $trxPath
    Write-PreflightResult (New-PreflightResult `
        -Outcome $strictEvidence.outcome `
        -Reason $strictEvidence.reason `
        -OperationExecuted $strictEvidence.operationExecuted `
        -SentinelState $strictEvidence.sentinelState `
        -CleanupState $strictEvidence.cleanupState)
    $scriptExitCode = if ($strictEvidence.outcome -ceq 'go') { 0 } else { 2 }
}
catch {
    if (-not $resultAlreadyWritten) {
        if ($childProcessStarted) {
            $reason = if ([string]$_.Exception.Message -eq 'evidence-result-unavailable') {
                'evidence-result-unavailable'
            }
            else {
                'handoff-failed'
            }
            Write-PreflightResult (New-PreflightResult `
                -Outcome 'no-go' `
                -Reason $reason `
                -OperationExecuted $true `
                -SentinelState 'unknown' `
                -CleanupState 'manual-reconciliation-required')
            $scriptExitCode = 2
        }
        else {
            $reason = if ([string]$_.Exception.Message -eq 'dotnet-start-failed') {
                'dotnet-start-failed'
            }
            else {
                'preflight-failed'
            }
            Write-PreflightResult (New-PreflightResult -Outcome 'error' -Reason $reason)
            $scriptExitCode = 1
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
            try {
                $process.Kill()
            }
            catch {
                # 只處理本 script 建立的 child；OS 已回收時不掩蓋主要 sanitized evidence。
            }
        }
        finally {
            $process.Dispose()
        }
    }

    foreach ($name in $inputEnvironmentNames) {
        if ($previousEnvironment.ContainsKey($name)) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
    }

    if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
        try {
            Remove-Item -LiteralPath $temporaryDirectory -Force -Recurse -ErrorAction Stop
        }
        catch {
            # 防毒或索引器可能短暫鎖住 TRX；此最佳努力清理不得阻斷 environment/credential cleanup。
        }
    }

    $fixture = $null
    $credentialPassword = $null
}

exit $scriptExitCode
