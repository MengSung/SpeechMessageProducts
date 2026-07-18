# 會友資訊 區長→小組→會友 樹狀 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把「會友資訊」從平面清單改成「區長 → 小組（含小組長）→ 會友」三層可折疊樹狀，第三層點小組才載入該組成員與頭像。

**Architecture:** 沿用本專案既有分層 —「純邏輯（可單元測試、無 CRM 依賴）放 `ChurchReport.Services.MemberInfo`，CRM I/O 與 MVC 動作留在 `MemberInfoController`，畫面在 `MemberInfoGrid.cshtml`」。骨架（區/組/人數）由一支便宜查詢＋純聚合器產生並快取；第三層成員由點擊時的 AJAX 逐組載入，頭像重用既有批次 API。

**Tech Stack:** ASP.NET Core MVC (net10.0)、DevExtreme MVC（DataGrid）、Dynamics 365 (`Microsoft.Xrm.Sdk`)、xUnit + FluentAssertions（測試）。

## Global Constraints

- 目標框架 net10.0；`.cshtml` 會編入 DLL → 改動需重新發佈＋重啟 app pool 才生效（開發期以 `dotnet build` 驗證編譯）。
- 測試框架固定 xUnit + FluentAssertions；測試專案 `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`；命名空間 `ChurchReport.MemberInfo.Tests`；file-scoped namespace。
- 純邏輯類別放 `ChurchReport/Services/MemberInfo/`，命名空間 `ChurchReport.Services.MemberInfo`，比照 `MemberInfoAccessResolver`/`MemberInfoScopeGuard`（`public static`，無 CRM 依賴）。
- 安全（沿用既有教訓）：第三層/搜尋/無小組端點一律再以 `CanViewContactsBatch` 逐一把關，**絕不信任前端傳來的 `listId`／`contactId`**；**使用者專屬資料不得進共用快取**（牧養名單骨架不快取或綁登入身分；全教會骨架不含個資才可共用快取）。
- 人數與成員一律「在籍且非結案」：`statecode==0` 且 `customertypecode != 結案值`（沿用 `BuildCurrentContactQuery`／`IsCurrentContactEntity`）。
- 所有指令請在主儲存庫根目錄執行：`D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport`（分支 `Sunny_5.1.2.TuneMemberView`）。
- 規格文件：`docs/superpowers/specs/2026-07-15-member-info-district-group-tree-design.md`（本計畫依此而來）。
- **JSON 大小寫**：本專案 `Startup.cs` 用 `AddNewtonsoftJson` + `DefaultContractResolver` → 序列化為 **PascalCase**。前端讀 ViewModel 欄位一律用 PascalCase（如 `tree.Districts`、`d.AreaName`、`g.ListId`、`row.ContactId`）；DevExtreme `dataField`/`key` 亦用 PascalCase。

---

## Task 1: `MemberInfoScopeGuard.IsListAllowed`（純邏輯，授權用）

**Files:**
- Modify: `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs`

**Interfaces:**
- Produces: `bool MemberInfoScopeGuard.IsListAllowed(string access, IReadOnlyCollection<string> allowedListIds, string requestedListId)` — 全教會可開任何小組名單；牧養名單只可開自己帶的名單。

- [ ] **Step 1: 寫失敗測試**

Create `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs`:

```csharp
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardListTests
{
    [Fact]
    public void Church_AllowsAnyList()
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, null, "L1")
            .Should().BeTrue();
    }

    [Fact]
    public void Shepherd_AllowsOnlyOwnLists()
    {
        var mine = new[] { "L1", "L2" };
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.ShepherdList, mine, "L2").Should().BeTrue();
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.ShepherdList, mine, "L9").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BlankRequestedList_Denied(string requested)
    {
        MemberInfoScopeGuard.IsListAllowed(MemberInfoAccess.Church, null, requested)
            .Should().BeFalse();
    }

    [Fact]
    public void UnknownAccess_Denied()
    {
        MemberInfoScopeGuard.IsListAllowed("whatever", new[] { "L1" }, "L1")
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoScopeGuardListTests`
Expected: FAIL（編譯錯誤：找不到 `IsListAllowed`）

- [ ] **Step 3: 實作**

Append to `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`（在既有 class 內、`IsContactAllowed` 之後新增；檔頭已 `using System.Linq;`？若無請補）：

```csharp
        public static bool IsListAllowed(
            string access,
            IReadOnlyCollection<string> allowedListIds,
            string requestedListId)
        {
            if (string.IsNullOrWhiteSpace(requestedListId))
            {
                return false;
            }

            if (string.Equals(access, MemberInfoAccess.Church, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(access, MemberInfoAccess.ShepherdList, StringComparison.Ordinal))
            {
                return false;
            }

            if (allowedListIds == null || allowedListIds.Count == 0)
            {
                return false;
            }

            foreach (var id in allowedListIds)
            {
                if (string.Equals(id, requestedListId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoScopeGuardListTests`
Expected: PASS（4 筆）

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs
git commit -m "feat(memberinfo): add IsListAllowed scope guard for group tree"
```

---

## Task 2: ViewModels 與聚合器輸入 DTO

**Files:**
- Create: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- Create: `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`

**Interfaces:**
- Produces（供後續所有 Task 使用，型別/欄位名固定）：
  - `ChurchReport.ViewModels.MemberInfoTree.GroupNodeViewModel { string ListId; string GroupName; string LeaderName; int MemberCount; }`
  - `DistrictNodeViewModel { string RaceLeaderKey; string AreaName; string RaceLeaderName; int MemberCount; List<GroupNodeViewModel> Groups; }`
  - `DistrictTreeViewModel { List<DistrictNodeViewModel> Districts; bool HasUngrouped; int UngroupedCount; string Scope; }`
  - `GroupMemberRowViewModel { string ContactId; string FullName; string Gender; DateTime? BirthDate; string Phone; string SpiritualIdentity; string Address; string MembershipStatus; string Relation; string Goal; }`
  - `ChurchReport.Services.MemberInfo.SmallGroupDescriptor { string ListId; string GroupName; string AreaName; string RaceLeaderName; string RaceLeaderKey; string LeaderName; }`
  - `GroupMembershipRow { string ListId; string ContactId; bool IsCurrent; }`

- [ ] **Step 1: 建立 ViewModels**

Create `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ChurchReport.ViewModels.MemberInfoTree
{
    public class GroupNodeViewModel
    {
        public string ListId { get; set; }
        public string GroupName { get; set; }
        public string LeaderName { get; set; }
        public int MemberCount { get; set; }
    }

    public class DistrictNodeViewModel
    {
        public string RaceLeaderKey { get; set; }
        public string AreaName { get; set; }
        public string RaceLeaderName { get; set; }
        public int MemberCount { get; set; }
        public List<GroupNodeViewModel> Groups { get; set; } = new List<GroupNodeViewModel>();
    }

    public class DistrictTreeViewModel
    {
        public List<DistrictNodeViewModel> Districts { get; set; } = new List<DistrictNodeViewModel>();
        public bool HasUngrouped { get; set; }
        public int UngroupedCount { get; set; }
        public string Scope { get; set; }
    }

    public class GroupMemberRowViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; }
        public string SpiritualIdentity { get; set; }
        public string Address { get; set; }
        public string MembershipStatus { get; set; }
        public string Relation { get; set; }
        public string Goal { get; set; }
    }
}
```

- [ ] **Step 2: 建立聚合器輸入 DTO**

Create `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`:

```csharp
namespace ChurchReport.Services.MemberInfo
{
    /// <summary>一個小組名單(list)的骨架描述，供 DistrictTreeBuilder 聚合，不含成員資料。</summary>
    public class SmallGroupDescriptor
    {
        public string ListId { get; set; }
        public string GroupName { get; set; }
        public string AreaName { get; set; }        // new_area_name（{區牧}牧區）；空白時上游補區牧姓名
        public string RaceLeaderName { get; set; }  // 區長姓名
        public string RaceLeaderKey { get; set; }   // 區長 contactId 字串；未填為 null/空
        public string LeaderName { get; set; }      // 小組長姓名
    }

