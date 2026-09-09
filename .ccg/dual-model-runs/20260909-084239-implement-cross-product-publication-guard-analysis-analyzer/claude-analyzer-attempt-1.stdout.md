# 分析報告：ChurchReport 跨產品資料發布與網路時序防護（唯讀分析，未修改任何檔案）

## 0. 分析範圍與方法

已完整讀取手冊／契約／PRD／design/implement 五份規格文件，並實際讀取以下程式檔案以核對規格與現況落差：
`ListManager.cs`、`ListSmallGroupWeeklyReport.cs`、`SmallGroupDataList.cs`、`SmallGroupController.DataApi.cs`、`SmallGroupController.IntegrateView.cs`、`SmallGroupController.LineLogin.cs`、`SmallGroupController.Save.cs`、`SmallGroupController.Crud.cs`、`NewPersonController.cs`、`IntegrateView.cshtml`、`_GeneralGroupGrids.cshtml`，以及既有測試 `ListManagerIntegratePublicationTests.cs`、`SmallGroupDataListSnapshotIsolationTests.cs`。

---

## 1. 實際可造成同一 `PresentRecordId` 重複發布／渲染的缺口

### 🔴 Critical-1：`EnsureAndGetIntegrateDetachedRead` 的「快取命中」路徑完全跳過 `ValidateIntegrateCandidate`

`ListManager.cs:347-360`：

```csharp
if (m_ListSmallGroupWeeklyReport == null ||
    !m_ListSmallGroupWeeklyReport.LoadFlag ||
    m_PublishedIntegrateLoadKey != requestedKey)
{
    var candidate = m_IntegrateCandidateFactory(listEntityId) ?? throw ...;
    ValidateIntegrateCandidate(candidate, listEntityId);   // ← 唯一驗證重複 key 的地方
    m_ListSmallGroupWeeklyReport = candidate;
    m_PublishedIntegrateLoadKey = requestedKey;
    ActiveListId = listEntityId;
}
return m_ListSmallGroupWeeklyReport.CreateDetachedReadCopy();   // ← key 相同時，直接複製「目前」的狀態，不再驗證
```

`ValidateUniqueRowKeys`（同檔 `408-429`）只在**候選建立瞬間**執行一次。只要 `m_PublishedIntegrateLoadKey == requestedKey`（同一 scope 的後續每一次 AJAX/Razor 讀取都會命中這條路徑），之後任何把 `Member` 直接寫進 `m_SmallGroupData.Members` / `m_NewPersonFollowUpData.Members` 的動作，都不會再被擋下，會原樣被 `CreateDetachedReadSnapshot()` 深拷貝後送進 `DataSourceLoader.Load`。這與手冊 2.2 節「發布前驗證……必須在實際消費該集合的邊界驗證」、design.md 第 13 行「這層是 defense in depth」的設計意圖直接矛盾——目前實作只在**候選建立**時做，不是在**每次交付**時做。

**會製造重複 `PresentRecordId` 的實際寫入路徑（均未走 gate，且都在快取命中窗口內生效）：**

- `SmallGroupController.Crud.cs:34-47` `InsertPresentRecord`：
  `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.InsertMember(values);`
  直接呼叫 `SmallGroupData.InsertMember`，**沒有**經過 `SmallGroupDataList.ExecuteSynchronized`（對照同檔 `UpdateSmallGroupPresentRecord` 正確地呼叫 `dataList.UpdateSmallGroupAndAllMember`）。也沒有呼叫 `EnsureCorrectUserData()`。
