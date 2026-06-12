# 會友資訊（Member Info）Implementation Plan — 最終合併版

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（建議）或 superpowers:executing-plans 逐任務實作。步驟用 `- [ ]` 追蹤。
>
> 本檔**取代**先前兩份：`2026-06-11-member-info-feature.md`（Claude 版）與 `2026-06-11-member-info-feature-codex-rewrite.md`（Codex 版）。合併了 Claude 版的逐步可貼上程式碼，與 Codex 版的安全強化（受保護圖片代理、現行連絡人檢查、雙向關係查詢）。

**Goal:** 左側導覽新增「會友資訊」：連絡人網格（照片/姓名/手機/小組），點姓名彈出**唯讀**細節（大頭貼＋手機/地址/信仰狀態/關係目標），可切換「聚會紀錄」「裝備紀錄」兩**只屬於該被點擊連絡人**的子網格；牧師傳道/牧養主任→全教會現行連絡人、小組長→自己名單，其餘不顯示且不得取資料。

**Architecture:** 新增獨立 `MemberInfoController : BaseChurchController`，所有以 `contactId` 為參數的端點（含圖片）一律走伺服器端 `CanViewContact` 範圍把關；純判定邏輯抽成可單元測試的靜態類別；CRM/視圖沿用既有 `ToolUtility`/DevExtreme 慣例。與既有「組員資訊」「小組牧養點名彈窗」並存、不更動。

**Tech Stack:** ASP.NET Core MVC (net10.0) + DevExtreme 21.2.7 + Microsoft Dataverse/Dynamics 365；測試 xUnit + FluentAssertions（net10.0）。

**Spec:** `docs/superpowers/specs/2026-06-11-member-info-feature-design.md`

---

## 工作環境前置說明（先讀）

- **程式碼在主目錄** `…\音訊產品版本\ChurchReport\`，git 分支 `Jesus_5.0.9.8_AddPicture`。本 session 的 worktree 只有 README，不要在 worktree 內實作。
- 建議自 `Jesus_5.0.9.8_AddPicture` 開 `feature/member-info` 分支。
- 建置：`dotnet build "ChurchReport/ChurchReport.csproj"`；測試：`dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`。
- 多數手動驗證需以**真實 CRM 連絡人帳號**登入（不同角色）。

## 既有基礎建設（重要，務必先看再動手）

`Views/Home/_GeneralGroupGrids.cshtml`（小組牧養網格，近期已被修改）已有「網格大頭照＋點擊開彈窗」：
- `contactImageCellTemplate`／`renderContactAvatar`＋`/Personal/GetContactImagesBatch` 批次預載；
- `openMemberDetailPopup(rowData, gridType)`＋DOM id **`#memberDetailPopup`**：**可編輯**的點名/探訪/代禱彈窗。

本功能（唯讀、含地址/信仰狀態/關係目標＋兩子網格）刻意**不同命名以免覆蓋**：彈窗 id＝`#memberInfoDetailPopup`、開窗函式＝`openMemberInfoDetailPopup`、儲存格模板＝`memberInfoAvatarCellTemplate`／`memberInfoNameCellTemplate`、切換＝`memberInfoDetailSwitch`、子網格初始化＝`initMemberInfoSubGrid`。

## 安全基線（本版核心強化，務必落實）

1. **不信任前端 `contactId`**：`Detail`、`LoadContactPresentRecords`、`LoadContactStorLessons`、`GetContactImage`、`GetContactImagesBatch` 全部先過 `CanViewContact`。
2. **照片不直接用 `/Personal/GetContactImage`**（該端點無範圍檢查）：會友資訊改用受保護代理 `/MemberInfo/GetContactImage`、`/MemberInfo/GetContactImagesBatch`。
   - 備註：`/Personal/GetContactImage` 全站既有的「任何登入者可取任意 contact 照片」屬**既有問題**、不在本功能範圍；本功能至少不因此新增暴露面，並可於後續另案收斂（見文末）。
3. **子網格只查該彈窗 contactId**，不因登入者是全教會就回全教會紀錄。
4. **全教會**＝`statecode=0`（啟用）且（可解析時）`customertypecode≠結案`；**牧養名單**＝僅自己名單成員白名單。

---

## File Structure（建立/修改總覽）

**新增**
- `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs` — 存取等級常數。
- `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs` — 職稱+登入型別→存取等級（純）。
- `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs` — 白名單判定（純）。
- `ChurchReport/ViewModels/MemberInfoListRowViewModel.cs` — 網格列。
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs` — 細節＋關係目標。
- `ChurchReport/Controllers/MemberInfoController.cs` — 端點＋scope helper＋受保護圖片代理。
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml` — 網格頁。
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml` — 唯讀細節彈窗（含兩子網格）。
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` 等 — 純邏輯測試。

**修改**
- `ChurchReport/Controllers/BaseChurchController.cs` — 新增 `SetupMemberInfoViewBag()` 並於 `SetupBasicViewBag()` 末尾呼叫。
- `ChurchReport/Views/Shared/_Layout.cshtml` — 一般牧養導覽分支、奉獻前，插一次「會友資訊」。

**CRM 速查**：`contact`：`contactid, fullname, mobilephone, address2_line1, customertypecode, new_spiriitual_identity（拼字如此）, new_church_jobtitle, statecode, entityimage`；`new_present_record`：`new_contact_new_present_record(→contact), new_sunday_present_this_week(int), new_group_present_this_week(int), new_explanation, new_group_date`。已證實可用方法（呼叫點）：`RetrieveMemberListCollectionByListId(listGuid)`、`RetrieveStorLessonsByFetchXml(fullName, contactId)`、`RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)`、`OptionSetMetadataService.GetOptionSetMapping/GetOptionSetText`。

---

## Task 1: 建立純邏輯單元測試專案

**Files:** Create `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`、`ChurchReport.MemberInfo.Tests/SanityTest.cs`

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ChurchReport\ChurchReport.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: SanityTest.cs**

```csharp
using Xunit; using FluentAssertions;
namespace ChurchReport.MemberInfo.Tests;
public class SanityTest { [Fact] public void Sanity() => true.Should().BeTrue(); }
```

- [ ] **Step 3:** `dotnet sln "ChurchReport.sln" add "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
- [ ] **Step 4:** `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"` → 1 passed。
  > 若參考 Web Exe 造成 `Program` 衝突，於測試 csproj 加 `<GenerateProgramFile>false</GenerateProgramFile>`。
- [ ] **Step 5:** `git add ChurchReport.MemberInfo.Tests ChurchReport.sln && git commit -m "test: 建立 MemberInfo 純邏輯測試專案"`

---

## Task 2: `MemberInfoAccessResolver`（TDD）

**Files:** Create `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`、`MemberInfoAccessResolver.cs`；Test `MemberInfoAccessResolverTests.cs`

- [ ] **Step 1: 常數**

```csharp
namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoAccess
    {
        public const string Church = "全教會";
        public const string ShepherdList = "牧養名單";
    }
}
```

- [ ] **Step 2: 失敗測試**

