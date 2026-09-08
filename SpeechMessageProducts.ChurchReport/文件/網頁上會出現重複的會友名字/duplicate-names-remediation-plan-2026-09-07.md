# 小組牧養頁面重複姓名：修正計劃與實作手冊

- 日期：2026-09-07
- 目前分支：`Fbllc.4.9.9.8.6.FixDuplicateName`
- 專案：ChurchReport（ASP.NET Core MVC + DevExtreme + Dynamics 365 / Dataverse）
- 文件性質：研究整合、修正計劃、實作與驗證手冊
- 參考報告：`duplicate-names-investigation-2026-09-07.md`（Claude，2026-09-07 更新版）
- 參考報告 SHA-256：`2F71CE2C7B68D51D477DD2B6D8B80097E0CDDF655B777A48608437A9B6B1A87E`
- 本階段限制：只產出計劃與手冊，尚未修改應用程式、查詢正式 CRM 資料或進行執行期重現

---

## 1. 執行摘要

目前不能只用一個原因解釋畫面上的重複姓名。靜態程式碼已確認至少存在三條彼此獨立、也可能同時發生的問題路徑：

1. **伺服器共用可變物件的並行競態。** 同一 Session 的多個請求會取得同一個快取 `ListManager`，但 `LoadFlag` 的檢查與資料重建沒有協調；`DownloadIntegrateData` 也是共享且帶有大量可變欄位的實例。
2. **Dataverse 已存在重複出席記錄。** 目前讀取路徑是一筆 `new_present_record` 轉成一列，沒有業務鍵防重；部分建立流程也不具冪等性。
3. **DevExtreme 前端版本／虛擬捲動問題。** 後端 ASP.NET wrapper 是 23.1.5，實際 `dx.all.js` 與 `dx.aspnet.mvc.js` 是 22.1.6，且靜態檔案快取一年但 script URL 沒有內容版本。這可能使不同使用者載入不同年代的資產，並放大 virtual scrolling、refresh、重複 key 的問題。

本次程式碼複核後，建議優先級如下：

| 優先級 | 問題 | 判斷 |
|---|---|---|
| P0 | LINE 登入使用 `Task.WhenAll` 同時操作同一個 request-scoped context 與同一個 `ListManager` | 已確認，必須先修 |
| P0 | `LoadFlag` 在完整資料載入前就被設成 `true`，其他請求可讀到半完成資料 | 已確認，必須先修 |
| P0 | `LoadFlag` 只表示「載入過」，沒有驗證使用者、小組、日期、週報是否相符 | 已確認，可能回傳舊小組／舊日期資料 |
| P0 | 整份報告在共享物件上逐欄、逐筆修改，沒有 build-then-swap | 已確認，必須改為完整快照發布 |
| P1 | Dataverse 建立流程不具平台層唯一性與冪等性 | 已確認風險，須查正式資料後修 |
| P1 | DevExtreme 前後端版本不一致，且長期快取沒有 cache busting | 已確認，須統一版本 |
| P2 | 單純的 DevExtreme DOM／virtual scrolling 重複渲染 | 有公開案例，但本案尚未取得執行期證據 |

**建議的最終方向不是只在 Controller 做 `Distinct()`。** 正確解法應同時包含：

- 先建立可判別三層資料的診斷證據。
- 把整合資料載入集中成單一 async 協調入口。
- 每次載入使用新的 loader 與新的報告物件，在區域變數完成後一次替換快照。
- 使用完整載入鍵判斷資料是否有效，不再只看 `LoadFlag`。
- 讓 Dataverse 寫入具備唯一鍵與 Upsert／冪等請求。
- 統一 DevExtreme 前後端版本並對靜態資產做內容版本化。

---

## 2. 證據分級

### 2.1 已確認事實

以下項目可直接由目前主樹程式碼確認：