- `NewPersonController.cs:154-161` `InsertNewPresentRecord`：同樣直接呼叫 `m_NewPersonFollowUpData.InsertMember(values)`，未鎖、未驗證。
- `NewPersonController.cs:523-536` `HandleSuccessfulNewPersonCreation`：
  ```csharp
  Task.Factory.StartNew(() =>
      InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList
          .AddNewPersonToMember(viewModel),
      TaskCreationOptions.LongRunning);
  ```
  這是背景 fire-and-forget，讀取 `m_ListSmallGroupWeeklyReport` 的時間點是「Task 實際執行時」而非「排程時」，完全繞過 `ListManager.m_IntegratePublicationGate`（只用了 `SmallGroupDataList._syncRoot`）。若 `SaveNewPerson` 因網路重試／使用者連點被觸發兩次且兩次都回傳同一 CRM `PresentRecordId`（`UploadNewPersonToCrm` 沒有 idempotency key 保護，見 Critical-3），兩個背景 Task 會各自把相同 `PresentRecordId` 的 `Member` `Add` 兩次到同一個已發布 List——下一次 `LoadIntegrate`/`LoadNewPersonFollowUp` 若命中快取路徑，會把兩筆相同 ID 原樣吐給 Grid。

**故障模型（不依賴猜測防火牆型號，可在單元測試重現）：**
1. `EnsureAndGetIntegrateDetachedRead` 完成一次候選建立與驗證（MISS path，key 已發布）。
2. 任一上述無鎖寫入路徑，在 key 不變的情況下插入一筆與既有列相同 `PresentRecordId` 的 `Member`。
3. 下一次任何 consumer（`LoadIntegrate`、`LoadNewPersonFollowUp`、`GetChartDataList` 依賴同一 snapshot）呼叫 `EnsureAndGetIntegrateDetachedRead`，因為 key 相同直接走快取命中路徑，重複 ID 未經驗證直接送出。

這是本任務要求的「不得假設兩個不同資料庫 ID」下，**唯一已在程式中證實可行的重複發布路徑**——它不需要兩個 `PresentRecordId`，只需要同一個 ID 被寫入兩次。

### 🟠 Warning-1：Razor 初始渲染持有的是「活的」Session 可變物件，不是 detached snapshot

`SmallGroupController.IntegrateView.cs:103-111`：
```csharp
private IActionResult HandleIntegrateViewLogin(string loginParameter)
{
    ...
    return View("~/Views/Home/IntegrateView.cshtml", InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
}
```
`SmallGroupController.LineLogin.cs:100-101` 同樣：
```csharp
return View("~/Views/Home/IntegrateView.cshtml", InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
```
兩者都直接把 `ListManager` 內部欄位（Session holder 的可變物件圖）交給 Razor，違反 3.3 節「Controller……不得直接取得下列物件的可變參考：……`ListManager` 內部 Members collection」與 Step 9 表格 C 列「Razor 不持有正在修改的共享集合」的完成條件。目前 `_GeneralGroupGrids.cshtml`（`Html.DevExtreme().DataGrid<Member>()...DataSource(d => d.WebApi()...)`）**沒有**把 Model 的 Members 直接轉成 `<tr>`，Grid 資料完全來自 `LoadIntegrate`/`LoadNewPersonFollowUp` 的 AJAX（已核實，見下方「有效防線」），因此本項**不會單獨造成同一列出現兩次**，但：
- `SetupIntegrateData(ListEntityId)`（`ListManager.cs:309-312`）呼叫 `EnsureAndGetIntegrateDetachedRead` 卻**丟棄**回傳的 detached copy，然後 `HandleIntegrateViewLogin`/`HandleLineLogin` 再另外去讀活欄位——這代表 Razor 用來判斷 `Model.LoginType`、`Model.GroupType`、`Model.m_SmallGroupDataList.m_SmallGroupData.DisplayFlag`、`Model.GroupArray`（Lookup datasource）的資料，理論上可能與同一並行請求正在寫入的候選發生 torn read（例如 `GroupType` 已切換但 `DisplayFlag` 尚未切換），造成 Grid 顯示/隱藏區塊與後續 AJAX 資料不一致。這是 Session Leakage／scope 混合的潛在入口，即使目前尚未觀察到會直接造成「同 ID 兩列」。

### 🟡 Info-1：`ValidateUniqueRowKeys` 使用 `StringComparer.OrdinalIgnoreCase`

