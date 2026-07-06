using ChurchReport.Security;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class LoginResponseFactoryTests
    {
        [Fact]
        public void Build_DoesNotExposeCredentials()
        {
            var payload = LoginResponseFactory.Build("IntegrateView", "list-1", "User");
            var json = JsonConvert.SerializeObject(payload);

            json.Should().NotContain("password");
            json.Should().NotContain("account");
            json.Should().NotContain("new_app_pass");
        }

        [Fact]
        public void Build_PreservesFrontEndContractFields()
        {
            var payload = LoginResponseFactory.Build("IntegrateView", "list-1", "User");

            payload.DisplayViewType.Should().Be("IntegrateView");
            payload.ActiveListId.Should().Be("list-1");
            payload.fullname.Should().Be("User");
            payload.message.Should().Be("登入 User 成功!");
        }

        [Fact]
        public void Build_NullActiveListId_BecomesEmptyString()
        {
            var payload = LoginResponseFactory.Build("MultiGroupView", null, "User");

            payload.ActiveListId.Should().Be(string.Empty);
        }
    }
}