```csharp
using Xunit; using FluentAssertions; using ChurchReport.Services.MemberInfo;
namespace ChurchReport.MemberInfo.Tests;
public class MemberInfoAccessResolverTests
{
    [Theory]
    [InlineData("牧師傳道")] [InlineData("牧養主任")]
    [InlineData("主任牧師、牧養主任")] [InlineData("  牧師傳道  ")]
    public void PastorRole_ReturnsChurch(string j) =>
        MemberInfoAccessResolver.Resolve(j, "小組長").Should().Be(MemberInfoAccess.Church);

    [Fact]
    public void PastorWinsOverShepherd() =>
        MemberInfoAccessResolver.Resolve("牧養主任", "小組長").Should().Be(MemberInfoAccess.Church);

    [Fact]
    public void GroupLeader_ReturnsShepherdList() =>
        MemberInfoAccessResolver.Resolve("核心同工", "小組長").Should().Be(MemberInfoAccess.ShepherdList);

    [Theory]
    [InlineData("", "個人回報")] [InlineData("會計", "個人回報")]
    [InlineData(null, null)] [InlineData("會友", "")]
    public void NoQualifyingRole_ReturnsNull(string? j, string? t) =>
        MemberInfoAccessResolver.Resolve(j, t).Should().BeNull();
}
```

- [ ] **Step 3:** `dotnet test ...` → FAIL（找不到 resolver）。
- [ ] **Step 4: 實作**

```csharp
namespace ChurchReport.Services.MemberInfo
{
    /// <summary>職稱含「牧師傳道」/「牧養主任」→全教會；否則 LoginType=="小組長"→牧養名單；皆非→null。</summary>
    public static class MemberInfoAccessResolver
    {
        public static string? Resolve(string? churchJobTitle, string? loginType)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();
            if (jobTitle.Contains("牧師傳道") || jobTitle.Contains("牧養主任"))
                return MemberInfoAccess.Church;
            if (string.Equals((loginType ?? string.Empty).Trim(), "小組長", System.StringComparison.Ordinal))
                return MemberInfoAccess.ShepherdList;
            return null;
        }
    }
}
```

- [ ] **Step 5:** `dotnet test ...` → PASS。
- [ ] **Step 6:** `git add ChurchReport/Services/MemberInfo ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs && git commit -m "feat: MemberInfoAccessResolver（含測試）"`

---

## Task 3: `MemberInfoScopeGuard`（純白名單，TDD）

**Files:** Create `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`；Test `MemberInfoScopeGuardTests.cs`

> 此為**純**白名單判定，供單元測試與 controller 的 `CanViewContact` 組合使用。`全教會`是否「現行」的 CRM 檢查放在 Task 5 的 `IsCurrentContact`。

- [ ] **Step 1: 失敗測試**

```csharp
using System.Collections.Generic; using Xunit; using FluentAssertions; using ChurchReport.Services.MemberInfo;
namespace ChurchReport.MemberInfo.Tests;
public class MemberInfoScopeGuardTests
{
    private static readonly HashSet<string> Shepherd = new(System.StringComparer.OrdinalIgnoreCase)
    { "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222" };

    [Fact] public void Church_AllowsAnyNonEmpty() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, "99999999-9999-9999-9999-999999999999").Should().BeTrue();
    [Fact] public void Shepherd_AllowsInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd, "22222222-2222-2222-2222-222222222222").Should().BeTrue();
    [Fact] public void Shepherd_DeniesNotInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd, "99999999-9999-9999-9999-999999999999").Should().BeFalse();
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public void DeniesMissingId(string? r) =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, r).Should().BeFalse();
    [Fact] public void DeniesNoAccess() =>
        MemberInfoScopeGuard.IsContactAllowed(null, Shepherd, "11111111-1111-1111-1111-111111111111").Should().BeFalse();
}
```

- [ ] **Step 2:** `dotnet test ... --filter MemberInfoScopeGuardTests` → FAIL。
- [ ] **Step 3: 實作**

```csharp
using System.Collections.Generic;
namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoScopeGuard
    {
        public static bool IsContactAllowed(string? access, IReadOnlyCollection<string> shepherdContactIds, string? requestedContactId)
        {
            if (string.IsNullOrWhiteSpace(requestedContactId)) return false;
            if (access == MemberInfoAccess.Church) return true;
            if (access == MemberInfoAccess.ShepherdList)
            {
                if (shepherdContactIds == null) return false;
                foreach (var id in shepherdContactIds)
                    if (string.Equals(id, requestedContactId, System.StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4:** `dotnet test ...` → PASS。
- [ ] **Step 5:** `git add ... && git commit -m "feat: MemberInfoScopeGuard（含測試）"`

---

## Task 4: 導覽旗標 `SetupMemberInfoViewBag()` 與 `_Layout` 入口

**Files:** Modify `BaseChurchController.cs`（`SetupBasicViewBag` 約 478-487）、`Views/Shared/_Layout.cshtml`（奉獻 `<li>` 前，約 463）

- [ ] **Step 1: BaseChurchController**

於 `SetupBasicViewBag()` 末端（`SetupFeeDataListCount();` 後）加 `SetupMemberInfoViewBag();`，並新增方法：

```csharp
        /// <summary>
        /// 設定 ViewBag.MemberInfoAccess（"全教會"/"牧養名單"/null）。
        /// ✅ 只快取「正向」結果：避免登入連絡人尚未載入時，把「無權限」永久寫死該 Session。
        /// </summary>
        protected void SetupMemberInfoViewBag()
        {
            try
            {
                var cached = HttpContext?.Session?.GetString("_MemberInfoAccess");
                if (!string.IsNullOrEmpty(cached)) { ViewBag.MemberInfoAccess = cached; return; }

                var pim = InMemoryContext?.PersonalInfomationModel;
                if (pim != null && pim.m_LoginContact == null)
                {
                    try { pim.SetPersonalInfomationViewModel(); } catch { /* 本次視為未判定 */ }
                }

                var loginContact = pim?.m_LoginContact;
                if (loginContact == null) { ViewBag.MemberInfoAccess = null; return; } // 未判定→不快取

                string jobTitle = ToolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? "";
                string loginType = InMemoryContext?.ListManager?.LoginType ?? "";
                string access = ChurchReport.Services.MemberInfo.MemberInfoAccessResolver.Resolve(jobTitle, loginType);

                if (!string.IsNullOrEmpty(access)) HttpContext?.Session?.SetString("_MemberInfoAccess", access);
                ViewBag.MemberInfoAccess = access;
            }
            catch { ViewBag.MemberInfoAccess = null; }
        }
```

- [ ] **Step 2: `_Layout.cshtml`** — 在 `<li><a href="/Dedication/QPayView/網頁登入">…奉獻</a></li>`（約 463 行）**前**插一次：

```cshtml
                        @if (ViewBag.MemberInfoAccess == "全教會" || ViewBag.MemberInfoAccess == "牧養名單")
                        {
                            <li><a href="/MemberInfo/Index"><i class="fas fa-id-card"></i>會友資訊</a></li>
                        }
