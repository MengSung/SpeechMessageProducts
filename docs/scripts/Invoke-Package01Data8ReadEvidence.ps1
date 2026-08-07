<#
.SYNOPSIS
執行 P7.1 Package01 的 Data8 唯讀真機 evidence test，並只回傳去識別化 JSON。

.DESCRIPTION
此 handoff 僅適用於 Lenovo Legion 的 ChurchReport 開發 worktree，且只允許固定的
`sunnyvalechback` CE 9.1 Data8 Embedded composition。操作者必須傳入自己建立或確認的
test-owned contact、dedication booking、disciple lesson GUID 與有限日期／paid period；這些
值只在 child dotnet test 的 process environment 中存活，永不寫檔、顯示、記錄或回傳。

script 不提示、接收或輸出密碼；它只會在 repository 與 fixture 驗證通過後，從固定的 Windows
Generic Credential target 讀取 CE 9.1 密碼，並將該值暫時注入 child test process。它不啟動
Gateway 或 Official Worker，不修改 appsettings、feature flag 或任何 CRM 記錄。Credential
讀取失敗會在啟動 dotnet 或 CE operation 前 fail closed。dotnet test 最多執行 180 秒，所有
stdout、stderr、TRX 與暫存目錄均由本 script 的 finally 清理；既有 process environment 也會
在 finally 還原。最終輸出
只包含 operation ID、成功／失敗分類與列數，沒有 endpoint、Organization GUID、帳號、密碼、
token、cookie、CRM payload 或原始例外。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryPath,

    [Parameter(Mandatory = $true)]
    [string] $ContactId,

    [Parameter(Mandatory = $true)]
    [string] $DedicationBookingId,

    [Parameter(Mandatory = $true)]
    [string] $DiscipleLessonId,

    [Parameter(Mandatory = $true)]
    [string] $StartDate,

    [Parameter(Mandatory = $true)]
    [string] $EndDate,

    [Parameter(Mandatory = $true)]
    [string] $PaidPeriod,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptExitCode = 1
$resultAlreadyWritten = $false
$temporaryDirectory = $null
$process = $null
$credentialPassword = $null
$credentialTarget = 'speechmessage.crm91.p62'
$previousEnvironment = @{}
$inputEnvironmentNames = @(
    'CRM_PASSWORD',
    'SPEECHMESSAGE_P7_1_LIVE',
    'P7_1_CONTACT_ID',
    'P7_1_DEDICATION_BOOKING_ID',
    'P7_1_DISCIPLE_LESSON_ID',
    'P7_1_START_DATE',
    'P7_1_END_DATE',
    'P7_1_PAID_PERIOD'
)

function New-EvidenceResult {
    param(
        [string] $Outcome,
        [string] $Reason,
        [bool] $OperationExecuted,
        [object[]] $Operations = @()
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        reason = $Reason
        mode = 'Embedded'
        profileAlias = 'sunnyvalechback'
        ceVersion = '9.1'
        operationExecuted = $OperationExecuted
        featureFlagChanged = $false
    }
    if ($Operations.Count -gt 0) {
        $result.operations = $Operations
    }

    return $result
}

function Write-EvidenceResult {
    param([object] $Result)

    # handoff 預設也輸出 JSON，避免互動文字與可回貼 evidence 混雜。
    $script:resultAlreadyWritten = $true
    $Result | ConvertTo-Json -Compress -Depth 6
}

function Test-NonEmptyGuid {
    param([string] $Value)

    $parsed = [Guid]::Empty
    return [Guid]::TryParse($Value, [ref]$parsed) -and $parsed -ne [Guid]::Empty
}

function Test-UtcDate {
    param([string] $Value)

    $parsed = [DateTimeOffset]::MinValue
    return [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal,
        [ref]$parsed)
}

