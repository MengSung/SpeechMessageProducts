/*
 * 檔案整體說明
 *
 * 目的與定位：驗證登入principal被標記為已認證、必要LINE工作識別保留且密碼不進claims，並處理空輸入。
 *
 * 主要功能與流程：
 * 本檔宣告 LoginClaimsFactoryTests。
 * 測試案例入口包含 Build_MarksPrincipalAuthenticated、Build_AccountLogin_DoesNotStoreCredential、
 * Build_LineLogin_CarriesLineIdAsWorkingKey、Build_NullInputs_DoNotThrow。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