```

- [ ] **Step 3:** `dotnet build "ChurchReport/ChurchReport.csproj"` → succeeded。
- [ ] **Step 4: 手動驗證** — 牧養主任/小組長登入顯示「會友資訊」；一般組員不顯示。
- [ ] **Step 5:** `git add ChurchReport/Controllers/BaseChurchController.cs ChurchReport/Views/Shared/_Layout.cshtml && git commit -m "feat: 會友資訊導覽旗標與入口"`

---

## Task 5: `MemberInfoController` 骨架 + Scope（含 `CanViewContact`）+ Index

**Files:** Create `ChurchReport/ViewModels/MemberInfoListRowViewModel.cs`、`ChurchReport/Controllers/MemberInfoController.cs`、`ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 列 ViewModel**

```csharp
namespace ChurchReport.ViewModels
{
    public class MemberInfoListRowViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string SmallGroupName { get; set; }
    }
}
```

- [ ] **Step 2: Controller（骨架 + scope helper + Index + 牧養名單清單）**

```csharp
using ChurchReport.Services.MemberInfo;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    public class MemberInfoController : BaseChurchController
    {
        public MemberInfoController(
            IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache, IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider, ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool) { }

        // ---- 範圍與權限 ----
        private string GetAccess() { SetupMemberInfoViewBag(); return ViewBag.MemberInfoAccess as string; }

        /// <summary>確保 m_MultiGroupList 已載入（邊界保險，沿用 SetupListManager）。</summary>
        private void EnsureShepherdListsLoaded()
        {
            var lm = InMemoryContext?.ListManager; if (lm == null) return;
            var loaded = lm.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if ((loaded == null || loaded.Count == 0) && !string.IsNullOrEmpty(lm.m_Password))
                lm.SetupListManager(lm.m_Account, lm.m_Password, lm.m_SelectDate != default ? lm.m_SelectDate : DateTime.Now);
        }

        /// <summary>牧養名單者自己名單成員 contactId 白名單（含 fullname）。每請求快取。</summary>
        private Dictionary<string, string> _shepherdCache;
        private Dictionary<string, string> GetShepherdMembers()
        {
            if (_shepherdCache != null) return _shepherdCache;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnsureShepherdListsLoaded();
            var groups = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (groups != null)
            {
                var tu = ToolUtility;
                foreach (var g in groups)
                {
                    if (!Guid.TryParse(g.ListEntityId, out var listGuid)) continue;
                    var members = tu.RetrieveMemberListCollectionByListId(listGuid);
                    if (members?.Entities == null) continue;
                    foreach (var m in members.Entities)
                    {
                        var cid = tu.GetEntityLookupAttribute(m, "entityid");
                        if (cid == Guid.Empty || result.ContainsKey(cid.ToString())) continue;
                        var c = tu.m_Crm2011OrganizationService.Retrieve("contact", cid, new ColumnSet("fullname"));
                        result[cid.ToString()] = tu.GetEntityStringAttribute(c, "fullname");
                    }
                }
            }
            _shepherdCache = result; return result;
        }

        /// <summary>解析 customertypecode「結案」的 OptionSet 值（找不到回 null）。</summary>
        private int? ResolveClosedCustomerTypeValue()
        {
            try
            {
                var svc = new ChurchReport.Services.OptionSetMetadataService(
                    ToolUtility.m_Crm2011OrganizationService, null, new MemoryCache(new MemoryCacheOptions()));
                var map = svc.GetOptionSetMapping("contact", "customertypecode");
                if (map != null && map.TryGetValue("結案", out var v)) return v;
            }
            catch { }
            return null;
        }

        /// <summary>該 contact 是否為「現行」：statecode=0 且（可解析時）customertypecode≠結案。</summary>
        private bool IsCurrentContact(Guid contactId)
        {
            try
            {
                var c = ToolUtility.m_Crm2011OrganizationService.Retrieve(
                    "contact", contactId, new ColumnSet("statecode", "customertypecode"));
                var state = c.GetAttributeValue<OptionSetValue>("statecode");
                if (state == null || state.Value != 0) return false;
                var closed = ResolveClosedCustomerTypeValue();
                if (closed.HasValue)
                {
                    var ctc = c.GetAttributeValue<OptionSetValue>("customertypecode");
                    if (ctc != null && ctc.Value == closed.Value) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>統一範圍把關。全教會→需現行；牧養名單→需在白名單。</summary>
        private bool CanViewContact(Guid contactId)
        {
            var access = GetAccess();
            if (string.IsNullOrEmpty(access)) return false;
            if (access == MemberInfoAccess.Church) return IsCurrentContact(contactId);
            if (access == MemberInfoAccess.ShepherdList)
                return MemberInfoScopeGuard.IsContactAllowed(access, GetShepherdMembers().Keys.ToList(), contactId.ToString());
            return false;
        }

        // ---- 頁面 ----
        [HttpGet] [Route("/MemberInfo")] [Route("/MemberInfo/Index")]
        public IActionResult Index()
        {
            try
            {
                SetupBasicViewBag(); SetMultiGroupLayoutParameter();
                var access = ViewBag.MemberInfoAccess as string;
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                    return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = "您沒有檢視會友資訊的權限" });
                return View("MemberInfoGrid");
            }
            catch (Exception e) { return HandleError(e, "MemberInfo.Index"); }
        }

        // ---- 網格資料 ----
        [HttpGet]
        public object LoadMemberInfoList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access == MemberInfoAccess.ShepherdList)
                    return DataSourceLoader.Load(LoadShepherdListRows(), loadOptions);
                if (access == MemberInfoAccess.Church)
                    return LoadChurchWideContacts(loadOptions); // Task 6
                return DataSourceLoader.Load(new List<MemberInfoListRowViewModel>(), loadOptions);
            }
            catch (Exception e) { return HandleError(e, "MemberInfo.LoadMemberInfoList"); }
        }

        /// <summary>牧養名單：逐名單載入，同 contact 出現多名單則合併小組名稱。</summary>
        private List<MemberInfoListRowViewModel> LoadShepherdListRows()
        {
            EnsureShepherdListsLoaded();
            var byId = new Dictionary<string, MemberInfoListRowViewModel>(StringComparer.OrdinalIgnoreCase);
            var groups = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (groups == null) return new List<MemberInfoListRowViewModel>();
            var tu = ToolUtility;
            foreach (var g in groups)
            {
                if (!Guid.TryParse(g.ListEntityId, out var listGuid)) continue;
                var members = tu.RetrieveMemberListCollectionByListId(listGuid);
                if (members?.Entities == null) continue;
                foreach (var m in members.Entities)
                {
                    var cid = tu.GetEntityLookupAttribute(m, "entityid");
                    if (cid == Guid.Empty) continue;
                    var c = tu.m_Crm2011OrganizationService.Retrieve("contact", cid,
                        new ColumnSet("fullname", "mobilephone", "statecode"));
                    var state = c.GetAttributeValue<OptionSetValue>("statecode");
                    if (state == null || state.Value != 0) continue; // 僅現行
                    var key = cid.ToString();
                    if (byId.TryGetValue(key, out var row))
                    {
                        if (!string.IsNullOrEmpty(g.Name) && !row.SmallGroupName.Contains(g.Name))
                            row.SmallGroupName += "、" + g.Name;
                    }
                    else
                    {
                        byId[key] = new MemberInfoListRowViewModel
                        {
                            ContactId = key,
                            FullName = tu.GetEntityStringAttribute(c, "fullname"),
                            Phone = tu.GetEntityStringAttribute(c, "mobilephone"),
                            SmallGroupName = g.Name ?? ""
                        };
                    }
                }
            }
            return byId.Values.ToList();
        }
    }
}
```

