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
        groups[0].GroupTime = "  週三 19:30  ";
        groups[0].GroupPlace = "  恩典教室  ";
        groups[1].GroupTime = "   ";
        groups[1].GroupPlace = null;
        var memberships = new[]
        {
            Member("L1", "C1"),
            Member("L2", "C2")
        };

        var tree = DistrictTreeBuilder.Build(groups, memberships, new[] { "C1", "C2", "C3" }, true, "church");

        var district = tree.Districts.Single();
        district.GroupCount.Should().Be(3);
        district.MemberCount.Should().Be(2);
        tree.Ungrouped.Should().NotBeNull();
        tree.Ungrouped!.MemberCount.Should().Be(1);

        var firstGroup = district.Groups.Single(group => group.ListId == "L1");
        firstGroup.GroupName.Should().Be("晨光組");
        firstGroup.MemberCount.Should().Be(1);
        firstGroup.GroupTime.Should().Be("週三 19:30");
        firstGroup.GroupPlace.Should().Be("恩典教室");

        var secondGroup = district.Groups.Single(group => group.ListId == "L2");
        secondGroup.GroupName.Should().Be("活泉組");
        secondGroup.MemberCount.Should().Be(1);
        secondGroup.GroupTime.Should().BeEmpty();
        secondGroup.GroupPlace.Should().BeEmpty();

        district.Groups.Single(group => group.ListId == "L3").MemberCount.Should().Be(0);
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
        var groups = new[]
        {
            Group("L0", "", "", "A牧區", "未填區長組"),
            Group("L1", "R1", "區長甲", "B牧區", "甲區"),
            Group("L2", "R2", "區長乙", "Z牧區", "乙區")
        };

        var tree = DistrictTreeBuilder.Build(
            groups,
            Array.Empty<GroupMembershipRow>(),
            Array.Empty<string>(),
            true,
            "church");

        tree.Districts.Select(x => x.RaceLeaderName)
            .Should().ContainInOrder("區長甲", "區長乙", DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Districts.Last().RaceLeaderName.Should().Be(DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Ungrouped.Should().NotBeNull();
    }

    [Fact]
    public void Build_UsesFallbacksAndKeepsChurchUngroupedNodeWhenEmpty()
    {
        var groups = new[] { Group("L1", "", "", "", "") };

        var tree = DistrictTreeBuilder.Build(groups, Array.Empty<GroupMembershipRow>(), Array.Empty<string>(), true, "church");

        tree.Districts.Single().RaceLeaderName.Should().Be(DistrictTreeBuilder.UnknownRaceLeaderName);
        tree.Districts.Single().AreaName.Should().Be(DistrictTreeBuilder.MissingAreaName);
        tree.Districts.Single().AreaName.Should().BeEmpty();
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
