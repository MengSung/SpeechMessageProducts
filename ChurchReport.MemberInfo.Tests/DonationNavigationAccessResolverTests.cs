/*
 * 檔案整體說明
 *
 * 目的與定位：驗證奉獻管理導覽只對核准會計角色可見，普通角色不能因顯示邏輯而取得管理入口。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationNavigationAccessResolverTests。
 * 測試案例入口包含 CanAccessDonationManagement_returns_true_for_accounting_roles、
 * CanAccessDonationManagement_returns_false_for_non_accounting_roles。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Services.Donation;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class DonationNavigationAccessResolverTests
{
    [Theory]
    [InlineData("會計")]
    [InlineData("主責會計")]
    [InlineData("  會計同工  ")]
    public void CanAccessDonationManagement_returns_true_for_accounting_roles(string jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("小組長")]
    [InlineData("牧養主任")]
    [InlineData("行政同工")]
    public void CanAccessDonationManagement_returns_false_for_non_accounting_roles(string? jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeFalse();
    }
}