> 註：`GetEntityLookupAttribute(Entity,"entityid")`（`PersonalController.cs:271`）、`GetAttributeValue<OptionSetValue>` 為 SDK 標準、`OptionSetMetadataService` 既有（`PersonalController.cs:542`）。

- [ ] **Step 3: 網格視圖（暫接全教會空資料，Task 6 補；圖片先用 Task 8 的受保護端點佔位 URL）**

Create `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`：

```cshtml
@{ ViewBag.Title = "會友資訊"; }
<div style="font-size:120%; color:darkcyan; font-weight:bold; margin:8px 0;"><h4>會友資訊</h4></div>

<div id="memberinfo-grid">
    @(Html.DevExtreme().DataGrid<ChurchReport.ViewModels.MemberInfoListRowViewModel>()
        .ID("MemberInfoGridContainer")
        .ShowBorders(true).ColumnAutoWidth(true).ShowRowLines(true).RowAlternationEnabled(true)
        .Paging(p => p.PageSize(50).Enabled(true))
        .Scrolling(s => s.RowRenderingMode(GridRowRenderingMode.Virtual))
        .SearchPanel(s => s.Visible(true).Placeholder("搜尋姓名.."))
        .RemoteOperations(true)
        .Columns(columns =>
        {
            columns.Add().DataField("ContactId").Caption("照片").Width(56).MinWidth(56)
                .AllowEditing(false).AllowSorting(false).AllowFiltering(false).Fixed(true)
                .CellTemplate(new JS("memberInfoAvatarCellTemplate"));
            columns.AddFor(m => m.FullName).Caption("姓名").Width(120)
                .CellTemplate(new JS("memberInfoNameCellTemplate"));
            columns.AddFor(m => m.Phone).Caption("手機").Width(140);
            columns.AddFor(m => m.SmallGroupName).Caption("小組").Width(180);
        })
        .DataSource(d => d.Mvc().Controller("MemberInfo").LoadAction("LoadMemberInfoList").Key("ContactId"))
    )
</div>

<script>
    // 照片：用受保護代理 /MemberInfo/GetContactImage（非 /Personal/...）
    window.memberInfoAvatarCellTemplate = function (container, options) {
        var d = options.data || {};
        var contactId = d.ContactId || d.contactId || options.value || '';
        var host = container && container.get ? container.get(0) : container; if (!host) return;
        host.textContent = '';
        var fb = 'data:image/svg+xml;utf8,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><circle cx="20" cy="20" r="20" fill="#e9ecef"/><text x="20" y="25" font-size="14" text-anchor="middle" fill="#6c757d">人</text></svg>');
        var img = document.createElement('img');
        img.src = contactId ? ('/MemberInfo/GetContactImage?contactId=' + encodeURIComponent(contactId) + '&size=48') : fb;
        img.onerror = function () { if (img.src !== fb) img.src = fb; };
        img.alt = '大頭照'; img.loading = 'lazy';
        img.style.cssText = 'width:40px;height:40px;border-radius:50%;object-fit:cover;border:2px solid #e0e0e0;display:block;margin:0 auto;';
        host.appendChild(img);
    };
    // 姓名：可點 → 唯讀彈窗（彈窗本體於 Task 7 加入）
    window.memberInfoNameCellTemplate = function (container, options) {
        var d = options.data || {}; var contactId = d.ContactId || d.contactId || ''; var fullName = d.FullName || d.fullName || '';
        var host = container && container.get ? container.get(0) : container; if (!host) return;
        var a = document.createElement('a'); a.href = 'javascript:void(0)'; a.textContent = fullName;
        a.style.cssText = 'color:#3b5bdb;font-weight:bold;text-decoration:underline;cursor:pointer;';
        a.addEventListener('click', function (e) { e.preventDefault(); openMemberInfoDetailPopup(contactId, fullName); });
        host.appendChild(a);
    };
    function openMemberInfoDetailPopup(contactId, fullName) { console.log("openMemberInfoDetailPopup", contactId, fullName); } // Task 7 取代
</script>
```

- [ ] **Step 4:** `dotnet build ...` → succeeded。
- [ ] **Step 5: 手動驗證** — 小組長登入→「會友資訊」網格出現自己名單成員（照片此時為破圖/預設，因 `/MemberInfo/GetContactImage` 於 Task 8 才建；可先暫時觀察姓名/手機/小組）。
- [ ] **Step 6:** `git add ChurchReport/ViewModels/MemberInfoListRowViewModel.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml && git commit -m "feat: 會友資訊網格 + 牧養名單 + 範圍把關(CanViewContact)"`

---

## Task 6: 全教會資料來源（伺服器端分頁/搜尋/排序）

**Files:** Modify `MemberInfoController.cs`

- [ ] **Step 1: 新增 `LoadChurchWideContacts`**

```csharp
        /// <summary>全教會：CRM 伺服器端分頁，啟用且非結案，支援姓名/手機搜尋，排序限 FullName/Phone。</summary>
        private object LoadChurchWideContacts(DataSourceLoadOptions loadOptions)
        {
            var tu = ToolUtility; var svc = tu.m_Crm2011OrganizationService;
            int take = loadOptions.Take > 0 ? Math.Min(loadOptions.Take, 200) : 50;
            int skip = loadOptions.Skip > 0 ? loadOptions.Skip : 0;
            int pageNumber = (skip / take) + 1;
            int? closed = ResolveClosedCustomerTypeValue();

            var q = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "mobilephone"),
                PageInfo = new PagingInfo { Count = take, PageNumber = pageNumber, ReturnTotalRecordCount = true }
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            if (closed.HasValue) q.Criteria.AddCondition("customertypecode", ConditionOperator.NotEqual, closed.Value);

            // 搜尋：fullname 或 mobilephone 模糊
            var search = loadOptions.SearchValue as string;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var f = new FilterExpression(LogicalOperator.Or);
                f.AddCondition("fullname", ConditionOperator.Like, "%" + search.Trim() + "%");
                f.AddCondition("mobilephone", ConditionOperator.Like, "%" + search.Trim() + "%");
                q.Criteria.AddFilter(f);
            }

            // 排序：只支援 FullName/Phone，其餘回退 fullname asc
            var sortField = "fullname"; var desc = false;
            if (loadOptions.Sort != null && loadOptions.Sort.Length > 0)
            {
                var s = loadOptions.Sort[0];
                if (string.Equals(s.Selector, "Phone", StringComparison.OrdinalIgnoreCase)) sortField = "mobilephone";
                desc = s.Desc;
            }
            q.AddOrder(sortField, desc ? OrderType.Descending : OrderType.Ascending);

            var ec = svc.RetrieveMultiple(q);
            var data = ec.Entities.Select(c => new MemberInfoListRowViewModel
            {
                ContactId = c.Id.ToString(),
                FullName = tu.GetEntityStringAttribute(c, "fullname"),
                Phone = tu.GetEntityStringAttribute(c, "mobilephone"),
                SmallGroupName = "" // OQ-2：全教會「小組」欄位來源待定，第一版留白
            }).ToList();

            return new { data, totalCount = ec.TotalRecordCount >= 0 ? ec.TotalRecordCount : data.Count };
        }
```

