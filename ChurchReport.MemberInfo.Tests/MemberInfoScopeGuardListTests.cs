/*
 * 檔案整體說明
 *
 * 目的與定位：驗證名單查閱只接受已授權可見集合內的正式ID，空ID、未知權限及欠缺可見範圍時拒絕。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoScopeGuardListTests。
 * 測試案例入口包含 KnownAccess_AllowsVisibleList、KnownAccess_DeniesListOutsideAuthoritativeScope、
 * BlankRequestedList_Denied、MissingVisibleScope_Denied、UnknownAccess_Denied。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardListTests
{
    private static readonly string[] VisibleLists = { "L1", "L2" };

    [Theory]
    [InlineData(MemberInfoAccess.Church)]
    [InlineData(MemberInfoAccess.ShepherdList)]
    public void KnownAccess_AllowsVisibleList(string access)
    {
        MemberInfoScopeGuard.IsListAllowed(access, VisibleLists, "l2").Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberInfoAccess.Church)]
    [InlineData(MemberInfoAccess.ShepherdList)]
    public void KnownAccess_DeniesListOutsideAuthoritativeScope(string access)
    {
        MemberInfoScopeGuard.IsListAllowed(access, VisibleLists, "L9").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRequestedList_Denied(string? requested)
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, VisibleLists, requested!).Should().BeFalse();
    }

    [Fact]
    public void MissingVisibleScope_Denied()
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, null!, "L1").Should().BeFalse();
    }

    [Fact]
    public void UnknownAccess_Denied()
    {
        MemberInfoScopeGuard.IsListAllowed("whatever", VisibleLists, "L1").Should().BeFalse();
    }
}
