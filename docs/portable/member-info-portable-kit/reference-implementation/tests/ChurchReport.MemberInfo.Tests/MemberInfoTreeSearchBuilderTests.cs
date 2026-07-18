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
        result.HasUngrouped.Should().BeTrue("C2 is a matching contact without a current group membership");
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
        // Rows 是搜尋回應新增的集合契約；上游查不到資料時應回傳空集合，而不是遺留舊結果或讓序列化端遇到 null。
        var result = MemberInfoTreeSearchBuilder.Build(null, null, true);

        result.MatchingListIds.Should().BeEmpty();
        result.HasUngrouped.Should().BeFalse();
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ReturnsDistinctAuthorizedRowsSortedByCommitmentTypeThenName()
    {
        // memberships 保留兩位目前小組成員的樹節點關聯；matching IDs 則代表 Controller 完成聯絡人授權後的白名單。
        var memberships = new[] { Member("L1", "C1"), Member("L2", "C2") };
        var rows = new[]
        {
            // C1/c1 模擬 CRM GUID 大小寫不一致且重複回列；C9 雖有完整資料列，但未列在授權白名單中。
            // C2 的姓名在 Ordinal 排序較前，但 metadata rank 較後；證明搜尋使用客製化順位而非姓名或 raw 值。
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

        // 斷言同時鎖定三個真實安全／一致性需求：ID 不分大小寫去重、未授權 C9 不得外洩、metadata rank 優先排序。
        // 保留第一筆 c1 也避免同一會友在搜尋網格重複出現，或因後來的重複列覆寫正確顯示資料。
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