function Test-PaidPeriod {
    param([string] $Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Trim().Length -le 128 -and
        $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Get-P7CredentialPassword {
    <#
    .SYNOPSIS
    從固定的 Windows Generic Credential target 讀取 P7.1 CE 9.1 測試密碼。

    .DESCRIPTION
    僅回傳給本機子測試程序使用的短暫字串；不輸出、記錄或序列化任何憑證
    內容。Native credential buffer 由 C# finally 立即 CredFree，字元緩衝區
    也在 native helper 內清除；若 target 不存在、格式不符或 API 失敗，
    呼叫端收到 null 並採 fail-closed，不會啟動 dotnet test 或 CE operation。
    #>
    try {
        if ($null -eq ('SpeechMessage.P7.CredentialReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P7
{
    /// <summary>
    /// 只讀 Windows Generic Credential 的短暫秘密；不讀取或回傳 target、帳號或 native pointer。
    /// </summary>
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
        private static extern bool CredRead(
            string target,
            uint type,
            uint flags,
            out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        /// <summary>
        /// 讀取單一 Generic Credential blob，並在離開 native 邊界前清除暫存字元。
        /// </summary>
        public static string ReadGenericSecret(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0)
            {
                return null;
            }

            IntPtr credentialPointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out credentialPointer) ||
                    credentialPointer == IntPtr.Zero)
                {
                    return null;
                }

                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
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
                if (credentialPointer != IntPtr.Zero)
                {
                    CredFree(credentialPointer);
                }
            }
        }
    }
}
'@ -ErrorAction Stop
        }

        return [SpeechMessage.P7.CredentialReader]::ReadGenericSecret($credentialTarget)
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
                'P7_1_EVIDENCE_JSON=(\{[^\r\n]+\})')
        }
        if ($matches.Count -ne 1) {
            throw 'evidence-result-unavailable'
        }

        $evidence = $matches[0].Groups[1].Value | ConvertFrom-Json -ErrorAction Stop
        $expectedIds = @(
            'fee.dedication.retrieve.by.contact',
            'fee.dedication.retrieve.by.contact.date.range',
            'fees.retrieve.by.dedication.period',
            'fees.editor.load.by.disciplelesson',
            'lessons.stor.retrieve.by.contact',
            'lessons.stor.retrieve.by.disciplelesson'
        )
        $operations = @($evidence.operations)
        if ($evidence.schemaVersion -ne 1 -or
            $evidence.mode -cne 'Embedded' -or
            $evidence.profileAlias -cne 'sunnyvalechback' -or
            $evidence.ceVersion -cne '9.1' -or
            $operations.Count -ne $expectedIds.Count) {
            throw 'evidence-result-unavailable'
        }

        $sanitizedOperations = @()
        foreach ($id in $expectedIds) {
            $entry = @($operations | Where-Object { $_.OperationId -ceq $id })
            if ($entry.Count -ne 1 -or
                $entry[0].Status -cnotin @('succeeded', 'failed') -or
                ($null -ne $entry[0].RowCount -and
                 ($entry[0].RowCount -isnot [int] -or $entry[0].RowCount -lt 0 -or $entry[0].RowCount -gt 4096))) {
                throw 'evidence-result-unavailable'
            }

            $sanitizedOperations += [ordered]@{
                operationId = $id
                status = [string]$entry[0].Status
                rowCount = $entry[0].RowCount
            }
        }

        $allSucceeded = @($sanitizedOperations | Where-Object { $_.status -ne 'succeeded' }).Count -eq 0
        return [pscustomobject]@{
            outcome = if ($allSucceeded -and $evidence.outcome -ceq 'go') { 'go' } else { 'no-go' }
            reason = if ($allSucceeded -and $evidence.outcome -ceq 'go') { '' } else { 'one-or-more-operations-failed' }
            operations = $sanitizedOperations
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }
}