    /// <summary>一筆「成員在某小組」的關聯，IsCurrent 已由上游判定(在籍且非結案)。</summary>
    public class GroupMembershipRow
    {
        public string ListId { get; set; }
        public string ContactId { get; set; }
        public bool IsCurrent { get; set; }
    }
}
```

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded（0 error）

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs
git commit -m "feat(memberinfo): add district-tree view models and builder input DTOs"
```

---

## Task 3: `DistrictTreeBuilder`（純聚合器，TDD）

**Files:**
- Create: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- Test: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`

**Interfaces:**
- Consumes: `SmallGroupDescriptor`、`GroupMembershipRow`（Task 2）、`DistrictTreeViewModel`（Task 2）
- Produces: `DistrictTreeViewModel DistrictTreeBuilder.Build(IEnumerable<SmallGroupDescriptor> groups, IEnumerable<GroupMembershipRow> memberships, IReadOnlyCollection<string> allCurrentContactIds, bool includeUngrouped, string scope)`；常數 `UnknownRaceLeaderName="區長未填"`、`MissingAreaName="(未填牧區)"`。

- [ ] **Step 1: 寫失敗測試**

Create `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class DistrictTreeBuilderTests
{
    private static SmallGroupDescriptor Grp(string listId, string race, string raceKey, string area = "曉光牧區")
        => new SmallGroupDescriptor { ListId = listId, GroupName = "組" + listId, AreaName = area, RaceLeaderName = race, RaceLeaderKey = raceKey, LeaderName = "長" + listId };

    private static GroupMembershipRow Mem(string listId, string contactId, bool current = true)
        => new GroupMembershipRow { ListId = listId, ContactId = contactId, IsCurrent = current };

    [Fact]
    public void GroupsUnderSameRaceLeader_CollapseIntoOneDistrict()
    {
        var groups = new[] { Grp("L1", "陳志明", "R1"), Grp("L2", "陳志明", "R1") };
        var mem = new[] { Mem("L1", "c1"), Mem("L1", "c2"), Mem("L2", "c3") };

        var tree = DistrictTreeBuilder.Build(groups, mem, new string[0], false, "church");

        tree.Districts.Should().HaveCount(1);
        tree.Districts[0].RaceLeaderName.Should().Be("陳志明");
        tree.Districts[0].Groups.Should().HaveCount(2);
    }

    [Fact]
    public void DistrictCount_DeduplicatesContactAcrossItsGroups()
    {
        var groups = new[] { Grp("L1", "陳志明", "R1"), Grp("L2", "陳志明", "R1") };
        var mem = new[] { Mem("L1", "c1"), Mem("L2", "c1"), Mem("L2", "c2") }; // c1 在兩組

        var tree = DistrictTreeBuilder.Build(groups, mem, new string[0], false, "church");

        tree.Districts[0].MemberCount.Should().Be(2);          // 去重
        FindGroup(tree, "L1").MemberCount.Should().Be(1);
        FindGroup(tree, "L2").MemberCount.Should().Be(2);      // 每組各自計
    }

    [Fact]
    public void NonCurrentMembers_Excluded()
    {
        var groups = new[] { Grp("L1", "陳志明", "R1") };
        var mem = new[] { Mem("L1", "c1"), Mem("L1", "c2", current: false) };

        var tree = DistrictTreeBuilder.Build(groups, mem, new string[0], false, "church");

        FindGroup(tree, "L1").MemberCount.Should().Be(1);
    }

    [Fact]
    public void MissingRaceLeader_BecomesUnknownDistrict()
    {
        var groups = new[] { Grp("L1", null, null) };
        var tree = DistrictTreeBuilder.Build(groups, new GroupMembershipRow[0], new string[0], false, "church");
        tree.Districts[0].RaceLeaderName.Should().Be(DistrictTreeBuilder.UnknownRaceLeaderName);
    }

    [Fact]
    public void BlankAreaName_FallsBackToPlaceholder()
    {
        var groups = new[] { Grp("L1", "陳志明", "R1", area: "") };
        var tree = DistrictTreeBuilder.Build(groups, new GroupMembershipRow[0], new string[0], false, "church");
        tree.Districts[0].AreaName.Should().Be(DistrictTreeBuilder.MissingAreaName);
    }

    [Fact]
    public void Ungrouped_IsCurrentContactsNotInAnyGroup()
    {
        var groups = new[] { Grp("L1", "陳志明", "R1") };
        var mem = new[] { Mem("L1", "c1") };
        var allCurrent = new[] { "c1", "c2", "c3" }; // c2,c3 沒在任何組

        var tree = DistrictTreeBuilder.Build(groups, mem, allCurrent, true, "church");

        tree.HasUngrouped.Should().BeTrue();
        tree.UngroupedCount.Should().Be(2);
    }

    [Fact]
    public void Ungrouped_NotIncludedWhenFlagFalse()
    {
        var tree = DistrictTreeBuilder.Build(new SmallGroupDescriptor[0], new GroupMembershipRow[0], new[] { "c1" }, false, "shepherd");
        tree.HasUngrouped.Should().BeFalse();
        tree.UngroupedCount.Should().Be(0);
    }

    private static GroupNodeViewModelRef FindGroup(DistrictTreeViewModel t, string listId)
    {
        foreach (var d in t.Districts)
            foreach (var g in d.Groups)
                if (g.ListId == listId) return new GroupNodeViewModelRef(g.MemberCount);
        return new GroupNodeViewModelRef(-1);
    }
    private class GroupNodeViewModelRef { public int MemberCount; public GroupNodeViewModelRef(int c){MemberCount=c;} }
}
```

（`FindGroup` 以小包裝回傳，避免測試檔另外 `using` ViewModels 命名空間；實作用真正的 VM。）

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~DistrictTreeBuilderTests`
Expected: FAIL（找不到 `DistrictTreeBuilder`）

- [ ] **Step 3: 實作**

