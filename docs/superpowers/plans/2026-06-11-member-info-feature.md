# 會友資訊（Member Info）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在左側導覽新增「會友資訊」入口：一個連絡人網格（照片/姓名/手機/小組），點姓名彈出唯讀細節（大頭貼＋手機/地址/信仰狀態/關係目標）並可切換「聚會紀錄」「裝備紀錄」兩子網格；依角色（牧師傳道/牧養主任→全教會、小組長→自己名單）決定可見性與資料範圍。

> **核心條件（務必遵守）：彈窗內的「聚會紀錄」與「裝備紀錄」兩子網格，只顯示「被點擊彈出的那一位連絡人」的資料，絕非全教會或整份名單的資料。** 兩者一律以該連絡人的 `contactId` 為查詢條件（見 Task 8、Task 9）。

**Architecture:** 新增獨立 `MemberInfoController : BaseChurchController`，所有端點以 `contactId` 參數化；純判定邏輯（角色解析、範圍白名單）抽成可單元測試的靜態類別；CRM/視圖整合沿用既有 `ToolUtility`/連線池/DevExtreme 樣板並以「建置＋登入點擊」驗證。與既有「組員資訊」並存、不更動之。

**Tech Stack:** ASP.NET Core MVC (net10.0) + DevExtreme 21.2.7 + Microsoft Dataverse/Dynamics 365 CRM；測試 xUnit + FluentAssertions（net10.0）。

**Spec:** `docs/superpowers/specs/2026-06-11-member-info-feature-design.md`

---

## 工作環境前置說明（請先讀）

- **程式碼位置**：實際專案在主目錄 `…\音訊產品版本\ChurchReport\`，git 分支 `Jesus_5.0.9.8_AddPicture`。本 session 的 worktree 只有 README，不要在 worktree 內實作。
- **建議分支**：自 `Jesus_5.0.9.8_AddPicture` 開一個 `feature/member-info` 分支實作（`git switch -c feature/member-info`）。
- **建置**：`dotnet build "ChurchReport/ChurchReport.csproj"`（或 Visual Studio 建置方案）。
- **執行驗證**：Visual Studio F5，或 `dotnet run --project "ChurchReport/ChurchReport.csproj"`，以瀏覽器登入。多數驗證需以**真實 CRM 連絡人帳號**登入（不同角色）。
- **CRM 欄位速查**：`contact`：`fullname, mobilephone, address2_line1, customertypecode(會員身分), new_spiriitual_identity(信仰狀態，拼字如此), new_church_jobtitle(教會職稱), statecode, entityimage`；`new_present_record`：`new_sunday_present_this_week(int), new_group_present_this_week(int), new_explanation(string), new_group_date(date), new_contact_new_present_record(→contact)`。

---

## 既有基礎建設（重要，務必先看再動手）

`Views/Home/_GeneralGroupGrids.cshtml`（小組牧養網格，**近期已被修改**）已存在一套「網格大頭照 + 點擊開彈窗」基礎建設，本功能須**避免衝突並沿用慣例**：

- `contactImageCellTemplate` / `renderContactAvatar`：以 `new JS(...)` 模板渲染連絡人大頭照，並透過 `/Personal/GetContactImagesBatch` 批次預載。
- `openMemberDetailPopup(rowData, gridType)` + DOM id **`#memberDetailPopup`**：既有的「**可編輯**點名/探訪/代禱」彈窗（與本功能的唯讀細節不同用途）。
- 因此本功能（唯讀、且含地址/信仰狀態/關係目標 + 聚會/裝備兩子網格）刻意採**不同命名以免覆蓋**：
  - 細節彈窗 id＝**`#memberInfoDetailPopup`**、開啟函式＝**`openMemberInfoDetailPopup`**。
  - 照片/姓名儲存格＝`new JS("memberInfoAvatarCellTemplate")` / `new JS("memberInfoNameCellTemplate")`（仿既有 `renderContactAvatar`，但點擊開的是唯讀彈窗）。
- 可選優化（非必做）：把 `renderContactAvatar`／批次預載抽到共用 `wwwroot/js/contact-avatar.js`，供兩個網格共用以免重複（見文末「後續可選」）。

## File Structure（建立/修改總覽）

**新增**
- `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs` — 存取等級常數。
- `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs` — 由職稱+登入型別解析存取等級（純邏輯）。
- `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs` — 範圍白名單判定（純邏輯）。
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs` — 細節彈窗資料 + 關係目標清單。
- `ChurchReport/Controllers/MemberInfoController.cs` — Index / LoadMemberInfoList / Detail / LoadContactPresentRecords / LoadContactStorLessons + 範圍輔助。
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml` — 會友資訊網格頁。
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml` — 唯讀細節彈窗（含兩子網格）。
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` — 純邏輯單元測試（net10.0, xUnit）。
- `ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`

**修改**
- `ChurchReport/Controllers/BaseChurchController.cs` — 新增 `SetupMemberInfoViewBag()`，並於 `SetupBasicViewBag()` 末尾呼叫。
- `ChurchReport/Views/Shared/_Layout.cshtml` — 新增「會友資訊」`<li>`（依 `ViewBag.MemberInfoAccess`）。

---

## Task 1: 建立純邏輯單元測試專案

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
- Create: `ChurchReport.MemberInfo.Tests/SanityTest.cs`

- [ ] **Step 1: 建立測試專案 csproj**

Create `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`:

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

- [ ] **Step 2: 加一個 sanity 測試**

Create `ChurchReport.MemberInfo.Tests/SanityTest.cs`:

```csharp
using Xunit;
using FluentAssertions;

namespace ChurchReport.MemberInfo.Tests;

public class SanityTest
{
    [Fact]
    public void Sanity_TrueIsTrue()
    {
        true.Should().BeTrue();
    }
}
```

- [ ] **Step 3: 加入方案**

Run: `dotnet sln "ChurchReport.sln" add "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: `Project ... added to the solution.`

- [ ] **Step 4: 還原並執行測試**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: PASS（1 passed）。

> 疑難排解：若參考 Web Exe 專案造成 `Program` 衝突或建置錯誤，於本測試 csproj 的 `<PropertyGroup>` 加 `<GenerateProgramFile>false</GenerateProgramFile>` 後重試。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport.MemberInfo.Tests ChurchReport.sln
git commit -m "test: 建立 MemberInfo 純邏輯單元測試專案"
```

---

## Task 2: `MemberInfoAccessResolver`（角色→存取等級，TDD）

**Files:**
- Create: `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`
- Create: `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs`

- [ ] **Step 1: 先寫常數類別（讓測試可編譯）**

Create `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`:

```csharp
namespace ChurchReport.Services.MemberInfo
{
    /// <summary>會友資訊存取等級常數。</summary>
    public static class MemberInfoAccess
    {
        public const string Church = "全教會";      // 牧師傳道 / 牧養主任
        public const string ShepherdList = "牧養名單"; // 帶領牧養小組者
        // 無存取權則回傳 null（不顯示會友資訊）
    }
}
```

