using ChurchReport.Security;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LoginClaimsFactoryTests
    {
        [Fact]
        public void Build_MarksPrincipalAuthenticated()
        {
            var principal = LoginClaimsFactory.Build("cid-1", "alice", "", "ACCOUNT");

            principal.Identity.Should().NotBeNull();
            principal.Identity!.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public void Build_AccountLogin_DoesNotStoreCredential()
        {
            var principal = LoginClaimsFactory.Build("cid-1", "alice", "", "ACCOUNT");

            principal.FindFirst(LoginClaimsFactory.AccountClaim)!.Value.Should().Be("alice");
            principal.FindFirst(LoginClaimsFactory.LoginTypeClaim)!.Value.Should().Be("ACCOUNT");
            principal.FindFirst(LoginClaimsFactory.PasswordKeyClaim)!.Value.Should().Be("");
            principal.FindFirst(LoginClaimsFactory.ContactIdClaim)!.Value.Should().Be("cid-1");
        }

        [Fact]
        public void Build_LineLogin_CarriesLineIdAsWorkingKey()
        {
            var principal = LoginClaimsFactory.Build("cid-2", "LineIdLogin", "U0123456789abcdef0123456789abcdef", "LINE");

            principal.FindFirst(LoginClaimsFactory.LoginTypeClaim)!.Value.Should().Be("LINE");
            principal.FindFirst(LoginClaimsFactory.PasswordKeyClaim)!.Value.Should().Be("U0123456789abcdef0123456789abcdef");
        }

        [Fact]
        public void Build_NullInputs_DoNotThrow()
        {
            var principal = LoginClaimsFactory.Build(null, null, null, null);

            principal.Identity!.IsAuthenticated.Should().BeTrue();
            principal.FindFirst(LoginClaimsFactory.AccountClaim)!.Value.Should().Be("");
        }
    }
}
