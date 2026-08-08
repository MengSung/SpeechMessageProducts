<#
.SYNOPSIS
    執行 P7.2 Slice B1/B2 的受控 CE 9.1 Data8 evidence handoff。

.DESCRIPTION
    這個 Windows PowerShell 5.1 相容腳本只接受 task-owned fixture descriptor，
    固定使用 sunnyvalechback／crm91／Data8，並從 Windows Generic Credential
    speechmessage.crm91.p62 讀取密碼。預設只做 preflight；只有明確指定
    -ExecuteFixture 才會依序執行 B1 LINE profile sentinel write/restore 及 B2
    ungrouped commitment read/parity。子測試程序有 180 秒上限，輸出只允許
    sanitized evidence JSON，不會修改 feature flag 或啟動 Official Worker。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryPath,

    [Parameter(Mandatory = $false)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $false)]
    [string] $B1FixtureDescriptorPath,

    [Parameter(Mandatory = $false)]
    [string] $B2FixtureDescriptorPath,

    [switch] $Json,

    [switch] $ExecuteFixture,

    [switch] $ResumeB2Only
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptExitCode = 1
$resultAlreadyWritten = $false
$temporaryDirectories = [System.Collections.Generic.List[string]]::new()
$processes = [System.Collections.Generic.List[Diagnostics.Process]]::new()
$credentialPassword = $null
$previousEnvironment = @{}
$inputEnvironmentNames = @(
    'CRM_PASSWORD',
    'SPEECHMESSAGE_P7_2_B1_LIVE',
    'P7_2_B1_CONTACT_ID',
    'P7_2_B1_FIXTURE_OWNER',
    'P7_2_B1_FIXTURE_MARKER',
    'SPEECHMESSAGE_P7_2_B2_LIVE',
    'P7_2_B2_FIXTURE_OWNER',
    'P7_2_B2_FIXTURE_MARKER',
    'P7_2_B2_EVIDENCE_PATH'
)
$credentialTarget = 'speechmessage.crm91.p62'
$expectedProfileAlias = 'sunnyvalechback'
$expectedDeploymentProfileAlias = 'crm91'
$expectedB1OperationId = 'memberinfo.contact.update.line.profile'
$expectedB2OperationId = 'memberinfo.contact.count.ungrouped.commitment'

if ([string]::IsNullOrWhiteSpace($ProfileInputPath)) {
    $ProfileInputPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'
}
if ([string]::IsNullOrWhiteSpace($B1FixtureDescriptorPath)) {
    $B1FixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\contact-line-profile-fixture.json'
}
if ([string]::IsNullOrWhiteSpace($B2FixtureDescriptorPath)) {
    $B2FixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\ungrouped-commitment-fixture.json'
}

function New-HandoffResult {
    param(
        [string] $Outcome,
        [string] $Reason,
        [bool] $PreflightOnly = -not [bool]$ExecuteFixture,
        [bool] $OperationExecuted = $false,
        [object[]] $Operations = @(),
        [object[]] $Checks = @()
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        reason = $Reason
        profileAlias = $expectedProfileAlias
        deploymentProfileAlias = $expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $PreflightOnly
        operationExecuted = $OperationExecuted
        featureFlagChanged = $false
    }
    if ($Checks.Count -gt 0) { $result.checks = $Checks }
    if ($Operations.Count -gt 0) { $result.operations = $Operations }
    return $result
}

function Write-HandoffResult {
    param([object] $Result)
    $script:resultAlreadyWritten = $true
    $Result | ConvertTo-Json -Compress -Depth 8
}

function Read-StrictJsonFile {
    param([string] $Path, [int] $MaximumBytes, [string] $FailureReason)
    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw $FailureReason }
        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) { throw $FailureReason }
        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { throw $FailureReason }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) { throw $FailureReason }
        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch { throw $FailureReason }
    finally { if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) } }
}

