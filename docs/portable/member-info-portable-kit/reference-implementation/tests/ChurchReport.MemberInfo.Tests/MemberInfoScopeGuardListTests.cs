using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardListTests
{
    private static readonly string[] VisibleLists = { "L1", "L2" };

    [Theory]
    [InlineData(MemberInfoAccess.Church)]
    [InlineData(MemberInfoAccess.ShepherdList)]
    public void KnownAccess_AllowsVisibleList(string access)
    {
        MemberInfoScopeGuard.IsListAllowed(access, VisibleLists, "l2").Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberInfoAccess.Church)]
    [InlineData(MemberInfoAccess.ShepherdList)]
    public void KnownAccess_DeniesListOutsideAuthoritativeScope(string access)
    {
        MemberInfoScopeGuard.IsListAllowed(access, VisibleLists, "L9").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRequestedList_Denied(string? requested)
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, VisibleLists, requested!).Should().BeFalse();
    }

    [Fact]
    public void MissingVisibleScope_Denied()
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, null!, "L1").Should().BeFalse();
    }

    [Fact]
    public void UnknownAccess_Denied()
    {
        MemberInfoScopeGuard.IsListAllowed("whatever", VisibleLists, "L1").Should().BeFalse();
    }
}
