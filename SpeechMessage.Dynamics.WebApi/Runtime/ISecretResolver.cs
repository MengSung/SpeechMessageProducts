// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ISecretResolver.cs
// 目的：把「秘密參考名稱」解析成真正密文。
//
// 保母教學：
// - JSON / appsettings 只能放參考名稱，不能放密碼本體。
// - 解析出的密文不可寫進 log、metrics、exception.ToString() 常駐欄位。
// - 測試可用記憶體實作；正式環境可接 KeyVault / Windows Credential Manager。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 秘密參考解析器。
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// 解析秘密。找不到時回傳 false。
    /// </summary>
    bool TryResolve(string secretReference, out string? secretValue);
}
