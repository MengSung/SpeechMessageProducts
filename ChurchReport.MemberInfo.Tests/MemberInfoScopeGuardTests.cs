/*
 * 檔案整體說明
 *
 * 目的與定位：驗證會友查閱依全教會或牧養名單範圍判斷，缺ID、缺權限或不在授權集合的會友全部拒絕。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoScopeGuardTests。
 * 測試案例入口包含 Church_AllowsAnyNonEmpty、Shepherd_AllowsInList、Shepherd_DeniesNotInList、DeniesMissingId、DeniesNoAccess。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardTests
{
    private static readonly HashSet<string> Shepherd = new(StringComparer.OrdinalIgnoreCase)
    {
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
    };

    [Fact]
    public void Church_AllowsAnyNonEmpty() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeTrue();

    [Fact]
    public void Shepherd_AllowsInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "22222222-2222-2222-2222-222222222222").Should().BeTrue();

    [Fact]
    public void Shepherd_DeniesNotInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeniesMissingId(string? requested) =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, requested!).Should().BeFalse();

    [Fact]
    public void DeniesNoAccess() =>
        MemberInfoScopeGuard.IsContactAllowed(null, Shepherd,
            "11111111-1111-1111-1111-111111111111").Should().BeFalse();
}