- [ ] **Step 2: 寫失敗測試**

Create `ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs`:

```csharp
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoAccessResolverTests
{
    [Theory]
    [InlineData("牧師傳道")]
    [InlineData("牧養主任")]
    [InlineData("主任牧師、牧養主任")]   // 包含即可
    [InlineData("  牧師傳道  ")]          // 前後空白
    public void Resolve_JobTitleContainsPastorRole_ReturnsChurch(string jobTitle)
    {
        MemberInfoAccessResolver.Resolve(jobTitle, "小組長")
            .Should().Be(MemberInfoAccess.Church);
    }

    [Fact]
    public void Resolve_PastorRoleWins_OverShepherd()
    {
        // 同時是牧者也帶小組 → 全教會優先
        MemberInfoAccessResolver.Resolve("牧養主任", "小組長")
            .Should().Be(MemberInfoAccess.Church);
    }

    [Fact]
    public void Resolve_GroupLeaderWithoutPastorRole_ReturnsShepherdList()
    {
        MemberInfoAccessResolver.Resolve("核心同工", "小組長")
            .Should().Be(MemberInfoAccess.ShepherdList);
    }

    [Theory]
    [InlineData("", "個人回報")]
    [InlineData("會計", "個人回報")]
    [InlineData(null, null)]
    [InlineData("會友", "")]
    public void Resolve_NoQualifyingRole_ReturnsNull(string? jobTitle, string? loginType)
    {
        MemberInfoAccessResolver.Resolve(jobTitle, loginType)
            .Should().BeNull();
    }
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: FAIL（找不到 `MemberInfoAccessResolver`）。

- [ ] **Step 4: 實作 resolver**

Create `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`:

```csharp
namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// 依教會職稱與登入型別解析會友資訊存取等級（純函式、無外部相依，便於單元測試）。
    /// 規則：職稱含「牧師傳道」或「牧養主任」→ 全教會；否則 LoginType=="小組長" → 牧養名單；皆非 → null。
    /// </summary>
    public static class MemberInfoAccessResolver
    {
        public static string? Resolve(string? churchJobTitle, string? loginType)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();

            if (jobTitle.Contains("牧師傳道") || jobTitle.Contains("牧養主任"))
            {
                return MemberInfoAccess.Church;
            }

            if (string.Equals((loginType ?? string.Empty).Trim(), "小組長", System.StringComparison.Ordinal))
            {
                return MemberInfoAccess.ShepherdList;
            }

            return null;
        }
    }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: PASS（全部）。

- [ ] **Step 6: Commit**

```bash
git add ChurchReport/Services/MemberInfo ChurchReport.MemberInfo.Tests/MemberInfoAccessResolverTests.cs
git commit -m "feat: 新增 MemberInfoAccessResolver 角色存取解析（含測試）"
```

---

## Task 3: `MemberInfoScopeGuard`（範圍白名單，TDD）

**Files:**
- Create: `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`

- [ ] **Step 1: 寫失敗測試**

Create `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardTests
{
    private static readonly HashSet<string> Shepherd = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
    };

    [Fact]
    public void Church_AllowsAnyNonEmptyId()
    {
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeTrue();
    }

    [Fact]
    public void Shepherd_AllowsIdInOwnList()
    {
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "22222222-2222-2222-2222-222222222222").Should().BeTrue();
    }

    [Fact]
    public void Shepherd_DeniesIdNotInOwnList()
    {
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeniesWhenRequestedIdMissing(string? requested)
    {
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, requested)
            .Should().BeFalse();
    }

    [Fact]
    public void DeniesWhenNoAccess()
    {
        MemberInfoScopeGuard.IsContactAllowed(null, Shepherd,
            "11111111-1111-1111-1111-111111111111").Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj" --filter MemberInfoScopeGuardTests`
Expected: FAIL（找不到 `MemberInfoScopeGuard`）。

- [ ] **Step 3: 實作 guard**

Create `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`:

```csharp
using System.Collections.Generic;

namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// 伺服器端範圍白名單判定（純函式）。防止以任意 contactId 越權查詢。
    /// 全教會：允許任一非空 id；牧養名單：requestedId 必須在自己名單成員集合內；無存取權：一律拒絕。
    /// </summary>
    public static class MemberInfoScopeGuard
    {
        public static bool IsContactAllowed(
            string? access,
            IReadOnlyCollection<string> shepherdContactIds,
            string? requestedContactId)
        {
            if (string.IsNullOrWhiteSpace(requestedContactId))
            {
                return false;
            }

            if (access == MemberInfoAccess.Church)
            {
                return true;
            }

            if (access == MemberInfoAccess.ShepherdList)
            {
                if (shepherdContactIds == null)
                {
                    return false;
                }

                foreach (var id in shepherdContactIds)
                {
                    if (string.Equals(id, requestedContactId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: PASS（全部）。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs
git commit -m "feat: 新增 MemberInfoScopeGuard 範圍白名單判定（含測試）"
```

---

## Task 4: 導覽旗標 `SetupMemberInfoViewBag()` 與 `_Layout` 入口

**Files:**
- Modify: `ChurchReport/Controllers/BaseChurchController.cs`（`SetupBasicViewBag` 區段，約 478-487 行）
- Modify: `ChurchReport/Views/Shared/_Layout.cshtml`（約 463 行「奉獻」之前）

- [ ] **Step 1: 在 `BaseChurchController` 新增方法並於 `SetupBasicViewBag` 呼叫**

在 `BaseChurchController.cs` 的 `SetupBasicViewBag()` 方法末端（`SetupFeeDataListCount();` 之後）加一行呼叫，並於其後新增方法：

```csharp
        // 於 SetupBasicViewBag() 內，SetupFeeDataListCount(); 之後加：
        SetupMemberInfoViewBag();
```

於同檔 `#region ViewBag 設定輔助方法` 內新增：

