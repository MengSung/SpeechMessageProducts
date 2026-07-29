using System.Collections.Concurrent;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class DonationDynamicsAccessBootstrapLifecycleTests
{
    [Fact]
    public async Task Process_bootstrap_reuses_one_generation_and_requires_restart_for_configuration_changes()
    {
        await DonationDynamicsAccessBootstrap.DisposeAsync();

        try
        {
            var first = CreateGatewayConfiguration("https://gateway-a.internal/");
            var replacement = CreateGatewayConfiguration("https://gateway-b.internal/");

            DonationDynamicsAccessBootstrap.TryCreatePackage01Client(first).Should().NotBeNull();
            DonationDynamicsAccessBootstrap.TryCreatePackage01Client(first).Should().NotBeNull();

            var action = () => DonationDynamicsAccessBootstrap.TryCreatePackage01Client(replacement);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*restart*");

            await DonationDynamicsAccessBootstrap.DisposeAsync();

            DonationDynamicsAccessBootstrap.TryCreatePackage01Client(replacement).Should().NotBeNull();
        }
        finally
        {
            await DonationDynamicsAccessBootstrap.DisposeAsync();
        }
    }

    [Fact]
    public void Bootstrap_does_not_retain_an_unbounded_static_provider_dictionary()
    {
        var staticFields = typeof(DonationDynamicsAccessBootstrap).GetFields(
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        staticFields.Should().NotContain(field =>
            field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>));
    }

    private static IConfiguration CreateGatewayConfiguration(string endpoint)
    {
        var values = new Dictionary<string, string?>
        {
            ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
            ["DynamicsAccess:ExecutionMode"] = "Gateway",
            ["DynamicsAccess:ProfileAlias"] = "jesus-prod",
            ["DynamicsAccess:Gateway:Endpoint"] = endpoint,
            ["DynamicsAccess:Gateway:ApiPrefix"] = "/v1"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
