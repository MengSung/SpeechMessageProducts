<#
.SYNOPSIS
以去識別化方式確認 P6 IFD 登入名稱與 Credential Manager target 的帳號是否一致。

.DESCRIPTION
此工具只在 Lenovo Legion 預計執行 Gateway／Worker 的同一 Windows 使用者下使用。它先驗證
既有的本機 profile-input JSON，再讓操作者分別輸入兩個 IFD 登入頁已確認可用的使用者名稱，
並以 Windows CredRead 比較對應 Generic Credential 的 UserName 欄位。工具不讀取 credential blob、
不要求或輸入密碼、不寫入 profile、不發送網路要求、不啟動 Gateway 或 Worker，也不執行 CE operation。

輸出只含 crm82／crm91 的固定狀態，不含使用者名稱、Credential Manager target、端點、組織、
密碼、token、cookie 或例外文字。每次 native credential handle 只由內嵌的比對方法擁有，並在
finally 立即 CredFree；操作者輸入及從 native record 暫時取得的使用者名稱都不會被輸出、記錄、
快取或寫檔，PowerShell 程序結束即是其唯一且有界的保留期限。

若從另一個 PowerShell 啟動本工具，請以 -Crm82ExpectedUserName 與
-Crm91ExpectedUserName 傳入兩個非機密使用者名稱；這會避免巢狀主控台無法顯示 Read-Host
提示。若未傳入其中一個參數，工具才會針對該 profile 顯示互動提示。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ProfileInputPath,

    [string] $Crm82ExpectedUserName,

    [string] $Crm91ExpectedUserName,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$references = $null
$expectedUserNames = $null
$result = $null
$scriptExitCode = 1

function Test-SafeIdentifier {
    param(
        [string] $Value,
        [int] $MaximumLength
    )

    if ([string]::IsNullOrEmpty($Value) -or $Value.Length -gt $MaximumLength -or
        -not (($Value[0] -ge 'A' -and $Value[0] -le 'Z') -or
              ($Value[0] -ge 'a' -and $Value[0] -le 'z') -or
              ($Value[0] -ge '0' -and $Value[0] -le '9')) -or
        -not (($Value[$Value.Length - 1] -ge 'A' -and $Value[$Value.Length - 1] -le 'Z') -or
              ($Value[$Value.Length - 1] -ge 'a' -and $Value[$Value.Length - 1] -le 'z') -or
              ($Value[$Value.Length - 1] -ge '0' -and $Value[$Value.Length - 1] -le '9'))) {
        return $false
    }

    foreach ($character in $Value.ToCharArray()) {
        if (-not (($character -ge 'A' -and $character -le 'Z') -or
                  ($character -ge 'a' -and $character -le 'z') -or
                  ($character -ge '0' -and $character -le '9') -or
                  $character -eq '.' -or $character -eq '-' -or $character -eq '_')) {
            return $false
        }
    }

    return $true
}

function Test-SafeOperatorUserName {
    param([string] $Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 256 -and
        $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Get-ProfileCredentialReferences {
    param([string] $Path)

    $bytes = $null
    $text = $null
    try {
        $resolvedPath = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
            throw 'profile-input-invalid'
        }

        $fileInfo = Get-Item -LiteralPath $resolvedPath -ErrorAction Stop
        if ($fileInfo.Length -le 0 -or $fileInfo.Length -gt 131072) {
            throw 'profile-input-invalid'
        }

        $bytes = [IO.File]::ReadAllBytes($resolvedPath)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            throw 'profile-input-invalid'
        }

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $document = $text | ConvertFrom-Json -ErrorAction Stop
        $rootPropertyNames = if ($null -eq $document) {
            @()
        }
        else {
            @($document.PSObject.Properties.Name)
        }
        if ($null -eq $document -or
            $rootPropertyNames.Count -ne 2 -or
            @($rootPropertyNames | Where-Object {
                $_ -cnotin @('schemaVersion', 'profiles')
            }).Count -ne 0 -or
            $document.schemaVersion -ne 1) {
            throw 'profile-input-invalid'
        }

        $profiles = @($document.profiles)
        if ($profiles.Count -ne 2) {
            throw 'profile-input-invalid'
        }

        $resolvedReferences = [ordered]@{}
        foreach ($alias in @('crm82', 'crm91')) {
            $profile = @($profiles | Where-Object {
                $_.profileAlias -ceq $alias
            })
            if ($profile.Count -ne 1 -or
                @($profile[0].PSObject.Properties.Name) -notcontains 'identity' -or
                $null -eq $profile[0].identity -or
                $profile[0].identity.mode -cne 'WindowsCredentialReference' -or
                -not (Test-SafeIdentifier -Value ([string]$profile[0].identity.reference) -MaximumLength 256)) {
                throw 'profile-input-invalid'
            }

            $resolvedReferences[$alias] = [string]$profile[0].identity.reference
        }

        return $resolvedReferences
    }
    catch {
        throw 'profile-input-invalid'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }

        $text = $null
        $document = $null
        $rootPropertyNames = $null
        $profiles = $null
    }
}

