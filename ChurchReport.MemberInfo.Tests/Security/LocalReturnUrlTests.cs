using ChurchReport.Security;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LocalReturnUrlTests
    {
        [Theory]
        [InlineData("/SmallGroup/IntegrateView/1", true)]
        [InlineData("/", true)]
        [InlineData("~/Home/Index", true)]
        [InlineData("//evil.example.com", false)]
        [InlineData("/\\evil.example.com", false)]
        [InlineData("https://evil.example.com", false)]
        [InlineData("http://evil.example.com/path", false)]
        [InlineData("evil.example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsLocal_ClassifiesUrls(string? url, bool expected)
        {
            LocalReturnUrl.IsLocal(url).Should().Be(expected);
        }
    }
}