> `TotalRecordCount` 於 `ReturnTotalRecordCount=true` 有效（Dataverse 上限約 5000，超過回 -1，以當頁筆數後備）。`loadOptions.Sort[0].Selector/.Desc` 為 DevExtreme 標準。

- [ ] **Step 2:** `dotnet build ...` → succeeded。
- [ ] **Step 3: 手動驗證** — 牧養主任登入→全教會清單；翻頁、姓名/手機搜尋、姓名排序正常；結案者不出現。
- [ ] **Step 4:** `git commit -am "feat: 會友資訊全教會伺服器端分頁查詢"`

---

## Task 7: 唯讀細節彈窗（大頭貼 + 基本資訊 + 子導覽）

**Files:** Create `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`、`Views/MemberInfo/_MemberDetailPopup.cshtml`；Modify `MemberInfoController.cs`（`Detail`）、`MemberInfoGrid.cshtml`（Popup + 取代存根）

- [ ] **Step 1: 細節 ViewModel**

```csharp
using System.Collections.Generic;
namespace ChurchReport.ViewModels
{
    public class MemberInfoDetailViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string MobilePhone { get; set; }
        public string Address { get; set; }
        public string SpiritualIdentity { get; set; }
        public List<RelationGoalItem> RelationGoals { get; set; } = new List<RelationGoalItem>();
    }
    public class RelationGoalItem { public string Role { get; set; } public string TargetName { get; set; } }
}
```

- [ ] **Step 2: `Detail` 動作（CanViewContact 把關；關係目標 Task 10 補）**

```csharp
        [HttpGet] [Route("/MemberInfo/Detail")]
        public IActionResult Detail(string contactId)
        {
            try
            {
                EnsureCorrectUserData();
                if (!Guid.TryParse(contactId, out var guid) || !CanViewContact(guid))
                    return StatusCode(403, "無權檢視此連絡人");

                var tu = ToolUtility;
                var c = tu.m_Crm2011OrganizationService.Retrieve("contact", guid,
                    new ColumnSet("contactid", "fullname", "mobilephone", "address2_line1", "new_spiriitual_identity"));

                string spiritual = "";
                if (c.Contains("new_spiriitual_identity"))
                {
                    try
                    {
                        var val = tu.GetOptionSetAttribute(c, "new_spiriitual_identity");
                        var svc = new ChurchReport.Services.OptionSetMetadataService(
                            tu.m_Crm2011OrganizationService, null, new MemoryCache(new MemoryCacheOptions()));
                        spiritual = svc.GetOptionSetText("contact", "new_spiriitual_identity", val);
                    }
                    catch { spiritual = ""; }
                }

                var vm = new ChurchReport.ViewModels.MemberInfoDetailViewModel
                {
                    ContactId = guid.ToString(),
                    FullName = tu.GetEntityStringAttribute(c, "fullname"),
                    MobilePhone = tu.GetEntityStringAttribute(c, "mobilephone"),
                    Address = tu.GetEntityStringAttribute(c, "address2_line1"),
                    SpiritualIdentity = spiritual,
                    RelationGoals = GetRelationGoals(guid) // Task 10；先回空
                };
                return PartialView("_MemberDetailPopup", vm);
            }
            catch (Exception e) { return HandleError(e, "MemberInfo.Detail"); }
        }

        private List<ChurchReport.ViewModels.RelationGoalItem> GetRelationGoals(Guid contactId)
            => new List<ChurchReport.ViewModels.RelationGoalItem>(); // Task 10 補完
```

- [ ] **Step 3: 彈窗 partial（大頭貼用受保護端點；JS 函式加 memberInfo 前綴）**

Create `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`：

```cshtml
@model ChurchReport.ViewModels.MemberInfoDetailViewModel
<div style="display:flex; gap:16px; align-items:flex-start; flex-wrap:wrap;">
    <div style="flex:0 0 200px;">
        <img src="/MemberInfo/GetContactImage?contactId=@Model.ContactId&size=0"
             onerror="this.onerror=null;this.src='/MemberInfo/GetContactImage?contactId=@Model.ContactId&size=180';"
             style="width:180px;height:180px;border-radius:10px;object-fit:cover;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,.25);" />
        <div style="margin-top:12px; display:flex; flex-direction:column; gap:8px;">
            <button type="button" class="memberinfo-subnav" data-tab="present" onclick="memberInfoDetailSwitch('present')"
                    style="padding:10px;border:none;border-radius:6px;cursor:pointer;background:#667eea;color:#fff;">聚會紀錄</button>
            <button type="button" class="memberinfo-subnav" data-tab="equip" onclick="memberInfoDetailSwitch('equip')"
                    style="padding:10px;border:none;border-radius:6px;cursor:pointer;background:#adb5bd;color:#fff;">裝備紀錄</button>
        </div>
    </div>
    <div style="flex:1 1 280px;">
        <h3 style="margin:0 0 8px;">@Model.FullName</h3>
        <table style="width:100%; border-collapse:collapse;">
            <tr><td style="color:#868e96;width:90px;">手機</td><td>@Model.MobilePhone</td></tr>
            <tr><td style="color:#868e96;">地址</td><td>@Model.Address</td></tr>
            <tr><td style="color:#868e96;">信仰狀態</td><td>@Model.SpiritualIdentity</td></tr>
            <tr><td style="color:#868e96;vertical-align:top;">關係目標</td>
                <td>
                    @if (Model.RelationGoals != null && Model.RelationGoals.Count > 0)
                    { foreach (var r in Model.RelationGoals) { <div>@r.Role：@r.TargetName</div> } }
                    else { <span style="color:#adb5bd;">（無）</span> }
                </td></tr>
        </table>
        <hr />
        <div id="member-subgrid-present" data-contact-id="@Model.ContactId"></div>
        <div id="member-subgrid-equip" data-contact-id="@Model.ContactId" style="display:none;"></div>
    </div>
</div>

<script>
    window._memberInfoSubInit = { present: false, equip: false };
    function memberInfoDetailSwitch(tab) {
        var p = document.getElementById('member-subgrid-present');
        var q = document.getElementById('member-subgrid-equip');
        document.querySelectorAll('.memberinfo-subnav').forEach(function (b) {
            b.style.background = (b.getAttribute('data-tab') === tab) ? '#667eea' : '#adb5bd';
        });
        p.style.display = (tab === 'present') ? 'block' : 'none';
        q.style.display = (tab === 'equip') ? 'block' : 'none';
        initMemberInfoSubGrid(tab, p.getAttribute('data-contact-id'));
    }
    // 子網格初始化（Task 8/9 內補上 present/equip 區塊）
    function initMemberInfoSubGrid(tab, contactId) { /* Task 8/9 */ }
</script>
```

- [ ] **Step 4: 在網格頁加入 Popup 並實作 `openMemberInfoDetailPopup`**