function Read-StrictTextFile {
    param([string] $Path, [int] $MaximumBytes, [string] $FailureReason)
    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw $FailureReason }
        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) { throw $FailureReason }
        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { throw $FailureReason }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) { throw $FailureReason }
        return $text
    }
    catch { throw $FailureReason }
    finally { if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) } }
}

function Test-NonEmptyGuid {
    param([object] $Value)
    $parsed = [Guid]::Empty
    return $Value -is [string] -and [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -and $parsed -ne [Guid]::Empty
}

function Test-SafeOwnerIdentity {
    param([object] $Value)
    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 256 -and $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Test-CredentialTargetPresent {
    try {
        if ($null -eq ('SpeechMessage.P72.CredentialPresenceReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace SpeechMessage.P72 {
    public static class CredentialPresenceReader {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential { public uint Flags; public uint Type; public IntPtr TargetName; public IntPtr Comment; public long LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public IntPtr TargetAlias; public IntPtr UserName; }
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("Advapi32.dll", SetLastError = true)] private static extern void CredFree(IntPtr credential);
        public static bool Exists(string target) { IntPtr pointer = IntPtr.Zero; try { if (String.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0 || !CredRead(target, 1, 0, out pointer) || pointer == IntPtr.Zero) return false; return Marshal.PtrToStructure<NativeCredential>(pointer).Type == 1; } catch { return false; } finally { if (pointer != IntPtr.Zero) CredFree(pointer); } }
    }
}
'@ -ErrorAction Stop
        }
        return [SpeechMessage.P72.CredentialPresenceReader]::Exists($credentialTarget)
    }
    catch { return $false }
}

function Get-CredentialPassword {
    try {
        if ($null -eq ('SpeechMessage.P72Live.CredentialReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace SpeechMessage.P72Live {
    public static class CredentialReader {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential { public uint Flags; public uint Type; public IntPtr TargetName; public IntPtr Comment; public long LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public IntPtr TargetAlias; public IntPtr UserName; }
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("Advapi32.dll", SetLastError = true)] private static extern void CredFree(IntPtr credential);
        public static string ReadGenericSecret(string target) { IntPtr pointer = IntPtr.Zero; try { if (String.IsNullOrWhiteSpace(target) || !CredRead(target, 1, 0, out pointer) || pointer == IntPtr.Zero) return null; var credential = Marshal.PtrToStructure<NativeCredential>(pointer); if (credential.Type != 1 || credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 8192 || (credential.CredentialBlobSize & 1) != 0) return null; var count = checked((int)credential.CredentialBlobSize / 2); var chars = new char[count]; try { Marshal.Copy(credential.CredentialBlob, chars, 0, count); var length = count; while (length > 0 && chars[length - 1] == '\0') length--; for (var i = 0; i < length; i++) if (chars[i] == '\0') return null; return length == 0 ? null : new String(chars, 0, length); } finally { Array.Clear(chars, 0, chars.Length); } } catch { return null; } finally { if (pointer != IntPtr.Zero) CredFree(pointer); } }
    }
}
'@ -ErrorAction Stop
        }
        return [SpeechMessage.P72Live.CredentialReader]::ReadGenericSecret($credentialTarget)
    }
    catch { return $null }
}

function Test-Matrix {
    param([object] $Matrix)
    if ($null -eq $Matrix -or $Matrix.schemaVersion -cne 'p7.2.fixture-activation.v1' -or
        $Matrix.defaultDispatch -cne 'fail-closed' -or
        $Matrix.allowedExecutionHost -cne 'Lenovo Legion local development environment' -or
        $Matrix.allowedConnector -cne 'Data8' -or
        $Matrix.defaultCeSupport.ce82 -cne 'unsupported') { return $false }
    foreach ($expected in @(
        @{ Id = 'contact-line-profile'; Operation = $expectedB1OperationId },
        @{ Id = 'ungrouped-commitment-aggregate'; Operation = $expectedB2OperationId })) {
        $slice = @($Matrix.slices | Where-Object { $_.id -ceq $expected.Id })
        if ($slice.Count -ne 1 -or $slice[0].status -cne 'required-for-activation' -or
            @($slice[0].operationIds).Count -ne 1 -or $slice[0].operationIds[0] -cne $expected.Operation -or
            $slice[0].requiredCeVersion -cne '9.1' -or [string]::IsNullOrWhiteSpace([string]$slice[0].cleanup) -or
            [string]::IsNullOrWhiteSpace([string]$slice[0].reconciliation)) { return $false }
    }
    return $true
}

