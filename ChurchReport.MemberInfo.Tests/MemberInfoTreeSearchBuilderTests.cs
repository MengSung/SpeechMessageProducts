/*
 * 檔案整體說明
 *
 * 目的與定位：驗證搜尋結果只取授權現行成員、牧養範圍不回傳未分組資料，並依委身類型及姓名穩定排序。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoTreeSearchBuilderTests。
 * 測試案例入口包含 Build_ReturnsDistinctListsContainingMatchingCurrentContacts、Build_ShepherdNeverReturnsUngrouped、
 * Build_ToleratesNullInputs、Build_ReturnsDistinctAuthorizedRowsSortedByCommitmentTypeThenName、Member、Row。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Services.MemberInfo;
using ChurchReport.ViewModels.MemberInfoTree;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoTreeSearchBuilderTests
{
    [Fact]
    public void Build_ReturnsDistinctListsContainingMatchingCurrentContacts()
    {
        var memberships = new[]
        {
            Member("L2", "C1"),
            Member("L1", "c1"),
            Member("L3", "C2", false),
            Member("L4", "C3")
        };

        var result = MemberInfoTreeSearchBuilder.Build(memberships, new[] { "C1", "C2" }, true);

        result.MatchingListIds.Should().ContainInOrder("L1", "L2");
        result.HasUngrouped.Should().BeTrue();
    }

    [Fact]
    public void Build_ShepherdNeverReturnsUngrouped()
    {
        var result = MemberInfoTreeSearchBuilder.Build(
            new[] { Member("L1", "C1") },
            new[] { "C2" },
            false);

        result.MatchingListIds.Should().BeEmpty();
        result.HasUngrouped.Should().BeFalse();
    }

    [Fact]
    public void Build_ToleratesNullInputs()
    {
        var result = MemberInfoTreeSearchBuilder.Build(null, null, true);

        result.MatchingListIds.Should().BeEmpty();
        result.HasUngrouped.Should().BeFalse();
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ReturnsDistinctAuthorizedRowsSortedByCommitmentTypeThenName()
    {
        var memberships = new[] { Member("L1", "C1"), Member("L2", "C2") };
        var rows = new[]
        {
            Row("C2", "會友甲", 1, true),
            Row("c1", "會友乙", 0, true),
            Row("C1", "重複列", 0, true),
            Row("C9", "不可見", null, false)
        };

        var result = MemberInfoTreeSearchBuilder.Build(
            memberships,
            new[] { "C1", "C2" },
            true,
            rows);

        result.Rows.Select(row => row.ContactId).Should().ContainInOrder("c1", "C2");
        result.Rows.Select(row => row.FullName).Should().ContainInOrder("會友乙", "會友甲");
    }

    private static GroupMembershipRow Member(string listId, string contactId, bool current = true) => new()
    {
        ListId = listId,
        ContactId = contactId,
        IsCurrent = current
    };

    private static GroupMemberRowViewModel Row(
        string contactId,
        string fullName,
        int? membershipStatusOrder,
        bool hasMembershipStatusValue) => new()
    {
        ContactId = contactId,
        FullName = fullName,
        MembershipStatusOrder = membershipStatusOrder,
        HasMembershipStatusValue = hasMembershipStatusValue
    };
}
