# 調查報告：小組回報頁面間歇性出現重複姓名

- 日期：2026-09-07
- 分支：`Fbllc_4.9.9.8.5_Fix_Qualification`
- 專案：ChurchReport（ASP.NET Core MVC + DevExtreme 21.2.7 + Dynamics 365 / Dataverse）
- 調查者：Claude（僅靜態程式碼分析 + 網路文獻查證，**未執行、未在執行期驗證**）

---

## 0. 給審閱者（Codex）

請你獨立評估以下內容，我特別想知道：

1. 我的主假設 **H1（並行載入競態）** 是否成立？推論鏈有沒有斷點？
2. 我自己標記為「最弱環節」的地方（§3.4 相鄰成對的解釋）你認為站不站得住腳？如果不成立，還有什麼機制能產生「每人剛好兩筆且相鄰」？
3. 我是否遺漏了更簡單的解釋（Occam）？特別是我可能沒看到的路徑。
4. §5 的鑑別測試設計是否足以區分 H1 / H2？有沒有更好的測試？
5. §8 的修復草案有沒有副作用或更好的做法？
6. **§3.6 的推論**：我論證「Δt ≈ 0 而 W ≈ 數百毫秒 → 一旦進入載入路徑碰撞機率接近 1」，因此間歇性必須由前置條件（`LoadFlag` 是否為 false）解釋，而非由競態本身解釋。這個推論對嗎？§3.6 末尾的 P1-a~g 清單有沒有遺漏？

**請直接反駁我。** 我沒有執行期證據，這份報告全部建立在靜態閱讀之上，很可能有錯。

### 閱讀本專案原始碼的注意事項

- 大量 `.cs` 檔案是 **Big5 編碼**，用 UTF-8 讀會看到亂碼註解（例如 `�p�պ޲z���`）。**這是編碼問題，不是程式碼損毀**，不要據此下結論。
- `.worktrees/` 目錄下有多份舊版副本，**請忽略**，只看 `ChurchReport/ChurchReport/` 主樹。
- 本報告所有路徑相對於 `ChurchReport/ChurchReport/`（即 `.csproj` 所在目錄），除非另外標明。

---

## 1. 症狀

使用者（小組長）在手機 LINE 內建瀏覽器開啟「小組回報」頁面（`/SmallGroup/IntegrateView/...`），「小組牧養」表格中**每個人的姓名連續出現兩次**。

從使用者提供的截圖觀察到的關鍵特徵：

| 觀察 | 內容 |
|---|---|
| 重複形式 | 每人**恰好 2 筆**，且**彼此相鄰**（`初X昇, 初X昇, 章X紘, 章X紘, 李X采, 李X采, …`） |
| 整體順序 | 小組長×2 → 小組組員×2×N → 牧師師母×2 |
| 範圍 | 截圖可見的**所有人**都被複製 |
| 頻率 | **間歇性**。使用者原話：「不是每一次都這樣，有些人有、有些人沒有，有時候有、有時候沒有」 |
| 使用者直覺 | 懷疑與「網頁暫存」有關 |
| 日期脈絡 | 頁面標示「小組日期對應到主日期間是: 2026/9/5 ~ 2026/9/11」，小組日期選 2026/9/6 |

---

## 2. 資料如何走到畫面（這一節是事實，不是推論）

### 2.1 前端

`Views/Home/IntegrateView.cshtml` 這個頁面在載入時會**同時發出 3 個 AJAX 請求**：

| # | 端點 | 宣告位置 |
|---|---|---|
| 1 | `/SmallGroup/GetChartDataList` | `Views/Home/IntegrateView.cshtml:44` |
| 2 | `/SmallGroup/LoadIntegrate` | `Views/Home/_GeneralGroupGrids.cshtml:169` |
| 3 | `/NewPerson/LoadNewPersonFollowUp` | `Views/Home/_GeneralGroupGrids.cshtml:403` |

「小組牧養」grid 的設定（`_GeneralGroupGrids.cshtml:36-200`）：

```csharp
.RemoteOperations(ro => ro.Filtering(false).Sorting(false).Paging(false)
                          .Grouping(false).Summary(false))
.RepaintChangesOnly(true)
.CacheEnabled(true)
.Scrolling(s => s.Mode(GridScrollingMode.Virtual)
                 .RowRenderingMode(GridRowRenderingMode.Virtual))
.Paging(p => p.PageSize(100).Enabled(true))
.DataSource(d => d.WebApi()
    .Controller("SmallGroup").LoadAction("LoadIntegrate")
    .Key("PresentRecordId")
    .LoadParams(new { id = @ViewBag.ListId }))
```

