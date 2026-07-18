using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Configuration;

public sealed class RuntimeConfigurationConsumerSourceContractTests
{
    private static readonly string[] ConsumerPaths =
    [
        "SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs",
        "SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs",
        "SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs",
        "SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs",
        "SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs",
        "SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs",
        "SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs",
        "SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs",
        "SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs",
        "SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs",
        "SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs",
        "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs",
        "SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs"
    ];

    [Fact]
    public void Frozen_consumers_do_not_create_local_configuration_builders()
    {
        var offenders = FindPaths(source => source.Contains("new ConfigurationBuilder", StringComparison.Ordinal));

        offenders.Should().BeEmpty(
            "AdHocConfigurationBuilderConsumerCount must be 0/13 after the X04A repair");
    }

    [Fact]
    public void Frozen_consumers_obtain_configuration_from_the_host_bridge()
    {
        var offenders = FindPaths(source => !source.Contains("RuntimeConfiguration.Current", StringComparison.Ordinal));

        offenders.Should().BeEmpty(
            "BridgeConsumerCount must be 13/13 after the X04A repair");
    }

    [Fact]
    public void Frozen_inventory_contains_exactly_thirteen_existing_product_paths()
    {
        ConsumerPaths.Should().HaveCount(13);

        var missingPaths = ConsumerPaths
            .Where(relativePath => !File.Exists(Path.Combine(ProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        missingPaths.Should().BeEmpty();
    }

    private static IReadOnlyList<string> FindPaths(Func<string, bool> predicate)
    {
        return ConsumerPaths
            .Where(relativePath => predicate(File.ReadAllText(
                Path.Combine(ProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)))))
            .ToArray();
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
