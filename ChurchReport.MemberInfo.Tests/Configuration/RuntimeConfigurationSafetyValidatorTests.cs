using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Configuration;

public sealed class RuntimeConfigurationSafetyValidatorTests
{
    private static readonly string[] SensitiveKeyManifest =
    [
        "LineMessaging:Jesus:ChannelAccessToken",
        "LineMessaging:JesusBack:ChannelAccessToken",
        "LineLogin:ChannelSecret",
        "MiniApp:ChannelSecret",
        "CrmConnection:Username",
        "CrmConnection:Password",
        "LinePay:ChannelSecret",
        "Payment:Profiles:JesusTest:Credentials:ShopNo",
        "Payment:Profiles:JesusTest:Credentials:A1",
        "Payment:Profiles:JesusTest:Credentials:A2",
        "Payment:Profiles:JesusTest:Credentials:B1",
        "Payment:Profiles:JesusTest:Credentials:B2",
        "Payment:Profiles:JesusTest:Credentials:XKeyId",
        "Payment:Profiles:MyPayProduction:Credentials:Key",
        "Payment:Profiles:MyPayProduction:Credentials:IV",
        "Sinopac:A1",
        "Sinopac:A2",
        "Sinopac:B1",
        "Sinopac:B2",
        "Sinopac:XKeyID",
        "MyPay:Key"
    ];

    private static readonly string[] ProductionControlKeys =
    [
        "Security:EnforceGlobalAuthorization",
        "Security:AllowSessionIdentityFallback",
        "LinePay:IsSandbox",
        "Cash_Environment",
        "PAY_PROVIDER",
        "Payment:DefaultProfile",
        "Payment:Profiles:MyPayProduction:Environment",
        "TSPG:TestMode"
    ];

    [Fact]
    public void Safe_production_configuration_passes()
    {
        var act = () => RuntimeConfigurationSafetyValidator.Validate(
            BuildConfiguration(SafeProductionValues()),
            "Production");

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(UnsafeControlCases))]
    public void Production_rejects_each_unsafe_control(string key, string unsafeValue)
    {
        var values = SafeProductionValues();
        values[key] = unsafeValue;

        var act = () => RuntimeConfigurationSafetyValidator.Validate(
            BuildConfiguration(values),
            "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{key}*");
    }

    [Theory]
    [MemberData(nameof(PlaceholderMarkers))]
    public void Production_rejects_known_placeholder_secret_markers(string marker)
    {
        var values = SafeProductionValues();
        var fixtureValue = $"synthetic-{marker}-fixture";
        values["LineMessaging:Jesus:ChannelAccessToken"] = fixtureValue;

        var act = () => RuntimeConfigurationSafetyValidator.Validate(
            BuildConfiguration(values),
            "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LineMessaging:Jesus:ChannelAccessToken*")
            .Which.Message.Should().NotContain(fixtureValue);
    }

    [Fact]
    public void Production_rejects_missing_sensitive_manifest_key()
    {
        var values = SafeProductionValues();
        values.Remove("LineMessaging:Jesus:ChannelAccessToken");

        var act = () => RuntimeConfigurationSafetyValidator.Validate(
            BuildConfiguration(values),
            "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LineMessaging:Jesus:ChannelAccessToken*");
    }

    [Fact]
    public void Development_bypasses_production_runtime_configuration_gate()
    {
        var act = () => RuntimeConfigurationSafetyValidator.Validate(
            BuildConfiguration(new Dictionary<string, string?>()),
            "Development");

        act.Should().NotThrow();
    }

    [Fact]
    public void Current_repository_production_overlay_sets_all_eight_safe_controls()
    {
        var projectDirectory = ChurchReportProjectDirectory();
        var baseConfiguration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var productionOverlay = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
        var effectiveConfiguration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();

        var overlayPresenceCount = ProductionControlKeys.Count(key => !string.IsNullOrWhiteSpace(productionOverlay[key]));
        var safeEffectiveCount = ProductionControlKeys.Count(key => IsSafeProductionControl(key, effectiveConfiguration[key]));
        var unsafeOrInheritedCount = ProductionControlKeys.Count(key =>
            string.IsNullOrWhiteSpace(productionOverlay[key]) || !IsSafeProductionControl(key, effectiveConfiguration[key]));

        overlayPresenceCount.Should().Be(8);
        safeEffectiveCount.Should().Be(8);
        unsafeOrInheritedCount.Should().Be(0);
        baseConfiguration.Should().NotBeNull();
    }

    public static TheoryData<string, string> UnsafeControlCases()
    {
        return new TheoryData<string, string>
        {
            { "Security:EnforceGlobalAuthorization", "false" },
            { "Security:AllowSessionIdentityFallback", "true" },
            { "LinePay:IsSandbox", "true" },
            { "Cash_Environment", "Development" },
            { "Cash_Environment", "Staging" },
            { "Cash_Environment", "test" },
            { "Cash_Environment", "sandbox" },
            { "PAY_PROVIDER", "TestProvider" },
            { "Payment:DefaultProfile", "JesusTest" },
            { "Payment:Profiles:MyPayProduction:Environment", "Sandbox" },
            { "TSPG:TestMode", "true" }
        };
    }

    public static TheoryData<string> PlaceholderMarkers()
    {
        return new TheoryData<string>
        {
            "placeholder",
            "replace",
            "runtime_secret",
            "your_",
            "_here",
            "todo",
            "dummy",
            "example",
            "sample",
            "changeme"
        };
    }

    private static Dictionary<string, string?> SafeProductionValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:EnforceGlobalAuthorization"] = "true",
            ["Security:AllowSessionIdentityFallback"] = "false",
            ["LinePay:IsSandbox"] = "false",
            ["Cash_Environment"] = "Production",
            ["PAY_PROVIDER"] = "高鉅金流",
            ["Payment:DefaultProfile"] = "MyPayProduction",
            ["Payment:Profiles:MyPayProduction:Environment"] = "Production",
            ["TSPG:TestMode"] = "false"
        };

        for (var index = 0; index < SensitiveKeyManifest.Length; index++)
        {
            values[SensitiveKeyManifest[index]] = $"synthetic-runtime-secret-{index}";
        }

        return values;
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static bool IsSafeProductionControl(string key, string? value)
    {
        return key switch
        {
            "Security:EnforceGlobalAuthorization" => bool.TryParse(value, out var parsed) && parsed,
            "Security:AllowSessionIdentityFallback" => bool.TryParse(value, out var parsed) && !parsed,
            "LinePay:IsSandbox" => bool.TryParse(value, out var parsed) && !parsed,
            "Cash_Environment" => string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "正式環境", StringComparison.Ordinal),
            "PAY_PROVIDER" => value == "高鉅金流",
            "Payment:DefaultProfile" => value == "MyPayProduction",
            "Payment:Profiles:MyPayProduction:Environment" => value == "Production",
            "TSPG:TestMode" => bool.TryParse(value, out var parsed) && !parsed,
            _ => false
        };
    }

    private static string ChurchReportProjectDirectory()
    {
        return Path.Combine(ProjectRoot(), "SpeechMessageProducts.ChurchReport");
    }

    private static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
