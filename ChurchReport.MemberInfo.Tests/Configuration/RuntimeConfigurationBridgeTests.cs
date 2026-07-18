using System;
using System.Collections.Generic;
using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Configuration;

public sealed class RuntimeConfigurationBridgeTests
{
    [Fact]
    public void Current_before_initialization_fails_closed_without_configuration_value()
    {
        var bridge = new RuntimeConfigurationBridge();

        var act = () => _ = bridge.Current;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Fact]
    public void Initialized_bridge_exposes_effective_higher_priority_configuration()
    {
        var effectiveConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-base-value"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-external-overlay"
            })
            .Build();
        var bridge = new RuntimeConfigurationBridge();

        bridge.Initialize(effectiveConfiguration);

        bridge.Current["LineMessaging:Jesus:ChannelAccessToken"]
            .Should().Be("synthetic-external-overlay");
    }

    [Fact]
    public void Different_second_initialization_is_rejected_without_configuration_value()
    {
        var bridge = new RuntimeConfigurationBridge();
        bridge.Initialize(BuildConfiguration("synthetic-first"));

        var act = () => bridge.Initialize(BuildConfiguration("synthetic-second"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already initialized*");
    }

    private static IConfiguration BuildConfiguration(string value)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LineMessaging:Jesus:ChannelAccessToken"] = value
            })
            .Build();
    }
}