重點：**`RemoteOperations` 全 false**，代表伺服器回傳完整清單、前端自行分頁排序。所以畫面上的重複 = 伺服器回傳的陣列裡真的有兩筆。**不是前端分頁邏輯造成的。**

### 2.2 後端讀取

`Controllers/SmallGroupController/SmallGroupController.DataApi.cs:81-133`

```csharp
[HttpGet]
public object LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
{
    EnsureCorrectUserData();
    EnsureIntegrateDataLoaded(id);

    var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
        .m_SmallGroupDataList.m_SmallGroupData.Members;

    return DataSourceLoader.Load(tasks, loadOptions);   // 直接回傳，無去重
}
```

`Controllers/SmallGroupController/SmallGroupController.IntegrateView.cs:103-110`

```csharp
private void EnsureIntegrateDataLoaded(string id)
{
    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
    if (weeklyReport == null || !weeklyReport.LoadFlag)
        InMemoryContext.ListManager.SetupIntegrateData(id);
}
```

`Controllers/NewPersonController.cs:110-119` 有**逐字相同**的另一份：

```csharp
private void EnsureNewPersonDataLoaded(string id)
{
    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
    if (weeklyReport == null || !weeklyReport.LoadFlag)
        InMemoryContext.ListManager.SetupIntegrateData(id);   // 同一個共用物件
}
```

### 2.3 共用狀態的來源

`Models/InMemoryDataContextSmallGroup.cs:512-551`：`ListManager` 由 `IMemoryCache` 以 session id 為 key 取出，絕對/滑動過期各 30 分鐘。

```csharp
public ListManager ListManager
{
    get
    {
        var key = GetCurrentSessionId() + "_ListManager";
        if (_memoryCache.Get(key) == null)
        {
            var options = new MemoryCacheEntryOptions();
            options.SetAbsoluteExpiration(DateTime.Now.AddMinutes(30));
            options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
            m_ListManager = new ListManager();
            _memoryCache.Set<ListManager>(key, m_ListManager, options);
            SetSessionDirtyFlag();
        }
        return _memoryCache.Get<ListManager>(key);
    }
}
```

`IInMemoryDataContext` 註冊為 **Scoped**（`Startup.cs:477`），但它取出的 `ListManager` 來自 **共用的 IMemoryCache**，所以**同一 session 的並行請求拿到的是同一個 `ListManager` 物件實例**。

### 2.4 載入流程

`Models/ListManager.cs:207-230`

```csharp
public ListSmallGroupWeeklyReport m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();  // ← 第 40 行，行內初始化
DownloadIntegrateData m_DownloadIntegrateData = new DownloadIntegrateData();                        // ← 第 47 行，共用實例

public void SetupIntegrateData(String ListEntityId)
{
    WeeklyReportRecord aWeeklyReportRecord =
        m_MultiGroupList.m_WeeklyReportRecordListData.FirstOrDefault(e => e.ListEntityId == ListEntityId);

    if (aWeeklyReportRecord != null)
    {
        if (m_ListSmallGroupWeeklyReport == null)
        {
            m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();
            m_ListSmallGroupWeeklyReport.LoadFlag = true;
        }
        else
        { }                                    // ← 沿用舊物件

        m_ListSmallGroupWeeklyReport.ListEntityId = ListEntityId;
        ...
        m_DownloadIntegrateData.SetupIntegrateData(m_Account, m_Password, LoginType, this.m_SelectDate,
            ListEntityId, aWeeklyReportRecord.WeeklyReportEntityId, ref m_ListSmallGroupWeeklyReport);
    }
}
```

`WebServiceConnector/DownloadIntegrateData.Core.cs:92-117`

```csharp
public void SetupIntegrateData(..., ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
{
    this.m_LoginType = LoginType;
    this.m_Sunday = CalculateSunday(aDownloadDate);
    this.SetupHeaderData(...);          // ← LoadFlag 在這裡才被設 true
    this.SetupShepherdData(...);        // ← 成員清單在這裡被填
    this.SetupWeeklyReportData(...);
    this.SetupWeeklyReportChartData(...);
}
```

`WebServiceConnector/DownloadIntegrateData.Setup.cs:18-52`

```csharp
public void SetupHeaderData(string Account, string Password, ...)
{
    FindLoginUser(Account, Password);            // ← CRM 查詢（網路往返）
    if (m_ContactId == Guid.Empty) return;

    aListSmallGroupWeeklyReport.LoadFlag = true; // ← 唯一有效的 LoadFlag = true
    ...
}
```

`FindLoginUser`（`DownloadIntegrateData.Core.cs:146-158`）是實打實的 CRM 往返：