- `LoadIntegrate` 直接回傳 `m_SmallGroupDataList.m_SmallGroupData.Members`，沒有輸出 invariant 檢查或去重。
- `LoadNewPersonFollowUp` 與 `LoadIntegrate` 會使用同一個 Session 對應的 `ListManager` 與同一份 `m_ListSmallGroupWeeklyReport`。
- `IInMemoryDataContext` 雖為 Scoped，內部 `ListManager` 卻放在全應用程式的 `IMemoryCache`，同一快取 key 可跨請求共用同一個可變物件。
- `ListManager` getter 使用 `if (Get(key) == null) Set(...)`，建立過程不是 single-flight；並行 miss 可能建立多個實例並互相覆蓋。
- `ListManager.m_DownloadIntegrateData` 是長期共用實例，而 `DownloadIntegrateData` 內含登入者、聯絡人、名單、週報、日期等可變欄位。
- `SetupIntegrateData`、`SetupShepherdData` 與成員 `Add` 路徑沒有載入協調。
- `SetupHeaderData` 在完整成員、週報與圖表資料載入前就設定 `LoadFlag = true`。
- `SetupIntegrateData` 若重新建立 report，也會在實際載入開始前設定 `LoadFlag = true`。
- `EnsureIntegrateDataLoaded` 與 `EnsureNewPersonDataLoaded` 只檢查 null／`LoadFlag`，未比較 `ListEntityId`、日期、週報或登入身分。
- LINE 登入的 `HandleLineLogin` 使用 `Task.WhenAll` 並行執行 `SetupSmallGroupData`、`SetupViewBagForSmallGroup`、`EnsureIntegrateDataLoaded`；後兩者在語意上依賴前者，不是獨立工作。
- LINE 登入把 `lineUserId` 傳給名稱上預期 `listEntityId` 的 `EnsureIntegrateDataLoaded`，需要以執行流程確認是否可能傳錯識別碼。
- 一筆 Dataverse `new_present_record` 會建立一筆畫面 `Member`；查詢與輸出沒有依業務鍵合併。
- Grid 以 `PresentRecordId` 為 key，同時開啟 virtual scrolling、virtual row rendering、`CacheEnabled(true)`、`RepaintChangesOnly(true)`。
- 無週報時的 fallback 路徑會把遞增 counter 轉成 `PresentRecordId`；這個 key 雖可在單次清單內唯一，但不是跨 reload 穩定的資料身分。
- `ChurchReport.csproj` 的 `DevExtreme.AspNet.Core` 是 23.1.5。
- 實際靜態 `dx.all.js`、`dx.aspnet.mvc.js` 與 CSS 標示 22.1.6。
- Layout 實際引用的 `dx.aspnet.data.js` 位於 `wwwroot/lib/devextreme-aspnet-data`，沒有版本標頭；專案另有未被該 Layout 引用的 2.8.6 副本。
- 靜態檔案設定一年快取，但 DevExtreme script 標籤沒有 `asp-append-version="true"` 或帶 hash 的檔名。
- 一般 HTML／API 回應設有 `no-store`，所以「API JSON 被一般瀏覽器 HTTP cache 重播」不是目前最強假設；靜態 JavaScript 的長期快取則仍是實際風險。

### 2.2 高可信推論

- 若同一份 `ListManager` 同時執行兩次完整載入，兩條執行緒可改寫同一個報告物件及 loader 欄位，結果不只可能重複，也可能混入錯誤小組、錯誤日期或半完成資料。
- `LoadFlag = true` 設得太早，會讓第二個請求跳過載入並讀取第一個請求尚未完成的物件；因此即使沒有出現 2N 筆，仍可能出現缺列、欄位前後不一致或圖表與 Grid 不一致。
- 若 Network response 已含相同 `PresentRecordId` 兩次，Dataverse 不可能真的有兩筆相同 primary key；此時更支持伺服器記憶體重複引用／組裝競態，或查詢 join 放大。現行 N:1 join 本身不太可能放大。
- 若相同 `ContactId` 在同一週報下對應不同 `PresentRecordId`，更支持 Dataverse 已存在業務重複資料。
- 重複的 Grid key 在 DevExtreme virtual scrolling 下可能導致額外重繪異常，所以伺服器重複資料與前端渲染問題可能互相放大，不能硬分成只能擇一的原因。

### 2.3 尚待執行期驗證的假設

- 截圖當次的 `/SmallGroup/LoadIntegrate` 原始 JSON 是否已重複。
- `DataSource.items()` 是否重複、而 Network response 不重複。
- DOM rows 是否重複、而 DataSource items 不重複。
- 截圖中每組重複資料的 `PresentRecordId` 與 `ContactId` 是否相同。
- 當次請求是否經過 LINE 登入 `Task.WhenAll` 路徑、返回頁面／bfcache、日期切換或上傳後 refresh。
- 正式 Dataverse 中是否真的存在 `(週報或名單＋週起始日＋Contact)` 的重複 active record。
- 正式環境是單一 IIS worker、web garden、多台主機或負載平衡，是否有 sticky session。
- 正式頁面實際下載到瀏覽器的 DevExtreme 版本，以及是否有使用者仍持有舊的一年快取資產。

---

## 3. 對 Claude 更新版報告的審閱與融合

### 3.1 同意的核心內容

Claude 對下列風險的判斷成立，應直接納入修正：

- ASP.NET Core Session 不會像舊 ASP.NET Framework 自動把同一 Session 的請求序列化。
- Session 對應的共享 `ListManager` 是可變物件，且目前沒有安全發布完整資料的機制。
- `LoadFlag` 的 check-then-act 與 CRM I/O 之間存在競態窗口。
- 共用 `DownloadIntegrateData` 會產生比重複列更嚴重的跨小組、跨日期欄位污染風險。
- Dataverse 建立流程缺少冪等性，正式資料必須檢查。
- virtual scrolling、refresh 與重複 key 是應測的前端路徑。

### 3.2 需要修正或補強的地方

#### A. `RemoteOperations(false)` 不能推出「畫面重複一定等於伺服器 JSON 重複」

它只能表示排序、分頁等主要由前端處理。DevExtreme DataSource 狀態、reload／refresh 與 DOM virtual row recycling 仍可能造成 UI 層重複。因此必須分三層取證：

1. Network 原始 JSON。
2. DevExtreme DataSource items／visible rows。
3. 實際 DOM。

#### B. `PresentRecordId` 的二分法不夠完整

Claude 原測試把結果分為：同 ID = H1、不同 ID = H2。正確判讀應是：

