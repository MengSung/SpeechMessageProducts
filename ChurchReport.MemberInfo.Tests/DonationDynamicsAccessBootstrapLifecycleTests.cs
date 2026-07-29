using System.Collections.Concurrent;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchReport Dynamics bootstrap 的行程級世代生命週期。
/// 測試鎖定兩項 release blocker：相同設定只能重用一個 ServiceProvider/HTTP pool，設定變更必須先重啟與 Dispose；
/// 靜態狀態不得以無界 dictionary 按使用者、Session 或 profile 累積 provider，避免跨要求狀態與資源洩漏。
/// </summary>
public sealed class DonationDynamicsAccessBootstrapLifecycleTests
{
    /// <summary>
    /// 證明同一設定重用既有世代，而不同 Gateway endpoint 不會在行程內熱切換或與舊世代並存。
    /// </summary>
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

    /// <summary>
    /// 以反射防止回歸成無界靜態 provider cache；這類集合會同時保留 handler、socket、timer 與設定檔狀態。
    /// </summary>
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
