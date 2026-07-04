using Line.Messaging;

namespace LineMessagingProcessor.RichMenus.Tests.Support;

internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor
{
    private readonly Queue<string> _createdRichMenuIds = new();

    public List<string> Calls { get; } = new();

    public List<ResponseRichMenu> ExistingRichMenus { get; } = new();

    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> LinkedUsers { get; } = new(StringComparer.Ordinal);

    public string? DefaultRichMenuId { get; private set; }

    public int UploadedImageCount { get; private set; }

    public int CreateAliasCount { get; private set; }

    public int UpdateAliasCount { get; private set; }

    public void EnqueueCreatedRichMenuId(string richMenuId)
    {
        _createdRichMenuIds.Enqueue(richMenuId);
    }

    public Task<string> CreateRichMenuAsync(RichMenu richMenu)
    {
        var richMenuId = _createdRichMenuIds.Count == 0
            ? $"rich-menu-{ExistingRichMenus.Count + 1:000}"
            : _createdRichMenuIds.Dequeue();

        Calls.Add($"create:{richMenu.Name}");
        ExistingRichMenus.Add(richMenu.ToResponseRichMenu(richMenuId));
        return Task.FromResult(richMenuId);
    }

    public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream)
    {
        Calls.Add($"upload:{richMenuId}");
        UploadedImageCount++;
        return Task.CompletedTask;
    }

    public Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
    {
        Calls.Add("list");
        return Task.FromResult<IList<ResponseRichMenu>>(ExistingRichMenus);
    }

    public Task SetDefaultRichMenuAsync(string richMenuId)
    {
        Calls.Add($"default:{richMenuId}");
        DefaultRichMenuId = richMenuId;
        return Task.CompletedTask;
    }

    public Task<string> GetDefaultRichMenuIdAsync()
    {
        Calls.Add("get-default");
        return Task.FromResult(DefaultRichMenuId ?? string.Empty);
    }

    public Task CancelDefaultRichMenuAsync()
    {
        Calls.Add("cancel-default");
        DefaultRichMenuId = null;
        return Task.CompletedTask;
    }

    public Task<string> GetRichMenuIdOfUserAsync(string userId)
    {
        Calls.Add($"get-user:{userId}");
        return Task.FromResult(LinkedUsers.TryGetValue(userId, out var richMenuId) ? richMenuId : string.Empty);
    }

    public Task LinkRichMenuToUserAsync(string userId, string richMenuId)
    {
        Calls.Add($"link:{userId}:{richMenuId}");
        LinkedUsers[userId] = richMenuId;
        return Task.CompletedTask;
    }

    public Task UnlinkRichMenuFromUserAsync(string userId)
    {
        Calls.Add($"unlink:{userId}");
        LinkedUsers.Remove(userId);
        return Task.CompletedTask;
    }

    public Task DeleteRichMenuAsync(string richMenuId)
    {
        Calls.Add($"delete:{richMenuId}");
        ExistingRichMenus.RemoveAll(menu => menu.RichMenuId == richMenuId);
        return Task.CompletedTask;
    }

    public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
    {
        Calls.Add($"alias-create:{richMenuAliasId}:{richMenuId}");
        CreateAliasCount++;
        Aliases[richMenuAliasId] = richMenuId;
        return Task.CompletedTask;
    }

    public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
    {
        Calls.Add($"alias-update:{richMenuAliasId}:{richMenuId}");
        UpdateAliasCount++;
        Aliases[richMenuAliasId] = richMenuId;
        return Task.CompletedTask;
    }

    public Task DeleteRichMenuAliasAsync(string richMenuAliasId)
    {
        Calls.Add($"alias-delete:{richMenuAliasId}");
        Aliases.Remove(richMenuAliasId);
        return Task.CompletedTask;
    }

    public Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
    {
        Calls.Add($"alias-get:{richMenuAliasId}");
        if (!Aliases.TryGetValue(richMenuAliasId, out var richMenuId))
        {
            throw new LineRichMenuAliasNotFoundException(richMenuAliasId);
        }

        return Task.FromResult(new RichMenuAlias
        {
            RichMenuAliasId = richMenuAliasId,
            RichMenuId = richMenuId
        });
    }

    public Task<RichMenuAliasList> GetRichMenuAliasListAsync()
    {
        Calls.Add("alias-list");
        return Task.FromResult(new RichMenuAliasList
        {
            Aliases = Aliases
                .Select(item => new RichMenuAlias
                {
                    RichMenuAliasId = item.Key,
                    RichMenuId = item.Value
                })
                .ToList()
        });
    }
}