在 `MemberInfoGrid.cshtml`：(1) 加入 Popup；(2) 把存根換成實作；**保留** `memberInfoAvatarCellTemplate`/`memberInfoNameCellTemplate`。id 用 `memberInfoDetailPopup`（異於既有 `#memberDetailPopup`）。

```cshtml
@(Html.DevExtreme().Popup().ID("memberInfoDetailPopup")
    .Width("80%").Height("80%").Title("會友細節").ShowTitle(true).Visible(false)
    .DragEnabled(true).HideOnOutsideClick(true))

<script>
    function openMemberInfoDetailPopup(contactId, fullName) {
        var popup = $("#memberInfoDetailPopup").dxPopup("instance");
        popup.option("title", fullName + " － 會友細節");
        popup.option("contentTemplate", function (el) {
            $(el).html('<div style="padding:16px;">載入中...</div>');
            $.ajax({
                url: '/MemberInfo/Detail', type: 'GET', data: { contactId: contactId },
                success: function (html) { $(el).html(html); if (typeof memberInfoDetailSwitch === 'function') memberInfoDetailSwitch('present'); },
                error: function (xhr) { $(el).html('<div style="padding:16px;color:#c92a2a;">載入失敗：' + (xhr.status === 403 ? '無權檢視此連絡人' : '請稍後再試') + '</div>'); }
            });
        });
        popup.show();
    }
</script>
```

- [ ] **Step 5:** `dotnet build ...` → succeeded（圖片仍需 Task 8 端點，先可破圖）。
- [ ] **Step 6:** `git add ChurchReport/ViewModels/MemberInfoDetailViewModel.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml && git commit -m "feat: 會友資訊唯讀細節彈窗(含 CanViewContact 把關)"`

---

## Task 8: 受保護圖片代理端點（修補 IDOR）

**Files:** Modify `MemberInfoController.cs`（新增 `GetContactImage`、`GetContactImagesBatch`、縮圖 helper、請求模型）

> 修補 Codex 指出的真實漏洞：`/Personal/GetContactImage(+Batch)` 無呼叫者範圍檢查。會友資訊一律走以下受保護代理；未授權 id 回預設圖/不出現。

- [ ] **Step 1: 新增端點與 helper（檔頭補 `using SixLabors.ImageSharp;` 等）**

於 `MemberInfoController.cs` 加入（class 內）：

```csharp
        [HttpGet] [Route("/MemberInfo/GetContactImage")]
        public IActionResult GetContactImage(string contactId, int size = 48)
        {
            try
            {
                EnsureCorrectUserData();
                if (!Guid.TryParse(contactId, out var guid) || !CanViewContact(guid))
                    return DefaultAvatar();

                var c = ToolUtility.m_Crm2011OrganizationService.Retrieve("contact", guid, new ColumnSet("entityimage"));
                if (!c.Contains("entityimage") || c["entityimage"] == null) return DefaultAvatar();

                var original = (byte[])c["entityimage"];
                var bytes = size <= 0 ? original : ThumbnailJpeg(original, Math.Clamp(size, 32, 256));
                Response.Headers["Cache-Control"] = "private, max-age=600";
                return File(bytes, "image/jpeg");
            }
            catch { return DefaultAvatar(); }
        }

        public class MemberImageBatchRequest { public string[] ContactIds { get; set; } public int Size { get; set; } }

        [HttpPost] [Route("/MemberInfo/GetContactImagesBatch")]
        public IActionResult GetContactImagesBatch([FromBody] MemberImageBatchRequest request)
        {
            var images = new Dictionary<string, string>();
            try
            {
                EnsureCorrectUserData();
                if (request?.ContactIds == null || request.ContactIds.Length == 0)
                    return Json(new { success = true, images });

                int size = Math.Clamp(request.Size > 0 ? request.Size : 48, 32, 256);

                // 先過範圍把關，僅查允許的 id（一次 RetrieveMultiple）
                var allowed = new List<Guid>();
                foreach (var s in request.ContactIds)
                    if (Guid.TryParse(s, out var g) && CanViewContact(g)) allowed.Add(g);
                if (allowed.Count == 0) return Json(new { success = true, images });

                var q = new QueryExpression("contact") { ColumnSet = new ColumnSet("contactid", "entityimage") };
                q.Criteria.AddCondition("contactid", ConditionOperator.In, allowed.Select(g => (object)g).ToArray());
                var ec = ToolUtility.m_Crm2011OrganizationService.RetrieveMultiple(q);
                foreach (var e in ec.Entities)
                {
                    if (e.Contains("entityimage") && e["entityimage"] != null)
                        images[e.Id.ToString()] = "data:image/jpeg;base64," + Convert.ToBase64String(ThumbnailJpeg((byte[])e["entityimage"], size));
                }
                return Json(new { success = true, images });
            }
            catch { return Json(new { success = false, images }); }
        }

        private IActionResult DefaultAvatar()
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'><circle cx='24' cy='24' r='24' fill='#e9ecef'/><text x='24' y='30' font-size='18' text-anchor='middle' fill='#6c757d'>人</text></svg>";
            return Content(svg, "image/svg+xml");
        }

        private static byte[] ThumbnailJpeg(byte[] original, int size)
        {
            try
            {
                using var input = new System.IO.MemoryStream(original);
                using var image = SixLabors.ImageSharp.Image.Load(input);
                if (image.Width > size || image.Height > size)
                    image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(size, size),
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Crop
                    }));
                using var output = new System.IO.MemoryStream();
                image.Save(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 });
                return output.ToArray();
            }
            catch { return original; }
        }
```

於檔頭 `using` 區加：`using SixLabors.ImageSharp.Processing;`（`Mutate`/`Resize` 擴充）。其餘 ImageSharp 型別已以完整命名空間引用。

> ⚠️ **效能（務必加上）**：上方為求清楚未含快取。逐格載入時，每張照片＝`CanViewContact`（全教會會做 `IsCurrentContact` 一次 Retrieve）＋ entityimage 一次 Retrieve；50 列全教會網格約上百次 CRM 呼叫。實作時請：①把 `GetContactImage` 的縮圖結果加 `IMemoryCache`（仿 `PersonalController.ImageUpload.cs` 的 `contact-image-thumb:{guid}:{size}` 快取，30 分鐘）；②把 `IsCurrentContact` 結果以一個 `Dictionary<Guid,bool> _currentContactCache` 做**每請求**快取，避免同一 contactId 重複查 statecode。牧養名單資料量小可接受；全教會務必加快取，或改走 `GetContactImagesBatch` 批次預載。

- [ ] **Step 2:** `dotnet build ...` → succeeded。
- [ ] **Step 3: 手動驗證** — 會友資訊網格與彈窗大頭貼正常顯示；有照片顯示照片、無照片顯示預設圖。
- [ ] **Step 4: IDOR 驗證** — 以小組長身分 `GET /MemberInfo/GetContactImage?contactId=<非自己名單GUID>` → 回預設圖（非真照片）。
- [ ] **Step 5:** `git commit -am "feat: 會友資訊受保護圖片代理端點(修補 IDOR)"`

---

## Task 9: 聚會紀錄子網格（個人靈修與聚會紀錄）

