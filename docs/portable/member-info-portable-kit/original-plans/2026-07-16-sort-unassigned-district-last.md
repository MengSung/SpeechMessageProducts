# Sort Unassigned District Last Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將會友資訊中的「區長未填」區域排在所有已填區長之後，並維持「無小組」為最後節點。

**Architecture:** 只調整 `DistrictTreeBuilder` 產生 `Districts` 時的第一排序鍵；前端原本就會在所有 `Districts` 之後附加 `Ungrouped`。不修改 ViewModel、Razor、JavaScript、會員計數或權限流程。

**Tech Stack:** C# 14、.NET 10、LINQ、xUnit、FluentAssertions

---

### Task 1: 區長未填排序

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- Modify: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs:108-113`

- [x] **Step 1: 新增會失敗的排序測試**

在 `DistrictTreeBuilderTests` 加入：

```csharp
[Fact]
public void Build_SortsUnknownRaceLeaderAfterAssignedDistrictsAndBeforeUngrouped()
{
    var groups = new[]
    {
        Group("L0", "", "", "A牧區", "未填區長組"),
        Group("L1", "R1", "區長甲", "B牧區", "張區"),
        Group("L2", "R2", "區長乙", "Z牧區", "黃區")
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
```

- [x] **Step 2: 執行單一測試並確認 RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~Build_SortsUnknownRaceLeaderAfterAssignedDistrictsAndBeforeUngrouped"
```

Expected: FAIL；現行排序會把 `A牧區` 的「區長未填」排在第一筆。

- [x] **Step 3: 加入最小排序鍵**

在 `DistrictTreeBuilder.Build` 的 `Districts` 排序最前方加入：

```csharp
.OrderBy(
    district => string.Equals(
        district.RaceLeaderName,
        UnknownRaceLeaderName,
        StringComparison.Ordinal)
        ? 1
        : 0)
.ThenBy(district => district.AreaName, StringComparer.Ordinal)
```

後續 `RaceLeaderName` 與 `RaceLeaderKey` 排序維持原樣。

- [x] **Step 4: 執行單一測試並確認 GREEN**

使用 Step 2 相同命令。

Expected: PASS。

- [x] **Step 5: 執行完整驗證**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
dotnet build ChurchReport.sln -c Debug --no-restore --no-incremental --nologo
git diff --check
```

Expected: 所有 MemberInfo 測試通過；Debug build 為 0 warnings、0 errors；`git diff --check` 無錯誤。

- [x] **Step 6: 由 VS 2026 重新啟動並交由使用者驗收**

確認 `<本機連接埠>` 使用目前 Worktree 啟動，請使用者驗證順序：

```text
所有已填區長 → 區長未填 → 無小組
```

依使用者指示不執行 Git Commit。
