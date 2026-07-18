using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoAccessResolverTests
{
    [Theory]
    [InlineData("牧師傳道")]
    [InlineData("牧養主任")]
    [InlineData("主任牧師、牧養主任")] // 包含即可
    [InlineData("  牧師傳道  ")]        // 前後空白
    public void PastorRole_ReturnsChurch(string jobTitle)
    {
        MemberInfoAccessResolver.Resolve(jobTitle, "小組長")
            .Should().Be(MemberInfoAccess.Church);
    }

    [Fact]
    public void PastorWinsOverShepherd()
    {
        MemberInfoAccessResolver.Resolve("牧養主任", "小組長")
            .Should().Be(MemberInfoAccess.Church);
    }

    [Fact]
    public void GroupLeader_ReturnsShepherdList()
    {
        MemberInfoAccessResolver.Resolve("核心同工", "小組長")
            .Should().Be(MemberInfoAccess.ShepherdList);
    }

    [Theory]
    [InlineData("", "個人回報")]
    [InlineData("會計", "個人回報")]
    [InlineData(null, null)]
    [InlineData("會友", "")]
    public void NoQualifyingRole_ReturnsNull(string? jobTitle, string? loginType)
    {
        MemberInfoAccessResolver.Resolve(jobTitle, loginType)
            .Should().BeNull();
    }
}