| Network | DataSource | DOM | 主要方向 |
|---|---|---|---|
| 已重複，且相同 `PresentRecordId` | 通常重複 | 可能重複 | 伺服器組裝競態／同一 row 被加入兩次 |
| 已重複，不同 `PresentRecordId`、相同業務鍵 | 重複 | 重複 | Dataverse 業務重複資料 |
| 不重複 | 重複 | 重複 | DataSource reload／store 狀態問題 |
| 不重複 | 不重複 | 重複 | DevExtreme virtual rendering／DOM recycling 問題 |

此外，不能只比較姓名；真實世界允許同名。至少要同時記錄 `PresentRecordId`、`ContactId`、`ListEntityId`、`WeeklyReportEntityId` 與週起始日。

#### C. 依 `PresentRecordId` 去重並不能同時修掉 H1 與 H2

- 對 H1：同一資料列被加入兩次時，`PresentRecordId` 相同，確實可止血。
- 對 H2：Dataverse 的兩筆實體一定有不同 primary key，所以依 `PresentRecordId` 去重不會移除 H2。

若改依 `ContactId` 去重，則可能靜默隱藏髒資料、丟失兩筆記錄中不同的出席或關懷內容。因此 Controller 去重只能是受監控的暫時防線，不能取代資料清理與唯一鍵。

#### D. `Count > 1` 不應改成單純 `FirstOrDefault()`

`Count == 1 ? record : null` 會在重複時繼續建立，確實錯誤；但單純 `FirstOrDefault()` 會任意選一筆並掩蓋資料異常。正確狀態機應為：

- 0 筆：以冪等方式建立／Upsert。
- 1 筆：使用該筆。
- 大於 1 筆：停止再次建立、記錄所有 ID、回報資料衝突，交由明確合併策略處理。

#### E. `LoadFlag` 的主要問題不只競態窗口，而是語意錯誤

它目前在完整載入前變成 true，而且沒有綁定載入 key。建議不要再把單一 bool 當成快取有效性的唯一依據。至少要有：

- `LoadState`：NotLoaded／Loading／Loaded／Failed。
- `LoadedKey`：登入身分、List ID、週起始日或選取日期、Weekly Report ID。
- 完成時間／版本。
- 完整載入成功後才發布 Loaded 狀態。

#### F. §3.6 的 Δt／W 推論只在特定入口成立

一般 `IntegrateView` HTML action 會先在伺服器完成 `SetupIntegrateViewData`，再把 HTML 回給瀏覽器；正常情況下 AJAX 發出時資料已載入。故「三個 AJAX 每次都幾乎必撞」不是所有入口的既定事實。

但 LINE 登入路徑的 `Task.WhenAll` 在**同一個伺服器請求內**直接並行操作相依狀態，這比瀏覽器 AJAX 時序更明確，也應排在第一個修正項目。另需驗證下列前置條件：

- HTML 是否來自 bfcache／返回頁面，未重新執行伺服器 action。
- `ListManager` 是否因 key 不穩定、快取驅逐、跨 process 或重新建立而處於未載入狀態。
- 日期切換、上傳後 refresh 是否與重建重疊。
- MultiGroup 路徑是否把資料設回 null／false。

### 3.3 Claude 未涵蓋但目前分支已發現的事項

- `Extensions/ListManagerCacheExtensions.cs` 已有 `SetupIntegrateDataWithCache` 名稱，但目前方法內仍直接呼叫 `listManager.SetupIntegrateData(listEntityId)`；全專案也找不到實際呼叫。它沒有提供鎖、single-flight、快照或快取結果，不可誤認為問題已處理。
- `GetCurrentSessionId()` 在 Session 不存在時，每次產生帶 thread id 與 ticks 的新 key。這避免不同無 Session 呼叫共用資料，但每次 getter 都可能建立新的 30 分鐘 cache entry，具有記憶體成長與狀態不一致風險。正常 HTTP 流程應禁止在 Session 尚未可用時建立此類狀態。
- Layout 引用 `~/lib/devextreme/css/...`，但目前該目錄不存在；雖然未必造成姓名重複，版本盤點時應一併清理 404 與重複／失效資產引用。

---

## 4. 目標架構

### 4.1 不變條件（Invariants）

修正後必須一直成立：

1. 同一載入鍵同一時間最多只有一個資料建構者。
2. 讀取者只能取得上一份完整快照或新一份完整快照，不可看見半完成集合。
3. 載入失敗不得把舊快照清空，也不得將狀態標成 Loaded。
4. 不同 Session／使用者／小組／日期的 mutable state 不得共用。
5. Grid row key 必須非空、唯一、穩定。
6. 同一業務事件的重送不得在 Dataverse 建立第二筆資料。
7. 所有 gate、cache entry、連線、task 與 cancellation registration 都要有明確生命週期，不可使用無上限的 static dictionary。

### 4.2 建議資料流

```text
Controller
  -> EnsureIntegrateSnapshotAsync(loadKey, cancellationToken)
       -> 快速檢查完整且相符的快照
       -> 取得該 Session state 內的 async gate
       -> gate 內再次檢查 loadKey
       -> 建立新的 DownloadIntegrateData
       -> 建立新的 ListSmallGroupWeeklyReport candidate
       -> 在 candidate 上完成所有 CRM 讀取、排序與驗證
       -> 驗證 row key 與業務重複
       -> 一次性發布 candidate
       -> 釋放 gate
  -> Controller 只持有並回傳該次取得的 snapshot 參考
```

