using ChurchReport.Services.MemberInfo;
using ChurchReport.ViewModels.MemberInfoTree;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class DistrictTreeBuilderTests
{
    [Fact]
    public void Build_GroupsByRaceLeaderKeyAndDeduplicatesMembers()
    {
        var groups = new[]
        {
            Group("L1", "R1", "同名區長", "曉光牧區", "甲組"),
            Group("L2", "R1", "同名區長", "曉光牧區", "乙組"),
            Group("L3", "R2", "同名區長", "曉光牧區", "丙組")
        };
        var memberships = new[]
        {
            Member("L1", "C1"),
            Member("L1", "c1"),
            Member("L2", "C1"),
            Member("L2", "C2"),
            Member("L3", "C3")
        };

        var tree = DistrictTreeBuilder.Build(groups, memberships, new[] { "C1", "C2", "C3", "C4" }, true, "church");

        tree.Districts.Should().HaveCount(2);
        tree.Districts.Single(x => x.RaceLeaderKey == "R1").MemberCount.Should().Be(2);
        tree.Districts.Single(x => x.RaceLeaderKey == "R1").Groups.Single(x => x.ListId == "L1").MemberCount.Should().Be(1);
        tree.Districts.Single(x => x.RaceLeaderKey == "R1").Groups.Single(x => x.ListId == "L2").MemberCount.Should().Be(2);
        tree.Ungrouped.Should().NotBeNull();
        tree.Ungrouped!.MemberCount.Should().Be(1);
    }

    [Fact]
    public void Build_PreservesGroupMetadataAndCountsAllDistrictGroups()
    {
        var groups = new[]
        {
            Group("L1", "R1", "陳區長", "恩典牧區", "晨光組"),
            Group("L2", "R1", "陳區長", "恩典牧區", "活泉組"),
            Group("L3", "R1", "陳區長", "恩典牧區", "平安組")
        };
        var descriptorGroupTimeProperty = typeof(SmallGroupDescriptor).GetProperty("GroupTime");
        var descriptorGroupPlaceProperty = typeof(SmallGroupDescriptor).GetProperty("GroupPlace");
        descriptorGroupTimeProperty.Should().NotBeNull();
        descriptorGroupPlaceProperty.Should().NotBeNull();
        descriptorGroupTimeProperty!.SetValue(groups[0], "  週三 19:30  ");
        descriptorGroupPlaceProperty!.SetValue(groups[0], "  恩典教室  ");
        descriptorGroupTimeProperty.SetValue(groups[1], "   ");
        descriptorGroupPlaceProperty.SetValue(groups[1], null);
        var memberships = new[]
        {
            Member("L1", "C1"),
            Member("L2", "C2")
        };

        var tree = DistrictTreeBuilder.Build(groups, memberships, new[] { "C1", "C2", "C3" }, true, "church");

        var district = tree.Districts.Single();
        var districtGroupCountProperty = typeof(DistrictNodeViewModel).GetProperty("GroupCount");
        districtGroupCountProperty.Should().NotBeNull();
        districtGroupCountProperty!.GetValue(district).Should().Be(3);
        district.MemberCount.Should().Be(2);
        tree.Ungrouped.Should().NotBeNull();
        tree.Ungrouped!.MemberCount.Should().Be(1);

        var groupTimeProperty = typeof(GroupNodeViewModel).GetProperty("GroupTime");
        var groupPlaceProperty = typeof(GroupNodeViewModel).GetProperty("GroupPlace");
        groupTimeProperty.Should().NotBeNull();
        groupPlaceProperty.Should().NotBeNull();

        var firstGroup = district.Groups.Single(group => group.ListId == "L1");
        firstGroup.GroupName.Should().Be("晨光組");
        firstGroup.MemberCount.Should().Be(1);
        groupTimeProperty!.GetValue(firstGroup).Should().Be("週三 19:30");
        groupPlaceProperty!.GetValue(firstGroup).Should().Be("恩典教室");

        var secondGroup = district.Groups.Single(group => group.ListId == "L2");
        secondGroup.GroupName.Should().Be("活泉組");
        secondGroup.MemberCount.Should().Be(1);
        groupTimeProperty.GetValue(secondGroup).Should().Be(string.Empty);
        groupPlaceProperty.GetValue(secondGroup).Should().Be(string.Empty);

        var thirdGroup = district.Groups.Single(group => group.ListId == "L3");
        thirdGroup.GroupName.Should().Be("平安組");
        thirdGroup.MemberCount.Should().Be(0);
    }

    [Fact]
    public void Build_ExcludesNonCurrentMembershipsAndSortsNodes()
    {
        var groups = new[]
        {
            Group("L2", "R2", "乙", "B牧區", "乙組"),
            Group("L1", "R1", "甲", "A牧區", "甲組")
        };
        var memberships = new[] { Member("L1", "C1", false), Member("L2", "C2") };

        var tree = DistrictTreeBuilder.Build(groups, memberships, Array.Empty<string>(), false, "shepherd");

        tree.Districts.Select(x => x.AreaName).Should().ContainInOrder("A牧區", "B牧區");
        tree.Districts.Single(x => x.RaceLeaderKey == "R1").MemberCount.Should().Be(0);
        tree.Ungrouped.Should().BeNull();
        tree.Scope.Should().Be("shepherd");
    }

    [Fact]
    public void Build_SortsUnknownRaceLeaderAfterAssignedDistrictsAndBeforeUngrouped()
    {
        // L0 故意使用字典序最前面的 A 牧區；若排序只看牧區名稱，它會錯誤地跑到已指派區長之前。
        // L1、L2 則代表資料完整的正常區長，用來鎖定「有區長優先、同類別內再照既有欄位排序」的兩階段規則。
        var groups = new[]
        {
            Group("L0", "", "", "A牧區", "未填區長組"),
            Group("L1", "R1", "區長甲", "B牧區", "甲區"),
            Group("L2", "R2", "區長乙", "Z牧區", "乙區")
        };

        // church scope 即使沒有任何目前會友，也必須保留獨立的「無小組」節點；畫面會把它接在所有區長節點之後。
        // 因此這裡同時保留空的 current-contact 集合，避免修正未填區長排序時誤把最後的無小組入口裁掉。
        var tree = DistrictTreeBuilder.Build(
            groups,
            Array.Empty<GroupMembershipRow>(),
            Array.Empty<string>(),
            true,
            "church");

        // 這組斷言防止 UI 再次出現「區長未填」搶在正式區長前面，並確認無小組入口仍可由 View 追加在樹尾。
        tree.Districts.Select(x => x.RaceLeaderName)
            .Should().ContainInOrder("區長甲", "區長乙", DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Districts.Last().RaceLeaderName.Should().Be(DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Ungrouped.Should().NotBeNull();
    }

    [Fact]
    public void Build_UsesFallbacksAndKeepsChurchUngroupedNodeWhenEmpty()
    {
        // 空白區長、牧區與小組名稱模擬 CRM 階層欄位尚未補齊的真實資料；各欄位的呈現策略並不相同。
        // 區長與小組長需要可辨識的替代文字，但牧區依產品決策必須保持空白，不能自行顯示「未填」標籤。
        var groups = new[] { Group("L1", "", "", "", "") };

        var tree = DistrictTreeBuilder.Build(groups, Array.Empty<GroupMembershipRow>(), Array.Empty<string>(), true, "church");

        // 同時檢查常數與實際輸出為空，可防止日後只改了其中一處，讓未填牧區重新在樹節點上冒出提示文字。
        tree.Districts.Single().RaceLeaderName.Should().Be(DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Districts.Single().AreaName.Should().Be(DistrictTreeBuilder.MissingAreaName);
        tree.Districts.Single().AreaName.Should().BeEmpty("未填牧區時畫面應直接留白");
        tree.Districts.Single().Groups.Single().LeaderName.Should().Be(DistrictTreeBuilder.MissingGroupLeaderName);
        tree.Ungrouped.Should().NotBeNull();
        tree.Ungrouped!.MemberCount.Should().Be(0);
    }

    [Fact]
    public void Build_ToleratesNullInputs()
    {
        var tree = DistrictTreeBuilder.Build(null, null, null, true, "church");

        tree.Districts.Should().BeEmpty();
        tree.Ungrouped.Should().NotBeNull();
    }

    [Fact]
    public void Build_FromTotalCurrentCount_DeductsDistinctCurrentGroupedContacts()
    {
        var groups = new[] { Group("L1", "R1", "甲", "A牧區", "甲組") };
        var memberships = new[]
        {
            Member("L1", "C1"),
            Member("L1", "c1"),
            Member("L1", "C2"),
            Member("L1", "C3", false)
        };

        var tree = DistrictTreeBuilder.Build(groups, memberships, 4, true, "church");

        tree.Ungrouped.Should().NotBeNull();
        tree.Ungrouped!.MemberCount.Should().Be(2);
    }

    private static SmallGroupDescriptor Group(
        string listId,
        string raceKey,
        string raceName,
        string areaName,
        string groupName) => new()
        {
            ListId = listId,
            RaceLeaderKey = raceKey,
            RaceLeaderName = raceName,
            AreaName = areaName,
            GroupName = groupName,
            LeaderName = string.Empty
        };

    private static GroupMembershipRow Member(string listId, string contactId, bool current = true) => new()
    {
        ListId = listId,
        ContactId = contactId,
        IsCurrent = current
    };
}
