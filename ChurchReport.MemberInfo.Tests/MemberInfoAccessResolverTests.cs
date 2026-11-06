/*
 * 檔案整體說明
 *
 * 目的與定位：驗證教會角色優先順序、全教會／牧養名單分類及無匹配身分拒絕，防止登入類型擴張權限。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoAccessResolverTests。
 * 測試案例入口包含 PastorRole_ReturnsChurch、PastorWinsOverShepherd、GroupLeader_ReturnsShepherdList、
 * NoQualifyingRole_ReturnsNull。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoAccessResolverTests
{
    [Theory]
    [InlineData("牧師傳道")]
    [InlineData("牧養主任")]
    [InlineData("檢視全教會照片資訊")]
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