```csharp
private void FindLoginUser(string Account, string Password)
{
    if (Account != "LineIdLogin")
        this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
    else
        this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
    this.m_ContactId = m_ContactEntity.Id;
}
```

`WebServiceConnector/DownloadIntegrateData.Setup.cs:62-92`

```csharp
public void SetupShepherdData(string ListEntityId, string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport r)
{
    r.m_SmallGroupDataList = new SmallGroupDataList();     // ← 整個換掉
    this.GetAllMemeberDataList(ListEntityId, WeeklyReportEntityId, ref r);
    if (!r.GroupType.Contains("幸福")) { SetSmallGroupData(ref r); SetNewPersonFollowUpData(ref r); }
    else { SetHappyGroupData(ref r); }
    ...
    SortAndCleanMemberStatus(ref r);
}
```

`WebServiceConnector/DownloadIntegrateData.Members.cs:23-33、92-99、297`

```csharp
// 23-33
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData = new SmallGroupData
{
    Members = new List<Member>(),
    LoginType = aListSmallGroupWeeklyReport.LoginType
};

// 92-99：一筆出席記錄 = 一列
foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
    ProcessPresentRecordEntityWithCache(GroupName, PresentRecordEntity, contactCache, ref aListSmallGroupWeeklyReport);

// 297：每次 Add 都重新走物件圖解析目標清單
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(member);
```

### 2.5 排序

`WebServiceConnector/DownloadIntegrateData.Setup.cs:97-118`

```csharp
private void SortAndCleanMemberStatus(ref ListSmallGroupWeeklyReport report)
{
    report...m_SmallGroupData.Members = report...m_SmallGroupData.Members.OrderBy(o => o.Status).ToList();
    ...
    RemoveNumericAndBlank(report...m_SmallGroupData.Members);   // 排序後才把 "07. " 這種數字前綴拿掉
}
```

`OrderBy` 是 **LINQ 穩定排序**，而且**只以 Status 為鍵**。同一身分別（例如全部「07. 小組組員」）的成員，**維持插入順序**。

→ **結論：畫面上相鄰的兩筆，代表它們在 `Members` 清單裡就是相鄰的。**

### 2.6 CRM 查詢

`ToolUtility/QueryOperations/ComplexQueryService.cs:134-176`（注意：在 `ToolUtility/` 專案，不在主專案）

```csharp
public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
    string parentEntityId, string associationName, string childEntityName)
{
    // filter: 父 id = parentEntityId AND statecode = 0
    var link = new LinkEntity { LinkCriteria = filter,
        LinkFromEntityName = childEntityName,      // new_present_record
        LinkFromAttributeName = associationName,
        LinkToAttributeName = parentEntityIdName,
        LinkToEntityName = parentEntityName };     // new_group_present_weekly_report

    var query = new QueryExpression { EntityName = childEntityName };
    query.ColumnSet.AllColumns = true;
    query.LinkEntities.Add(link);
    // ← 沒有 Distinct、沒有 Orders、沒有 PagingInfo
    return ((RetrieveMultipleResponse)_crmClient.Execute(new RetrieveMultipleRequest { Query = query })).EntityCollection;
}
```

- Join 方向是 present_record → weekly_report，屬 **N:1**，所以 join 本身**不會**放大列數。
- **沒有 `Distinct`，沒有 `Orders`** → 回傳順序未定義。
- 整條路徑上，`Distinct()` 只出現在 `Members.cs:131` / `Members.cs:488`，且**只作用於要批次撈取的 contact id 陣列，不作用於顯示清單**。

### 2.7 全域檢查結果

- **載入路徑（`WebServiceConnector/` 下的 Download*）完全沒有任何 `lock`。** 整個 `WebServiceConnector/` 唯一的 `lock` 在**上傳**路徑：`UploadIntegrateData.Core.cs:130`（`static readonly object m_UploadDataLocker`）。
- 沒有任何地方對 `Members` 依 `PresentRecordId` 或 `ContactId` 去重。

---

## 3. 主假設 H1：同一 session 的並行請求同時重建共用清單

### 3.1 前提條件（皆已在 §2 用程式碼證實）

| # | 事實 | 位置 |
|---|---|---|
| E1 | 同一頁面同時發出 3 個 AJAX | `IntegrateView.cshtml:44`、`_GeneralGroupGrids.cshtml:169/403` |
| E2 | 其中 2 個各自執行**相同、無鎖**的 check-then-act | `IntegrateView.cs:107`、`NewPersonController.cs:116` |
| E3 | 兩者操作的是**同一個** `ListManager` 物件（IMemoryCache 共用） | `InMemoryDataContextSmallGroup.cs:512` |
| E4 | ASP.NET Core **不對 session 上鎖**，同 session 請求真正並行 | 見 §7 文獻 |
| E5 | `List<Member>.Add` 非執行緒安全，且每次重新解析物件圖 | `Members.cs:297` |
| E6 | 排序穩定且只依 Status → 相鄰即為插入相鄰 | `Setup.cs:97-118` |
| E7 | 全路徑無鎖、無去重 | §2.7 |