function Initialize-CredentialIdentityNative {
    if ($null -ne ('SpeechMessage.P6.CredentialIdentityNative' -as [type])) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P6
{
    /// <summary>
    /// 將 Windows Credential Manager 使用者名稱比對限制在單一同步 native 呼叫。
    /// 此型別不讀取 blob、絕不回傳帳號或祕密字串，且每一個成功 CredRead 取得的 handle
    /// 都由 finally 中的 CredFree 釋放；呼叫端只會收到固定整數狀態，因此 native 資料
    /// 不會越過 PowerShell 診斷邊界、進入 IPC、紀錄或下一個 profile。
    /// </summary>
    public static class CredentialIdentityNative
    {
        private const uint GenericCredentialType = 1;

        /// <summary>
        /// 讀取一個 Generic Credential 的 metadata snapshot。native buffer 的唯一 owner 是本型別，
        /// 成功後無論比對、marshal 或例外結果皆由 MatchesCredentialUserName 的 finally 釋放。
        /// </summary>
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(
            string target,
            uint type,
            uint reservedFlag,
            out IntPtr credentialPointer);

        /// <summary>
        /// 釋放 CredRead 配置的 native buffer。呼叫時不得保存其中任何 pointer 或欄位；
        /// 這是此工具唯一 native handle 的決定性清理路徑。
        /// </summary>
        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        /// <summary>
        /// 比對目前 Windows 使用者可見 target 的帳號 metadata 與操作者本次輸入的 IFD 登入名稱。
        /// 回傳 1 表示相符、2 表示 metadata 可讀但不相符、0 表示 target 不可讀或 native 資料無效。
        /// 方法不讀取 blob，不回傳 target、帳號或例外；所有 native record 只存活到 finally 釋放 handle。
        /// </summary>
        public static int MatchesCredentialUserName(string target, string expectedUserName)
        {
            IntPtr credentialPointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out credentialPointer) ||
                    credentialPointer == IntPtr.Zero)
                {
                    return 0;
                }

                NativeCredential record = (NativeCredential)Marshal.PtrToStructure(
                    credentialPointer,
                    typeof(NativeCredential));
                if (record.Type != GenericCredentialType ||
                    string.IsNullOrEmpty(record.UserName))
                {
                    return 0;
                }

                return string.Equals(
                    record.UserName,
                    expectedUserName,
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 2;
            }
            catch
            {
                // Native metadata 失敗一律收斂為不可讀；不讓 Win32/Marshal 細節穿越去識別化邊界。
                return 0;
            }
            finally
            {
                if (credentialPointer != IntPtr.Zero)
                {
                    CredFree(credentialPointer);
                }
            }
        }

        /// <summary>
        /// 只保留 CredRead ABI 所需欄位排列。工具不讀取 BlobLength 或 BlobPointer，
        /// 因此 credential secret 從不進入 managed 記憶體、比較結果或輸出。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobLength;
            public IntPtr BlobPointer;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string UserName;
        }
    }
}
'@ -ErrorAction Stop
}

function Emit-Result {
    param(
        [hashtable] $Evidence,
        [int] $ExitCode
    )

    if ($Json) {
        $Evidence | ConvertTo-Json -Depth 5 -Compress
    }
    else {
        [pscustomobject]$Evidence
    }

    exit $ExitCode
}

try {
    $references = Get-ProfileCredentialReferences -Path $ProfileInputPath
    $expectedUserNames = [ordered]@{}
    $expectedUserNames['crm82'] = if ([string]::IsNullOrWhiteSpace($Crm82ExpectedUserName)) {
        Read-Host 'CE 8.2 IFD login username (compared locally only; not printed or saved)'
    }
    else {
        $Crm82ExpectedUserName
    }
    $expectedUserNames['crm91'] = if ([string]::IsNullOrWhiteSpace($Crm91ExpectedUserName)) {
        Read-Host 'CE 9.1 IFD login username (compared locally only; not printed or saved)'
    }
    else {
        $Crm91ExpectedUserName
    }
    foreach ($alias in @('crm82', 'crm91')) {
        if (-not (Test-SafeOperatorUserName -Value $expectedUserNames[$alias])) {
            throw 'operator-input-invalid'
        }
    }

    Initialize-CredentialIdentityNative
    $profiles = [Collections.Generic.List[object]]::new(2)
    $allMatched = $true
    foreach ($alias in @('crm82', 'crm91')) {
        $comparison = [SpeechMessage.P6.CredentialIdentityNative]::MatchesCredentialUserName(
            $references[$alias],
            $expectedUserNames[$alias])
        $state = switch ($comparison) {
            1 { 'matches-operator-provided-ifd-login'; break }
            2 { 'does-not-match-operator-provided-ifd-login'; break }
            default { 'credential-identity-unreadable'; break }
        }
        if ($comparison -ne 1) {
            $allMatched = $false
        }

        $profiles.Add([ordered]@{
            profileAlias = $alias
            credentialUserNameState = $state
        })
    }

    $result = [ordered]@{
        schemaVersion = 1
        outcome = if ($allMatched) { 'go' } else { 'no-go' }
        profiles = @($profiles)
        operationExecuted = $false
        featureFlagChanged = $false
    }
    $scriptExitCode = if ($allMatched) { 0 } else { 2 }
}
catch {
    $reason = if ($_.Exception.Message -eq 'operator-input-invalid') {
        'operator-input-invalid'
    }
    elseif ($_.Exception.Message -eq 'profile-input-invalid') {
        'profile-input-invalid'
    }
    else {
        'credential-identity-validation-failed'
    }
    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'error'
        reason = $reason
        operationExecuted = $false
        featureFlagChanged = $false
    }
    $scriptExitCode = 1
}
finally {
    if ($null -ne $references) {
        $references.Clear()
    }
    if ($null -ne $expectedUserNames) {
        $expectedUserNames.Clear()
    }

    $profiles = $null
    $references = $null
    $expectedUserNames = $null
}

Emit-Result -Evidence $result -ExitCode $scriptExitCode
