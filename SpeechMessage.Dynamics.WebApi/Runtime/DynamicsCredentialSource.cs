// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsCredentialSource.cs
// 目的：Windows 驗證的憑證來源。
//
// 保母教學：
// - HostIdentity：用 IIS / Windows Service / gMSA 目前進程身分。
// - SecretReference：用秘密庫裡的服務帳號，不是 JSON 明文帳密。
// - 兩個來源互斥，不可混填。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// Windows 驗證的憑證來源。
/// </summary>
public enum DynamicsCredentialSource
{
    /// <summary>
    /// 使用主機目前服務身分。
    /// </summary>
    HostIdentity = 0,

    /// <summary>
    /// 使用秘密參考解析出的非人類服務帳號。
    /// </summary>
    SecretReference = 1
}