### 4.3 建議的載入鍵

最低需求：

```text
UserIdentity + ListEntityId + SelectedDate/WeekStart + WeeklyReportEntityId
```

Session 只負責隔離狀態，不應被當成業務資料正確性的唯一識別。若使用者身分切換，必須建立新的 state 或清除舊 state。

---

## 5. 分階段實作計劃

## Phase 0：先取得可判別證據

### 目的

在改變資料行為前，先判斷重複發生在 Network、DataSource、DOM 或 Dataverse 哪一層。

### 0.1 伺服器診斷日誌

在整合資料載入的入口、完成、失敗與 API 回傳前，記錄：

- Correlation ID／Load operation ID。
- 經 HMAC 處理後的 Session 識別短碼；不可記錄原始 Session ID、LINE user id、帳密或 cookie。
- process／instance 識別、Managed Thread ID、開始與結束時間。
- requested load key 與實際 snapshot load key。
- `LoadState`／舊 `LoadFlag` 前後值。
- `ListManager`、report、member list 的 reference identity（只作診斷）。
- CRM 回傳 present record 數量。
- 最終輸出 member 數量。
- 重複 primary key 數量。
- 重複業務鍵數量及其 record IDs；正式 log 不記姓名等不必要個資。

建議加結構化 event，例如：

- `IntegrateLoadStarted`
- `IntegrateLoadJoinedExisting`
- `IntegrateLoadCompleted`
- `IntegrateLoadFailed`
- `DuplicatePresentRecordIdDetected`
- `DuplicateBusinessKeyDetected`
- `SnapshotKeyMismatch`

### 0.2 瀏覽器取證

重現時保存 HAR，鎖定 `/SmallGroup/LoadIntegrate`，記錄：

- response body 的總筆數。
- 每筆 `PresentRecordId`、`ContactId`、`FullName`。
- response headers 與是否由 memory cache／disk cache／service worker 提供。
- Console 中 `DevExpress.VERSION`。
- `performance.getEntriesByName(...)` 的實際 script URL。

再比對：

```javascript
const grid = $("#SmallGroupgridContainer").dxDataGrid("instance");
const items = grid.getDataSource().items();
const visibleRows = grid.getVisibleRows()
  .filter(r => r.rowType === "data")
  .map(r => r.data);

console.table(items.map(x => ({
  fullName: x.FullName,
  presentRecordId: x.PresentRecordId,
  contactId: x.ContactId
})));

console.table(visibleRows.map(x => ({
  fullName: x.FullName,
  presentRecordId: x.PresentRecordId,
  contactId: x.ContactId
})));
```

注意：`DataSource.items()` 可能只代表目前 page／已載入區段，不能取代 Network response。

### 0.3 Dataverse 查核

以截圖當週、當小組為範圍匯出：

- present record id。
- contact id。
- weekly report id。
- list id。
- createdon／modifiedon／createdby。
- statecode／`new_not_display`。
- 出席、關懷等不可直接丟棄的欄位。

依候選業務鍵分組計數。現階段可先用 `(WeeklyReportId, ContactId)`，但正式唯一鍵須由業務確認是否應改為 `(ListId, WeekStartDate, ContactId)`。

### Phase 0 完成條件

- 至少取得一次正常與一次異常的 HAR／API 資料。
- 能把異常歸到 Network、DataSource 或 DOM 三層之一。
- 確認正式 CRM 是否已有重複業務記錄。
- 確認正式部署拓樸與實際前端版本。

---

## Phase 1：低風險止血與資料 invariant

### 1.1 API 回傳前檢查 exact duplicate

在 `LoadIntegrate` 與 `LoadNewPersonFollowUp` 回傳前，先驗證：

- `PresentRecordId` 不得為空。
- Grid key 不得重複。
- 若同一 `PresentRecordId` 出現多次，記錄 Critical 診斷事件。

可在 feature flag 下暫時只移除**完全相同 `PresentRecordId`** 的重複 row，以避免 DevExtreme 收到 duplicate key。去重時必須保留告警與計數，不能無聲處理。

不要在尚未確認業務規則前直接依 `FullName` 或 `ContactId` 去重。

### 1.2 暫時關閉高風險 Grid 組合做 A/B 診斷

用設定旗標建立兩組：

- A：現行 virtual scrolling。
- B：Standard scrolling、關閉 virtual row rendering、`RepaintChangesOnly(false)`；必要時暫停 grid cache。

如果 Network 與 DataSource 唯一、只有 A 組 DOM 重複，才把 DevExtreme rendering 列為直接根因。

### Phase 1 完成條件

- API 不再把重複 Grid key 無聲交給 DevExtreme。
- 可由設定快速切換 Grid 診斷模式。
- 不會因止血而合併合法同名人員或丟失不同 CRM 記錄。

---

## Phase 2：修正伺服器競態與快照發布

這是主要程式修正階段。

### 2.1 先修 LINE 登入相依工作被錯誤平行化

檔案：`Controllers/SmallGroupController/SmallGroupController.LineLogin.cs`

