using ChurchReport.Services.ContactAvatar;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class DefaultAvatarSvgTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(200000)]
    public void ForGender_ReturnsMaleAvatar_ForSupportedMaleCodes(int genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Male);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(200001)]
    public void ForGender_ReturnsFemaleAvatar_ForSupportedFemaleCodes(int genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Female);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100000000)]
    [InlineData(0)]
    public void ForGender_ReturnsNeutralAvatar_ForUnknownCodes(int? genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Neutral);
    }
}