`ListManager.cs:415`：`new HashSet<string>(StringComparer.OrdinalIgnoreCase)`。手冊 2.1 節要求「若身份是 GUID，驗證器應使用 `Guid.TryParse` 後比較 GUID 值」。若 `PresentRecordId` 實際是 Dataverse GUID 字串，`OrdinalIgnoreCase` 在絕大多數情況下等價（GUID 字面量無大小寫語意差異），但嚴格照手冊應改為 `Guid.TryParse` 後比較值以避免格式差異（如帶/不帶連字號、大小寫混合但非標準格式）造成的假陰性或假陽性。列為 Info，非本次重複列的根因，但屬於規格明確要求的落差。

---

## 2. 現有已生效、且不應被破壞或重複建置的防線

- **`ListManager.m_IntegratePublicationGate`（instance lock）+ operation-local candidate**：`BuildIntegrateCandidate` 在 gate 內建立全新 `ListSmallGroupWeeklyReport`，候選未通過 `ValidateIntegrateCandidate` 前不會寫回 `m_ListSmallGroupWeeklyReport`（`ListManager.cs:323-361`）。這是 `ListManagerIntegratePublicationTests.cs` 已覆蓋且通過的核心不變條件，**新設計不得引入第二個候選建立入口繞過此 gate**。
- **Scope key 完整（`IntegrateLoadKey` record struct，`ListManager.cs:461-467`）**：帳號、憑證指紋（SHA-256、非明文）、LoginType、日期、ListEntityId、WeeklyReportEntityId 六欄位缺一即視為新 scope，`EnsureAndGetIntegrateDetachedRead_DateChanges_RebuildsCompleteScope`／`_CredentialChanges_RebuildsCompleteScope` 已驗證。**新增前端 generation/token 時不得取代或簡化這把 key**，只能疊加。
- **`SmallGroupDataList._syncRoot` 統一同步根**（`SmallGroupDataList.cs:37-121`）：`m_SmallGroupData`/`m_NewPersonFollowUpData`/`m_HappyGroup`/`m_AllMemeberData` 四個 setter 與 `UpdateSmallGroupAndAllMember`/`DeleteMemberFromAllGroups`/`AddMemberToAllMemberData`/`RebuildSmallGroupAndNewPersonDataFromAllMembers`/`AddNewPersonToMember` 全部走同一把鎖，`SmallGroupDataListSnapshotIsolationTests.cs` 已驗證背景變更不會破壞並行列舉、兩份快照不互相污染。**新的 `RowPublicationGuard` 應該疊加在這把鎖保護的邊界之後，而不是另建一把鎖**（避免 design.md 第 3 節警告的「兩把不協調的鎖」）。
- **`CreateDetachedReadCopy`/`CreateDetachedReadSnapshot`/`CreateBackgroundUploadCopy` 三段式深拷貝**（`ListSmallGroupWeeklyReport.cs:100-173`、`SmallGroupDataList.cs:236-310`）：已對 Member 逐一 `new Member(member)`，讀取端修改不會污染 Session。`CreateBackgroundUploadCopy_DeepCopiesAllMemberCollectionsAndRequiredUploadMetadata`、`CreatingTwoSnapshots_DoesNotCrossContaminateSources` 已驗證。
- **`SaveIntegrate` 背景上傳的隔離**（`SmallGroupController.Save.cs:37-109`）：`CreateBackgroundUploadCopy()` 之後背景 Task 只捕獲純量與副本，不捕獲 `HttpContext`/Session；`RemoveTransferredMembersFromBackgroundCopy` 只操作副本。`SaveIntegrateBackgroundUploadRunnerTests.cs` 已覆蓋。**這是好樣板，`HandleSuccessfulNewPersonCreation` 的背景寫入（Critical-1 之一）應該仿照此模式改寫，而非繼續操作活的 Session 圖。**
- **DevExtreme Grid 資料完全來自受控 AJAX，不是 Razor 內嵌列**：`_GeneralGroupGrids.cshtml` 兩個 `DataGrid<Member>()` 都用 `.DataSource(d => d.WebApi()...LoadAction(...).Key("PresentRecordId"))`，證實目前**沒有**「Razor 靜態渲染 + Grid AJAX 二次渲染」造成的雙重 DOM 列風險；design.md「不建立第二條取數管線」的前提目前成立，新 coordinator 應該包裝既有 WebApi CustomStore，而非另開 fetch。
- **`GetChartDataList`/`AssignSmallGroupGet` 已標記 `[ResponseCache(NoStore = true)]`**（`SmallGroupController.DataApi.cs:177,260,363`），避免瀏覽器/中介快取回放舊使用者資料。