function Test-ProfileInput {
    param([object] $Profile)
    if ($null -eq $Profile -or $Profile.schemaVersion -ne 1) { return $false }
    $crm91 = @($Profile.profiles | Where-Object { $_.profileAlias -ceq 'crm91' })
    return $crm91.Count -eq 1 -and $crm91[0].workerKind -ceq 'OfficialCrm91Worker' -and
        $crm91[0].authentication -ceq 'Ifd' -and $crm91[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm91[0].identity.reference -ceq $credentialTarget
}

function Test-ChurchReportConfiguration {
    param([string] $RepositoryRoot)
    try {
        $production = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.json') 512KB 'churchreport-config-invalid'
        $development = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') 128KB 'churchreport-config-invalid'
        $catalog = '"sunnyvalechback"\s*:\s*\{[^\r\n\}]*"CeVersion"\s*:\s*"9\.1"[^\r\n\}]*"ServiceUri"\s*:\s*"https://sunnyvalechback\.speechmessage\.com\.tw/XRMServices/2011/Organization\.svc"'
        return [Regex]::IsMatch($production, $catalog, 'IgnoreCase') -and
            [Regex]::IsMatch($development, '"ProfileAlias"\s*:\s*"sunnyvalechback"', 'IgnoreCase') -and
            [Regex]::IsMatch($development, '"ConnectionMode"\s*:\s*"Embedded"', 'IgnoreCase') -and
            [Regex]::IsMatch($development, '"Package01FeeReadsEnabled"\s*:\s*false', 'IgnoreCase') -and
            [Regex]::IsMatch($development, '"Package02ContactBasicInfoUpdatesEnabled"\s*:\s*false', 'IgnoreCase')
    }
    catch { return $false }
}

function Test-FixtureDescriptor {
    param([object] $Fixture, [string] $FixtureId, [string] $Marker, [string] $CurrentIdentity, [bool] $RequireContactId)
    if ($null -eq $Fixture -or $Fixture.schemaVersion -ne 1 -or $Fixture.fixtureId -cne $FixtureId -or
        $Fixture.profileAlias -cne $expectedProfileAlias -or $Fixture.ceVersion -cne '9.1' -or $Fixture.connector -cne 'Data8' -or
        $Fixture.marker -cne $Marker -or -not (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -or
        -not [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    return (-not $RequireContactId) -or (Test-NonEmptyGuid $Fixture.contactId)
}

function Get-StrictEvidence {
    param([string] $TrxPath, [string] $Marker, [string] $OperationId, [string] $Kind)
    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) { throw 'evidence-result-unavailable' }
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($TrxPath, $settings)
        $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader)
        $matches = @()
        foreach ($node in @($document.SelectNodes('//*[local-name()="StdOut"]'))) { $matches += [Regex]::Matches($node.InnerText, [Regex]::Escape($Marker) + '(\{[^\r\n]+\})') }
        if ($matches.Count -ne 1) { throw 'evidence-result-unavailable' }
        $evidence = $matches[0].Groups[1].Value | ConvertFrom-Json -ErrorAction Stop
        if ($evidence.schemaVersion -ne 1 -or $evidence.operationId -cne $OperationId -or
            $evidence.profileAlias -cne $expectedProfileAlias -or $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
            $evidence.ceVersion -cne '9.1' -or $evidence.connector -cne 'Data8' -or $evidence.preflightOnly -ne $false -or
            $evidence.operationExecuted -isnot [bool] -or $evidence.featureFlagChanged -ne $false -or
            $evidence.outcome -cnotin @('go', 'no-go')) { throw 'evidence-result-unavailable' }
        if ($Kind -eq 'B1') {
            if ($evidence.sentinelState -cnotin @('confirmed', 'unknown') -or $evidence.cleanupState -cnotin @('restored', 'manual-reconciliation-required')) { throw 'evidence-result-unavailable' }
            if ($evidence.outcome -ceq 'go' -and ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or $evidence.sentinelState -cne 'confirmed' -or $evidence.cleanupState -cne 'restored')) { throw 'evidence-result-unavailable' }
            return [pscustomobject]@{ operationId = $OperationId; outcome = [string]$evidence.outcome; reason = [string]$evidence.reason; operationExecuted = [bool]$evidence.operationExecuted; sentinelState = [string]$evidence.sentinelState; cleanupState = [string]$evidence.cleanupState }
        }
        if ($evidence.parityState -cnotin @('matched', 'mismatch', 'unknown')) { throw 'evidence-result-unavailable' }
        if ($evidence.outcome -ceq 'go' -and ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or $evidence.parityState -cne 'matched')) { throw 'evidence-result-unavailable' }
        return [pscustomobject]@{ operationId = $OperationId; outcome = [string]$evidence.outcome; reason = [string]$evidence.reason; operationExecuted = [bool]$evidence.operationExecuted; parityState = [string]$evidence.parityState; rowCount = [int]$evidence.rowCount }
    }
    catch { if ($_.Exception.Message -eq 'evidence-result-unavailable') { throw }; throw 'evidence-result-unavailable' }
    finally { if ($null -ne $reader) { $reader.Dispose() } }
}