Create `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.ViewModels.MemberInfoTree;

namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// 由「小組名單描述 + 成員關聯 + 全體在籍名單」聚合出 區長→小組 骨架與人數。純函式、無 CRM 依賴、可單元測試。
    /// </summary>
    public static class DistrictTreeBuilder
    {
        public const string UnknownRaceLeaderName = "區長未填";
        public const string MissingAreaName = "(未填牧區)";
        private const string UnknownRaceLeaderKey = "__unknown_race__";

        public static DistrictTreeViewModel Build(
            IEnumerable<SmallGroupDescriptor> groups,
            IEnumerable<GroupMembershipRow> memberships,
            IReadOnlyCollection<string> allCurrentContactIds,
            bool includeUngrouped,
            string scope)
        {
            var groupList = (groups ?? Enumerable.Empty<SmallGroupDescriptor>()).ToList();
            var currentMem = (memberships ?? Enumerable.Empty<GroupMembershipRow>())
                .Where(m => m != null && m.IsCurrent && !string.IsNullOrEmpty(m.ListId) && !string.IsNullOrEmpty(m.ContactId))
                .ToList();

            // 每個 list 的在籍成員集合（去重）
            var membersByList = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in currentMem)
            {
                if (!membersByList.TryGetValue(m.ListId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    membersByList[m.ListId] = set;
                }
                set.Add(m.ContactId);
            }

            // 依區長分群
            var districts = new Dictionary<string, DistrictNodeViewModel>(StringComparer.Ordinal);
            var districtMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var g in groupList)
            {
                var key = string.IsNullOrWhiteSpace(g.RaceLeaderKey) ? UnknownRaceLeaderKey : g.RaceLeaderKey;
                if (!districts.TryGetValue(key, out var district))
                {
                    district = new DistrictNodeViewModel
                    {
                        RaceLeaderKey = key,
                        RaceLeaderName = string.IsNullOrWhiteSpace(g.RaceLeaderName) ? UnknownRaceLeaderName : g.RaceLeaderName,
                        AreaName = string.IsNullOrWhiteSpace(g.AreaName) ? MissingAreaName : g.AreaName
                    };
                    districts[key] = district;
                    districtMembers[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                membersByList.TryGetValue(g.ListId, out var groupSet);
                var count = groupSet?.Count ?? 0;

                district.Groups.Add(new GroupNodeViewModel
                {
                    ListId = g.ListId,
                    GroupName = g.GroupName ?? string.Empty,
                    LeaderName = string.IsNullOrWhiteSpace(g.LeaderName) ? "小組長未填" : g.LeaderName,
                    MemberCount = count
                });

                if (groupSet != null)
                {
                    foreach (var c in groupSet) { districtMembers[key].Add(c); }
                }
            }

            foreach (var kv in districts)
            {
                kv.Value.MemberCount = districtMembers[kv.Key].Count;
                kv.Value.Groups = kv.Value.Groups.OrderBy(x => x.GroupName, StringComparer.Ordinal).ToList();
            }

            var result = new DistrictTreeViewModel
            {
                Scope = scope,
                Districts = districts.Values
                    .OrderBy(d => d.AreaName, StringComparer.Ordinal)
                    .ThenBy(d => d.RaceLeaderName, StringComparer.Ordinal)
                    .ToList()
            };

            if (includeUngrouped && allCurrentContactIds != null)
            {
                var grouped = new HashSet<string>(currentMem.Select(m => m.ContactId), StringComparer.OrdinalIgnoreCase);
                var ungrouped = allCurrentContactIds.Where(id => !string.IsNullOrEmpty(id) && !grouped.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                result.UngroupedCount = ungrouped;
                result.HasUngrouped = ungrouped > 0;
            }

            return result;
        }
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~DistrictTreeBuilderTests`
Expected: PASS（7 筆）

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs
git commit -m "feat(memberinfo): add DistrictTreeBuilder pure aggregator with tests"
```

---

## Task 4: `RelationGoalFormatter`（純邏輯，關係/目標兩欄，TDD）

**Files:**
- Create: `ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`
- Test: `ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs`

**Interfaces:**
- Produces: `(string Relations, string Goals) RelationGoalFormatter.Format(IEnumerable<(string Role, string TargetName)> items)` — 去重(role|target)、保序、兩欄以「、」串接、索引對齊。

- [ ] **Step 1: 寫失敗測試**

Create `ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class RelationGoalFormatterTests
{
    [Fact]
    public void PairsRelationAndGoalInSameOrder()
    {
        var items = new List<(string, string)> { ("妻子", "王小明"), ("門徒", "李大華") };
        var (rel, goal) = RelationGoalFormatter.Format(items);
        rel.Should().Be("妻子、門徒");
        goal.Should().Be("王小明、李大華");
    }

    [Fact]
    public void Deduplicates()
    {
        var items = new List<(string, string)> { ("妻子", "王小明"), ("妻子", "王小明") };
        var (rel, goal) = RelationGoalFormatter.Format(items);
        rel.Should().Be("妻子");
        goal.Should().Be("王小明");
    }

    [Fact]
    public void SkipsEntriesWithBothEmpty()
    {
        var items = new List<(string, string)> { ("", ""), ("母親", "王小明") };
        var (rel, goal) = RelationGoalFormatter.Format(items);
        rel.Should().Be("母親");
        goal.Should().Be("王小明");
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyStrings()
    {
        var (rel, goal) = RelationGoalFormatter.Format(null);
        rel.Should().Be(string.Empty);
        goal.Should().Be(string.Empty);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~RelationGoalFormatterTests`
Expected: FAIL（找不到 `RelationGoalFormatter`）

- [ ] **Step 3: 實作**

Create `ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Services.MemberInfo
{
    public static class RelationGoalFormatter
    {
        public static (string Relations, string Goals) Format(IEnumerable<(string Role, string TargetName)> items)
        {
            if (items == null)
            {
                return (string.Empty, string.Empty);
            }

            var roles = new List<string>();
            var targets = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var role = (item.Role ?? string.Empty).Trim();
                var target = (item.TargetName ?? string.Empty).Trim();
                if (role.Length == 0 && target.Length == 0)
                {
                    continue;
                }

                var key = role + "|" + target;
                if (!seen.Add(key))
                {
                    continue;
                }

                roles.Add(role);
                targets.Add(target);
            }

            return (string.Join("、", roles), string.Join("、", targets));
        }
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~RelationGoalFormatterTests`
Expected: PASS（4 筆）

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs
git commit -m "feat(memberinfo): add RelationGoalFormatter with tests"
```

---

## Task 5: Controller CRM 取數輔助方法（骨架來源）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（新增 private 方法；沿用既有 `ToolUtility`、`GetConnection`、`BuildCurrentContactQuery`、`RetrieveAllContacts`、`TryGetClosedCustomerTypeValue`、`GetListMemberContactId`、`GetShepherdContactIds` 等）

**Interfaces:**
- Produces（private，供 Task 6–8）：
  - `List<SmallGroupDescriptor> FetchSmallGroupDescriptors(IReadOnlyCollection<Guid> onlyListIds)` — `onlyListIds==null` 代表全教會全部；否則限縮。
  - `List<GroupMembershipRow> FetchGroupMemberships(IReadOnlyCollection<Guid> listIds)`
  - `List<string> GetAllCurrentContactIds()`
  - `List<string> GetShepherdListIds()`

- [ ] **Step 1: 新增 `using`**

在 `MemberInfoController.cs` 檔頭補：

```csharp
using ChurchReport.ViewModels.MemberInfoTree;
```

（`ChurchReport.Services.MemberInfo` 已 using。）

- [ ] **Step 2: 新增取數方法**

在 `MemberInfoController` 類別內新增：

```csharp
        /// <summary>取小組名單骨架（listname/區名/區長/小組長）。onlyListIds=null 表全教會全部。</summary>
        private List<SmallGroupDescriptor> FetchSmallGroupDescriptors(IReadOnlyCollection<Guid> onlyListIds)
        {
            var service = ToolUtility.m_Crm2011OrganizationService;
            var query = new QueryExpression("list")
            {
                ColumnSet = new ColumnSet(
                    "listid", "listname", "new_area_name",
                    "new_contact_race_leager_list",
                    "new_contact_family_leader_list",
                    "new_contact_list_arealeader")
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
            query.Criteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
            if (onlyListIds != null)
            {
                if (onlyListIds.Count == 0) { return new List<SmallGroupDescriptor>(); }
                query.Criteria.AddCondition("listid", ConditionOperator.In, onlyListIds.Select(g => (object)g).ToArray());
            }
            query.PageInfo = new PagingInfo { Count = 2000, PageNumber = 1, ReturnTotalRecordCount = false };

            var result = new List<SmallGroupDescriptor>();
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                foreach (var list in page.Entities)
                {
                    var raceRef = list.GetAttributeValue<EntityReference>("new_contact_race_leager_list");
                    var leaderRef = list.GetAttributeValue<EntityReference>("new_contact_family_leader_list");
                    var areaName = ToolUtility.GetEntityStringAttribute(list, "new_area_name");
                    if (string.IsNullOrWhiteSpace(areaName))
                    {
                        var areaRef = list.GetAttributeValue<EntityReference>("new_contact_list_arealeader");
                        if (!string.IsNullOrWhiteSpace(areaRef?.Name)) { areaName = areaRef.Name + "牧區"; }
                    }
                    result.Add(new SmallGroupDescriptor
                    {
                        ListId = list.Id.ToString(),
                        GroupName = ToolUtility.GetEntityStringAttribute(list, "listname"),
                        AreaName = areaName,
                        RaceLeaderName = raceRef?.Name ?? string.Empty,
                        RaceLeaderKey = raceRef?.Id.ToString() ?? string.Empty,
                        LeaderName = leaderRef?.Name ?? string.Empty
                    });
                }
                if (!page.MoreRecords) { break; }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            return result;
        }

        /// <summary>取這些名單的成員關聯，並帶回每位 contact 的在籍狀態(IsCurrent)。</summary>
        private List<GroupMembershipRow> FetchGroupMemberships(IReadOnlyCollection<Guid> listIds)
        {
            var rows = new List<GroupMembershipRow>();
            if (listIds == null || listIds.Count == 0) { return rows; }

            var service = ToolUtility.m_Crm2011OrganizationService;

            // 1) listmember：listid + entityid(contactId)
            var memberships = new List<(Guid ListId, Guid ContactId)>();
            var contactIds = new HashSet<Guid>();
            var mq = new QueryExpression("listmember")
            {
                ColumnSet = new ColumnSet("listid", "entityid")
            };
            mq.Criteria.AddCondition("listid", ConditionOperator.In, listIds.Select(g => (object)g).ToArray());
            mq.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1, ReturnTotalRecordCount = false };
            while (true)
            {
                var page = service.RetrieveMultiple(mq);
                foreach (var lm in page.Entities)
                {
                    var cid = GetListMemberContactId(lm);
                    var listRef = lm.GetAttributeValue<EntityReference>("listid");
                    if (cid == Guid.Empty || listRef == null) { continue; }
                    memberships.Add((listRef.Id, cid));
                    contactIds.Add(cid);
                }
                if (!page.MoreRecords) { break; }
                mq.PageInfo.PageNumber++;
                mq.PageInfo.PagingCookie = page.PagingCookie;
            }
            if (memberships.Count == 0) { return rows; }

            // 2) 一次撈 contact 在籍判斷欄位
            var current = new HashSet<Guid>();
            var closed = TryGetClosedCustomerTypeValue();
            const int batch = 500;
            var idList = contactIds.ToList();
            for (var i = 0; i < idList.Count; i += batch)
            {
                var chunk = idList.GetRange(i, Math.Min(batch, idList.Count - i));
                var cq = new QueryExpression("contact") { ColumnSet = new ColumnSet("contactid", "statecode", "customertypecode") };
                cq.Criteria.AddCondition("contactid", ConditionOperator.In, chunk.Select(g => (object)g).ToArray());
                foreach (var c in service.RetrieveMultiple(cq).Entities)
                {
                    var state = c.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? -1;
                    var type = c.GetAttributeValue<OptionSetValue>("customertypecode")?.Value;
                    var isCurrent = state == 0 && (!closed.HasValue || type != closed.Value);
                    if (isCurrent) { current.Add(c.Id); }
                }
            }

            foreach (var m in memberships)
            {
                rows.Add(new GroupMembershipRow
                {
                    ListId = m.ListId.ToString(),
                    ContactId = m.ContactId.ToString(),
                    IsCurrent = current.Contains(m.ContactId)
                });
            }
            return rows;
        }

        /// <summary>全教會所有「在籍且非結案」的 contactId（供無小組計算）。</summary>
        private List<string> GetAllCurrentContactIds()
        {
            var service = ToolUtility.m_Crm2011OrganizationService;
            var query = BuildCurrentContactQuery(new ColumnSet("contactid"), null, false);
            return RetrieveAllContacts(service, query).Select(c => c.Id.ToString()).ToList();
        }

        /// <summary>登入者所帶名單的 listId（牧養名單範圍）。</summary>
        private List<string> GetShepherdListIds()
        {
            EnsureShepherdListsLoaded();
            var records = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (records == null) { return new List<string>(); }
            return records
                .Where(r => Guid.TryParse(r.ListEntityId, out _))
                .Select(r => r.ListEntityId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
```

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat(memberinfo): add CRM fetch helpers for district tree skeleton"
```

---

## Task 6: `LoadDistrictTree` 動作（骨架，含快取）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`

**Interfaces:**
- Consumes: Task 3 `DistrictTreeBuilder`、Task 5 取數方法。
- Produces: `GET /MemberInfo/LoadDistrictTree` → JSON `DistrictTreeViewModel`。

- [ ] **Step 1: 新增動作**

```csharp
        [HttpGet]
        public IActionResult LoadDistrictTree()
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();

                if (access == MemberInfoAccess.Church)
                {
                    var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                    const string cacheKey = "member-info-tree:church";
                    if (memoryCache != null && memoryCache.TryGetValue(cacheKey, out DistrictTreeViewModel cached) && cached != null)
                    {
                        return Json(cached);
                    }

                    var descriptors = FetchSmallGroupDescriptors(null);
                    var listIds = descriptors.Where(d => Guid.TryParse(d.ListId, out _)).Select(d => Guid.Parse(d.ListId)).ToList();
                    var memberships = FetchGroupMemberships(listIds);
                    var allCurrent = GetAllCurrentContactIds();
                    var tree = DistrictTreeBuilder.Build(descriptors, memberships, allCurrent, includeUngrouped: true, scope: "church");

                    memoryCache?.Set(cacheKey, tree, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
                        SlidingExpiration = TimeSpan.FromMinutes(1),
                        Size = Math.Max(1, tree.Districts.Count)
                    });
                    return Json(tree);
                }

                if (access == MemberInfoAccess.ShepherdList)
                {
                    // 使用者專屬 → 不進共用快取
                    var listIds = GetShepherdListIds().Where(s => Guid.TryParse(s, out _)).Select(Guid.Parse).ToList();
                    var descriptors = FetchSmallGroupDescriptors(listIds);
                    var memberships = FetchGroupMemberships(listIds);
                    var tree = DistrictTreeBuilder.Build(descriptors, memberships, new List<string>(), includeUngrouped: false, scope: "shepherd");
                    return Json(tree);
                }

                return Forbid();
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadDistrictTree");
            }
        }
```

- [ ] **Step 2: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: 手動驗證（發佈後）**

於瀏覽器登入後開 DevTools Console 執行：
```js
fetch('/MemberInfo/LoadDistrictTree').then(r=>r.json()).then(console.log)
```
Expected：回傳 `{ districts:[...], hasUngrouped, ungroupedCount, scope }`；全教會 `scope="church"` 且各 district 有 `areaName/raceLeaderName/memberCount/groups[]`，group 有 `groupName/leaderName/memberCount`。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat(memberinfo): add LoadDistrictTree skeleton endpoint (cached for church)"
```

---

## Task 7: `LoadGroupMembers` 動作（第三層，逐組載入）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`

**Interfaces:**
- Consumes: `MemberInfoScopeGuard.IsListAllowed`（Task 1）、`RelationGoalFormatter`（Task 4）、`GroupMemberRowViewModel`（Task 2）、既有 `CanViewContactsBatch`、`CreateMembershipStatusResolver`、`GetSharedOptionSetService`。
- Produces: `GET /MemberInfo/LoadGroupMembers?listId=&search=` → JSON `{ data: GroupMemberRowViewModel[] }`；private `Dictionary<Guid,(string,string)> BatchRelationGoals(IReadOnlyCollection<Guid>)`；private `List<GroupMemberRowViewModel> BuildMemberRows(IReadOnlyCollection<Guid> contactIds, string search)`。

- [ ] **Step 1: 新增批次關係/目標與成員列建構**

```csharp
        /// <summary>一次撈一批 contact 的關係/目標(connection)，回傳 contactId → (關係, 目標) 兩欄字串。</summary>
        private Dictionary<Guid, (string Relation, string Goal)> BatchRelationGoals(IReadOnlyCollection<Guid> contactIds)
        {
            var result = new Dictionary<Guid, (string, string)>();
            if (contactIds == null || contactIds.Count == 0) { return result; }

            var perContact = contactIds.ToDictionary(id => id, _ => new List<(string, string)>());
            try
            {
                var ids = contactIds.Select(g => (object)g).ToArray();
                var query = new QueryExpression("connection")
                {
                    ColumnSet = new ColumnSet("record1id", "record2id", "record1roleid", "record2roleid")
                };
                query.Criteria.FilterOperator = LogicalOperator.Or;
                query.Criteria.AddCondition("record1id", ConditionOperator.In, ids);
                query.Criteria.AddCondition("record2id", ConditionOperator.In, ids);

                foreach (var conn in ToolUtility.m_Crm2011OrganizationService.RetrieveMultiple(query).Entities)
                {
                    var r1 = conn.GetAttributeValue<EntityReference>("record1id");
                    var r2 = conn.GetAttributeValue<EntityReference>("record2id");
                    AddConnectionSide(perContact, r1, r2, conn.GetAttributeValue<EntityReference>("record2roleid"));
                    AddConnectionSide(perContact, r2, r1, conn.GetAttributeValue<EntityReference>("record1roleid"));
                }
            }
            catch
            {
                // connection 停用/不可讀時：關係/目標留空，不讓整組載入壞掉。
            }

            foreach (var kv in perContact)
            {
                result[kv.Key] = RelationGoalFormatter.Format(kv.Value.Select(x => (x.Item1, x.Item2)));
            }
            return result;
        }

        private static void AddConnectionSide(
            Dictionary<Guid, List<(string, string)>> map,
            EntityReference self, EntityReference target, EntityReference targetRole)
        {
            if (self == null || !map.TryGetValue(self.Id, out var list)) { return; }
            var role = targetRole?.Name ?? string.Empty;
            var name = target?.Name ?? string.Empty;
            if (role.Length == 0 && name.Length == 0) { return; }
            list.Add((role, name));
        }

        /// <summary>依 contactId 集合建構成員列（在籍、10 欄）。search 非空時過濾姓名/手機/會員身分。</summary>
        private List<GroupMemberRowViewModel> BuildMemberRows(IReadOnlyCollection<Guid> contactIds, string search)
        {
            var rows = new List<GroupMemberRowViewModel>();
            if (contactIds == null || contactIds.Count == 0) { return rows; }

            var service = ToolUtility.m_Crm2011OrganizationService;
            var resolveMembership = CreateMembershipStatusResolver();
            var optionSvc = GetSharedOptionSetService();
            var relationMap = BatchRelationGoals(contactIds);
            var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

            const int batch = 500;
            var idList = contactIds.ToList();
            for (var i = 0; i < idList.Count; i += batch)
            {
                var chunk = idList.GetRange(i, Math.Min(batch, idList.Count - i));
                var q = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet(
                        "contactid", "fullname", "gendercode", "birthdate",
                        "mobilephone", "new_spiriitual_identity", "address2_line1",
                        "customertypecode", "statecode")
                };
                q.Criteria.AddCondition("contactid", ConditionOperator.In, chunk.Select(g => (object)g).ToArray());

                foreach (var contact in service.RetrieveMultiple(q).Entities)
                {
                    if (!IsCurrentContactEntity(contact)) { continue; }

                    var membership = resolveMembership(contact);
                    var fullName = ToolUtility.GetEntityStringAttribute(contact, "fullname");
                    var phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone");
                    if (term != null &&
                        (fullName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) &&
                        (phone?.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) &&
                        (membership?.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    relationMap.TryGetValue(contact.Id, out var rg);
                    var genderVal = contact.GetAttributeValue<OptionSetValue>("gendercode")?.Value;
                    var birth = contact.GetAttributeValue<DateTime?>("birthdate");

                    rows.Add(new GroupMemberRowViewModel
                    {
                        ContactId = contact.Id.ToString(),
                        FullName = fullName,
                        Gender = genderVal.HasValue ? optionSvc.GetOptionSetText("contact", "gendercode", genderVal.Value) : string.Empty,
                        BirthDate = (birth.HasValue && birth.Value.Year > 1) ? birth : (DateTime?)null,
                        Phone = phone,
                        SpiritualIdentity = GetOptionSetText(contact, "new_spiriitual_identity"),
                        Address = ToolUtility.GetEntityStringAttribute(contact, "address2_line1"),
                        MembershipStatus = membership,
                        Relation = rg.Relation ?? string.Empty,
                        Goal = rg.Goal ?? string.Empty
                    });
                }
            }

            return rows.OrderBy(r => r.FullName, StringComparer.Ordinal).ToList();
        }
```

- [ ] **Step 2: 新增動作**

```csharp
        [HttpGet]
        public IActionResult LoadGroupMembers(string listId, string search)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                var allowedListIds = access == MemberInfoAccess.Church ? null : (IReadOnlyCollection<string>)GetShepherdListIds();

                if (!MemberInfoScopeGuard.IsListAllowed(access, allowedListIds, listId) || !Guid.TryParse(listId, out var listGuid))
                {
                    return Forbid();
                }

                // 該名單成員 → 逐一 CanViewContactsBatch 把關
                var memberships = FetchGroupMemberships(new[] { listGuid });
                var contactIds = memberships
                    .Where(m => Guid.TryParse(m.ContactId, out _))
                    .Select(m => Guid.Parse(m.ContactId))
                    .Distinct().ToList();
                var allowed = CanViewContactsBatch(contactIds);

                var rows = BuildMemberRows(allowed, search);
                return Json(new { data = rows });
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadGroupMembers");
            }
        }
```

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: 手動驗證（發佈後）**

取 Task 6 回傳的任一 `listId`，Console 執行：
```js
fetch('/MemberInfo/LoadGroupMembers?listId=<貼上GUID>').then(r=>r.json()).then(console.log)
```
Expected：`{ data:[{ContactId,FullName,Gender,BirthDate,Phone,SpiritualIdentity,Address,MembershipStatus,Relation,Goal}, ...] }`；不含頭像 bytes。無權者以 403 回應。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat(memberinfo): add LoadGroupMembers endpoint with batched relation/goal"
```

---

## Task 8: `LoadUngroupedMembers` 動作（無小組，分頁）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`

**Interfaces:**
- Consumes: Task 5/7 方法、既有 `DataSourceLoadOptions`。
- Produces: `GET /MemberInfo/LoadUngroupedMembers`（DevExtreme store）→ `{ data, totalCount }`。private `List<Guid> GetUngroupedContactIds()`（含短快取）。

- [ ] **Step 1: 新增無小組 id 計算（快取）與動作**

```csharp
        /// <summary>全教會：在籍且不在任何小組名單的 contactId（短快取，僅 id，非個資）。</summary>
        private List<Guid> GetUngroupedContactIds()
        {
            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
            const string cacheKey = "member-info-tree:ungrouped-ids:church";
            if (memoryCache != null && memoryCache.TryGetValue(cacheKey, out List<Guid> cached) && cached != null)
            {
                return cached;
            }

            var descriptors = FetchSmallGroupDescriptors(null);
            var listIds = descriptors.Where(d => Guid.TryParse(d.ListId, out _)).Select(d => Guid.Parse(d.ListId)).ToList();
            var grouped = new HashSet<string>(
                FetchGroupMemberships(listIds).Where(m => m.IsCurrent).Select(m => m.ContactId),
                StringComparer.OrdinalIgnoreCase);

            var ungrouped = GetAllCurrentContactIds()
                .Where(id => !grouped.Contains(id))
                .Where(id => Guid.TryParse(id, out _))
                .Select(Guid.Parse)
                .ToList();

            memoryCache?.Set(cacheKey, ungrouped, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
                SlidingExpiration = TimeSpan.FromMinutes(1),
                Size = Math.Max(1, ungrouped.Count)
            });
            return ungrouped;
        }

        [HttpGet]
        public object LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)
        {
            try
            {
                EnsureCorrectUserData();
                if (GetAccess() != MemberInfoAccess.Church)
                {
                    return DataSourceLoader.Load(new List<GroupMemberRowViewModel>(), loadOptions);
                }

                var ids = GetUngroupedContactIds();
                var allowed = CanViewContactsBatch(ids);              // 再把關
                var rows = BuildMemberRows(allowed, search);          // 已排序、含 10 欄
                return DataSourceLoader.Load(rows, loadOptions);      // 記憶體分頁
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadUngroupedMembers");
            }
        }
```

> 註：`BuildMemberRows` 會為每頁載入全部無小組成員的欄位。若日後無小組達數千人造成延遲，再優化為「先分頁 id、只查該頁欄位」；目前教會規模可接受，先求正確與一致。

- [ ] **Step 2: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: 手動驗證（發佈後，全教會帳號）**

```js
fetch('/MemberInfo/LoadUngroupedMembers?take=50&skip=0&requireTotalCount=true').then(r=>r.json()).then(console.log)
```
Expected：`{ data:[...50 筆...], totalCount:N }`；成員皆無小組、在籍。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat(memberinfo): add paged LoadUngroupedMembers endpoint"
```

---

## Task 9: `Index` 切到樹狀視圖並帶旗標

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（`Index` 動作）

- [ ] **Step 1: 調整 `Index` 回傳的 View 名稱與旗標**

將 `Index()` 內 `return View("MemberInfoGrid");` 保留（沿用同一 view 檔名，Task 10 會改寫其內容）。確認 `ViewBag.MemberInfoCanResync`（既有）仍設定。新增一個給前端判斷範圍的旗標：

```csharp
                ViewBag.MemberInfoScope = access; // "church" or "shepherd"
```

（放在 `ViewBag.MemberInfoCanResync = ...` 之後、`return View(...)` 之前。）

- [ ] **Step 2: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat(memberinfo): expose access scope flag to member info view"
```

---

## Task 10: 改寫視圖 — 版面/樣式/工具列/樹容器

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

**做法：** 保留檔案中既有、將被重用的 JS 區塊（**頭像批次**：`memberInfo*Avatar*`、`memberInfoPreloadImages`、`memberInfoScheduleImagePreload`、`memberInfoAvatarCellTemplate`、`memberInfoNameCellTemplate`、`memberInfoSettleAvatar`、`memberInfoStopAvatarSpinner`；**細節彈窗**：`openMemberInfoDetailPopup` 及其相關 popup/scroll 函式、以及頁面底部的 `@Html.DevExtreme().Popup().ID("memberInfoDetailPopup")…`）。移除：平面 `DataGrid`（`MemberInfoGridContainer`）、`memberInfoTogglePhotoFilter`、`memberInfoGridToolbarPreparing` 中的「顯示照片」按鈕。另：`memberInfoResyncLineProfiles` 內原本把進度列插在 `#MemberInfoGridContainer` 前，該容器已移除，請把進度列改用工具列的 `#miResyncStatus`（`document.getElementById('miResyncStatus')`）。

- [ ] **Step 1: 置換頁面主體 HTML**

把 `<div class="member-info-page">…</div>` 內、原本 grid-shell 那段（`@(Html.DevExtreme().DataGrid<MemberInfoListRowViewModel>()…)`）整段換成：

```html
<div class="member-info-page">
    <div class="member-info-toolbar">
        <h4 class="member-info-title">會友資訊</h4>
        <div class="member-info-access">@ViewBag.MemberInfoAccess</div>
    </div>

    <div class="mi-tree-actions">
        <input id="miTreeSearch" class="mi-search" type="search" placeholder="搜尋姓名、手機或會員身分" />
        <button id="miResyncBtn" type="button" class="mi-btn mi-btn-resync" style="display:none;">重新同步LINE</button>
    </div>

    <div id="miResyncStatus" class="mi-resync-status"></div>

    <div id="memberInfoTree" class="mi-tree" aria-live="polite"></div>
    <div id="miTreePager" class="mi-pager"></div>
</div>
```

- [ ] **Step 2: 加入樹狀 CSS**（附在既有 `<style>` 內末端）

```css
    .mi-tree-actions { display:flex; gap:8px; align-items:center; margin:6px 0 10px; flex-wrap:wrap; }
    .mi-search { flex:1 1 240px; min-width:200px; height:38px; font-size:16px; padding:0 12px;
        border:1px solid #cbd5e1; border-radius:8px; }
    .mi-btn { height:38px; border:none; border-radius:8px; padding:0 14px; font-weight:700; color:#fff; cursor:pointer; }
    .mi-btn-resync { background:#0ea5e9; }
    .mi-resync-status { margin:4px 0; font-weight:700; color:#2563eb; }

    .mi-tree { display:flex; flex-direction:column; gap:10px; }
    .mi-district { background:#fff; border:1px solid #d9e2ec; border-radius:10px; overflow:hidden;
        box-shadow:0 6px 14px rgba(15,23,42,.06); }
    /* 整列可點的大觸控區 */
    .mi-district-header, .mi-group-header {
        display:flex; align-items:center; gap:10px; width:100%; cursor:pointer;
        padding:14px 14px; min-height:52px; background:none; border:0; text-align:left; font:inherit; }
    .mi-district-header { background:#eef2ff; font-weight:800; color:#1e293b; font-size:1.02rem; }
    .mi-group-header { background:#f8fafc; border-top:1px solid #eef2f6; }
    .mi-district-header:active, .mi-group-header:active { filter:brightness(0.97); }
    .mi-chevron { flex:0 0 auto; width:26px; height:26px; display:inline-flex; align-items:center;
        justify-content:center; font-size:20px; color:#475569; transition:transform .15s ease; }
    .mi-open > .mi-district-header .mi-chevron, .mi-open > .mi-group-header .mi-chevron { transform:rotate(90deg); }
    .mi-title { flex:1 1 auto; }
    .mi-sub { color:#64748b; font-weight:600; margin-left:8px; font-size:.92rem; }
    .mi-count { flex:0 0 auto; color:#334155; font-weight:700; background:#e2e8f0; border-radius:999px; padding:3px 10px; font-size:.85rem; }
    .mi-district-body { display:none; }
    .mi-district.mi-open > .mi-district-body { display:block; }
    .mi-group { border-top:1px solid #eef2f6; }
    .mi-group-body { display:none; padding:8px 10px 12px; }
    .mi-group.mi-open > .mi-group-body { display:block; }
    .mi-grid-host { min-height:40px; }
    .mi-pager { display:flex; gap:8px; justify-content:center; align-items:center; margin:14px 0; }
    .mi-pager button { height:34px; padding:0 12px; border:1px solid #cbd5e1; border-radius:8px; background:#fff; cursor:pointer; }
    .mi-pager button[disabled] { opacity:.5; cursor:default; }
    @@media (max-width:640px){ .mi-district-header,.mi-group-header{ min-height:56px; } }
```

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded（Razor 編譯通過）

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat(memberinfo): tree view scaffold, styles, toolbar (drop photo filter)"
```

---

## Task 11: 視圖 — 載入骨架、渲染區/組手風琴、預設展開、整列可點

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`（`<script>` 區）

**Interfaces:**
- Consumes: `GET /MemberInfo/LoadDistrictTree`
- Produces（JS 全域函式，供 Task 12–14）：`miState`、`miRenderTree(tree)`、`miToggleDistrict(el)`、`miToggleGroup(el)`、`miCountGroups(tree)`。

- [ ] **Step 1: 加入樹渲染腳本**（新增於 `<script>` 內，置於重用的頭像/彈窗函式之外）

```javascript
    var miState = { tree: null, filtered: null, page: 0, pageSize: 50, search: '' };
    var miScope = '@(ViewBag.MemberInfoScope ?? "")';

    function miEl(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html != null) e.innerHTML = html;
        return e;
    }
    function miEsc(s) { return (s == null ? '' : String(s)).replace(/[&<>"]/g, function (c) {
        return ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' })[c]; }); }

    function miCountGroups(tree) {
        var n = 0; (tree.Districts || []).forEach(function (d) { n += (d.Groups || []).length; });
        return n;
    }

    // 依「每頁 50 個小組」把 districts 切頁（跨頁時 district 標題會重覆出現）
    function miPageSlice(tree) {
        var start = miState.page * miState.pageSize, end = start + miState.pageSize;
        var out = [], seen = 0;
        (tree.Districts || []).forEach(function (d) {
            var groups = d.Groups || [];
            var take = [];
            for (var i = 0; i < groups.length; i++) {
                if (seen >= start && seen < end) take.push(groups[i]);
                seen++;
            }
            if (take.length) out.push(Object.assign({}, d, { Groups: take }));
        });
        return { districts: out, totalGroups: seen };
    }

    function miDistrictHeader(d) {
        var h = miEl('button', 'mi-district-header');
        h.type = 'button';
        h.innerHTML = '<span class="mi-chevron">▸</span>' +
            '<span class="mi-title">' + miEsc(d.AreaName) +
            '<span class="mi-sub">區長：' + miEsc(d.RaceLeaderName) + '</span></span>' +
            '<span class="mi-count">本區 ' + (d.MemberCount || 0) + ' 人</span>';
        h.addEventListener('click', function () { miToggleDistrict(h.parentNode); });
        return h;
    }
    function miGroupHeader(g) {
        var h = miEl('button', 'mi-group-header');
        h.type = 'button';
        h.innerHTML = '<span class="mi-chevron">▸</span>' +
            '<span class="mi-title">' + miEsc(g.GroupName) +
            '<span class="mi-sub">小組長：' + miEsc(g.LeaderName) + '</span></span>' +
            '<span class="mi-count">' + (g.MemberCount || 0) + ' 人</span>';
        h.addEventListener('click', function () { miToggleGroup(h.parentNode); });
        return h;
    }

    function miToggleDistrict(districtEl) { districtEl.classList.toggle('mi-open'); }

    function miRenderTree(tree) {
        var host = document.getElementById('memberInfoTree');
        host.innerHTML = '';

        var singleGroup = miCountGroups(tree) === 1 && !tree.HasUngrouped;
        var paged = miPageSlice(tree);

        paged.districts.forEach(function (d) {
            var dEl = miEl('div', 'mi-district mi-open'); // 有小組的區：預設展開
            dEl.appendChild(miDistrictHeader(d));
            var body = miEl('div', 'mi-district-body');
            (d.Groups || []).forEach(function (g) {
                var gEl = miEl('div', 'mi-group');
                gEl.dataset.listId = g.ListId;
                gEl.appendChild(miGroupHeader(g));
                var gBody = miEl('div', 'mi-group-body');
                gBody.appendChild(miEl('div', 'mi-grid-host'));
                gEl.appendChild(gBody);
                body.appendChild(gEl);
                if (singleGroup) { miToggleGroup(gEl); } // 只有一組 → 直接打開
            });
            dEl.appendChild(body);
            host.appendChild(dEl);
        });

        if (tree.hasUngrouped) { host.appendChild(miRenderUngroupedNode(tree.ungroupedCount)); }
        miRenderPager(paged.totalGroups);
    }

    // 由 Task 12/13/14 覆寫/補上：
    function miToggleGroup(groupEl) { groupEl.classList.toggle('mi-open'); }
    function miRenderUngroupedNode(count) { return miEl('div'); }
    function miRenderPager(totalGroups) {}

    function miLoadTree() {
        var host = document.getElementById('memberInfoTree');
        host.innerHTML = '<div style="padding:16px;">載入中...</div>';
        $.ajax({ url: '/MemberInfo/LoadDistrictTree', type: 'GET' })
            .done(function (tree) { miRenderTree(tree || { districts: [] }); })
            .fail(function () { host.innerHTML = '<div style="padding:16px;color:#b91c1c;">載入失敗</div>'; });
    }

    $(function () {
        var rb = document.getElementById('miResyncBtn');
        if (memberInfoCanResync && rb) { rb.style.display = ''; rb.onclick = memberInfoResyncLineProfiles; }
        miLoadTree();
    });
```

- [ ] **Step 2: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: 手動驗證（發佈後）**

開「會友資訊」：所有有小組的區**預設展開**、看得到各組「名稱／小組長／人數」；點區列任一處可收合/展開；無小組節點與分頁此時為占位（下一 Task 補）。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat(memberinfo): render district/group accordion with default-expand"
```

---

## Task 12: 視圖 — 逐組成員表格（第三層，含頭像/姓名/關係目標、單組自動展開）

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`（覆寫 `miToggleGroup`）

**Interfaces:**
- Consumes: `GET /MemberInfo/LoadGroupMembers`、重用 `memberInfoAvatarCellTemplate`、`memberInfoNameCellTemplate`、`memberInfoPreloadImages`。

- [ ] **Step 1: 調整頭像批次選擇器（重要）**

原頭像批次以 `#MemberInfoGridContainer .member-info-avatar-img[...]` 掃描；改為掃整棵樹。將重用區塊中兩處 `$('#MemberInfoGridContainer .member-info-avatar-img[data-contact-id]')` 改為 `$('#memberInfoTree .member-info-avatar-img[data-contact-id]')`。（`memberInfoPreloadImages` 內用 `e.component.getDataSource()` 仍有效；跨多個 grid 時，改為掃描 DOM 佇列即可，`memberInfoScheduleImagePreload` 佇列邏輯不變。）

- [ ] **Step 2: 覆寫 `miToggleGroup` 掛載 DataGrid**

以下版本取代 Task 11 的占位 `miToggleGroup`：

```javascript
    function miBirthText(cell) {
        if (!cell.value) return '';
        var d = new Date(cell.value);
        return d.getFullYear() > 1 ? (d.getFullYear() + '/' + (d.getMonth() + 1) + '/' + d.getDate()) : '';
    }

    function miMemberColumns() {
        return [
            { dataField: 'ContactId', caption: '頭像', width: 58, minWidth: 58, allowSorting: false,
              allowFiltering: false, cellTemplate: memberInfoAvatarCellTemplate },
            { dataField: 'FullName', caption: '姓名', minWidth: 90, cellTemplate: memberInfoNameCellTemplate },
            { dataField: 'Gender', caption: '性別', width: 60, alignment: 'center' },
            { dataField: 'BirthDate', caption: '生日', dataType: 'date', width: 104, alignment: 'center', customizeText: miBirthText },
            { dataField: 'Phone', caption: '手機', width: 120 },
            { dataField: 'SpiritualIdentity', caption: '信仰狀態', width: 100, alignment: 'center' },
            { dataField: 'Address', caption: '地址', minWidth: 160 },
            { dataField: 'MembershipStatus', caption: '會員身份', width: 100, alignment: 'center' },
            { dataField: 'Relation', caption: '關係', width: 110 },
            { dataField: 'Goal', caption: '目標', width: 130 }
        ];
    }

    function miMountMemberGrid(hostEl, rows) {
        $(hostEl).dxDataGrid({
            dataSource: rows,
            keyExpr: 'ContactId',
            showBorders: true, showRowLines: true, showColumnLines: true, rowAlternationEnabled: true,
            columnAutoWidth: true, columnHidingEnabled: true, wordWrapEnabled: true,
            paging: { enabled: false }, scrolling: { showScrollbar: 'onHover' },
            noDataText: '沒有資料',
            columns: miMemberColumns(),
            onContentReady: function (e) { window.memberInfoPreloadImages({ component: e.component }); }
        });
    }

    function miToggleGroup(groupEl) {
        var host = groupEl.querySelector('.mi-grid-host');
        var open = groupEl.classList.toggle('mi-open');
        if (!open || groupEl.dataset.loaded === '1') { return; }

        host.innerHTML = '<div style="padding:10px;">載入中...</div>';
        $.ajax({ url: '/MemberInfo/LoadGroupMembers', type: 'GET',
                 data: { listId: groupEl.dataset.listId, search: miState.search || '' } })
         .done(function (res) {
            host.innerHTML = '';
            groupEl.dataset.loaded = '1';
            miMountMemberGrid(host, (res && res.data) || []);
         })
         .fail(function (xhr) {
            host.innerHTML = '<div style="padding:10px;color:#b91c1c;">' +
                (xhr.status === 403 ? '無權檢視此小組' : '載入失敗') + '</div>';
         });
    }
```

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: 手動驗證（發佈後）**

點任一小組列 → 展開成員表（10 欄），頭像陸續補上、姓名可點開細節彈窗；再點一次收合。以只帶一組的小組長帳號登入 → 該組**一進來就展開**顯示成員。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat(memberinfo): lazy per-group member grid with avatar batch + detail popup"
```

---

## Task 13: 視圖 — 無小組節點（直接成員表、分頁）

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`（覆寫 `miRenderUngroupedNode`）

- [ ] **Step 1: 覆寫 `miRenderUngroupedNode`**

```javascript
    function miRenderUngroupedNode(count) {
        var dEl = miEl('div', 'mi-district'); // 無小組：預設收合（不加 mi-open）
        var h = miEl('button', 'mi-district-header');
        h.type = 'button';
        h.innerHTML = '<span class="mi-chevron">▸</span>' +
            '<span class="mi-title">無小組</span>' +
            '<span class="mi-count">' + (count || 0) + ' 人</span>';
        var body = miEl('div', 'mi-district-body');
        var gridHost = miEl('div', 'mi-grid-host');
        gridHost.style.padding = '8px 10px 12px';
        body.appendChild(gridHost);
        dEl.appendChild(h);
        dEl.appendChild(body);

        h.addEventListener('click', function () {
            var open = dEl.classList.toggle('mi-open');
            if (open && dEl.dataset.loaded !== '1') {
                dEl.dataset.loaded = '1';
                miMountUngroupedGrid(gridHost);
            }
        });
        return dEl;
    }

    function miMountUngroupedGrid(hostEl) {
        var store = DevExpress.data.AspNet.createStore({
            key: 'ContactId',
            loadUrl: '/MemberInfo/LoadUngroupedMembers',
            loadParams: { search: miState.search || '' }
        });
        $(hostEl).dxDataGrid({
            dataSource: store,
            remoteOperations: { paging: true },
            showBorders: true, showRowLines: true, showColumnLines: true, rowAlternationEnabled: true,
            columnAutoWidth: true, columnHidingEnabled: true, wordWrapEnabled: true,
            paging: { pageSize: 50 },
            pager: { showPageSizeSelector: true, allowedPageSizes: [25, 50, 100], showInfo: true, visible: true },
            noDataText: '沒有資料',
            columns: miMemberColumns(),
            onContentReady: function (e) { window.memberInfoPreloadImages({ component: e.component }); }
        });
    }
```

- [ ] **Step 2: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: 手動驗證（全教會帳號）**

捲到底 → 「無小組（N 人）」預設收合；點開 → 直接顯示成員表（同 10 欄），底部分頁可換頁；成員皆無小組。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat(memberinfo): ungrouped node with paged member grid"
```

---

## Task 14: 視圖 — 搜尋（過濾樹）＋ 小組分頁（50/頁）

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

**Interfaces:**
- Consumes: `GET /MemberInfo/LoadDistrictTree`（前端過濾）；沿用 `LoadGroupMembers?search=`。

- [ ] **Step 1: 覆寫 `miRenderPager` 實作 50/頁**

```javascript
    function miRenderPager(totalGroups) {
        var pager = document.getElementById('miTreePager');
        pager.innerHTML = '';
        var pages = Math.max(1, Math.ceil(totalGroups / miState.pageSize));
        if (pages <= 1) return;

        var prev = miEl('button', null, '上一頁'); prev.disabled = miState.page <= 0;
        prev.onclick = function () { miState.page--; miRenderCurrent(); };
        var info = miEl('span', null, '第 ' + (miState.page + 1) + ' / ' + pages + ' 頁（共 ' + totalGroups + ' 組）');
        var next = miEl('button', null, '下一頁'); next.disabled = miState.page >= pages - 1;
        next.onclick = function () { miState.page++; miRenderCurrent(); };

        pager.appendChild(prev); pager.appendChild(info); pager.appendChild(next);
    }

    // 依目前 search 決定要渲染的樹（原樹或過濾後的樹）
    function miRenderCurrent() {
        var base = miState.filtered || miState.tree;
        miRenderTree(base);
    }
```

同時把 Task 11 的 `miRenderTree` 開頭 `miState.tree = tree;` 改為：只有在渲染「原始樹」時才覆寫 `miState.tree`（避免搜尋過濾樹蓋掉原樹）。做法：新增參數旗標——把 `miLoadTree().done` 內改成 `miState.tree = tree; miState.filtered = null; miState.page = 0; miRenderTree(tree);`，並將 `miRenderTree` 內第一行 `miState.tree = tree;` 刪除。

- [ ] **Step 2: 加入搜尋（前端過濾樹 + 自動展開命中組）**

```javascript
    function miFilterTree(term) {
        var t = (term || '').trim().toLowerCase();
        if (!t || !miState.tree) { miState.filtered = null; return; }
        var districts = [];
        (miState.tree.districts || []).forEach(function (d) {
            var groups = (d.groups || []).filter(function (g) {
                return (g.groupName || '').toLowerCase().indexOf(t) >= 0 ||
                       (g.leaderName || '').toLowerCase().indexOf(t) >= 0;
            });
            // 區長/區名命中 → 整區保留
            var hitDistrict = (d.areaName || '').toLowerCase().indexOf(t) >= 0 ||
                              (d.raceLeaderName || '').toLowerCase().indexOf(t) >= 0;
            var keep = hitDistrict ? (d.groups || []) : groups;
            if (keep.length) districts.push(Object.assign({}, d, { groups: keep }));
        });
        miState.filtered = { districts: districts, hasUngrouped: false, ungroupedCount: 0, scope: miState.tree.scope };
    }

    // 搜尋輸入：以組名/區名/小組長就地過濾；成員層級的姓名/手機由 LoadGroupMembers?search= 過濾
    (function wireSearch() {
        var box = document.getElementById('miTreeSearch');
        if (!box) return;
        var timer = null;
        box.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(function () {
                miState.search = box.value || '';
                miState.page = 0;
                miFilterTree(miState.search);
                miRenderCurrent();
                // 有搜尋字時，展開所有命中組並帶 search 載入其成員
                if ((miState.search || '').trim()) {
                    document.querySelectorAll('#memberInfoTree .mi-group').forEach(function (gEl) {
                        if (!gEl.classList.contains('mi-open')) miToggleGroup(gEl);
                    });
                }
            }, 250);
        });
    })();
```

> 說明：`miMemberColumns`/`miMountMemberGrid` 用 `miState.search` 帶入 `LoadGroupMembers`，故命中組展開後只顯示符合成員。清空搜尋 → `miFilterTree` 清 `filtered`、`miRenderCurrent` 回到原樹（預設展開、成員收合）。

- [ ] **Step 3: 驗證編譯**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: 手動驗證（發佈後）**

(a) 打字搜尋姓名 → 只留下含符合成員的小組並自動展開、只顯示符合的人；清空 → 回完整樹。
(b) 造出 >50 組的情境（全教會）→ 底部出現「上一頁／下一頁」，每頁最多 50 組，跨頁區標題重覆。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat(memberinfo): tree search filter and 50-groups-per-page paging"
```

---

## Task 15: 整合驗證與收尾

**Files:**
- Verify only（必要時小修）

- [ ] **Step 1: 全測試綠燈**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
Expected: PASS（Task 1/3/4 全部）

- [ ] **Step 2: 整體建置**

Run: `dotnet build ChurchReport.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 3: 端到端手動驗收（發佈＋重啟 app pool 後）**

逐項確認：
- 全教會帳號：所有有小組的區預設展開見小組名稱；點小組展開成員表(10 欄)、頭像/姓名可點；無小組預設收合、點開分頁；搜尋可過濾；>50 組時分頁。
- 牧養名單帳號：只見自己帶的區/組；只帶一組者一進來直接展開該組成員；無「無小組」節點。
- 無「顯示照片」按鈕；「重新同步LINE」僅全教會可見且可用。
- 手機上整列可點、好按。

- [ ] **Step 4: 移除殘留**

確認舊平面 grid 的 `MemberInfoListRowViewModel` 若已無其他引用可保留（`LoadMemberInfoList` 仍可留作備援或一併移除——若移除，連同 `LoadChurchMemberRows*`/`LoadShepherdMemberRows` 一起清）。本計畫預設**保留**舊端點不動以降低風險，僅前端不再使用。

- [ ] **Step 5: Commit（如有小修）**

```bash
git add -A
git commit -m "test(memberinfo): end-to-end verification fixes for district tree"
```

---

## Self-Review 對照（規格覆蓋）

- 三層（區長→小組→會友）：Task 3/6（骨架）＋ 11（區/組渲染）＋ 12（成員）✓
- 第一層 區名＋區長＋人數：Task 5（AreaName/RaceLeader 取值＋空白 fallback）＋ 3（人數去重）＋ 11（顯示）✓
- 第二層 小組名＋小組長＋人數：Task 5/3/11 ✓
- 第三層 10 欄（頭像/姓名/性別/生日/手機/信仰狀態/地址/會員身份/關係/目標）：Task 7（欄位）＋ 12（欄定義）✓
- 點小組才載入、重用頭像批次：Task 12 ✓
- 無小組（收合、點開直接成員、分頁）：Task 8 ＋ 13 ✓
- 預設展開規則（區展開／組收合／單組自動開）：Task 11/12 ✓
- 整列可點＋大三角：Task 10（CSS）＋ 11（handler）✓
- 50 組/頁分頁：Task 14 ✓
- 搜尋（保留、過濾樹）：Task 14 ＋ 7（成員層 search）✓
- 移除顯示照片、保留重新同步LINE：Task 10/11 ✓
- 全教會＋牧養名單兩範圍：Task 6/7/9 ✓
- 權限（IsListAllowed＋CanViewContactsBatch＋不快取個資）：Task 1/6/7/8 ✓