---

## 3. 最小可測試的後端 consumer-boundary guard 設計評論

design.md 提出的無狀態 `RowPublicationGuard`（method-local HashSet 驗證，容量上限）方向正確，但**必須明確定位在 Critical-1 揭露的缺口**：它不能只加在候選建立時（那裡已經有 `ValidateIntegrateCandidate`），必須加在：

1. `LoadIntegrate`（`SmallGroupController.DataApi.cs:120,127-128`）——對 `snapshot.m_SmallGroupDataList.m_SmallGroupData.Members` 送進 `DataSourceLoader.Load` **之前**再跑一次 guard；
2. `LoadNewPersonFollowUp`（`NewPersonController.cs:119-124`）——同理對 `m_NewPersonFollowUpData.Members`；
3. 兩者都在**每一次呼叫**（不論 `EnsureAndGetIntegrateDetachedRead` 內部走 hit 或 miss）執行，而不是依賴 `ListManager` 內部的一次性驗證。

這樣即使 Critical-1 的無鎖寫入路徑尚未全部修正，consumer boundary 仍能 fail closed，符合 PRD 驗收條件「實際 API consumer collection 內同一 `PresentRecordId` 出現兩次時會拒絕發布，且不會把衝突集合交給 `DataSourceLoader`」。

同時建議把 `InsertPresentRecord`／`InsertNewPresentRecord`／`HandleSuccessfulNewPersonCreation` 的寫入改為統一經過 `SmallGroupDataList.ExecuteSynchronized`（比照 `UpdateSmallGroupAndAllMember`），這是**修正根因**，`RowPublicationGuard` 只是 defense-in-depth，不能取代它——這正是手冊「絕對禁止的錯誤修法」章節反覆強調的「不能只加驗證掩蓋真正的資料組裝錯誤」。

---

## 4. 前端 single-owner／generation token／dispose 設計評論

design.md 的 `CollectionLoadCoordinator`（每 DOM instance 一個 owner、單調遞增 generation、abort 僅作資源回收而非正確性邊界）方向與手冊 4.1/4.1.1 一致，且明確要求「DevExtreme adapter 繼續使用既有 WebApi data source，不建立第二條平行取數管線」——**這點必須嚴格遵守**，因為目前 `_GeneralGroupGrids.cshtml` 的兩個 Grid 已用 `.DataSource(d => d.WebApi()...)`，且第 4.1.2 節提到「受控 `load` 必須在 Promise resolve 給 Grid 前檢查世代與 row key」，這代表要包裝的是 `CustomStore.load`，不是整個 DevExtreme WebApi 設定。目前 repository 未見任何 `CollectionLoadCoordinator`/`devextreme-publication-adapter.js`，此為預期中「待建立」項目，非缺陷。

需要特別注意的相容性風險（design.md 未明確提及）：`_GeneralGroupGrids.cshtml` 目前設定 `.RemoteOperations(ro => ro.Filtering(false).Sorting(false).Paging(false)...)`——即前端已停用 remote 分頁/排序，代表 Grid 每次 `load` 都會拿到完整集合。世代/token 檢查的插入點應該包在 `LoadAction` 對應的 CustomStore `load` callback，而非改動 `RemoteOperations`/`Paging` 設定，否則會意外改變現有分頁行為（手冊 4.5 節警告的相容性風險）。

---

## 5. Session／Memory／Resource Leakage 風險與 cleanup 要求

