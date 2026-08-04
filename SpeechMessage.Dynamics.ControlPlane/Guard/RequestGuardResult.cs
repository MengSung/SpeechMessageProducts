namespace SpeechMessage.Dynamics.ControlPlane.Guard;

/// <summary>
/// RequestGuard 的純值結果。結果不回傳原始參數、endpoint、Credential 或 Profile 詳情，
/// 讓呼叫端可安全映射 HTTP 錯誤且不把可能敏感的 request 內容保存到 log/session/cache。
/// </summary>
public sealed record RequestGuardResult(bool Succeeded, string ErrorCode)
{
    /// <summary>建立允許結果。</summary>
    public static RequestGuardResult Allow() => new(true, string.Empty);

    /// <summary>建立 fail-closed 結果；errorCode 必須是固定安全分類碼。</summary>
    public static RequestGuardResult Reject(string errorCode) => new(false, errorCode);
}
