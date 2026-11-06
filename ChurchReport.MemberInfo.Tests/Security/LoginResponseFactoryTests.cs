/*
 * 檔案整體說明
 *
 * 目的與定位：驗證登入JSON回應保留前端必要欄位、缺名單時回空字串且不含登入憑證。
 *
 * 主要功能與流程：
 * 本檔宣告 LoginResponseFactoryTests。
 * 測試案例入口包含 Build_DoesNotExposeCredentials、Build_PreservesFrontEndContractFields、
 * Build_NullActiveListId_BecomesEmptyString。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