| 風險 | 證據 | 分類 |
|---|---|---|
| 背景 Task 捕獲活的 Session 物件（非副本），生命週期不受 request 控制 | `NewPersonController.cs:531-534` `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)` 直接操作 `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport`，無 `CreateBackgroundUploadCopy()` 對照組的隔離 | 🔴 Critical（與 Critical-1 同一根因，另計為資源生命週期風險：無法追蹤此 Task 何時完成、例外未被觀察） |
| `InsertPresentRecord`/`InsertNewPresentRecord` 略過 `EnsureCorrectUserData()` | `SmallGroupController.Crud.cs:34-47`、`NewPersonController.cs:154-161`，對照同檔其餘 action 皆呼叫 `EnsureCorrectUserData()` | 🟠 Warning（Session 一致性檢查不一致，可能在 Session 密碼已變更但尚未同步時，仍對舊 `ListManager` 資料圖寫入） |
| `GetMultiGroupChartDataList` 的 `_memoryCache` 使用 `{日期}_{帳號}` 作 key（`SmallGroupController.DataApi.cs:283`） | 未見上限／逐使用者上限文件化，`CreateCacheOptions()` 需另行確認是否有 sliding/absolute expiration | 🟡 Info（未直接檢視 `CreateCacheOptions()` 實作，若已有到期設定則風險可忽略；建議在落地清冊時明列此 cache 的容量與存活期） |

---

## 6. TDD 測試矩陣（按規格第 7 節要求，標出「已覆蓋」vs「本次分析發現的缺口」）

| 測試案例 | 現況 |
|---|---|
| 同名不同 ID 兩列皆保留 | 需新增（現有測試聚焦 key 驗證與 scope，未見 literal 同名不同 ID fixture） |
| 相同 ID 出現兩次即拒絕發布 —— **候選建立時** | ✅ 已覆蓋：`EnsureAndGetIntegrateDetachedRead_DuplicateStableRowKey_DoesNotPublishCandidate` |
| 相同 ID 出現兩次即拒絕發布 —— **快取命中路徑（候選建立後才被寫入重複 ID）** | ❌ 缺口，對應 Critical-1，**必須新增為 RED 測試**：先呼叫一次 `EnsureAndGetIntegrateDetachedRead` 使 key 發布，再透過等價於 `InsertPresentRecord`/`AddNewPersonToMember` 的路徑寫入重複 ID，斷言下一次 `LoadIntegrate`/`LoadNewPersonFollowUp` 或 `EnsureAndGetIntegrateDetachedRead` 呼叫會 fail closed |
| 回應亂序（第一次延遲、第二次先完成，只接受新世代） | 前端 coordinator 尚未存在，待建立後補測 |
| 重複 mount | 同上 |
| 32 併發同 scope single-flight | ✅ 已覆蓋：`EnsureAndGetIntegrateDetachedRead_ConcurrentSameKey_LoadsOnceAndReturnsIsolatedSnapshots` |
| A/B Session isolation（不同帳號／日期／認證世代） | ✅ 部分覆蓋：`_DateChanges_RebuildsCompleteScope`、`_CredentialChanges_RebuildsCompleteScope`；缺「兩個不同 Session 的 `ListManager` instance 完全獨立」的顯式測試（目前架構每個 Session 應有各自 `ListManager` instance，需確認 DI 生命週期為 per-session/per-scoped 而非 singleton——**本次分析未讀取 DI 註冊碼，列為待查項目，不下結論**） |
| caller mutation isolation | ✅ 已覆蓋：`EnsureAndGetIntegrateDetachedRead_CallerMutatesResult_PublishedSnapshotRemainsUnchanged` |
| 取消／resource drain | `SaveIntegrateBackgroundUploadRunnerTests.cs` 覆蓋 `SaveIntegrate` 背景流程；**`HandleSuccessfulNewPersonCreation` 的 `Task.Factory.StartNew` 完全沒有對應測試**，屬缺口 |
| Insert 路徑併發鎖 | ❌ 缺口：`InsertPresentRecord`/`InsertNewPresentRecord` 目前未走 `ExecuteSynchronized`，`SmallGroupDataListSnapshotIsolationTests.cs` 現有的鎖等待測試（`UpdateMember_WaitsForSourceGraphLock`、`AddMemberToAllMemberData_WaitsForSourceGraphLock`）**沒有對應的 `InsertMember_WaitsForSourceGraphLock`** |

