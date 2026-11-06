/*
 * 檔案整體說明
 *
 * 目的與定位：以站內與外部網址案例驗證返回路徑分類，防止登入後重新導向至未授權站外位置。
 *
 * 主要功能與流程：
 * 本檔宣告 LocalReturnUrlTests。
 * 測試案例入口包含 IsLocal_ClassifiesUrls。
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
