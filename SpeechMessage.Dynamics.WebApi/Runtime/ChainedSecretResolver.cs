// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ChainedSecretResolver.cs
// 目的：把多個 ISecretResolver 串起來，第一個成功解析者勝出。
//
// 保姆級教學：
// 1. 常見順序：環境變數 -> 本機 local-dev 對照表（例如 CrmConnection bridge）。
// 2. 這不是把密碼寫進 DynamicsAccess JSON；仍然只接受「秘密名稱」。
// 3. 解析失敗就交給下一層，全部失敗回傳 false。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 依序嘗試多個 secret resolver。
/// </summary>
public sealed class ChainedSecretResolver : ISecretResolver
{
    private readonly IReadOnlyList<ISecretResolver> _resolvers;

    public ChainedSecretResolver(params ISecretResolver[] resolvers)
    {
        if (resolvers is null || resolvers.Length == 0)
        {
            throw new ArgumentException("At least one secret resolver is required.", nameof(resolvers));
        }

        _resolvers = resolvers;
    }

    public ChainedSecretResolver(IEnumerable<ISecretResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        _resolvers = resolvers.ToArray();
        if (_resolvers.Count == 0)
        {
            throw new ArgumentException("At least one secret resolver is required.", nameof(resolvers));
        }
    }

    /// <inheritdoc />
    public bool TryResolve(string secretReference, out string? secretValue)
    {
        secretValue = null;
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return false;
        }

        foreach (var resolver in _resolvers)
        {
            if (resolver.TryResolve(secretReference, out var value) && !string.IsNullOrEmpty(value))
            {
                secretValue = value;
                return true;
            }
        }

        return false;
    }
}
