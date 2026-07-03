using ChurchReport.Services.Donation;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class DonationNavigationAccessResolverTests
{
    [Theory]
    [InlineData("會計")]
    [InlineData("主責會計")]
    [InlineData("  會計同工  ")]
    public void CanAccessDonationManagement_returns_true_for_accounting_roles(string jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("小組長")]
    [InlineData("牧養主任")]
    [InlineData("行政同工")]
    public void CanAccessDonationManagement_returns_false_for_non_accounting_roles(string? jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeFalse();
    }
}