應調整為語意順序：

1. 查詢 LINE 對應 Contact。
2. 完成 `SetupSmallGroupData`。
3. 從完成後的 state 取得正確 `ActiveListId`／requested list id。
4. 確保整合資料快照已載入。
5. 最後建立 ViewBag 並回傳 View。

移除對同一個 `InMemoryContext` 的三個 `Task.Run`＋`Task.WhenAll`。同步 Dataverse SDK 包進 `Task.Run` 並不會把 I/O 變成真正 async，反而占用 ThreadPool，且讓 request-scoped mutable state 被多執行緒同時使用。

### 2.2 建立唯一的載入協調入口

建議放在新的 scoped/session-state service，或先在 `ListManager` 建立：

```csharp
Task<ListSmallGroupWeeklyReport> EnsureIntegrateSnapshotAsync(
    IntegrateLoadKey key,
    CancellationToken cancellationToken);
```

所有入口都必須走同一方法，包括：

- `IntegrateView` HTML 載入。
- `LoadIntegrate`。
- `LoadNewPersonFollowUp`。
- 圖表資料 API。
- 日期切換。
- 多小組切換。
- Equipment／Personal 等會觸發 `SetupIntegrateData` 的旁路。

不能只在兩個 Controller 各加一把不同的 gate；否則仍可從不同入口同時進入底層載入。

### 2.3 gate 生命週期

首選：gate 跟著 Session state／`ListManager` holder 存活，避免全域 `ConcurrentDictionary<string, SemaphoreSlim>` 無限增長。

若必須使用 keyed gate service，需具備：

- key 正規化。
- reference count／lease。
- idle eviction。
- 等待者取消。
- loader 例外後釋放。
- 移除時不得 dispose 仍被等待／持有的 semaphore。
- 多 process 場景不得誤以為本機 gate 能提供全域資料唯一性。

### 2.4 gate 內重新檢查完整載入鍵

快速路徑與 gate 內都要使用 `IsSnapshotValidFor(key)`，而不是只檢查 `LoadFlag`：

```text
if snapshot 完整且 key 相同 -> 直接回傳
await gate
    再檢查一次 snapshot key
    若已由前一請求完成 -> 直接回傳
    否則建構新 candidate
release gate
```

### 2.5 每次載入建立新的 loader

檔案：`Models/ListManager.cs`

取消共用 `m_DownloadIntegrateData`，每次載入使用新的 loader；較長期方案則把 loader 的 mutable fields 改為方法區域變數／參數。

### 2.6 建立整份 candidate，成功後一次替換

不要只對 `Members` 做 local list；應避免整份 report 的其他欄位在載入期間被讀到。

建議：

```csharp
var candidate = new ListSmallGroupWeeklyReport();
var loader = new DownloadIntegrateData();

loader.SetupIntegrateData(..., ref candidate);
ValidateCompleteSnapshot(candidate, key);
candidate.LoadFlag = true; // 僅相容舊程式，且只能在最後

Volatile.Write(ref _publishedSnapshot, candidate);
```

更佳的後續重構是讓 `DownloadIntegrateData.Load(...)` 直接回傳新物件，移除 `ref` 與共享欄位。

### 2.7 修正 LoadFlag

- 移除 `SetupHeaderData` 中過早的 `LoadFlag = true`。
- 移除 report 剛 new 出來就設 true 的程式。
- 失敗時維持舊的完整快照；若沒有舊快照，狀態為 Failed／NotLoaded。
- 不要在建構途中讓其他讀取者看到 Loading candidate。

### 2.8 Controller 固定使用一次取得的 snapshot

Controller 應：

```text
snapshot = await EnsureIntegrateSnapshotAsync(...)
members = snapshot....Members
return DataSourceLoader.Load(members, options)
```

不要在同一 action 中多次經由 `InMemoryContext.ListManager...` 重新走 getter，以免 cache eviction／swap 讓同一回應取到不同世代物件。

### 2.9 修正 ListManager 建立競態與無 Session 行為

檔案：`Models/InMemoryDataContextSmallGroup.cs`

- 正常請求若 Session 不可用，不應建立帶隨機 key 的 30 分鐘可變狀態；應明確失敗或使用只活在該 request 的 fallback。
- `ListManager` 建立需 single-flight。僅改成 `IMemoryCache.GetOrCreate` 仍不能保證 factory 只執行一次。
- 優先建立具明確 owner 的 Session state holder；如果保留 cache，應用 `Lazy<T>`／協調 service，並提供 eviction cleanup。
- 加入 cache size limit 或受控數量，避免 session state 無界成長。

### Phase 2 完成條件

- 任何入口都不能直接呼叫舊的無協調 `SetupIntegrateData`。
- 同一 load key 的並行請求只執行一次 CRM load。
- 其他請求只能看到完整舊快照或完整新快照。
- 載入失敗不會發布半成品，下一次可以正常重試。
- 不同小組、日期與使用者不會交叉讀取。
- gate／cache entry 在 Session state 過期後可回收。

---

## Phase 3：Dataverse 防重複與既有資料清理

### 3.1 定義唯一業務鍵

由業務確認「同一個人同一週同一小組只能有一筆出席記錄」是否絕對成立。候選：