### 3.2 競態視窗有多大（**這是本次調查最重要的發現**）

我一開始以為視窗只有微秒級。實際追下去發現**視窗是一次 CRM 網路往返**：

`ListManager.cs:40` 把 `m_ListSmallGroupWeeklyReport` **行內初始化為 new 物件**：

```csharp
public ListSmallGroupWeeklyReport m_ListSmallGroupWeeklyReport = new ListSmallGroupWeeklyReport();
```

因此在單組長情境下它**永不為 null**，`ListManager.cs:216-219` 的

```csharp
if (m_ListSmallGroupWeeklyReport == null) { ...; m_ListSmallGroupWeeklyReport.LoadFlag = true; }
```

**是死碼，永遠不會執行**（`LoadFlag` 也就不會在此提前設 true）。

`LoadFlag` 屬性預設值為 `false`（`Models/ListSmallGroupWeeklyReport.cs:34`，`bool` auto-property）。

全專案 `LoadFlag = true` 的有效賦值只有 `DownloadIntegrateData.Setup.cs:34`，而它在 `FindLoginUser()` **之後**。

**所以競態視窗 = 從控制器的 `!LoadFlag` 檢查，到 `FindLoginUser` 這個 CRM 查詢回來為止。** 對 Dataverse 而言這通常是 **100ms ~ 2s+**。瀏覽器在同一毫秒送出的兩個 AJAX，**極容易雙雙通過檢查**。

（例外：多小組使用者。`SmallGroupController.MultiGroupView.cs:66-68` 會明確把它設成 `null`：

```csharp
InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag = false;
InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport = null;
```

此時 `ListManager.cs:216` 分支才會成立、`LoadFlag` 提早被設 true，視窗反而變窄。但 `ShouldLoadIntegrateData`（`IntegrateView.cs:74-86`）對 `MultiGroupView` **無條件回傳 true**，每次都重載。）

### 3.3 兩條執行緒同時跑會發生什麼

`SetupShepherdData` 與 `GetAllMemeberDataList` 都是「先 `new` 一個新清單指派給共用欄位，再逐筆 `Add`」，而 `Add` 每次都重新解析 `r.m_SmallGroupDataList.m_AllMemeberData.Members`。可能的交錯：

```
A: r.m_SmallGroupDataList = new (obj1)
B: r.m_SmallGroupDataList = new (obj2)          ← A 的 obj1 被丟棄
A: obj2.m_AllMemeberData = new (listA)
B: obj2.m_AllMemeberData = new (listB)          ← listA 被丟棄
A: 迴圈 Add N 筆 → 解析到 listB
B: 迴圈 Add N 筆 → 解析到 listB
→ listB 共 2N 筆
```

另外，`m_DownloadIntegrateData` 是 `ListManager` 的**單一共用實例**（`ListManager.cs:47`），而 `DownloadIntegrateData` 帶有 `m_ListEntity`、`m_WeeklyReportEntity`、`m_ContactId`、`m_Sunday`、`m_LoginType` 等**可變實例欄位**。兩條執行緒並行呼叫會互相覆寫這些欄位 —— 這是另一類獨立的汙染風險（可能撈到別組、別週的資料）。

### 3.4 相鄰成對如何解釋（**我認為這是本報告最弱的一環**）

要得到 `A1,B1,A2,B2,...` 這種完美配對，需要兩條執行緒在 `Add` 迴圈中近乎同步地交錯。

**對我不利的論點**：`BatchRetrieveContacts` 已經先把所有 contact 撈進 Dictionary，所以 `ProcessPresentRecordEntityWithCache` 的迴圈是**純記憶體運算**，20 列大概只要微秒級。兩條執行緒要在微秒級迴圈中對 20 列**每一列**都完美交錯，機率不高。

**對我有利的論點**：兩條執行緒執行的是**完全相同的程式碼、相同的資料、相同的長度**，且啟動時間相近，在多核心上天然容易保持同步節奏；而且截圖只看得到約 10 個人，不需要「20 列全對」。

**但我必須誠實說：這個環節我無法用靜態分析證明。** 見 §4 的 H2，它可以毫不費力地解釋完美配對。

### 3.5 H1 如何解釋間歇性

