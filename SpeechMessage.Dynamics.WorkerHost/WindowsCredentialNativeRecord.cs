using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 表示一次 <c>CredReadW</c> 成功結果的有限內部投影。
/// 指標仍由 <see cref="WindowsCredentialManagerProvider"/> 擁有，不能保存至欄位、快取、背景工作或另一個 Session。
/// </summary>
internal readonly struct WindowsCredentialNativeRecord
{
    /// <summary>
    /// 建立一份只供目前同步解析範圍使用的 native credential 投影。
    /// </summary>
    public WindowsCredentialNativeRecord(
        IntPtr credentialPointer,
        string userName,
        IntPtr credentialBlob,
        int credentialBlobSize)
    {
        CredentialPointer = credentialPointer;
        UserName = userName;
        CredentialBlob = credentialBlob;
        CredentialBlobSize = credentialBlobSize;
    }

    /// <summary>
    /// 取得必須由 <c>CredFree</c> 釋放的最外層配置。
    /// </summary>
    public IntPtr CredentialPointer { get; }

    /// <summary>
    /// 取得非密碼的 Windows 使用者名稱投影。
    /// </summary>
    public string UserName { get; }

    /// <summary>
    /// 取得必須在釋放前清零的 credential blob 指標。
    /// </summary>
    public IntPtr CredentialBlob { get; }

    /// <summary>
    /// 取得 credential blob 的有限位元組數。
    /// </summary>
    public int CredentialBlobSize { get; }
}