> **範圍：只查彈窗 `contactId`，非全教會。** 端點 `LoadContactPresentRecords` 先 `CanViewContact`，再以 `RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)` 取「該連絡人」之 `new_present_record`。同一人多筆，故用 `PresentRecordId` 當唯一鍵。

**Files:** Modify `MemberInfoController.cs`（DTO + `LoadContactPresentRecords`）、`_MemberDetailPopup.cshtml`（present 區塊）

- [ ] **Step 1: DTO + 端點**

於 `MemberInfoController.cs` 命名空間內（class 外）加 DTO，class 內加端點：

```csharp
    public class ContactPresentRecordRow
    {
        public string PresentRecordId { get; set; }
        public string FullName { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public string PrayItem { get; set; }
    }
```

```csharp
        [HttpGet]
        public object LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                if (!Guid.TryParse(contactId, out var guid) || !CanViewContact(guid))
                    return DataSourceLoader.Load(new List<ContactPresentRecordRow>(), loadOptions);

                var tu = ToolUtility;
                var fullName = tu.GetEntityStringAttribute(
                    tu.m_Crm2011OrganizationService.Retrieve("contact", guid, new ColumnSet("fullname")), "fullname");

                var rows = new List<ContactPresentRecordRow>();
                var records = tu.RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId);
                if (records?.Entities != null)
                    foreach (var rec in records.Entities)
                    {
                        var full = tu.RetrieveEntity("new_present_record", rec.Id);
                        rows.Add(new ContactPresentRecordRow
                        {
                            PresentRecordId = rec.Id.ToString(),
                            FullName = fullName,
                            Sunday = tu.GetEntityIntAttribute(full, "new_sunday_present_this_week") > 0,
                            SmallGroup = tu.GetEntityIntAttribute(full, "new_group_present_this_week") > 0,
                            PrayItem = tu.GetEntityStringAttribute(full, "new_explanation")
                        });
                    }
                return DataSourceLoader.Load(rows, loadOptions);
            }
            catch (Exception e) { return HandleError(e, "MemberInfo.LoadContactPresentRecords"); }
        }
```

- [ ] **Step 2: 在 partial 初始化 present 子網格**

把 `_MemberDetailPopup.cshtml` 的 `initMemberInfoSubGrid` 換為：

```javascript
    function initMemberInfoSubGrid(tab, contactId) {
        if (tab === 'present' && !window._memberInfoSubInit.present) {
            window._memberInfoSubInit.present = true;
            $('#member-subgrid-present').dxDataGrid({
                dataSource: DevExpress.data.AspNet.createStore({
                    key: 'PresentRecordId',
                    loadUrl: '/MemberInfo/LoadContactPresentRecords',
                    loadParams: { contactId: contactId }   // 只查此彈出連絡人
                }),
                showBorders: true, showRowLines: true, columnAutoWidth: true,
                columns: [
                    { dataField: 'FullName', caption: '姓名', width: 100 },
                    { dataField: 'Sunday', caption: '主日', dataType: 'boolean', width: 70 },
                    { dataField: 'SmallGroup', caption: '小組', dataType: 'boolean', width: 70 },
                    { dataField: 'PrayItem', caption: '代禱' }
                ]
            });
        }
    }
```

- [ ] **Step 3:** `dotnet build ...` → succeeded。手動：開彈窗預設顯示聚會紀錄；A/B 連絡人各自獨立。
- [ ] **Step 4:** `git commit -am "feat: 會友細節-聚會紀錄子網格(per-contact)"`

---

## Task 10: 裝備紀錄子網格（上課紀錄單）

> **範圍：只查彈窗 `contactId`。** 先 `CanViewContact`，再 `RetrieveStorLessonsByFetchXml(fullName, contactId)`。

**Files:** Modify `MemberInfoController.cs`（`LoadContactStorLessons`）、`_MemberDetailPopup.cshtml`（equip 區塊）

- [ ] **Step 1: 端點（複用 `EquipmentStorLessons` 映射，需 `using ChurchReport.Models;`）**

```csharp
        [HttpGet]
        public object LoadContactStorLessons(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                if (!Guid.TryParse(contactId, out var guid) || !CanViewContact(guid))
                    return DataSourceLoader.Load(new List<ChurchReport.Models.EquipmentStorLessons>(), loadOptions);

                var tu = ToolUtility;
                var fullName = tu.GetEntityStringAttribute(
                    tu.m_Crm2011OrganizationService.Retrieve("contact", guid, new ColumnSet("fullname")), "fullname");

                var list = new List<ChurchReport.Models.EquipmentStorLessons>();
                var storLessons = tu.RetrieveStorLessonsByFetchXml(fullName, contactId);
                if (storLessons?.Entities != null)
                    foreach (var le in storLessons.Entities)
                    {
                        var lesson = le;
                        var dlId = tu.GetEntityLookupAttribute(ref lesson, "new_new_disciple_lessons_new_stor_les");
                        DateTime start = DateTime.MinValue; string stage = "";
                        if (dlId != Guid.Empty)
                        {
                            try
                            {
                                var dl = tu.RetrieveEntity("new_disciple_lessons", dlId);
                                start = tu.GetEntityDateTimeAttribute(ref dl, "new_class_start_date");
                                stage = tu.GetEntityStringAttribute(ref dl, "new_now_stage_name");
                            }
                            catch { }
                        }
                        list.Add(new ChurchReport.Models.EquipmentStorLessons
                        {
                            StorLessonsEntityId = lesson.Id.ToString(),
                            DiscipleLessonsName = tu.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les"),
                            StageName = stage,
                            CurrentComplete = tu.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
                            DiscipleLessonsDateTime = start
                        });
                    }
                return DataSourceLoader.Load(list, loadOptions);
            }
            catch (Exception e) { return HandleError(e, "MemberInfo.LoadContactStorLessons"); }
        }
```

> 映射同 `EquipmentController.LoadEquipmentStorLessons`（306-353），差別在直接由 contactId 查。

- [ ] **Step 2: 在 partial 初始化 equip 子網格** — 於 `initMemberInfoSubGrid` 內、present 區塊後加：

```javascript
        if (tab === 'equip' && !window._memberInfoSubInit.equip) {
            window._memberInfoSubInit.equip = true;
            $('#member-subgrid-equip').dxDataGrid({
                dataSource: DevExpress.data.AspNet.createStore({
                    key: 'StorLessonsEntityId',
                    loadUrl: '/MemberInfo/LoadContactStorLessons',
                    loadParams: { contactId: contactId }   // 只查此彈出連絡人
                }),
                showBorders: true, showRowLines: true, columnAutoWidth: true,
                columns: [
                    { dataField: 'DiscipleLessonsName', caption: '課程名稱', width: 200 },
                    { dataField: 'DiscipleLessonsDateTime', caption: '日期', dataType: 'date', format: 'yyyy/MM/dd', width: 110 },
                    { dataField: 'StageName', caption: '階段名稱', width: 100 },
                    { dataField: 'CurrentComplete', caption: '是否結業', dataType: 'boolean', width: 90 }
                ]
            });
        }
```

- [ ] **Step 3:** `dotnet build ...` → succeeded。手動：切「裝備紀錄」顯示課程/日期/階段/結業；A/B 各自獨立。
- [ ] **Step 4:** `git commit -am "feat: 會友細節-裝備紀錄子網格(per-contact)"`

