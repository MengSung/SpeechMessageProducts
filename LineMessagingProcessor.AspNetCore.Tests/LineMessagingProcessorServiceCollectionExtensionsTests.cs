using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>
/// 驗證 ASP.NET Core DI extension 對 LINE 與 RichMenu 共用工作流的註冊邊界。
/// 這些測試鎖住「基礎 RichMenu services 可自動註冊，但產品 catalog 必須由產品明確提供」的規則。
/// </summary>
public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
{
    /// <summary>
    /// 確認一般 LINE processor 註冊會同時提供 RichMenu workflow、assignment workflow 與文字 trigger policy。
    /// </summary>
    [Fact]
    public void AddLineMessagingProcessor_registers_client_processor_and_workflow()
    {
        var services = new ServiceCollection();

        services.AddLineMessagingProcessor(options =>
        {
            options.ChannelAccessToken = "test-token";
            options.ApiBaseUri = "https://api.line.me/v2";
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<LineMessagingClient>().Should().NotBeNull();
        provider.GetRequiredService<LineMessagingProcessor.LineMessagingProcessorClass>().Should().NotBeNull();
        provider.GetRequiredService<ILineNotificationWorkflow>().Should().BeOfType<LineNotificationWorkflow>();
        provider.GetRequiredService<ILineRichMenuWorkflow>().Should().BeOfType<LineRichMenuWorkflow>();
        provider.GetRequiredService<ILineRichMenuAssignmentWorkflow>().Should().BeOfType<LineRichMenuAssignmentWorkflow>();
        provider.GetRequiredService<ILineRichMenuTextTriggerResolver>().Should().BeOfType<LineRichMenuTextTriggerResolver>();
        provider.GetServices<IRichMenuPolicy>().Should().ContainSingle(policy => policy is LineRichMenuTextTriggerPolicy);
    }

    /// <summary>
    /// 確認產品在預設註冊後仍可覆蓋 RichMenu 文字 trigger 設定，避免 DI 註冊順序讓空設定永久生效。
    /// </summary>
    [Fact]
    public async Task AddLineRichMenus_updates_text_trigger_options_when_called_after_default_registration()
    {
        var services = new ServiceCollection();

        services.AddLineMessagingProcessor(options =>
        {
            options.ChannelAccessToken = "test-token";
            options.ApiBaseUri = "https://api.line.me/v2";
        });
        services.AddLineRichMenus(options =>
        {
            options.ExactTextToMenuKey["member center"] = "member-main";
        });
        // 測試只關心 orchestrator 能否使用覆蓋後的文字 trigger 設定，
        // 因此以 fake processor 與可預期 cache 取代真實 LINE API 呼叫。
        services.RemoveAll<ILineRichMenuProcessor>();
        services.RemoveAll<ILineRichMenuIdCache>();
        services.AddSingleton<ILineRichMenuProcessor, FakeRichMenuProcessor>();
        services.AddSingleton<ILineRichMenuIdCache>(_ =>
        {
            var cache = new InMemoryLineRichMenuIdCache();
            cache.Set("member-main", "rich-menu-member");
            return cache;
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var orchestrator = provider.GetRequiredService<IRichMenuOrchestrator>();
        var result = await orchestrator.ApplyAsync(new RichMenuContext("U123", receivedText: " member center "));

        result.Succeeded.Should().BeTrue();
        result.Changed.Should().BeTrue();
        result.AssignedMenuKey.Should().Be("member-main");
    }

    /// <summary>
    /// 確認預設註冊在 ASP.NET Core ValidateOnBuild/ValidateScopes 下能被完整解析。
    /// </summary>
    [Fact]
    public void AddLineMessagingProcessor_passes_aspnetcore_validate_on_build()
    {
        var services = new ServiceCollection();

        services.AddLineMessagingProcessor(options =>
        {
            options.ChannelAccessToken = "test-token";
            options.ApiBaseUri = "https://api.line.me/v2";
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        provider.GetRequiredService<ILineNotificationWorkflow>().Should().NotBeNull();
        provider.GetRequiredService<ILineRichMenuWorkflow>().Should().NotBeNull();
    }

    /// <summary>
    /// 確認 provisioning 註冊必須由產品提供 catalog，並會掛上對應的同步 workflow。
    /// </summary>
    [Fact]
    public void AddLineRichMenuProvisioning_registers_product_catalog_and_provisioning_workflow()
    {
        var services = new ServiceCollection();

        services.AddLineMessagingProcessor(options =>
        {
            options.ChannelAccessToken = "test-token";
            options.ApiBaseUri = "https://api.line.me/v2";
        });
        services.AddLineRichMenuProvisioning<FakeRichMenuCatalog>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        provider.GetRequiredService<ILineRichMenuCatalog>().Should().BeOfType<FakeRichMenuCatalog>();
        provider.GetRequiredService<ILineRichMenuProvisioningWorkflow>().Should().BeOfType<LineRichMenuProvisioningWorkflow>();
    }

    /// <summary>
    /// 供 DI 測試使用的 RichMenu processor 假物件。
    /// 它不呼叫 LINE，只提供可被 assignment/orchestration services 解析的最小行為。
    /// </summary>
    private sealed class FakeRichMenuProcessor : ILineRichMenuProcessor
    {
        public Task<string> CreateRichMenuAsync(RichMenu richMenu) => Task.FromResult("created-rich-menu");

        public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream) => Task.CompletedTask;

        public Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
            => Task.FromResult<IList<ResponseRichMenu>>(Array.Empty<ResponseRichMenu>());

        public Task SetDefaultRichMenuAsync(string richMenuId) => Task.CompletedTask;

        public Task<string> GetDefaultRichMenuIdAsync() => Task.FromResult(string.Empty);

        public Task CancelDefaultRichMenuAsync() => Task.CompletedTask;

        public Task<string> GetRichMenuIdOfUserAsync(string userId) => Task.FromResult(string.Empty);

        public Task LinkRichMenuToUserAsync(string userId, string richMenuId) => Task.CompletedTask;

        public Task UnlinkRichMenuFromUserAsync(string userId) => Task.CompletedTask;

        public Task DeleteRichMenuAsync(string richMenuId) => Task.CompletedTask;

        public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId) => Task.CompletedTask;

        public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId) => Task.CompletedTask;

        public Task DeleteRichMenuAliasAsync(string richMenuAliasId) => Task.CompletedTask;

        public Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
            => throw new LineRichMenuAliasNotFoundException(richMenuAliasId);

        public Task<RichMenuAliasList> GetRichMenuAliasListAsync()
            => Task.FromResult(new RichMenuAliasList
            {
                Aliases = new List<RichMenuAlias>()
            });
    }

    /// <summary>
    /// 供 provisioning DI 測試使用的產品 catalog 假物件。
    /// 空清單即可證明 DI 可以解析產品 catalog 型別，不需要真的佈建 LINE RichMenu。
    /// </summary>
    private sealed class FakeRichMenuCatalog : ILineRichMenuCatalog
    {
        public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LineRichMenuDefinition>>(Array.Empty<LineRichMenuDefinition>());
    }
}
