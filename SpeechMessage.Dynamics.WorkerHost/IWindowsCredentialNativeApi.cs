using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 隔離 Windows Credential Manager native handle 的最小內部邊界。
/// Provider 是 credential pointer 與 blob 的唯一清理 owner；測試替身使用同一契約驗證歸零與釋放順序。
/// </summary>
internal interface IWindowsCredentialNativeApi
{
    /// <summary>
    /// 取得 Generic Credential 的原生快照；成功後呼叫端一定要先清零 blob，再釋放 credential pointer。
    /// </summary>
    bool TryReadGeneric(
        string targetName,
        out WindowsCredentialNativeRecord credential);

    /// <summary>
    /// 在釋放 native credential 前覆寫其 credential blob，避免密碼留在 unmanaged 記憶體。
    /// </summary>
    void ZeroMemory(IntPtr address, int byteCount);

    /// <summary>
    /// 釋放 <c>CredReadW</c> 回傳的完整 credential 配置。
    /// </summary>
    void FreeCredential(IntPtr credentialPointer);
}
