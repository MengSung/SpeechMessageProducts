using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardTests
{
    private static readonly HashSet<string> Shepherd = new(StringComparer.OrdinalIgnoreCase)
    {
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
    };

    [Fact]
    public void Church_AllowsAnyNonEmpty() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeTrue();

    [Fact]
    public void Shepherd_AllowsInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "22222222-2222-2222-2222-222222222222").Should().BeTrue();

    [Fact]
    public void Shepherd_DeniesNotInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeniesMissingId(string? requested) =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, requested!).Should().BeFalse();

    [Fact]
    public void DeniesNoAccess() =>
        MemberInfoScopeGuard.IsContactAllowed(null, Shepherd,
            "11111111-1111-1111-1111-111111111111").Should().BeFalse();
}
