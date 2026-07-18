using Xunit;
using FluentAssertions;

namespace ChurchReport.MemberInfo.Tests;

public class SanityTest
{
    [Fact]
    public void Sanity_TrueIsTrue()
    {
        true.Should().BeTrue();
    }
}