function Get-StrictB2EvidenceFile {
    param([string] $EvidencePath)
    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.operationId -cne $expectedB2OperationId -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.parityState -cnotin @('confirmed', 'mismatch', 'unknown') -or
        $evidence.rowCount -isnot [int]) {
        throw 'evidence-result-unavailable'
    }
    if ($evidence.outcome -ceq 'go' -and
        ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or $evidence.parityState -cne 'confirmed')) {
        throw 'evidence-result-unavailable'
    }
    return [pscustomobject]@{
        operationId = $expectedB2OperationId
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        operationExecuted = [bool]$evidence.operationExecuted
        parityState = [string]$evidence.parityState
        rowCount = [int]$evidence.rowCount
    }
}

function Get-SanitizedEvidenceFailureReason {
    param([string] $TrxPath, [int] $ChildExitCode)
    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        if ($ChildExitCode -eq 0) { return 'evidence-result-unavailable' }
        return 'child-process-failed'
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
        $result = @($document.SelectNodes('//*[local-name()="UnitTestResult"]')) | Select-Object -First 1
        if ($null -eq $result) { return 'test-result-missing' }
        $documentText = [string]$document.InnerText
        if ($documentText.Contains('P7_2_B2_EVIDENCE_JSON=')) { return 'b2-evidence-outside-stdout' }
        $testOutcome = $result.GetAttribute('outcome')
        if ($testOutcome -ceq 'NotExecuted') { return 'test-not-executed' }
        if ($testOutcome -ceq 'Failed') {
            $diagnosticText = (@($document.SelectNodes('//*[local-name()="ErrorInfo"]/*[local-name()="Message" or local-name()="StackTrace"]')) | ForEach-Object { $_.InnerText }) -join "`n"
            if ($diagnosticText -match 'P72Data8B2LiveFactAttribute') { return 'b2-attribute-failure' }
            if ($diagnosticText -match 'ReadB2Fixture') { return 'b2-fixture-validation-failure' }
            if ($diagnosticText -match 'ResolveProfile') { return 'b2-profile-resolution-failure' }
            if ($diagnosticText -match 'P72Data8UngroupedCommitmentParityStore') { return 'b2-parity-store-failure' }
            if ($diagnosticText -match 'P72UngroupedCommitmentFixtureBridge') { return 'b2-parity-bridge-failure' }
            if ($diagnosticText -match 'DisposeRuntimeAsync|DisposeStore|DisposeLogger') { return 'b2-cleanup-failure' }
            if ($diagnosticText -match 'legacy-parity-mismatch') { return 'b2-legacy-parity-mismatch' }
            if ($diagnosticText -match 'read-timeout') { return 'b2-read-timeout' }
            if ($diagnosticText -match 'data8-read-failed-legacy-probe-succeeded') { return 'b2-data8-failed-legacy-succeeded' }
            if ($diagnosticText -match 'data8-read-failed-legacy-probe-failed') { return 'b2-data8-and-legacy-read-failed' }
            if ($diagnosticText -match 'data8-read-failed') { return 'b2-data8-read-failed' }
            if ($diagnosticText -match 'legacy-read-failed') { return 'b2-legacy-read-failed' }
            if ($diagnosticText -match 'read-failed') { return 'b2-read-failed' }
            if ($diagnosticText -match 'Should\(\)\.Be|Expected outcome') { return 'b2-assertion-failure' }
            if ($diagnosticText -match 'DirectoryNotFoundException') { return 'b2-repository-resolution-failure' }
            if ($diagnosticText -match 'NullReferenceException') { return 'b2-null-reference-failure' }
            if ($diagnosticText -match 'InvalidOperationException') { return 'b2-invalid-operation-failure' }
            return 'test-failed-before-evidence'
        }
        if ($ChildExitCode -eq 0) { return 'evidence-marker-missing' }
        return 'child-process-failed'
    }
    catch { return 'evidence-result-unavailable' }
    finally { if ($null -ne $reader) { $reader.Dispose() } }
}

