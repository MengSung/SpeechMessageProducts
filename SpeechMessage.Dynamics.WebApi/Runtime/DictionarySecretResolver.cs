// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DictionarySecretResolver.cs
// 目的：測試用記憶體秘密解析器。
//
// 保母教學：
// - 只給 unit test 使用。
// - 正式 DI 不要註冊這個類別。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 以字典解析秘密參考（測試用）。
/// </summary>
public sealed class DictionarySecretResolver : ISecretResolver
{
    private readonly IReadOnlyDictionary<string, string> _secrets;

    public DictionarySecretResolver(IReadOnlyDictionary<string, string> secrets)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <inheritdoc />
    public bool TryResolve(string secretReference, out string? secretValue)
    {
        secretValue = null;
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return false;
        }

        if (_secrets.TryGetValue(secretReference, out var value) && !string.IsNullOrEmpty(value))
        {
            secretValue = value;
            return true;
        }

        return false;
    }
}