```csharp
        /// <summary>
        /// 設定「會友資訊」導覽存取旗標 ViewBag.MemberInfoAccess。
        /// 依登入者 new_church_jobtitle 與 LoginType 解析；結果以 Session 快取避免每請求重算。
        /// </summary>
        protected void SetupMemberInfoViewBag()
        {
            try
            {
                // ✅ 修正：只快取「正向」結果。
                // 避免在登入連絡人尚未載入時，把「無權限」用空字串永久寫死該 Session，
                // 導致之後即使連絡人已載入、按鈕仍永遠不顯示。
                var cached = HttpContext?.Session?.GetString("_MemberInfoAccess");
                if (!string.IsNullOrEmpty(cached))
                {
                    ViewBag.MemberInfoAccess = cached;
                    return;
                }

                // 取得登入者 contact（若尚未載入則嘗試載入）
                var pim = InMemoryContext?.PersonalInfomationModel;
                if (pim != null && pim.m_LoginContact == null)
                {
                    try { pim.SetPersonalInfomationViewModel(); } catch { /* 載入失敗則本次視為「未判定」 */ }
                }

                var loginContact = pim?.m_LoginContact;
                if (loginContact == null)
                {
                    // 尚無法判定（連絡人未載入）→ 本次不顯示且「不快取」，下次請求再算
                    ViewBag.MemberInfoAccess = null;
                    return;
                }

                string jobTitle = ToolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? "";
                string loginType = InMemoryContext?.ListManager?.LoginType ?? "";
                string access = ChurchReport.Services.MemberInfo.MemberInfoAccessResolver.Resolve(jobTitle, loginType);

                // 僅在「確定有權限」時快取；無權限不寫快取，避免資料尚未就緒時誤鎖整個 Session
                if (!string.IsNullOrEmpty(access))
                {
                    HttpContext?.Session?.SetString("_MemberInfoAccess", access);
                }
                ViewBag.MemberInfoAccess = access;
            }
            catch
            {
                // 任何意外都不擋頁面，僅不顯示按鈕（且不快取）
                ViewBag.MemberInfoAccess = null;
            }
        }
```

> 註：`m_LoginContact`、`SetPersonalInfomationViewModel()`、`ListManager.LoginType`、`GetEntityStringAttribute(ref Entity,…)` 皆為既有用法（見 `PersonalController.cs:789`、`PersonalController.ImageUpload.cs:217`、`BaseChurchController.cs:480`）。

- [ ] **Step 2: 在 `_Layout.cshtml` 加入導覽項**

在 `Views/Shared/_Layout.cshtml` 找到（約第 463 行）：

```cshtml
                        <li><a href="/Dedication/QPayView/網頁登入"><i class="fas fa-donate"></i>奉獻</a></li>
```

在其**前面**插入：

```cshtml
                        @if (ViewBag.MemberInfoAccess == "全教會" || ViewBag.MemberInfoAccess == "牧養名單")
                        {
                            <li><a href="/MemberInfo/Index"><i class="fas fa-id-card"></i>會友資訊</a></li>
                        }
```

- [ ] **Step 3: 建置**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"`
Expected: Build succeeded（0 error）。

- [ ] **Step 4: 手動驗證（登入點擊）**

啟動應用並分別以下列帳號登入，確認左側導覽：
- 職稱含「牧養主任/牧師傳道」的連絡人 → 顯示「會友資訊」。
- 一般小組長（無上述職稱、但帶小組）→ 顯示「會友資訊」。
- 一般組員（無職稱、非小組長）→ **不**顯示「會友資訊」。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Controllers/BaseChurchController.cs ChurchReport/Views/Shared/_Layout.cshtml
git commit -m "feat: 會友資訊導覽旗標與入口（依角色顯示）"
```

---

## Task 5: `MemberInfoController` 骨架 + 牧養名單網格

**Files:**
- Create: `ChurchReport/Controllers/MemberInfoController.cs`
- Create: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 建立 Controller（Index + 牧養名單版 LoadMemberInfoList + 範圍輔助）**

Create `ChurchReport/Controllers/MemberInfoController.cs`:

```csharp
using ChurchReport.Models;
using ChurchReport.Services.MemberInfo;
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
    /// <summary>
    /// 會友資訊：連絡人網格 + 唯讀細節彈窗（聚會紀錄/裝備紀錄）。
    /// 牧師傳道/牧養主任 → 全教會；小組長 → 自己名單。所有 contactId 端點皆做範圍白名單。
    /// </summary>
    public class MemberInfoController : BaseChurchController
    {
        public MemberInfoController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
        }

        /// <summary>取得目前登入者之存取等級（沿用 SetupMemberInfoViewBag 的 Session 快取）。</summary>
        private string GetAccess()
        {
            SetupMemberInfoViewBag();
            return ViewBag.MemberInfoAccess as string;
        }

        /// <summary>
        /// ✅ 確保登入者的小組名單清單（m_MultiGroupList）已載入。
        /// 一般情況下 EnsureCorrectUserData() 會在 Session/密碼變動時重建 ListManager；
        /// 此處對「清單仍為空」的邊界情形再保險一次（沿用既有 SetupListManager 重建作法）。
        /// </summary>
        private void EnsureShepherdListsLoaded()
        {
            var lm = InMemoryContext?.ListManager;
            if (lm == null) return;

            var loaded = lm.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if ((loaded == null || loaded.Count == 0) && !string.IsNullOrEmpty(lm.m_Password))
            {
                lm.SetupListManager(
                    lm.m_Account,
                    lm.m_Password,
                    lm.m_SelectDate != default ? lm.m_SelectDate : DateTime.Now);
            }
        }

        /// <summary>牧養名單者「自己名單成員」的 contactId 白名單（含成員 fullname 對照）。</summary>
        private Dictionary<string, string> GetShepherdMembers()
        {
            // key = contactId(小寫), value = fullname
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnsureShepherdListsLoaded(); // ✅ 確保名單已載入再讀
            var multiGroupList = InMemoryContext?.ListManager?.m_MultiGroupList;
            if (multiGroupList?.m_WeeklyReportRecordListData == null) return result;

            var toolUtility = ToolUtility;
            foreach (var groupRecord in multiGroupList.m_WeeklyReportRecordListData)
            {
                if (!Guid.TryParse(groupRecord.ListEntityId, out var listGuid)) continue;
                var memberCollection = toolUtility.RetrieveMemberListCollectionByListId(listGuid);
                if (memberCollection?.Entities == null) continue;

                foreach (var memberEntity in memberCollection.Entities)
                {
                    var contactId = toolUtility.GetEntityLookupAttribute(memberEntity, "entityid");
                    if (contactId == Guid.Empty) continue;
                    if (!result.ContainsKey(contactId.ToString()))
                    {
                        var contact = toolUtility.m_Crm2011OrganizationService.Retrieve(
                            "contact", contactId, new ColumnSet("fullname"));
                        result[contactId.ToString()] = toolUtility.GetEntityStringAttribute(contact, "fullname");
                    }
                }
            }
            return result;
        }

        [HttpGet]
        [Route("/MemberInfo")]
        [Route("/MemberInfo/Index")]
        public IActionResult Index()
        {
            try
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

                var access = ViewBag.MemberInfoAccess as string;
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return RedirectToAction("DisplayErrorView", "Home",
                        new { ErrorMessage = "您沒有檢視會友資訊的權限" });
                }

                return View("MemberInfoGrid");
            }
            catch (Exception e)
            {
                return HandleError(e, "MemberInfo.Index");
            }
        }

        /// <summary>網格資料：牧養名單版（全教會版於 Task 6 補上）。</summary>
        [HttpGet]
        public object LoadMemberInfoList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();

                if (access == MemberInfoAccess.ShepherdList)
                {
                    var members = LoadShepherdListMembers();
                    return DataSourceLoader.Load(members, loadOptions);
                }

                // 全教會於 Task 6 實作；此處先回空避免未授權外洩
                return DataSourceLoader.Load(new List<Member>(), loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "MemberInfo.LoadMemberInfoList");
            }
        }

        /// <summary>牧養名單模式：逐名單載入成員（照片由前端以 contactId 載入）。</summary>
        private List<Member> LoadShepherdListMembers()
        {
            var list = new List<Member>();
            EnsureShepherdListsLoaded(); // ✅ 確保名單已載入再讀
            var multiGroupList = InMemoryContext?.ListManager?.m_MultiGroupList;
            if (multiGroupList?.m_WeeklyReportRecordListData == null) return list;

            var toolUtility = ToolUtility;
            foreach (var groupRecord in multiGroupList.m_WeeklyReportRecordListData)
            {
                if (!Guid.TryParse(groupRecord.ListEntityId, out var listGuid)) continue;
                var memberCollection = toolUtility.RetrieveMemberListCollectionByListId(listGuid);
                if (memberCollection?.Entities == null) continue;

                foreach (var memberEntity in memberCollection.Entities)
                {
                    var contactId = toolUtility.GetEntityLookupAttribute(memberEntity, "entityid");
                    if (contactId == Guid.Empty) continue;

                    var contact = toolUtility.m_Crm2011OrganizationService.Retrieve(
                        "contact", contactId, new ColumnSet("fullname", "mobilephone"));

                    list.Add(new Member
                    {
                        ContactId = contactId.ToString(),
                        FullName = toolUtility.GetEntityStringAttribute(contact, "fullname"),
                        Phone = toolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                        SmallGroupName = groupRecord.Name
                    });
                }
            }
            return list;
        }
    }
}
```

- [ ] **Step 2: 建立網格視圖**

Create `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`:

```cshtml
@using ChurchReport.Models
@{
    ViewBag.Title = "會友資訊";
}

