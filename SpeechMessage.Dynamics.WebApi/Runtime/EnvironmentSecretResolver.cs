// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/EnvironmentSecretResolver.cs
// 目的：預設秘密解析器（環境變數）。
//
// 保母教學：
// - 參考名稱 "DYNAMICS_CE91_PASSWORD" 會讀同名環境變數。
// - 這是本機 / 容器部署的最小可用實作。
// - 正式環境可替換成 KeyVault resolver，介面不用改。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 以環境變數解析秘密參考。
/// </summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    /// <inheritdoc />
    public bool TryResolve(string secretReference, out string? secretValue)
    {
        secretValue = null;
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable(secretReference.Trim());
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        secretValue = value;
        return true;
    }
}