- `(WeeklyReportId, ContactId)`；前提是 weekly report 本身唯一。
- `(ListId, WeekStartDate, ContactId)`；即使 weekly report 意外重複，也能阻止同一業務事件建立多筆。

若 Dataverse alternate key 對 lookup 組合、欄位型別或現有資料不適用，可新增規格化文字欄位，例如：

```text
{listId:N}:{weekStart:yyyyMMdd}:{contactId:N}
```

此欄位只能由伺服器以固定格式產生，不接受前端提供。

### 3.2 建立改為 Upsert／冪等操作

所有會建立 `new_present_record` 的路徑都要盤點，不只 `UploadIntegrateData`：

- 上傳週報。
- 個人回報補建。
- QR Code 流程。
- WeeklyReportProcessor。
- 新人／分組流程。
- Dataverse plugin、workflow、Power Automate（若存在）。

每次操作使用相同業務鍵。重送、timeout、使用者重點按鈕或多台主機同時執行時，平台層仍只能得到一筆資料。

### 3.3 明確處理重複狀態

查詢結果：

- 0：Upsert。
- 1：更新既有記錄。
- >1：停止新增，產生資料衝突告警，列入人工／批次修復清單。

不可把 >1 當 0，也不可無告警任取第一筆。

### 3.4 上傳流程防重送

目前 3 秒 AJAX timeout 不代表伺服器停止；使用者再次送出可能重複寫入。建議：

- 使用者送出後立即 disable 按鈕並顯示真正狀態。
- 產生 server-validated idempotency key。
- 若工作確實較久，API 回 202＋operation id，前端查詢狀態；不要把 client timeout 當成功完成。
- 客戶端斷線不應取消已進入不可安全中斷的 CRM transaction；但重試必須命中同一冪等鍵。

### 3.5 清理既有重複資料

正式資料不可直接批次刪除。程序：

1. 匯出重複群組與所有欄位／關聯作備份。
2. 決定 canonical record：優先保留擁有最完整使用者輸入、關聯與稽核資訊的一筆，不可只用最早或最新武斷決定。
3. 合併出席與關懷欄位，轉移必要關聯。
4. 先停用重複記錄，觀察後再決定刪除。
5. 重新查詢確認每一業務鍵只剩一筆 active record。
6. 唯一鍵生效後再恢復所有建立流程。

### Phase 3 完成條件

- 平台層能阻止重複業務鍵。
- 所有建立入口都通過重送測試。
- `Count > 1` 不再導致新增更多資料。
- 既有重複資料有可稽核的清理報告與回復方案。

---

## Phase 4：統一 DevExtreme 與靜態資產版本

### 4.1 建立版本清冊

需同時列出：

- NuGet `DevExtreme.AspNet.Core`。
- NuGet `DevExtreme.AspNet.Data`。
- `dx.all.js`。
- `dx.aspnet.mvc.js`。
- Layout 實際載入的 `dx.aspnet.data.js`。
- 所有 DevExtreme CSS。
- 瀏覽器 runtime `DevExpress.VERSION`。
- 正式站 response 的 ETag／Last-Modified／Cache-Control。

### 4.2 統一版本

前端 runtime、MVC wrapper、data helper 與 CSS 應來自同一個經支援且相容的版本組合。不能保留 23.1.5 wrapper 搭 22.1.6 runtime，也不能只更新其中一個檔案。

若授權與相容性允許，應整組升級到 DevExpress 目前支援且與 .NET 10 相容的版本；若本次先控制風險，也至少先整組對齊到同一已驗證版本，再規劃大版本升級。

### 4.3 修正 cache busting

所有本地靜態 script／CSS 應使用內容 hash，例如 Razor 的：

```html
<script src="~/js/devextreme/dx.all.js" asp-append-version="true"></script>
```

或部署時產生帶 hash 的檔名。只有內容版本化資產才適合 `public, max-age=31536000, immutable`。

目前全域 `no-store` 與 StaticFiles 再 `Append public,max-age` 可能形成互相衝突的 Cache-Control 值，也要用實際 response header 驗證並分流：

- 動態 HTML／API：`no-store`。
- 指紋化靜態資產：long cache＋immutable。
- 未指紋化靜態資產：不可一年強快取。

### 4.4 重新驗證 Grid

版本統一後測試：

- virtual scrolling。
- row rendering virtual／standard。
- refresh／reload。
- 上傳完成刷新。
- 日期切換。
- bfcache 返回。
- 手機尺寸與 LINE 內建瀏覽器。

若新版仍只有 DOM 層重複，再依 DevExpress 對應版本的修復票與 workaround 調整；不要直接套用舊版本專用的 `legacyMode` 而未驗證。

### Phase 4 完成條件

- 瀏覽器只載入一套、同版本的 DevExtreme 資產。
- 所有資產 URL 都能內容版本化，不再讓舊檔保留一年而 URL 不變。
- Console 無 DevExtreme 版本／duplicate key 警告，Network 無相關 404。
- virtual scrolling 壓力測試無重複 DOM row。

---

## Phase 5：測試、壓力驗證與上線

### 5.1 單元／併發測試

使用可控制的 fake loader 與 barrier，避免靠運氣重現：

