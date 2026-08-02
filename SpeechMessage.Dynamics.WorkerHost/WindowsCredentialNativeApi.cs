using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 封裝 Windows Credential Manager P/Invoke。每次成功讀取只把 pointer 交給同步 provider 範圍，
/// 不建立 static cache、SafeHandle registry、背景清理器或跨 Worker generation 的可變狀態。
/// </summary>
internal sealed class WindowsCredentialNativeApi : IWindowsCredentialNativeApi
{
    private const uint CredentialTypeGeneric = 1;

    /// <inheritdoc />
    public bool TryReadGeneric(
        string targetName,
        out WindowsCredentialNativeRecord credential)
    {
        if (!CredRead(
                targetName,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            credential = default;
            return false;
        }

        IntPtr blobPointer = IntPtr.Zero;
        var blobSize = 0;
        try
        {
            var nativeCredential = Marshal.PtrToStructure<NativeCredential>(
                credentialPointer);
            if (nativeCredential.CredentialBlobSize > int.MaxValue)
            {
                throw new Win32Exception();
            }

            blobPointer = nativeCredential.CredentialBlob;
            blobSize = (int)nativeCredential.CredentialBlobSize;
            var userName = Marshal.PtrToStringUni(nativeCredential.UserName) ??
                           string.Empty;
            credential = new WindowsCredentialNativeRecord(
                credentialPointer,
                userName,
                blobPointer,
                blobSize);
            return true;
        }
        catch
        {
            // 若 native 結構投影失敗，這一層仍保有 CredRead 配置所有權；
            // 先嘗試歸零已取得的 blob，再釋放外層配置，避免 provider 尚未取得 record 就發生洩漏。
            if (blobPointer != IntPtr.Zero && blobSize > 0)
            {
                ZeroMemory(blobPointer, blobSize);
            }

            CredFree(credentialPointer);
            throw;
        }
    }

    /// <inheritdoc />
    public void ZeroMemory(IntPtr address, int byteCount)
    {
        if (address == IntPtr.Zero || byteCount <= 0)
        {
            return;
        }

        // Credential blob 最大僅數 KiB；逐 byte 覆寫不建立額外 managed secret copy，
        // 且在 net48／netstandard2.0 均有一致語意。此成本只發生在 Worker client 建立時，不在 operation hot path。
        for (var index = 0; index < byteCount; index++)
        {
            Marshal.WriteByte(address, index, 0);
        }
    }

    /// <inheritdoc />
    public void FreeCredential(IntPtr credentialPointer)
    {
        if (credentialPointer != IntPtr.Zero)
        {
            CredFree(credentialPointer);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        int reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