function Quote-Argument { param([string] $Value) return '"' + $Value.Replace('"', '\"') + '"' }

function Invoke-LiveOperation {
    param([string] $Kind, [object] $Fixture, [string] $TestProjectPath, [string] $DotnetPath)
    $marker = if ($Kind -eq 'B1') { 'P7_2_B1_EVIDENCE_JSON=' } else { 'P7_2_B2_EVIDENCE_JSON=' }
    $operationId = if ($Kind -eq 'B1') { $expectedB1OperationId } else { $expectedB2OperationId }
    $testName = if ($Kind -eq 'B1') { 'Live_package02_data8_contact_line_profile_emits_sanitized_evidence' } else { 'Live_package02_data8_ungrouped_commitment_emits_sanitized_evidence' }
    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-profile-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory); $temporaryDirectories.Add($temporaryDirectory)
    $trxPath = Join-Path $temporaryDirectory ('P72Data8' + $Kind + 'Evidence.trx')
    [Environment]::SetEnvironmentVariable('CRM_PASSWORD', $credentialPassword, 'Process')
    [Environment]::SetEnvironmentVariable("SPEECHMESSAGE_P7_2_${Kind}_LIVE", '1', 'Process')
    [Environment]::SetEnvironmentVariable("P7_2_${Kind}_FIXTURE_OWNER", [string]$Fixture.ownerIdentity, 'Process')
    [Environment]::SetEnvironmentVariable("P7_2_${Kind}_FIXTURE_MARKER", [string]$Fixture.marker, 'Process')
    if ($Kind -eq 'B1') { [Environment]::SetEnvironmentVariable('P7_2_B1_CONTACT_ID', [string]$Fixture.contactId, 'Process') }
    $b2EvidencePath = Join-Path $temporaryDirectory 'P72Data8B2Evidence.json'
    if ($Kind -eq 'B2') { [Environment]::SetEnvironmentVariable('P7_2_B2_EVIDENCE_PATH', $b2EvidencePath, 'Process') }
    $arguments = 'test ' + (Quote-Argument $TestProjectPath) + ' --no-restore --filter ' + (Quote-Argument ('FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ContactProfileEvidenceTests.' + $testName)) + ' --logger ' + (Quote-Argument ('trx;LogFileName=' + [IO.Path]::GetFileName($trxPath))) + ' --results-directory ' + (Quote-Argument $temporaryDirectory) + ' --blame-hang-timeout 150s --verbosity quiet'
    $startInfo = [Diagnostics.ProcessStartInfo]::new(); $startInfo.FileName = $DotnetPath; $startInfo.Arguments = $arguments; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true; $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw 'dotnet-start-failed' }
    $processes.Add($process)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
        try { & taskkill.exe /PID $process.Id /T /F *> $null } catch { try { $process.Kill() } catch {} }
        return [pscustomobject]@{ operationId = $operationId; outcome = 'no-go'; reason = 'test-timeout'; operationExecuted = $true; sentinelState = 'unknown'; cleanupState = 'manual-reconciliation-required' }
    }
    $process.WaitForExit(); [void]$stdout.GetAwaiter().GetResult(); [void]$stderr.GetAwaiter().GetResult()
    try {
        if ($Kind -eq 'B2') {
            return Get-StrictB2EvidenceFile -EvidencePath $b2EvidencePath
        }
        return Get-StrictEvidence -TrxPath $trxPath -Marker $marker -OperationId $operationId -Kind $Kind
    }
    catch {
        $failureReason = Get-SanitizedEvidenceFailureReason -TrxPath $trxPath -ChildExitCode $process.ExitCode
        if ($Kind -eq 'B1') {
            return [pscustomobject]@{ operationId = $operationId; outcome = 'no-go'; reason = $failureReason; operationExecuted = $true; sentinelState = 'unknown'; cleanupState = 'manual-reconciliation-required' }
        }
        return [pscustomobject]@{ operationId = $operationId; outcome = 'no-go'; reason = $failureReason; operationExecuted = $true; parityState = 'unknown'; rowCount = 0 }
    }
}