**建議優先序（不需一次全改，符合 implement.md「先建立會失敗的測試」原則）：**
1. 先補「快取命中路徑重複 ID」RED 測試（對應 Critical-1）→ 加 consumer-boundary guard。
2. 補 `InsertMember`／`AddNewPersonToMember` 背景寫入的鎖與併發測試 → 修正 `InsertPresentRecord`/`InsertNewPresentRecord`/`HandleSuccessfulNewPersonCreation`。
3. 再進入前端 coordinator（design.md 第 3 節）。

---

## 7. 對規劃文件的修正建議

- **design.md 第 2 節「defense in depth」的假設需要修正**：文件寫「即使候選發布後的 mapping、selection 或未來維護修改意外重複列，仍不會把 duplicate key 交給 UI library」——但這句話的前提是 guard 要加在**每次交付**，而不是只補在候選建立（那個位置已經有 `ValidateIntegrateCandidate`，屬於重複勞動而非新防線）。建議 design.md 明確寫出 guard 的插入點是 `LoadIntegrate`/`LoadNewPersonFollowUp` 回傳前，而不是含糊帶過。
- **implement.md 執行清單遺漏 `InsertPresentRecord`/`InsertNewPresentRecord`/`HandleSuccessfulNewPersonCreation` 三個無鎖寫入點**：目前清單只提到「新增無狀態 `RowPublicationGuard`」與「API／Razor 實際 publication boundary 套用」，沒有把這三個具體檔案／方法列為需要改鎖或改為經 `ExecuteSynchronized` 呼叫的目標。若不補上，僅靠 consumer-boundary guard 只能「偵測並拒絕」重複，無法防止 Session 資料圖本身持續累積不一致寫入（治標不治本）。
- **手冊 1.1 節「初始開啟流程是第一階段修正範圍」與本次發現吻合，不需修正**：Critical-1 的重複 ID 需要「先有一次寫入」（Insert/AddNewPerson），理論上不完全發生在「尚未按任何按鈕」的瞬間；但因為 `HandleSuccessfulNewPersonCreation` 是 fire-and-forget 背景 Task，其執行時機與使用者下一次頁面刷新（含 F5 或重新整理小組頁）不同步，**使用者很可能是在「上一次操作」的背景寫入尚未完成或剛完成時，開啟本次週報頁面觸發快取命中路徑**，這與使用者回報「剛開啟、還沒按按鈕」的現象並不矛盾，反而是最貼近證據的一條路徑。建議 implement.md 把此路徑列為第一優先驗證項，而非只鎖定在「HTTP 重送」這單一故障模型。
- **未發現過度設計**：`RowPublicationGuard`／`CollectionLoadCoordinator` 的職責切分（無狀態、不保存 caller graph）與現有 `ListManager`/`SmallGroupDataList` 鎖模型相容，沒有看到會被判定為「無界 cache」、「跨產品全域鎖」等禁止項的規劃內容。
- **未發現與現況矛盾的相容性風險**，除第 4 節提到的 `RemoteOperations`/`Paging` 設定不應被 coordinator 改動之外。

---

### 結論摘要
本次分析在既有防線之外，找到一條**有具體檔案／行號證據、且完全符合「不得假設兩個資料庫 ID／不得按姓名去重」限制**的重複發布路徑：`ListManager.EnsureAndGetIntegrateDetachedRead` 快取命中時跳過驗證，加上 `InsertPresentRecord`／`InsertNewPresentRecord`／`HandleSuccessfulNewPersonCreation` 三處繞過 `SmallGroupDataList._syncRoot` 的無鎖寫入，兩者疊加即可讓同一 `PresentRecordId` 被寫入兩次且未經驗證地送達 DevExtreme Grid。建議以此作為 Step 9 表格 A/B 切片的具體 RED 測試目標，而非僅停留在「網路重送」的假設情境。

---
SESSION_ID: c6a50ff0-098c-4d60-af92-e289bd178af4
