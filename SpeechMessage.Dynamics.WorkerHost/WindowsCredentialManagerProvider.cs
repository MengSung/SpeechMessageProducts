using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 在官方 net48 Worker 行程內解析 Windows Credential Manager Generic Credential。
/// Provider 不建立 plaintext password <see cref="string"/>；它逐字元把 UTF-16 native blob
/// 複製到唯讀 <see cref="SecureString"/>，並在返回或失敗前先清零 blob、再釋放 native credential。
/// </summary>
public sealed class WindowsCredentialManagerProvider
{
    private const string UnavailableMessage = "The Windows credential reference is unavailable.";
    private const int MaximumCredentialBlobBytes = 2560;
    private readonly IWindowsCredentialNativeApi _nativeApi;

    /// <summary>
    /// 建立使用 Windows <c>CredReadW</c>／<c>CredFree</c> 的正式 provider。
    /// Provider 本身無快取、無 timer、無背景工作，也不保存 credential 或 native handle。
    /// </summary>
    public WindowsCredentialManagerProvider()
        : this(new WindowsCredentialNativeApi())
    {
    }

    /// <summary>
    /// 建立可驗證 native 資源生命週期的 provider；只供同組件與測試使用。
    /// </summary>
    internal WindowsCredentialManagerProvider(IWindowsCredentialNativeApi nativeApi)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
    }

    /// <summary>
    /// 解析指定 reference 並返回唯一 managed secret owner。
    /// 成功或失敗都在方法返回前清除 native blob 與 handle；任何錯誤只回傳固定訊息，
    /// 不洩漏 target、使用者名稱、密碼、Win32 error 或 native 結構內容。
    /// </summary>
    /// <param name="credentialReference">Worker profile 中經 allowlist 驗證的 Credential Manager target。</param>
    /// <returns>呼叫端必須在官方 CRM client 建立後立即 Dispose 的 credential。</returns>
    public OfficialCrmCredential Read(string credentialReference)
    {
        if (!IsSafeCredentialReference(credentialReference))
        {
            throw new InvalidOperationException(UnavailableMessage);
        }

        WindowsCredentialNativeRecord nativeCredential;
        try
        {
            if (!_nativeApi.TryReadGeneric(credentialReference, out nativeCredential))
            {
                throw new InvalidOperationException(UnavailableMessage);
            }
        }
        catch (Exception exception) when (IsRecoverableCredentialFailure(exception))
        {
            throw new InvalidOperationException(UnavailableMessage);
        }

        OfficialCrmCredential? result = null;
        Exception? failure = null;
        try
        {
            result = CreateCredential(nativeCredential);
        }
        catch (Exception exception) when (IsRecoverableCredentialFailure(exception))
        {
            failure = exception;
        }

        // 清理順序不可交換：blob 是 credential 配置內的指標，必須在 CredFree 使其失效前完成歸零。
        try
        {
            if (nativeCredential.CredentialBlob != IntPtr.Zero &&
                nativeCredential.CredentialBlobSize > 0)
            {
                _nativeApi.ZeroMemory(
                    nativeCredential.CredentialBlob,
                    nativeCredential.CredentialBlobSize);
            }
        }
        catch (Exception exception) when (IsRecoverableCredentialFailure(exception))
        {
            failure ??= exception;
        }

        try
        {
            if (nativeCredential.CredentialPointer != IntPtr.Zero)
            {
                _nativeApi.FreeCredential(nativeCredential.CredentialPointer);
            }
        }
        catch (Exception exception) when (IsRecoverableCredentialFailure(exception))
        {
            failure ??= exception;
        }

        if (failure is not null || result is null)
        {
            result?.Dispose();
            throw new InvalidOperationException(UnavailableMessage);
        }

        return result;
    }

    private static OfficialCrmCredential CreateCredential(
        WindowsCredentialNativeRecord nativeCredential)
    {
        if (nativeCredential.CredentialPointer == IntPtr.Zero ||
            nativeCredential.CredentialBlob == IntPtr.Zero ||
            nativeCredential.CredentialBlobSize <= 0 ||
            nativeCredential.CredentialBlobSize > MaximumCredentialBlobBytes ||
            (nativeCredential.CredentialBlobSize & 1) != 0 ||
            string.IsNullOrWhiteSpace(nativeCredential.UserName) ||
            nativeCredential.UserName.Length > 256)
        {
            throw new InvalidDataException(UnavailableMessage);
        }

        SplitWindowsUserName(
            nativeCredential.UserName,
            out var userName,
            out var domain);

        var password = new SecureString();
        try
        {
            for (var offset = 0;
                 offset < nativeCredential.CredentialBlobSize;
                 offset += sizeof(char))
            {
                var character = (char)(ushort)Marshal.ReadInt16(
                    nativeCredential.CredentialBlob,
                    offset);
                if (character == '\0')
                {
                    throw new InvalidDataException(UnavailableMessage);
                }

                password.AppendChar(character);
            }

            if (password.Length == 0)
            {
                throw new InvalidDataException(UnavailableMessage);
            }

            password.MakeReadOnly();
            return new OfficialCrmCredential(userName, domain, password);
        }
        catch
        {
            password.Dispose();
            throw;
        }
    }

    private static void SplitWindowsUserName(
        string value,
        out string userName,
        out string domain)
    {
        if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
        {
            throw new InvalidDataException(UnavailableMessage);
        }

        var separatorIndex = value.IndexOf('\\');
        if (separatorIndex < 0)
        {
            userName = value;
            domain = string.Empty;
            return;
        }

        if (separatorIndex == 0 ||
            separatorIndex == value.Length - 1 ||
            separatorIndex != value.LastIndexOf('\\'))
        {
            throw new InvalidDataException(UnavailableMessage);
        }

        domain = value.Substring(0, separatorIndex);
        userName = value.Substring(separatorIndex + 1);
    }

    private static bool IsSafeCredentialReference(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > 256 ||
            !IsAsciiAlphaNumeric(value[0]) ||
            !IsAsciiAlphaNumeric(value[value.Length - 1]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsAsciiAlphaNumeric(character) &&
                character != '-' &&
                character != '_' &&
                character != '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiAlphaNumeric(char character)
    {
        return (character >= 'a' && character <= 'z') ||
               (character >= 'A' && character <= 'Z') ||
               (character >= '0' && character <= '9');
    }

    private static bool IsRecoverableCredentialFailure(Exception exception)
    {
        return exception is InvalidOperationException ||
               exception is InvalidDataException ||
               exception is ExternalException ||
               exception is ArgumentException ||
               exception is NotSupportedException ||
               exception is ObjectDisposedException;
    }
}