<div style="font-size:120%; color:darkcyan; font-weight:bold; margin:8px 0;">
    <h4>會友資訊</h4>
</div>

<div id="memberinfo-grid">
    @(Html.DevExtreme().DataGrid<Member>()
        .ID("MemberInfoGridContainer")
        .ShowBorders(true)
        .ColumnAutoWidth(true)
        .ShowRowLines(true)
        .RowAlternationEnabled(true)
        .Paging(p => p.PageSize(20).Enabled(true))
        .Scrolling(s => s.RowRenderingMode(GridRowRenderingMode.Virtual))
        .SearchPanel(s => s.Visible(true).Placeholder("搜尋姓名.."))
        .RemoteOperations(true) // 全教會走伺服器端；牧養名單回固定清單亦可運作
        .Columns(columns =>
        {
            // 照片：採用專案既有慣例 new JS(函式名) 模板（DevExtreme 不支援 <%- %> inline 模板）
            columns.Add()
                .DataField("ContactId")
                .Caption("照片")
                .Width(56).MinWidth(56)
                .AllowEditing(false).AllowSorting(false).AllowFiltering(false)
                .Fixed(true)
                .CellTemplate(new JS("memberInfoAvatarCellTemplate"));

            // 姓名：可點，開啟「唯讀」細節彈窗（與 _GeneralGroupGrids 既有可編輯的 openMemberDetailPopup 不同）
            columns.AddFor(m => m.FullName).Caption("姓名").Width(120)
                .CellTemplate(new JS("memberInfoNameCellTemplate"));

            columns.AddFor(m => m.Phone).Caption("手機").Width(140);
            columns.AddFor(m => m.SmallGroupName).Caption("小組").Width(160);
        })
        .DataSource(d => d.Mvc()
            .Controller("MemberInfo")
            .LoadAction("LoadMemberInfoList")
            .Key("ContactId")
        )
    )
</div>

<!-- 細節彈窗本體於 Task 7 加入 -->

<script>
    // 照片儲存格模板（仿 _GeneralGroupGrids.cshtml 的 renderContactAvatar，但不綁定既有點名彈窗）
    window.memberInfoAvatarCellTemplate = function (container, options) {
        var d = options.data || {};
        var contactId = d.ContactId || d.contactId || options.value || '';
        var host = container && container.get ? container.get(0) : container;
        if (!host) return;
        host.textContent = '';
        var fallback = 'data:image/svg+xml;utf8,' + encodeURIComponent(
            '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><circle cx="20" cy="20" r="20" fill="#e9ecef"/><text x="20" y="25" font-size="14" text-anchor="middle" fill="#6c757d">人</text></svg>');
        var img = document.createElement('img');
        img.src = contactId ? ('/Personal/GetContactImage?contactId=' + encodeURIComponent(contactId) + '&size=48') : fallback;
        img.onerror = function () { if (img.src !== fallback) img.src = fallback; };
        img.alt = '大頭照'; img.loading = 'lazy';
        img.style.cssText = 'width:40px;height:40px;border-radius:50%;object-fit:cover;border:2px solid #e0e0e0;display:block;margin:0 auto;';
        host.appendChild(img);
    };

    // 姓名儲存格模板：可點 → 開啟唯讀細節彈窗
    window.memberInfoNameCellTemplate = function (container, options) {
        var d = options.data || {};
        var contactId = d.ContactId || d.contactId || '';
        var fullName = d.FullName || d.fullName || '';
        var host = container && container.get ? container.get(0) : container;
        if (!host) return;
        var a = document.createElement('a');
        a.href = 'javascript:void(0)';
        a.textContent = fullName;
        a.style.cssText = 'color:#3b5bdb;font-weight:bold;text-decoration:underline;cursor:pointer;';
        a.addEventListener('click', function (e) { e.preventDefault(); openMemberInfoDetailPopup(contactId, fullName); });
        host.appendChild(a);
    };

    // 真正的彈窗於 Task 7 實作；此處先存根（名稱刻意異於既有 openMemberDetailPopup，避免衝突）
    function openMemberInfoDetailPopup(contactId, fullName) {
        console.log("openMemberInfoDetailPopup", contactId, fullName);
    }
</script>
```

- [ ] **Step 3: 建置**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"`
Expected: Build succeeded。

- [ ] **Step 4: 手動驗證**

以「小組長」登入 → 點左側「會友資訊」→ 網格出現照片/姓名/手機/小組，且只含自己名單成員。點姓名暫時只在 console 印出。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat: 會友資訊網格 + 牧養名單資料來源"
```

---

## Task 6: 全教會資料來源（啟用且非結案＋伺服器端分頁/搜尋）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（`LoadMemberInfoList` 的全教會分支）

- [ ] **Step 1: 實作全教會分頁查詢**

把 `LoadMemberInfoList` 內「全教會於 Task 6 實作」那段，替換為呼叫新方法；並於 controller 內新增下列方法：

```csharp
        // LoadMemberInfoList 內，將全教會占位改為：
        if (access == MemberInfoAccess.Church)
        {
            return LoadChurchWideContacts(loadOptions);
        }