try {
    # 不論後續是 repository、fixture、credential 或 child-process 哪一個步驟失敗，都必須保留呼叫端
    # 原有值。快照放在所有 early-exit 之前，finally 才能只還原而不誤清除外層 PowerShell 的狀態。
    foreach ($name in $inputEnvironmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    $resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    $testProjectPath = Join-Path $resolvedRepositoryPath 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj'
    if (-not (Test-Path -LiteralPath $resolvedRepositoryPath -PathType Container) -or
        -not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
        Write-EvidenceResult (New-EvidenceResult -Outcome 'error' -Reason 'repository-invalid' -OperationExecuted $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    if (-not (Test-NonEmptyGuid $ContactId) -or
        -not (Test-NonEmptyGuid $DedicationBookingId) -or
        -not (Test-NonEmptyGuid $DiscipleLessonId) -or
        -not (Test-UtcDate $StartDate) -or
        -not (Test-UtcDate $EndDate) -or
        -not (Test-PaidPeriod $PaidPeriod)) {
        Write-EvidenceResult (New-EvidenceResult -Outcome 'error' -Reason 'fixture-input-invalid' -OperationExecuted $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    $start = [DateTimeOffset]::Parse($StartDate, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    $end = [DateTimeOffset]::Parse($EndDate, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    if ($start -gt $end) {
        Write-EvidenceResult (New-EvidenceResult -Outcome 'error' -Reason 'fixture-input-invalid' -OperationExecuted $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    $credentialPassword = Get-P7CredentialPassword
    if ([string]::IsNullOrWhiteSpace($credentialPassword)) {
        Write-EvidenceResult (New-EvidenceResult -Outcome 'no-go' -Reason 'credential-unavailable' -OperationExecuted $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        Write-EvidenceResult (New-EvidenceResult -Outcome 'error' -Reason 'dotnet-unavailable' -OperationExecuted $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    [Environment]::SetEnvironmentVariable('CRM_PASSWORD', $credentialPassword, 'Process')
    [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_1_LIVE', '1', 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_CONTACT_ID', $ContactId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_DEDICATION_BOOKING_ID', $DedicationBookingId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_DISCIPLE_LESSON_ID', $DiscipleLessonId, 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_START_DATE', $start.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture), 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_END_DATE', $end.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture), 'Process')
    [Environment]::SetEnvironmentVariable('P7_1_PAID_PERIOD', $PaidPeriod.Trim(), 'Process')

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-1-live-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    $trxPath = Join-Path $temporaryDirectory 'P7Data8ReadEvidence.trx'
    $stdoutPath = Join-Path $temporaryDirectory 'stdout.log'
    $stderrPath = Join-Path $temporaryDirectory 'stderr.log'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetCommand.Source
    $startInfo.Arguments = 'test "' + $testProjectPath + '" --no-restore --filter "FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage01Data8ReadEvidenceTests.Live_package01_data8_reads_emit_sanitized_evidence" --logger "trx;LogFileName=P7Data8ReadEvidence.trx" --results-directory "' + $temporaryDirectory + '" --verbosity quiet'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'dotnet-start-failed'
    }

    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
        try {
            $process.Kill()
        }
        catch {
            # process 已退出時沒有額外處理；仍回傳固定 timeout 分類。
        }

        Write-EvidenceResult (New-EvidenceResult -Outcome 'no-go' -Reason 'test-timeout' -OperationExecuted $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    [void]$standardOutputTask.GetAwaiter().GetResult()
    [void]$standardErrorTask.GetAwaiter().GetResult()
    $strictEvidence = Get-StrictEvidenceFromTrx -TrxPath $trxPath
    $operationExecuted = @($strictEvidence.operations).Count -eq 6
    Write-EvidenceResult (New-EvidenceResult `
        -Outcome $strictEvidence.outcome `
        -Reason $strictEvidence.reason `
        -OperationExecuted $operationExecuted `
        -Operations $strictEvidence.operations)
    $scriptExitCode = if ($strictEvidence.outcome -ceq 'go') { 0 } else { 2 }
}
catch {
    if (-not $resultAlreadyWritten) {
        $reason = if ([string]$_.Exception.Message -in @(
                'evidence-result-unavailable',
                'dotnet-start-failed')) {
            [string]$_.Exception.Message
        }
        else {
            'handoff-failed'
        }
        Write-EvidenceResult (New-EvidenceResult -Outcome 'error' -Reason $reason -OperationExecuted $false)
        $scriptExitCode = 1
    }
}
finally {
    foreach ($name in $inputEnvironmentNames) {
        if ($previousEnvironment.ContainsKey($name)) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
    }

    if ($null -ne $process) {
        $process.Dispose()
    }

    if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
        # 暫存目錄可能暫時遭防毒或索引器鎖住；清理失敗不可阻斷隨後的 credential、fixture 與
        # process environment 清理。保留不含路徑的結果 JSON，避免洩漏主機目錄或掩蓋主流程結果。
        try {
            Remove-Item -LiteralPath $temporaryDirectory -Force -Recurse -ErrorAction Stop
        }
        catch {
            # 此處刻意採最佳努力刪除且不可在 finally 重新拋出，確保 process-owned secret cleanup 仍會執行。
        }
    }

    $ContactId = $null
    $DedicationBookingId = $null
    $DiscipleLessonId = $null
    $StartDate = $null
    $EndDate = $null
    $PaidPeriod = $null
    $credentialPassword = $null
}

exit $scriptExitCode
