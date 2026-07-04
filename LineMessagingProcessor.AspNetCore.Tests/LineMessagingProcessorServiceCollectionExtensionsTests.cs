using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
{
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

    private sealed class FakeRichMenuCatalog : ILineRichMenuCatalog
    {
        public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LineRichMenuDefinition>>(Array.Empty<LineRichMenuDefinition>());
    }
}