```

新增方法（同 `MemberInfoController` 內）：

```csharp
        /// <summary>全教會模式：CRM 伺服器端分頁，啟用且會員身分≠結案，支援姓名搜尋。</summary>
        private object LoadChurchWideContacts(DataSourceLoadOptions loadOptions)
        {
            var toolUtility = ToolUtility;
            var svc = toolUtility.m_Crm2011OrganizationService;

            int take = loadOptions.Take > 0 ? loadOptions.Take : 20;
            int skip = loadOptions.Skip > 0 ? loadOptions.Skip : 0;
            int pageNumber = (skip / take) + 1;

            // 解析「結案」會員身分的 OptionSet 值（找不到則不排除）
            int? closedValue = null;
            try
            {
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    svc, null, new MemoryCache(new MemoryCacheOptions()));
                var mapping = optionSetService.GetOptionSetMapping("contact", "customertypecode");
                if (mapping != null && mapping.TryGetValue("結案", out var v)) closedValue = v;
            }
            catch { /* 無法解析則略過排除 */ }

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "mobilephone"),
                PageInfo = new PagingInfo
                {
                    Count = take,
                    PageNumber = pageNumber,
                    ReturnTotalRecordCount = true
                }
            };
            query.AddOrder("fullname", OrderType.Ascending);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // 啟用
            if (closedValue.HasValue)
            {
                query.Criteria.AddCondition("customertypecode", ConditionOperator.NotEqual, closedValue.Value);
            }

            // 搜尋：DevExtreme SearchPanel 會帶 searchValue（對 fullname 模糊）
            var search = loadOptions.SearchValue as string;
            if (!string.IsNullOrWhiteSpace(search))
            {
                query.Criteria.AddCondition("fullname", ConditionOperator.Like, "%" + search.Trim() + "%");
            }

            var ec = svc.RetrieveMultiple(query);
            var data = ec.Entities.Select(c => new Member
            {
                ContactId = c.Id.ToString(),
                FullName = toolUtility.GetEntityStringAttribute(c, "fullname"),
                Phone = toolUtility.GetEntityStringAttribute(c, "mobilephone"),
                SmallGroupName = "" // OQ-2：全教會的「小組」欄位來源待定，先留白
            }).ToList();

            // DevExtreme 伺服器端格式：{ data, totalCount }
            return new
            {
                data = data,
                totalCount = ec.TotalRecordCount >= 0 ? ec.TotalRecordCount : data.Count
            };
        }
```

> 註：`ec.TotalRecordCount` 於 `ReturnTotalRecordCount=true` 時有效（CRM 上限約 5000；超過回 -1，故以 `data.Count` 後備）。`GetEntityStringAttribute(Entity,…)`（非 ref 版）見 `PersonalController.cs:291`。

- [ ] **Step 2: 建置**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"`
Expected: Build succeeded。

- [ ] **Step 3: 手動驗證**

以「牧養主任」登入 → 「會友資訊」網格顯示全教會啟用連絡人；翻頁正常；用搜尋框輸入姓名片段可過濾；結案者不出現。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat: 會友資訊全教會伺服器端分頁查詢（啟用且非結案）"
```

---

## Task 7: 唯讀細節彈窗（大頭貼 + 基本資訊 + 子導覽）

**Files:**
- Create: `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（新增 `Detail`）
- Create: `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`（`openMemberDetail` + Popup）

- [ ] **Step 1: 建立細節 ViewModel**

Create `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`:

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
        public string SpiritualIdentity { get; set; } // 信仰狀態文字
        public List<RelationGoalItem> RelationGoals { get; set; } = new List<RelationGoalItem>();
    }

    public class RelationGoalItem
    {
        public string Role { get; set; }       // 角色（連接角色名稱）
        public string TargetName { get; set; } // 對象姓名
    }
}
```

- [ ] **Step 2: 新增 `Detail` 動作（含範圍把關，關係目標於 Task 10 補）**

於 `MemberInfoController` 新增：

```csharp
        [HttpGet]
        [Route("/MemberInfo/Detail")]
        public IActionResult Detail(string contactId)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();

                // 範圍把關（純邏輯已單元測試）
                var shepherd = access == MemberInfoAccess.ShepherdList
                    ? (IReadOnlyCollection<string>)GetShepherdMembers().Keys.ToList()
                    : System.Array.Empty<string>();

                if (!MemberInfoScopeGuard.IsContactAllowed(access, shepherd, contactId)
                    || !Guid.TryParse(contactId, out var guid))
                {
                    return StatusCode(403, "無權檢視此連絡人");
                }

                var toolUtility = ToolUtility;
                var contact = toolUtility.m_Crm2011OrganizationService.Retrieve(
                    "contact", guid,
                    new ColumnSet("contactid", "fullname", "mobilephone",
                                  "address2_line1", "new_spiriitual_identity"));

                string spiritualText = "";
                if (contact.Contains("new_spiriitual_identity"))
                {
                    var val = toolUtility.GetOptionSetAttribute(contact, "new_spiriitual_identity");
                    try
                    {
                        var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                            toolUtility.m_Crm2011OrganizationService, null,
                            new MemoryCache(new MemoryCacheOptions()));
                        spiritualText = optionSetService.GetOptionSetText("contact", "new_spiriitual_identity", val);
                    }
                    catch { spiritualText = ""; }
                }

                var vm = new ChurchReport.ViewModels.MemberInfoDetailViewModel
                {
                    ContactId = guid.ToString(),
                    FullName = toolUtility.GetEntityStringAttribute(contact, "fullname"),
                    MobilePhone = toolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                    Address = toolUtility.GetEntityStringAttribute(contact, "address2_line1"),
                    SpiritualIdentity = spiritualText,
                    RelationGoals = GetRelationGoals(guid) // Task 10 實作；先回空清單
                };

                return PartialView("_MemberDetailPopup", vm);
            }
            catch (Exception e)
            {
                return HandleError(e, "MemberInfo.Detail");
            }
        }

        // Task 10 會以連接角色實作；先提供空實作確保編譯與安全降級
        private List<ChurchReport.ViewModels.RelationGoalItem> GetRelationGoals(Guid contactId)
        {
            return new List<ChurchReport.ViewModels.RelationGoalItem>();
        }
```

- [ ] **Step 3: 建立彈窗 partial（大頭貼 + 資訊 + 子導覽 + 兩個子網格容器）**

Create `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`:

```cshtml
@model ChurchReport.ViewModels.MemberInfoDetailViewModel