1. 100 個同 Session、同 load key 的並行呼叫，loader invocation count 必須等於 1。
2. 所有呼叫取得相同完整快照，member count 與 key 集合一致。
3. 同 Session、不同 list/date 不能誤用舊 `LoadFlag`。
4. 不同 Session 同時載入不得共用 mutable report／loader。
5. loader 在中途丟例外時，不發布 candidate，gate 必須釋放，下一次重試成功。
6. 等待者 cancellation 不得取消其他請求正在建立的共用快照，除非設計明確採用 reference-counted cancellation。
7. exact duplicate key validator 能阻止／告警，不會依姓名合併合法同名。

### 5.2 整合測試

- 同一 Session 同時呼叫 `LoadIntegrate`、`LoadNewPersonFollowUp`、圖表端點。
- LINE 登入流程不得再平行操作相依 state。
- MultiGroup 快速切換 A→B→A，回傳 load key 與資料必須相符。
- 日期切換與 Grid refresh 同時發生時，只能發布對應日期的完整 snapshot。
- 模擬 IMemoryCache eviction，正在使用的 request 不得拿到 null／另一世代物件。
- 模擬兩個 app instance；本機 gate 不得被當成 Dataverse 唯一性保證。

### 5.3 UI 自動化

- 一般瀏覽器與 LINE WebView 尺寸。
- 連續上下捲動至資料集尾端 50 次。
- 連續 refresh／返回／前進／日期切換。
- CPU throttling 與不同 viewport 高度。
- 比較 Network row keys、DataSource row keys、DOM row keys。

### 5.4 CRM 冪等測試

- 相同 idempotency key 連續送出 10 次，只得到一筆 active record。
- 兩台應用程式實例同時 Upsert，同樣只得到一筆。
- 模擬 client timeout 後重送，不新增第二筆。
- 預先放入兩筆髒資料時，系統停止再建立並產生告警。

### 5.5 效能與資源生命週期

- 測量修正前後 P50／P95／P99 載入時間、CRM call 次數、allocation rate。
- 進行至少一輪高併發壓力與一輪 soak test。
- drain 後確認 gate entry、cache holder、Task、connection、timer、cancellation registration 與記憶體回到宣告基線。
- 不允許為了防重複而建立無上限 static semaphore dictionary。
- 確認 sync CRM I/O 沒有因額外 `Task.Run` 放大 ThreadPool starvation。

### 5.6 建議驗收門檻

- 100 並行同 key 載入、重複執行 100 輪：0 筆 duplicate Grid key、0 次半完成快照、每輪 loader 最多 1 次。
- 不同 Session／使用者／小組／日期隔離測試：0 次資料串用。
- Dataverse 重送測試：每個業務鍵恰好 1 筆 active record。
- DevExtreme runtime／wrapper／CSS 版本清冊一致。
- soak test 結束並 drain 後，受控狀態量回到基線範圍，無持續成長。

---

## 6. 建議修改檔案與責任範圍

實作時可按下表拆成小型 PR，避免一次同時改資料、前端與載入架構：

| 批次 | 檔案／模組 | 主要責任 |
|---|---|---|
| A 診斷 | `SmallGroupController.DataApi.cs`、`NewPersonController.cs`、載入協調 service | 結構化 log、duplicate invariant、correlation |
| B 入口修正 | `SmallGroupController.LineLogin.cs`、`IntegrateView.cs`、`Date.cs`、其他直接呼叫者 | 移除錯誤平行化，統一 async ensure 入口 |
| C 快照核心 | `ListManager.cs`、`DownloadIntegrateData.*` | 新 loader、candidate、完整驗證、原子發布 |
| D state lifecycle | `InMemoryDataContextSmallGroup.cs`、cache/state service | single-flight 建立、session 隔離、eviction cleanup |
| E Dataverse | `UploadIntegrateData.*`、`DownloadIntegrateData.PresentRecord.cs`、`WeeklyReportProcessor.cs`、QR／新人路徑 | 業務鍵、Upsert、>1 衝突處理 |
| F 前端版本 | `ChurchReport.csproj`、`Views/Shared/_Layout.cshtml`、DevExtreme 靜態資產、`Startup.cs` | 版本統一、cache busting、cache policy |
| G 測試 | 測試專案／壓測腳本 | 併發、隔離、冪等、virtual scroll、soak |

每批完成都應先驗證再進下一批。Dataverse schema／資料清理應獨立變更單，不與前端資產升級綁在同一次不可逆部署。

---

## 7. 上線與回復策略

### 7.1 功能旗標

建議加入：

- `DuplicateNameDiagnostics`
- `IntegrateSingleFlightEnabled`
- `RejectDuplicateGridKeys`
- `UseStandardGridScrolling`
- `DataverseIdempotentWriteEnabled`

診斷 log 須可控制採樣率，避免長期大量記錄個資或造成 I/O 壓力。

### 7.2 上線順序

1. 先上診斷與告警，不改輸出語意。
2. 修 LINE 入口與載入 single-flight／snapshot。
3. 觀察重複 primary key 是否歸零。
4. 部署 Dataverse 唯一鍵與冪等寫入。
5. 清理既有資料。
6. 統一 DevExtreme 版本並驗證 virtual scrolling。

