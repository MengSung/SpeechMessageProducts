using System.Collections.Concurrent;

namespace LineMessagingProcessor.RichMenus;

public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
{
    private readonly ConcurrentDictionary<string, RichMenuUserState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        _states.TryGetValue(Normalize(lineUserId), out var state);
        return Task.FromResult(state);
    }

    public Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        _states[NormalizeRequired(state.LineUserId, nameof(state.LineUserId))] = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        var key = Normalize(lineUserId);
        if (key.Length > 0)
        {
            _states.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RichMenuUserState> expired = _states.Values
            .Where(state => state.ExpiresAt.HasValue && state.ExpiresAt.Value <= now)
            .ToList();
        return Task.FromResult(expired);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }
}