<div style="display:flex; gap:16px; align-items:flex-start;">
    <!-- 左：大頭貼 + 子導覽 -->
    <div style="flex:0 0 200px;">
        <img src="/Personal/GetContactImage?contactId=@Model.ContactId&size=0"
             style="width:180px;height:180px;border-radius:10px;object-fit:cover;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,.25);" />
        <div style="margin-top:12px; display:flex; flex-direction:column; gap:8px;">
            <button type="button" class="dx-btn-subnav" data-tab="present"
                    onclick="memberDetailSwitch('present')"
                    style="padding:10px;border:none;border-radius:6px;cursor:pointer;background:#667eea;color:#fff;">聚會紀錄</button>
            <button type="button" class="dx-btn-subnav" data-tab="equip"
                    onclick="memberDetailSwitch('equip')"
                    style="padding:10px;border:none;border-radius:6px;cursor:pointer;background:#adb5bd;color:#fff;">裝備紀錄</button>
        </div>
    </div>

    <!-- 右：基本資訊 + 內容區 -->
    <div style="flex:1 1 auto;">
        <h3 style="margin:0 0 8px;">@Model.FullName</h3>
        <table style="width:100%; border-collapse:collapse;">
            <tr><td style="color:#868e96;width:90px;">手機</td><td>@Model.MobilePhone</td></tr>
            <tr><td style="color:#868e96;">地址</td><td>@Model.Address</td></tr>
            <tr><td style="color:#868e96;">信仰狀態</td><td>@Model.SpiritualIdentity</td></tr>
            <tr>
                <td style="color:#868e96;vertical-align:top;">關係目標</td>
                <td>
                    @if (Model.RelationGoals != null && Model.RelationGoals.Count > 0)
                    {
                        foreach (var r in Model.RelationGoals)
                        {
                            <div>@r.Role：@r.TargetName</div>
                        }
                    }
                    else
                    {
                        <span style="color:#adb5bd;">（無）</span>
                    }
                </td>
            </tr>
        </table>

        <hr />
        <div id="member-subgrid-present" data-contact-id="@Model.ContactId"></div>
        <div id="member-subgrid-equip" data-contact-id="@Model.ContactId" style="display:none;"></div>
    </div>
</div>

<script>
    // 子導覽切換（子網格於 Task 8/9 初始化）
    function memberDetailSwitch(tab) {
        var present = document.getElementById('member-subgrid-present');
        var equip = document.getElementById('member-subgrid-equip');
        document.querySelectorAll('.dx-btn-subnav').forEach(function (b) {
            b.style.background = (b.getAttribute('data-tab') === tab) ? '#667eea' : '#adb5bd';
        });
        present.style.display = (tab === 'present') ? 'block' : 'none';
        equip.style.display = (tab === 'equip') ? 'block' : 'none';
        if (typeof window.initMemberSubGrids === 'function') {
            window.initMemberSubGrids(tab, present.getAttribute('data-contact-id'));
        }
    }
</script>
```

- [ ] **Step 4: 在網格頁加入 Popup 並實作 `openMemberInfoDetailPopup`**

在 `MemberInfoGrid.cshtml`：(1) 加入下列 Popup 元件；(2) 把 Task 5 的 `openMemberInfoDetailPopup` 存根替換為下列實作。**保留** `memberInfoAvatarCellTemplate`／`memberInfoNameCellTemplate` 兩函式。彈窗 id 用 `memberInfoDetailPopup`，**刻意異於既有 `#memberDetailPopup`**（`_GeneralGroupGrids` 的可編輯點名彈窗）以免衝突。

```cshtml
<!-- 唯讀細節彈窗（id 與既有 #memberDetailPopup 不同） -->
@(Html.DevExtreme().Popup()
    .ID("memberInfoDetailPopup")
    .Width("80%")
    .Height("80%")
    .Title("會友細節")
    .ShowTitle(true)
    .Visible(false)
    .DragEnabled(true)
    .HideOnOutsideClick(true)
)

<script>
    // 取代 Task 5 的同名存根
    function openMemberInfoDetailPopup(contactId, fullName) {
        var popup = $("#memberInfoDetailPopup").dxPopup("instance");
        popup.option("title", fullName + " － 會友細節");
        popup.option("contentTemplate", function (contentElement) {
            $(contentElement).html('<div style="padding:16px;">載入中...</div>');
            $.ajax({
                url: '/MemberInfo/Detail',
                type: 'GET',
                data: { contactId: contactId },
                success: function (html) {
                    $(contentElement).html(html);
                    if (typeof memberDetailSwitch === 'function') { memberDetailSwitch('present'); } // 預設顯示聚會紀錄
                },
                error: function (xhr) {
                    $(contentElement).html('<div style="padding:16px;color:#c92a2a;">載入失敗：' +
                        (xhr.status === 403 ? '無權檢視此連絡人' : '請稍後再試') + '</div>');
                }
            });
        });
        popup.show();
    }
</script>
```

- [ ] **Step 5: 建置 + 手動驗證**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"` → Build succeeded。
手動：點網格姓名 → 彈窗出現大頭貼、手機、地址、信仰狀態、關係目標（先顯示「（無）」）、左側兩顆子導覽按鈕可切換（內容區尚空）。

- [ ] **Step 6: Commit**

```bash
git add ChurchReport/ViewModels/MemberInfoDetailViewModel.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
git commit -m "feat: 會友資訊唯讀細節彈窗（含範圍把關與子導覽）"
```

---

## Task 8: 聚會紀錄子網格（個人靈修與聚會紀錄）

> **範圍：只查「被點擊彈出的那一位連絡人」。** 端點 `LoadContactPresentRecords` 必收 `contactId`（來自彈窗 `@Model.ContactId`），以 `RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)` 取「該連絡人」之 `new_present_record`；不得載入其他人或全教會資料。同一連絡人可有多筆（多週）紀錄，故需唯一列鍵。

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（新增 `LoadContactPresentRecords` + DTO）
- Modify: `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`（初始化聚會紀錄子網格）

- [ ] **Step 1: 新增端點與列 DTO**

於 `MemberInfoController.cs` 命名空間內（class 外）新增 DTO，並於 controller 新增動作：

```csharp
    /// <summary>聚會紀錄列（個人靈修與聚會紀錄）。每位連絡人可有多筆（多週）。</summary>
    public class ContactPresentRecordRow
    {
        public string PresentRecordId { get; set; } // 唯一列鍵（同一連絡人多筆，不可用 FullName 當 key）
        public string FullName { get; set; }
        public bool Sunday { get; set; }      // 主日
        public bool SmallGroup { get; set; }  // 小組
        public string PrayItem { get; set; }  // 代禱
    }
```