---

## Task 11: 關係目標（connection 雙向查詢，安全降級）

> 採 Codex 改進：查 `record1id` **或** `record2id`，角色用 `record1roleid`/`record2roleid`，避免漏掉反向關係。OQ-1：此 CRM 是否啟用 connection／實際角色欄位待真實資料確認；查詢失敗即回空清單。

**Files:** Modify `MemberInfoController.cs`（`GetRelationGoals`）

- [ ] **Step 1: 實作（取代占位）**

```csharp
        private List<ChurchReport.ViewModels.RelationGoalItem> GetRelationGoals(Guid contactId)
        {
            var result = new List<ChurchReport.ViewModels.RelationGoalItem>();
            var svc = ToolUtility.m_Crm2011OrganizationService;
            // 方向一：本人為 record1id → 對象為 record2id，角色取 record2roleid
            CollectConnections(svc, contactId, "record1id", "record2id", "record2roleid", result);
            // 方向二：本人為 record2id → 對象為 record1id，角色取 record1roleid
            CollectConnections(svc, contactId, "record2id", "record1id", "record1roleid", result);
            return result;
        }

        private void CollectConnections(IOrganizationService svc, Guid contactId,
            string selfField, string targetField, string roleField,
            List<ChurchReport.ViewModels.RelationGoalItem> sink)
        {
            try
            {
                var q = new QueryExpression("connection")
                { ColumnSet = new ColumnSet(targetField, roleField) };
                q.Criteria.AddCondition(selfField, ConditionOperator.Equal, contactId);

                var ec = svc.RetrieveMultiple(q);
                foreach (var conn in ec.Entities)
                {
                    string role = conn.GetAttributeValue<EntityReference>(roleField)?.Name ?? "";
                    string target = conn.GetAttributeValue<EntityReference>(targetField)?.Name ?? "";
                    if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(target))
                        sink.Add(new ChurchReport.ViewModels.RelationGoalItem { Role = role, TargetName = target });
                }
            }
            catch { /* 未啟用 connection / 權限不足 → 略過該方向 */ }
        }
```

- [ ] **Step 2:** `dotnet build ...` → succeeded。手動：對有 connection 的 contact 顯示「角色：對象」；無者顯示「（無）」且不報錯。
- [ ] **Step 3:** `git commit -am "feat: 會友細節-關係目標(connection 雙向, 安全降級)"`

---

## Task 12: 端到端與越權（含圖片）驗證

- [ ] **Step 1:** `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"` → PASS。
- [ ] **Step 2: 三角色**
  - 牧養主任：全教會、搜尋分頁、彈窗四項＋兩子網格皆正常。
  - 小組長：只含自己名單現行成員、彈窗正常。
  - 一般組員：左側無「會友資訊」；直接打 `/MemberInfo/Index` 被導錯誤頁。
- [ ] **Step 3: per-contact** — 點 A、B 兩連絡人，聚會/裝備紀錄各自獨立且筆數符合該人 CRM 實際（非全教會彙總）。
- [ ] **Step 4: IDOR（含圖片）** — 小組長對非自己名單 GUID：
  - `/MemberInfo/Detail` → 403；
  - `/MemberInfo/LoadContactPresentRecords`、`/MemberInfo/LoadContactStorLessons` → 空資料；
  - `/MemberInfo/GetContactImage` → 預設圖（非真照片）；
  - `/MemberInfo/GetContactImagesBatch` 帶混合 id → 回傳只含被授權者。
- [ ] **Step 5:** 視需要修正後 `git commit -am "test: 會友資訊端到端與越權驗證"`

---

## Self-Review（規格涵蓋對照）

| 規格／安全需求 | 任務 |
|---|---|
| 左側導覽入口（單一插入、依角色） | Task 4 |
| 網格 照片/姓名/手機/小組 | Task 5（牧養名單）、Task 6（全教會） |
| 唯讀彈窗（大頭貼/手機/地址/信仰狀態/關係目標） | Task 7、Task 11 |
| 聚會紀錄（姓名/主日/小組/代禱）**限該連絡人** | Task 9 |
| 裝備紀錄（課程/日期/階段/結業）**限該連絡人** | Task 10 |
| 需求3 全教會現行（啟用且非結案＋分頁） | Task 2/4/6 + `IsCurrentContact` |
| 需求4 小組長只看自己名單 | Task 2/5 + `CanViewContact` 白名單 |
| 伺服器端越權防護（含**圖片**） | Task 5/8/9/10、Task 12 |
| 唯讀（不寫回 CRM） | 全程無更新端點 |

**一致性檢查**：型別/方法名跨任務一致（`MemberInfoAccess.Church/ShepherdList`、`Resolve`、`IsContactAllowed`、`GetAccess`、`EnsureShepherdListsLoaded`、`GetShepherdMembers`、`ResolveClosedCustomerTypeValue`、`IsCurrentContact`、`CanViewContact`、`MemberInfoListRowViewModel`、`MemberInfoDetailViewModel/RelationGoalItem`、`ContactPresentRecordRow`、`EquipmentStorLessons`；前端 `memberInfoAvatarCellTemplate`/`memberInfoNameCellTemplate`/`openMemberInfoDetailPopup`/`memberInfoDetailSwitch`/`initMemberInfoSubGrid`/`#memberInfoDetailPopup`，皆與 `_GeneralGroupGrids` 區隔）。`GetRelationGoals`/`initMemberInfoSubGrid` 採「先空實作、後續任務補完」確保每階段可編譯。

---

## 仍需實作時確認（開放問題）

- **OQ-1 關係目標**：此 CRM 是否啟用 `connection`、實際角色欄位是 `record1roleid`/`record2roleid` 或 `connectionroleid`。Task 11 已雙向＋安全降級；以真實資料驗證後微調。
- **OQ-2 全教會「小組」欄位**：第一版留白。若要顯示，建議「對**當頁** contact 小量批次查名單名稱」，**勿**對全部 contact 逐列查（效能）。注意：勿把未證實的名單篩選欄位（如某些 `purpose`/旗標值）當事實，需先在 CRM 核對。
- **`SetPersonalInfomationViewModel()` 成本**：由 `SetupBasicViewBag` 廣泛呼叫，僅在 `m_LoginContact==null` 時觸發；若實測偏重，改為更輕量的「僅取登入 contact 的 `new_church_jobtitle`」查詢。
- **既有 `/Personal/GetContactImage` 全站無範圍檢查**：屬既有問題、不在本功能範圍；本功能已改用受保護代理。建議另案評估是否收斂該端點。

## 後續可選（非本計畫必做）
- 抽 `ContactImageService` 共用縮圖/快取，讓 Personal 與 MemberInfo 共用（Task 8 第一版先在 controller 內自帶最小 helper）。
- 會友資訊網格改用批次預載大頭照（`/MemberInfo/GetContactImagesBatch`）取代逐格載入。
- 把 `_GeneralGroupGrids` 的 `renderContactAvatar` 與本功能的 avatar 模板抽到共用 `wwwroot/js/contact-avatar.js`（點擊行為參數化）。