| 現象 | H1 的解釋 |
|---|---|
| 有時候有、有時候沒有 | 需要兩個請求落在同一個 CRM 往返視窗內，純時序 |
| 重新整理常常就好了 | 第二次載入時 `LoadFlag` 已是 true，兩個請求都跳過載入，直接讀既有清單 |
| 有些人有、有些人沒有 | 交錯點不同 → 部分區段被複製 |
| 使用者覺得像「暫存」 | `ListManager` 快取 30 分鐘；快取有效時完全正常，過期後的第一次載入才可能出事。行為上確實**很像**快取問題，但根因不是暫存 |
| LINE 內建瀏覽器較常見 | **與使用者的行動網路速度無關**（見下方 §3.6）。若確有相關，比較可能的原因是 LINE 內建瀏覽器的返回／bfcache 行為讓 HTML 不經伺服器重新渲染，導致 AJAX 到達時 `LoadFlag` 仍為 false |

### 3.6 競態視窗由誰決定（重要：不要誤判成使用者端問題）

碰撞條件為 **Δt < W**：

- **W（視窗寬度）= 100% 伺服器端。** 等於「控制器檢查 `!LoadFlag`」到「`Setup.cs:34` 設 `LoadFlag = true`」的耗時，其中絕大部分是 `FindLoginUser()` 對 Dataverse 的一次往返。**使用者的手機效能、4G/Wi-Fi 速度對此毫無影響。**
- 另外請注意：**只有 `FindLoginUser` 之前的耗時算數**。`SetupShepherdData` 撈 20 筆成員、`SetupWeeklyReportChartData` 撈圖表資料再慢，都發生在 `LoadFlag = true` 之後，**不影響競態視窗**。
- **Δt（兩個請求的到達時間差）≈ 0**，因為三個 AJAX 由同一次頁面初始化幾乎同時送出。使用者端網路延遲對兩個請求是**共同偏移**而非差值，因此慢網路只是把兩者一起往後推，不會顯著改變 Δt。

**推論**：既然 Δt ≈ 0 而 W ≈ 數百毫秒，一旦進入載入路徑，碰撞機率接近 1。**因此本案的間歇性不來自競態本身，而來自「AJAX 到達時 `LoadFlag` 是否恰為 false」這個前置條件**。這是本假設中一個需要審閱者特別檢視的推論。

正常流程下 HTML 請求（`SetupIntegrateViewData`，`IntegrateView.cs:56-66`）就已經完成載入並把 `LoadFlag` 設為 true，等 AJAX 送達時三者都會跳過載入 —— **這正是「大多數時候正常」的原因**。因此需要解釋的是：什麼情況會讓 AJAX 到達時 `LoadFlag` 仍為 false？候選清單（標示責任歸屬）：

| # | 觸發條件 | 歸屬 |
|---|---|---|
| P1-a | `ListManager` 的 30 分鐘快取在 HTML 回應與 AJAX 之間到期／被驅逐 | 伺服器 |
| P1-b | `IMemoryCache` 因記憶體壓力提前逐出項目（compaction，非確定性） | 伺服器 |
| P1-c | HTML 走瀏覽器快取／bfcache／返回上一頁 → 伺服器端未執行頁面渲染載入 | **客戶端** |
| P1-d | 多小組使用者路徑明確設定 `LoadFlag = false` 且將物件設為 null（`MultiGroupView.cs:66-68`），且 `ShouldLoadIntegrateData` 對 MultiGroupView 無條件回傳 true | 伺服器（邏輯） |
| P1-e | 上傳流程 3 秒逾時視為成功 + `grid.refresh()`（`IntegrateView.cshtml:150-160`），伺服器可能仍在寫入 | 伺服器＋前端邏輯 |
| P1-f | 變更小組日期觸發 `UpdateIntegrateDate` 全量重載（`Date.cs:137`）與 grid 重新整理重疊 | 伺服器＋前端邏輯 |
| P1-g | IIS 多 worker process／web garden／多台機器 → `IMemoryCache` 與 `static` 鎖跨不過 process 邊界 | 部署架構 |

**請審閱者特別檢視這張表是否有遺漏**，因為它才是「為什麼時好時壞」的真正答案來源。

---

## 4. 次要假設

### H2：CRM 裡真的有重複的 `new_present_record`

若同一份週報下同一位聯絡人有兩筆 present record，畫面必然出現兩列（§2.6 無 Distinct、§2.7 無去重）。

支持 H2 的程式碼證據：

**(a) 建立出席記錄完全沒有「是否已存在」檢查** —— `WebServiceConnector/UploadIntegrateData.PresentRecord.cs:19-52`：