```csharp
        [HttpGet]
        public object LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();

                var shepherdMap = access == MemberInfoAccess.ShepherdList ? GetShepherdMembers() : null;
                var shepherd = shepherdMap != null
                    ? (IReadOnlyCollection<string>)shepherdMap.Keys.ToList()
                    : System.Array.Empty<string>();

                if (!MemberInfoScopeGuard.IsContactAllowed(access, shepherd, contactId)
                    || !Guid.TryParse(contactId, out var guid))
                {
                    return DataSourceLoader.Load(new List<ContactPresentRecordRow>(), loadOptions);
                }

                var toolUtility = ToolUtility;
                var fullName = toolUtility.GetEntityStringAttribute(
                    toolUtility.m_Crm2011OrganizationService.Retrieve("contact", guid, new ColumnSet("fullname")),
                    "fullname");

                var rows = new List<ContactPresentRecordRow>();
                var records = toolUtility.RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId);
                if (records?.Entities != null)
                {
                    foreach (var rec in records.Entities)
                    {
                        var full = toolUtility.RetrieveEntity("new_present_record", rec.Id);
                        rows.Add(new ContactPresentRecordRow
                        {
                            PresentRecordId = rec.Id.ToString(),
                            FullName = fullName,
                            Sunday = toolUtility.GetEntityIntAttribute(full, "new_sunday_present_this_week") > 0,
                            SmallGroup = toolUtility.GetEntityIntAttribute(full, "new_group_present_this_week") > 0,
                            PrayItem = toolUtility.GetEntityStringAttribute(full, "new_explanation")
                        });
                    }
                }

                return DataSourceLoader.Load(rows, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "MemberInfo.LoadContactPresentRecords");
            }
        }
```

> 註：屬性名來自 `ListSmallGroupWeeklyReport.cs:248-253`（`new_sunday_present_this_week` / `new_group_present_this_week` / `new_explanation`）。`GetEntityIntAttribute` 為既有方法。

- [ ] **Step 2: 在彈窗初始化聚會紀錄子網格**

在 `_MemberDetailPopup.cshtml` 的 `<script>` 內、`memberDetailSwitch` 之後新增：

```javascript
    window._memberSubGridInit = window._memberSubGridInit || { present: false, equip: false };

    window.initMemberSubGrids = function (tab, contactId) {
        if (tab === 'present' && !window._memberSubGridInit.present) {
            window._memberSubGridInit.present = true;
            $('#member-subgrid-present').dxDataGrid({
                dataSource: DevExpress.data.AspNet.createStore({
                    key: 'PresentRecordId',
                    loadUrl: '/MemberInfo/LoadContactPresentRecords',
                    loadParams: { contactId: contactId }   // 只查此彈出連絡人，非全教會
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
    };
```

> `DevExpress.data.AspNet.createStore` 由既有腳本 `dx.aspnet.data.js`（已於 `_Layout` 載入）提供。

- [ ] **Step 3: 建置 + 手動驗證**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"` → succeeded。
手動：開彈窗 → 預設「聚會紀錄」顯示姓名/主日/小組/代禱；無資料時顯示空網格。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml
git commit -m "feat: 會友細節－聚會紀錄子網格"
```

---

## Task 9: 裝備紀錄子網格（上課紀錄單）

> **範圍：只查「被點擊彈出的那一位連絡人」。** 端點 `LoadContactStorLessons` 必收 `contactId`（來自彈窗 `@Model.ContactId`），以 `RetrieveStorLessonsByFetchXml(fullName, contactId)` 取「該連絡人」之上課紀錄；不得載入其他人或全教會資料。

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（新增 `LoadContactStorLessons`）
- Modify: `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`（初始化裝備紀錄子網格）

- [ ] **Step 1: 新增端點（複用 `EquipmentStorLessons` 映射）**

於 `MemberInfoController.cs` 新增（並確保檔頭 `using ChurchReport.Models;` 已有）：

```csharp
        [HttpGet]
        public object LoadContactStorLessons(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();

                var shepherd = access == MemberInfoAccess.ShepherdList
                    ? (IReadOnlyCollection<string>)GetShepherdMembers().Keys.ToList()
                    : System.Array.Empty<string>();

                if (!MemberInfoScopeGuard.IsContactAllowed(access, shepherd, contactId)
                    || !Guid.TryParse(contactId, out var guid))
                {
                    return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
                }

                var toolUtility = ToolUtility;
                var fullName = toolUtility.GetEntityStringAttribute(
                    toolUtility.m_Crm2011OrganizationService.Retrieve("contact", guid, new ColumnSet("fullname")),
                    "fullname");

                var lessonsList = new List<EquipmentStorLessons>();
                var storLessons = toolUtility.RetrieveStorLessonsByFetchXml(fullName, contactId);
                if (storLessons?.Entities != null)
                {
                    foreach (var lessonEntity in storLessons.Entities)
                    {
                        var lesson = lessonEntity;
                        var discipleLessonId = toolUtility.GetEntityLookupAttribute(
                            ref lesson, "new_new_disciple_lessons_new_stor_les");

                        DateTime classStartDate = DateTime.MinValue;
                        string stageName = string.Empty;
                        if (discipleLessonId != Guid.Empty)
                        {
                            try
                            {
                                var discipleLesson = toolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId);
                                classStartDate = toolUtility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date");
                                stageName = toolUtility.GetEntityStringAttribute(ref discipleLesson, "new_now_stage_name");
                            }
                            catch { /* 取不到關聯課程則留空 */ }
                        }

                        lessonsList.Add(new EquipmentStorLessons
                        {
                            StorLessonsEntityId = lesson.Id.ToString(),
                            DiscipleLessonsName = toolUtility.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les"),
                            StageName = stageName,
                            CurrentComplete = toolUtility.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
                            DiscipleLessonsDateTime = classStartDate
                        });
                    }
                }

                return DataSourceLoader.Load(lessonsList, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "MemberInfo.LoadContactStorLessons");
            }
        }
