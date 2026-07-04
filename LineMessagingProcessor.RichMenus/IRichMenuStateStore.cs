namespace LineMessagingProcessor.RichMenus;

public interface IRichMenuStateStore
{
    Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default);

    Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default);

    Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