### 7.3 回復

- single-flight／Grid 設定可由 feature flag 回復。
- 快照架構回復前要確認不會重新暴露跨 Session／半完成資料；資料隔離問題不得以回復效能為理由重新啟用。
- Dataverse 已合併／刪除的資料不能只靠應用程式 rollback 復原，必須使用清理前匯出與 Dataverse 稽核／備份程序。
- Alternate Key 移除前要確認所有寫入者已停止依賴它。

---

## 8. 不應採用的快速修法

- 不要依 `FullName` 去重；同名是合法資料。
- 不要只在其中一個 Controller 加 `lock`；其他入口仍可進入同一底層方法。
- 不要在含 CRM I/O 的 async 路徑用 `lock` 長時間阻塞等待者。
- 不要建立永不清理的 `static ConcurrentDictionary<string, SemaphoreSlim>`。
- 不要只把 `LoadFlag = true` 提早；這會把「重複載入」變成「讀取半完成資料」。
- 不要只把 `Count > 1` 改成 `FirstOrDefault()`。
- 不要只做 Controller `Distinct()` 就宣稱根因已修。
- 不要只清 CRM 髒資料而不修寫入冪等性；重複會再出現。
- 不要只升級 `dx.all.js` 或只升級 NuGet wrapper；必須整套對齊。
- 不要以清除使用者瀏覽器快取作為正式修復；它只能暫時排除舊靜態資產。

---

## 9. 現場問題判斷速查表

| 現象 | 下一步 |
|---|---|
| Network JSON 已有同 `PresentRecordId` 兩次 | 查同一 load operation 是否並行、snapshot 是否共用寫入 |
| Network JSON 有同 `ContactId`、不同 `PresentRecordId` | 查 Dataverse 重複 business key 與建立來源 |
| Network 唯一、DataSource 重複 | 查 reload／refresh、store reuse、前端資產版本 |
| Network 與 DataSource 唯一、DOM 重複 | 關閉 virtual rendering A/B，升級／對齊 DevExtreme |
| 只在上傳 timeout 後出現 | 查重送與尚未完成的 server write，導入 idempotency key |
| 只在返回上一頁後出現 | 記錄 `pageshow.persisted`，查 bfcache 與 data refresh |
| 只在切換小組／日期後出現 | 查 requested key 與 published snapshot key 是否不一致 |
| 只在多台／多 worker 環境出現 | 查 sticky session；寫入唯一性移到 Dataverse 平台層 |

---

## 10. 最終判斷

Claude 的主假設「共享 `ListManager` 的並行載入競態」有充分的靜態程式碼基礎，但目前仍不是已由執行期證據證實的唯一根因。更新版 §3.6 對一般頁面流程的修正是有價值的：正常 HTML action 通常已先載入資料；真正的間歇性要從入口、快取狀態、bfcache、多小組／日期切換與部署拓樸解釋。

本次複核進一步確認，**LINE 登入內部的 `Task.WhenAll`、過早的 `LoadFlag = true`、缺少完整載入鍵、共享 loader、非原子快照發布、Dataverse 非冪等建立，以及 DevExtreme 22.1.6／23.1.5 混用**，都必須納入正式修正，不能只做一個 `Distinct()`。

最安全的執行順序為：

```text
三層取證
  -> 修 LINE 相依流程
  -> 建立 single-flight + build-then-swap 完整快照
  -> 修正 LoadState / LoadKey
  -> Dataverse 唯一鍵 + Upsert + 髒資料清理
  -> DevExtreme 整套版本統一與靜態資產版本化
  -> 併發、隔離、冪等、virtual scrolling 與 soak 驗證
```

只有當上述驗收門檻通過，才能宣告「重複姓名」以及更嚴重的跨小組／半完成資料風險已真正修復。

---

## 11. 參考資料

- Microsoft Learn — Session and state management in ASP.NET Core: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state>
- Microsoft Learn — Cache in-memory in ASP.NET Core: <https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory>
- Microsoft Learn — Use Upsert to create or update a record in Dataverse: <https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-upsert-insert-update-record>
- DevExpress T1194329 — Grid rows duplicated with virtual scrolling: <https://supportcenter.devexpress.com/ticket/details/t1194329/datagrid-grid-rows-get-duplicated-when-virtual-scrolling-is-enabled>
- DevExpress T754759 — reload with virtual scrolling duplicates rows: <https://supportcenter.devexpress.com/ticket/details/t754759/datagrid-duplicates-rows-if-the-data-source-s-reload-method-is-called-and-the-virtual>
- DevExtreme issue #20725 — virtual scrolling with duplicate keys: <https://github.com/DevExpress/DevExtreme/issues/20725>

---

## 12. 本文件完成時尚未執行的事項

- 未修改任何應用程式碼或 Dataverse schema／資料。
- 未執行 `dotnet build`、測試、壓力測試或 UI 自動化。
- 未連線正式站抓取 HAR。
- 未查詢正式 Dataverse 重複記錄。
- 未確認 IIS／負載平衡拓樸。
- 未確認授權可升級到哪個 DevExpress 支援版本。

這些項目應由 Phase 0 起依序完成，不應把靜態研究結果描述成已重現或已修復。