```csharp
foreach (Member aMemberInfomation in aSmallGroupData.Members)
{
    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ...);  // 無條件 Create
    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);
    if (aPresentRecord != null) PresentRecordEntityCollection.Entities.Add(aPresentRecord);
}
```

**(b) 週報數量判斷會讓問題滾雪球** —— `Tools/WeeklyReportProcessor.cs:157`：

```csharp
Entity GroupWeeklyReportEntity =
    GroupWeeklyReportEntityCollection.Entities.Count == 1
        ? GroupWeeklyReportEntityCollection.Entities[0]
        : null;      // Count == 2 時判定為「沒有」→ 下一步再建第三份
```

**(c) 個人回報路徑會補建** —— `WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:17-47`：找不到自己的出席記錄就 `CreatePresentRecordList(...)`。兩個並行請求都找不到 → 都建立 → 該人多一筆。**這能非常自然地解釋「有些人有、有些人沒有」。**

**(d) 原作者的自白註解** —— `Tools/PersonalQrCodeUtility.cs:275`：

```
// RelateMeetingStatisticsFlag 的作用是如果建立 N 個出席紀錄單，
// 但是我只要有一筆紀錄顯示在聚會統計即可，以免造成聚會統計有N筆掃描紀錄
```

→ **系統設計者早就知道一個人會有 N 筆出席記錄，當時只處理了統計端的症狀，沒有處理根因。**

**(e) 上傳流程的 3 秒超時當成功** —— `Views/Home/IntegrateView.cshtml:150-160`：

```javascript
timeout: 3000,
error: function (xhr, status, error) {
    if (status === "timeout") {
        ShowToast("資料已送出，正在背景上傳中...");
        setTimeout(function () { grid.refresh(); }, 2000);   // 伺服器很可能還在寫
    }
}
```

H2 對「相鄰成對」的解釋比 H1 輕鬆：若 CRM 回傳順序恰好沿著某個名稱索引掃描，同名兩筆自然相鄰。**但 §2.6 的查詢沒有 `Orders`，順序未定義，我無法證明它會依名稱排。**

**H2 的致命弱點**：CRM 資料汙染是**持久性**的。若真是 H2，該週的重複應該**每次開都在**，重新整理不會好。這與使用者描述的「不是每一次都這樣」**矛盾** —— 除非使用者看的是不同週、不同組。

### H3：DevExtreme 虛擬捲動的渲染 bug

兩個 grid 都同時開啟 `Scrolling(Virtual)` + `RowRenderingMode(Virtual)` + `CacheEnabled(true)` + `RepaintChangesOnly(true)`。DevExpress 支援中心有多張關於此組合產生重複列的票（見 §7）。

**我判斷 H3 不是主因**，理由：純渲染 bug 通常複製「一整個視窗區塊」，不會產生「每人恰好兩筆且兩兩相鄰」。但它可能是**加重因子**，尤其 `_GeneralGroupUploadButton.cshtml:45-46` 與 `IntegrateView.cshtml:141/157` 都有 `grid.refresh()`，而 DevExpress 票 T754759 正是「虛擬捲動下呼叫 reload 造成重複列」。

---

## 5. 鑑別測試（能一次分開 H1 與 H2）

在**重現當下**的頁面按 F12，於 Console 執行：

```javascript
copy($("#SmallGroupgridContainer").dxDataGrid("instance")
      .getDataSource().items()
      .map(i => i.FullName + " | " + i.PresentRecordId).join("\n"))
```

判讀：

| 結果 | 結論 |
|---|---|
| 兩列的 `PresentRecordId` **相同** | **H1 成立**（記憶體競態）。CRM 資料是乾淨的，重新整理會好 |
| 兩列的 `PresentRecordId` **不同** | **H2 成立**（CRM 真有重複記錄）。重新整理不會好，需清資料 |

補充測試（若要進一步確認 H1）：在 `ListManager.SetupIntegrateData` 進入/離開處加 log，記錄 `Thread.CurrentThread.ManagedThreadId`、session id、`Stopwatch` 時間戳。若同一 session id 出現**時間區間重疊**的兩次進入，H1 即獲執行期證實。**這個 log 不改變任何邏輯，是零風險的驗證手段。**

---

## 6. 我沒有做到的事（請把這些當成本報告的已知弱點）

