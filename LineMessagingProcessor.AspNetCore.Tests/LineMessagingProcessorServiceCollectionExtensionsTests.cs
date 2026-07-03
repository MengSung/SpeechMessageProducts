using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
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
    }
}
