using System.Reflection;
using ChurchReport.Controllers;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class RefererIdentityRemovedTests
    {
        [Fact]
        public void TryGetLineUserIdFromRequest_IsDeleted()
        {
            var method = typeof(BaseChurchController).GetMethod(
                "TryGetLineUserIdFromRequest",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method.Should().BeNull(
                "identity must never be derived from the client-controlled Referer header");
        }
    }
}