1. **沒有任何執行期驗證。** 沒跑過程式、沒重現、沒看過 log、沒查過 CRM 實際資料。
2. **沒有確認 Dataverse `RetrieveMultiple` 在無 `Orders` 時的實際回傳順序**，因此「相鄰」這個特徵我無法歸因到任何一方（H1、H2 都能勉強解釋）。
3. **沒有確認伺服器部署型態。** 若 IIS 有多個 worker process（web garden）或多台機器，`static readonly object m_UploadDataLocker`（`UploadIntegrateData.Core.cs:67`）與 `IMemoryCache` 都**跨不過 process 邊界**，H2 的機率會顯著上升。
4. **沒有確認截圖那一組當週的 CRM 實際記錄筆數。** 這是最直接的證據，我拿不到。
5. **沒有檢查是否有 CRM 外掛（plugin）/ workflow** 也會建立出席記錄。若有，H2 又多一個來源。
6. 使用者說「有些人有、有些人沒有」，但**截圖顯示的是全部人都重複**。這兩件事可能是**兩種不同情境**，也可能使用者指的是不同次的觀察。我在報告中把它們當成同一個現象處理，**這個假設本身可能就是錯的**。

---

## 7. 網路文獻佐證

**ASP.NET Core session 不上鎖（H1 的關鍵前提）**
- [Are sessions blocking in ASP.NET Core? — dotnet/AspNetCore.Docs#12110](https://github.com/dotnet/AspNetCore.Docs/issues/12110) — ASP.NET Core session 為 non-locking，兩個並行請求後寫覆蓋先寫；這是相對 ASP.NET 4.x 的**刻意設計變更**（4.x 會對 session 上鎖並序列化請求）。
- [Possible session state race condition — aspnet/Session#60](https://github.com/aspnet/Session/issues/60)
- [When a Single ASP.NET Client makes Concurrent Requests for Writeable Session Variables — Red Gate Simple Talk](https://www.red-gate.com/simple-talk/development/dotnet-development/single-asp-net-client-makes-concurrent-requests-writeable-session-variables/)

> 這點對本專案特別關鍵：從 ASP.NET 4.x 移植過來的程式碼，會**隱含假設「同一使用者的請求會自動排隊」**，而這個保護在 .NET Core 已經不存在。

**IMemoryCache 取用非原子**
- [CacheExtensions.GetOrCreate{Async} thread-safety — dotnet/runtime#71581](https://github.com/dotnet/runtime/issues/71581) — `GetOrCreate` 不持鎖，並行時 factory 會被執行多次。
- [Thread-safety on MemoryCache — David Guida](https://www.davidguida.net/2023-09-21-thread-safety-memorycache)
- [Caching in .NET — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/caching)

> 本專案 `ListManager` getter 是手寫的 `if (Get(key) == null) Set(key, new ...)`，比 `GetOrCreate` 更不安全。

**DevExtreme 虛擬捲動重複列**
- [DataGrid — Grid Rows Get Duplicated When Virtual Scrolling is Enabled (T1194329)](https://supportcenter.devexpress.com/ticket/details/t1194329/datagrid-grid-rows-get-duplicated-when-virtual-scrolling-is-enabled)
- [DataGrid: Virtual scrolling with duplicate keys — DevExtreme#20725](https://github.com/DevExpress/DevExtreme/issues/20725) — 存在重複 key 時 grid 會不斷重繪；workaround 為 `scrolling.legacyMode = true`。
- [DataGrid duplicates rows if the data source's reload method is called and virtual scrolling is active (T754759)](https://supportcenter.devexpress.com/ticket/details/t754759/datagrid-duplicates-rows-if-the-data-source-s-reload-method-is-called-and-the-virtual)

**Dataverse 防重複**
- [Alternate Keys and Upsert in Dataverse: Reliable Integrations Without Duplicates](https://theaugmenteddev.com/blog/dataverse-alternate-keys-upsert-integrations)
- [Dataverse: Duplicate Detection Rules vs Alternate Keys — Dynamics Chronicles](https://dynamics-chronicles.com/article/dataverse-duplicate-detection-rules-vs-alternate-keys)

---

## 8. 修復草案（尚未實作，僅供審閱）

### 8.0 修復的四個切入點

競態條件需要四個條件**同時成立**才會發生，破壞任何一個即可消除。下表的每個措施都對應破壞其中一個條件，供審閱者檢查涵蓋是否完整：

| 條件 | 本專案的體現 | 對應措施 |
|---|---|---|
| ① 共用的可變狀態 | `ListManager` 來自共用 `IMemoryCache`；`m_DownloadIntegrateData` 為單一共用實例且帶可變欄位 | 措施 4 |
| ② 檢查與動作之間存在空隙 | `!LoadFlag` 檢查 → `FindLoginUser()` CRM 往返 → 才設 `LoadFlag = true` | 措施 3 |
| ③ 多執行緒可同時進入 | 同頁 3 個 AJAX；ASP.NET Core 不對 session 上鎖 | 措施 2 |
| ④ 無鎖 | 整個載入路徑無任何 `lock`／`Semaphore` | 措施 2 |
| （旁路）不具冪等性 | 建立出席記錄無存在性檢查；回傳前不去重 | 措施 1、5、6 |

### 8.1 措施清單

依「先止血、再治本」排序：

| # | 措施 | 位置 | 說明 |
|---|---|---|---|
| 1 | **回傳前去重** | `SmallGroupController.DataApi.cs` `LoadIntegrate`、`NewPersonController` `LoadNewPersonFollowUp` | 依 `PresentRecordId` 做 `GroupBy().Select(g => g.First())`。不解決根因，但**對 H1 與 H2 都有效**，是最低風險的止血 |
| 2 | **序列化同 session 的載入** | `EnsureIntegrateDataLoaded` / `EnsureNewPersonDataLoaded` | 以 session id 為 key 的 `SemaphoreSlim`（**不能用 `lock`**，因為區塊內有 CRM I/O）；取得後**重新檢查 `LoadFlag`**，第二個進入者直接跳過 |
| 3 | **build-then-swap** | `DownloadIntegrateData.Setup.cs` / `Members.cs` | 先建立區域性 `List<Member>` 全部填完，最後**一次性原子指派**回共用物件，取代邊查邊往共用清單塞 |
| 4 | **移除共用可變狀態** | `ListManager.cs:47` | `m_DownloadIntegrateData` 改為每次呼叫新建，或把 `m_ListEntity`/`m_Sunday` 等改為方法參數，消除實例欄位互相覆寫 |
| 5 | **建立改為冪等** | `UploadIntegrateData.PresentRecord.cs:19`、`WeeklyReportProcessor.cs:157` | 建立前先查是否已存在；`Count == 1 ? [0] : null` 改為 `FirstOrDefault()` 並對 `Count > 1` 記錄告警 |
| 6 | **Dataverse Alternate Key** | CRM 設定 | 對 `new_present_record` 的 (週報, 聯絡人) 建立 Alternate Key，讓平台層拒絕重複；建立改用 Upsert |
| 7 | **檢視 grid 設定** | `_GeneralGroupGrids.cshtml` | 若 §5 測試顯示是 H1/H2 以外的渲染問題，再考慮 `scrolling.legacyMode`。**在根因釐清前不要動這裡**，以免遮蔽真正的 bug |

**建議順序**：先做 §5 的鑑別測試 → 依結果決定走 2/3/4（H1）或 5/6（H2）→ 無論哪一種，措施 1 都值得先上，因為它成本極低且雙向有效。

### 8.2 實作時的三個約束（請審閱者確認是否同意）

**(1) 有 I/O 的區塊不可使用 `lock`。** `SetupIntegrateData` 內含多次 CRM 網路呼叫，用 `lock` 會長時間占用執行緒池執行緒，並發下可能造成執行緒饑餓。應使用 `SemaphoreSlim` + `await WaitAsync()`。

**(2) 取得鎖之後必須重新檢查旗標（double-checked locking）。** 否則只是讓兩條執行緒排隊、依序各做一次完整載入，重複依然存在：

```csharp
if (!LoadFlag)                     // 快速路徑，避免每次都排隊
{
    await gate.WaitAsync();
    try
    {
        if (!LoadFlag)             // ← 這一行不能省略
            SetupIntegrateData(id);
    }
    finally { gate.Release(); }
}
```

**(3) 避免「先清空／重建、再逐筆填入共用集合」的模式。** 現行 `SetupShepherdData` → `GetAllMemeberDataList` → `Members.Add(...)`（`Members.cs:297`）正是此模式：重建與填入之間，共用集合處於半完成狀態，且第二條執行緒可同時 `Add`。應改為在區域變數建構完成後一次性指派（`List<T>` 的參考指派為原子操作）：

```csharp
var temp = new List<Member>();
foreach (...) temp.Add(member);          // 區域變數，不影響任何其他執行緒
report.m_SmallGroupDataList.m_AllMemeberData.Members = temp;   // 一次換上
```

**注意**：措施 2/3 只是**縮小或消除競態視窗**。若 §5 測試顯示為 H2（CRM 已存在重複記錄），這些措施**不會**修正既有的髒資料，仍需措施 5/6 及一次資料清理。

---

## 9. 一句話總結

> 我認為主因是 **ASP.NET Core 不對 session 上鎖的前提下，同一頁面的兩個並行 AJAX 各自無鎖地重建同一份共用的成員清單**，而競態視窗大小等於一次 CRM 登入查詢的往返時間（`LoadFlag` 直到 `FindLoginUser` 回來後才被設為 true）。次要可能是 CRM 中真的存在重複的出席記錄。**兩者可用 §5 的 `PresentRecordId` 測試一次分開。**