```

> 映射邏輯與 `EquipmentController.LoadEquipmentStorLessons`（306-353 行）一致，差別在直接以 contactId 查詢而非從記憶體成員清單反查。

- [ ] **Step 2: 在彈窗初始化裝備紀錄子網格**

在 `_MemberDetailPopup.cshtml` 的 `window.initMemberSubGrids` 函式內、`present` 區塊之後新增 `equip` 區塊：

```javascript
        if (tab === 'equip' && !window._memberSubGridInit.equip) {
            window._memberSubGridInit.equip = true;
            $('#member-subgrid-equip').dxDataGrid({
                dataSource: DevExpress.data.AspNet.createStore({
                    key: 'StorLessonsEntityId',
                    loadUrl: '/MemberInfo/LoadContactStorLessons',
                    loadParams: { contactId: contactId }   // 只查此彈出連絡人，非全教會
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

- [ ] **Step 3: 建置 + 手動驗證**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"` → succeeded。
手動：彈窗點「裝備紀錄」→ 顯示課程名稱/日期/階段名稱/是否結業；對有上課記錄者資料正確。

- [ ] **Step 4: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml
git commit -m "feat: 會友細節－裝備紀錄子網格"
```

---

## Task 10: 關係目標（連接/連接角色）

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`（實作 `GetRelationGoals`）

- [ ] **Step 1: 以連接實體實作 `GetRelationGoals`（安全降級）**

把 `MemberInfoController` 的 `GetRelationGoals` 占位實作替換為：

```csharp
        /// <summary>
        /// 關係目標：查 CRM connection 實體（本人為 record1id），取連接角色名與對象姓名。
        /// 若環境未啟用 connection 或查詢失敗，安全回傳空清單。
        /// </summary>
        private List<ChurchReport.ViewModels.RelationGoalItem> GetRelationGoals(Guid contactId)
        {
            var result = new List<ChurchReport.ViewModels.RelationGoalItem>();
            try
            {
                var svc = ToolUtility.m_Crm2011OrganizationService;
                var query = new QueryExpression("connection")
                {
                    ColumnSet = new ColumnSet("record2id", "record2idname", "connectionroleid")
                };
                query.Criteria.AddCondition("record1id", ConditionOperator.Equal, contactId);

                var ec = svc.RetrieveMultiple(query);
                foreach (var conn in ec.Entities)
                {
                    string role = "";
                    if (conn.Contains("connectionroleid") &&
                        conn["connectionroleid"] is EntityReference roleRef)
                    {
                        role = roleRef.Name ?? "";
                    }

                    string targetName = conn.Contains("record2idname")
                        ? conn.GetAttributeValue<string>("record2idname")
                        : (conn.Contains("record2id") && conn["record2id"] is EntityReference r2 ? r2.Name : "");

                    if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(targetName))
                    {
                        result.Add(new ChurchReport.ViewModels.RelationGoalItem
                        {
                            Role = role,
                            TargetName = targetName
                        });
                    }
                }
            }
            catch
            {
                // 未啟用 connection / 權限不足 → 視為無關係目標
            }
            return result;
        }
```

- [ ] **Step 2: 建置 + 手動驗證**

Run: `dotnet build "ChurchReport/ChurchReport.csproj"` → succeeded。
手動：對 CRM 中設有「連接」的連絡人開彈窗 → 「關係目標」列出「角色：對象」；無連接者顯示「（無）」且不報錯。

> ⚠️ OQ-1：若教會實際以某 `new_xxx` 欄位而非連接角色表示關係目標，改寫此方法即可（介面與彈窗不變）。

- [ ] **Step 3: Commit**

```bash
git add ChurchReport/Controllers/MemberInfoController.cs
git commit -m "feat: 會友細節－關係目標（連接角色，含安全降級）"
```

---

## Task 11: 端到端整合驗證與越權測試

**Files:**（僅驗證與必要修正）

- [ ] **Step 1: 全測試綠燈**

Run: `dotnet test "ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj"`
Expected: PASS（全部）。

- [ ] **Step 2: 三角色端到端**

- 牧養主任：會友資訊＝全教會、可搜尋分頁、彈窗四項資訊＋兩子網格皆正常。
- 小組長：會友資訊＝只含自己名單成員、彈窗正常。
- 一般組員：左側無「會友資訊」。
- **每位連絡人專屬**：分別點開連絡人 A 與 B，確認「聚會紀錄」「裝備紀錄」顯示的是各自的紀錄（A≠B），且筆數與 CRM 該人實際紀錄一致（非全教會彙總）。

- [ ] **Step 3: 越權（IDOR）驗證**

以「小組長」登入後，於瀏覽器直接呼叫不在自己名單之 contactId：
`/MemberInfo/Detail?contactId=<他人GUID>`、`/MemberInfo/LoadContactPresentRecords?contactId=<他人GUID>`、`/MemberInfo/LoadContactStorLessons?contactId=<他人GUID>`
Expected: Detail 回 403；兩個 Load 回空資料（不外洩）。

- [ ] **Step 4: 視需要修正後 Commit**

```bash
git add -A
git commit -m "test: 會友資訊端到端與越權驗證修正"
```

---

## Self-Review（規格涵蓋對照）

| 規格需求 | 對應任務 |
|---|---|
| 左側導覽「會友資訊」入口 | Task 4 |
| 網格：照片/姓名/手機/小組 | Task 5（牧養名單）, Task 6（全教會） |
| 點姓名彈出唯讀細節（大頭貼/手機/地址/信仰狀態/關係目標） | Task 7, Task 10 |
| 子導覽 聚會紀錄（姓名/主日/小組/代禱）— **只限該彈出連絡人** | Task 8 |
| 子導覽 裝備紀錄（課程名稱/日期/階段名稱/是否結業）— **只限該彈出連絡人** | Task 9 |
| 需求3：牧師傳道/牧養主任→全教會現行（啟用且非結案＋分頁） | Task 2（解析）, Task 4（旗標）, Task 6（資料） |
| 需求4：小組長→只看自己名單 | Task 2（解析）, Task 5（資料）, Task 3/各端點（白名單） |
| 伺服器端越權防護 | Task 3, Task 7/8/9（套用）, Task 11（驗證） |
| 唯讀（不寫回 CRM） | 全程無更新端點 |

**Placeholder/一致性檢查**：型別與方法名跨任務一致（`MemberInfoAccess.Church/ShepherdList`、`Resolve`、`IsContactAllowed`、`GetAccess`、`EnsureShepherdListsLoaded`、`GetShepherdMembers`、`GetRelationGoals`、`ContactPresentRecordRow`、`EquipmentStorLessons`、`MemberInfoDetailViewModel/RelationGoalItem`；前端 `memberInfoAvatarCellTemplate`／`memberInfoNameCellTemplate`／`openMemberInfoDetailPopup`／`#memberInfoDetailPopup` 皆與既有 `_GeneralGroupGrids` 命名區隔，避免覆蓋）。`GetRelationGoals` 於 Task 7 先以空實作引入、Task 10 補完。**三項缺口修正已併入**：①照片/姓名改用 `new JS(...)` 模板（Task 5、沿用 `contactImageCellTemplate` 慣例）；②`EnsureShepherdListsLoaded` 確保 `m_MultiGroupList` 載入再讀（Task 5）；③`SetupMemberInfoViewBag` 只快取「正向」結果避免誤鎖（Task 4）。OQ-1／OQ-2 以可運作預設＋註記處理，非程式碼占位。

---

## 後續可選（非本計畫必做）

- 把 `_GeneralGroupGrids.cshtml` 的 `renderContactAvatar`／`preloadContactImages`（批次預載）抽到共用 `wwwroot/js/contact-avatar.js`，讓小組牧養網格與會友資訊網格共用一份大頭照渲染邏輯，消除重複。抽取時 `renderContactAvatar` 的點擊行為需參數化（既有開 `openMemberDetailPopup`、會友資訊開 `openMemberInfoDetailPopup`）。
- 會友資訊網格亦可改用 `/Personal/GetContactImagesBatch` 批次預載大頭照（目前為逐格載入，伺服器端已有 MemoryCache，效能可接受）。