try {
    foreach ($name in $inputEnvironmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    if (-not (Test-Path -LiteralPath $resolvedRepositoryPath -PathType Container)) { Write-HandoffResult (New-HandoffResult 'error' 'repository-invalid'); $scriptExitCode = 1; throw 'result-written' }
    $matrixPath = Join-Path $resolvedRepositoryPath '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $testProjectPath = Join-Path $resolvedRepositoryPath 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj'
    if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or -not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) { Write-HandoffResult (New-HandoffResult 'error' 'repository-invalid'); $scriptExitCode = 1; throw 'result-written' }
    $matrix = Read-StrictJsonFile $matrixPath 256KB 'matrix-invalid'
    if (-not (Test-Matrix $matrix)) { Write-HandoffResult (New-HandoffResult 'error' 'matrix-invalid'); $scriptExitCode = 1; throw 'result-written' }
    if (-not (Test-ChurchReportConfiguration $resolvedRepositoryPath)) { Write-HandoffResult (New-HandoffResult 'no-go' 'churchreport-config-invalid'); $scriptExitCode = 2; throw 'result-written' }
    if (-not (Test-Path -LiteralPath $ProfileInputPath -PathType Leaf)) { Write-HandoffResult (New-HandoffResult 'no-go' 'profile-input-required'); $scriptExitCode = 2; throw 'result-written' }
    if (-not (Test-ProfileInput (Read-StrictJsonFile $ProfileInputPath 128KB 'profile-input-invalid'))) { Write-HandoffResult (New-HandoffResult 'no-go' 'profile-input-invalid'); $scriptExitCode = 2; throw 'result-written' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (-not (Test-Path -LiteralPath $B1FixtureDescriptorPath -PathType Leaf) -or -not (Test-Path -LiteralPath $B2FixtureDescriptorPath -PathType Leaf)) { Write-HandoffResult (New-HandoffResult 'no-go' 'fixture-input-required'); $scriptExitCode = 2; throw 'result-written' }
    $b1Fixture = Read-StrictJsonFile $B1FixtureDescriptorPath 32KB 'fixture-input-invalid'; $b2Fixture = Read-StrictJsonFile $B2FixtureDescriptorPath 32KB 'fixture-input-invalid'
    if (-not (Test-FixtureDescriptor $b1Fixture 'contact-line-profile' 'p7.2-contact-line-profile' $identity $true) -or -not (Test-FixtureDescriptor $b2Fixture 'ungrouped-commitment' 'p7.2-ungrouped-commitment' $identity $false)) { Write-HandoffResult (New-HandoffResult 'no-go' 'fixture-input-invalid'); $scriptExitCode = 2; throw 'result-written' }
    if (-not (Test-CredentialTargetPresent)) { Write-HandoffResult (New-HandoffResult 'no-go' 'credential-unavailable'); $scriptExitCode = 2; throw 'result-written' }
    if (-not $ExecuteFixture) { Write-HandoffResult (New-HandoffResult 'go' '' $true $false @() @('matrix-approved', 'profile-crm91-present', 'credential-target-present', 'b1-fixture-owner-matches-operator', 'b2-fixture-owner-matches-operator')); $scriptExitCode = 0; throw 'result-written' }
    $credentialPassword = Get-CredentialPassword
    if ([string]::IsNullOrWhiteSpace($credentialPassword)) { Write-HandoffResult (New-HandoffResult 'no-go' 'credential-unavailable'); $scriptExitCode = 2; throw 'result-written' }
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { Write-HandoffResult (New-HandoffResult 'error' 'dotnet-unavailable'); $scriptExitCode = 1; throw 'result-written' }
    $operationResults = @()
    $executionPairs = @()
    if ($ResumeB2Only) {
        $executionPairs += @{ Kind = 'B2'; Fixture = $b2Fixture }
    }
    else {
        $executionPairs += @{ Kind = 'B1'; Fixture = $b1Fixture }
        $executionPairs += @{ Kind = 'B2'; Fixture = $b2Fixture }
    }
    foreach ($pair in $executionPairs) {
        try { $operationResults += Invoke-LiveOperation $pair.Kind $pair.Fixture $testProjectPath $dotnetCommand.Source }
        catch { $operationResults += [pscustomobject]@{ operationId = if ($pair.Kind -eq 'B1') { $expectedB1OperationId } else { $expectedB2OperationId }; outcome = 'no-go'; reason = 'evidence-result-unavailable'; operationExecuted = $true } }
        if ($operationResults[-1].outcome -cne 'go') { break }
    }
    $allGo = $operationResults.Count -eq $executionPairs.Count -and @($operationResults | Where-Object { $_.outcome -cne 'go' }).Count -eq 0
    $finalOutcome = if ($allGo) { 'go' } else { 'no-go' }
    $finalReason = if ($allGo) { '' } else { 'live-evidence-incomplete' }
    Write-HandoffResult (New-HandoffResult `
        -Outcome $finalOutcome `
        -Reason $finalReason `
        -PreflightOnly $false `
        -OperationExecuted $true `
        -Operations $operationResults)
    $scriptExitCode = if ($allGo) { 0 } else { 2 }
}
catch {
    if (-not $resultAlreadyWritten) { Write-HandoffResult (New-HandoffResult 'error' 'handoff-failed'); $scriptExitCode = 1 }
}
finally {
    foreach ($process in $processes) { try { if (-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F *> $null } } catch {} finally { $process.Dispose() } }
    foreach ($name in $inputEnvironmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    foreach ($directory in $temporaryDirectories) { try { if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Force -Recurse -ErrorAction Stop } } catch {} }
    $credentialPassword = $null
}

exit $scriptExitCode
