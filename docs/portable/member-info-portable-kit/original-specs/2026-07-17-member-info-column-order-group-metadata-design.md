# 會友資訊欄位順序、姓名寬度與區／小組摘要設計

## 狀態與適用範圍

- 設計方案：使用者於 2026-07-17 批准方案 A。
- 適用工作區：`.worktrees/Sunny_5.1.2.WorktreeTuneMemberView`。
- 本規格只調整會友資訊樹狀檢視與三種共用會友 DataGrid，不修改會友細節彈窗。
- 本規格取代既有 `2026-07-17-member-info-resizable-sortable-columns-design.md` 中 `FullName` 的 `width: 96`、`minWidth: 80` 與舊欄位順序；其餘固定欄、調寬、排序、單一水平捲軸與觸控契約繼續有效。
- 使用者要求不 Commit、不合併；實作與驗證完成後仍由使用者先在 VS 2026 驗收。

## 目標

1. 進一步縮窄固定姓名欄，讓手機畫面能更早看見右側會友資料。
2. 移除姓名欄的人為最小寬度限制，讓使用者可繼續向左拖曳縮窄。
3. 在區長摘要顯示完整小組數與會友數。
4. 在小組摘要顯示 CRM 中已有的小組時間與小組地點，同時保持空值畫面乾淨。
5. 依使用者指定的工作順序重排會友欄位，並將「手機」更名為「行動電話」。

## 核准版面

### 區長摘要

區長姓名後方顯示兩個相鄰的數量標籤，順序固定為：

```text
區長：{區長姓名}　[{小組數} 組]　[本區 {會友數} 人]
```

- 小組數標籤位於既有「本區 N 人」標籤左側。
- `GroupCount` 是該區完整的實際小組節點數量。
- 獨立的「無小組」入口不屬於任何區長，不得算入 `GroupCount`。
- 小組分頁只裁切當頁顯示的 `Groups`；區長標頭仍顯示未分頁前的完整 `GroupCount`，不得用前端 `district.Groups.length` 臨時計算。
- 既有牧區空值規則不變：沒有牧區名稱時保持空白，不顯示「未填牧區」。

### 小組摘要

小組標頭固定保留：

```text
{小組名稱}
小組長：{小組長姓名}　[{會友數} 人]
```

小組時間或小組地點至少一項有值時，再於下方顯示摘要列：

```text
小組時間：{時間}　小組地點：{地點}
```

空值規則：

- 兩項都有值：依「小組時間、小組地點」順序顯示兩項。
- 只有一項有值：只顯示有值的項目，不留下空白標籤。
- 兩項皆空：整個時間／地點摘要列不存在，連「小組時間：」「小組地點：」標籤也不顯示。
- 無論時間與地點是否為空，小組名稱、小組長姓名及小組人數都必須繼續顯示。
- 值在後端與前端都先做 trim；不得顯示只有空白字元的摘要。

### 手機換行

- 區長數量標籤與小組時間／地點摘要可在窄螢幕自然換行，但不可遮住展開按鈕或領袖姓名。
- 不使用固定高度裁切摘要；內容變長時由標頭自然增高。
- 小組時間與地點是小組層級資訊，不可重複加入每一位會友的資料表欄位。

## 會友 DataGrid 欄位契約

三種表格——一般小組、搜尋結果、無小組——繼續共用 `miMemberColumns(remotePaging)`，欄位順序必須完全一致：

1. 頭像：`ContactId`
2. 姓名：`FullName`
3. 行動電話：`Phone`
4. 生日：`BirthDate`
5. 地址：`Address`
6. 信仰狀態：`SpiritualIdentity`
7. 會員身份：`MembershipStatus`
8. 關係目標：`RelationGoals`
9. 性別：`Gender`

### 頭像與姓名

- 頭像維持 `width: 72`、`fixed: true`、`fixedPosition: 'left'`、`allowResizing: false`、`allowSorting: false`。
- 姓名由 96px 縮小約 35%：`96 × 0.65 = 62.4`，採整數 `width: 62`。
- 姓名繼續 `fixed: true`、`fixedPosition: 'left'`。
- 姓名欄不得設定 `minWidth`；測試必須明確防止 `FullName` 欄位重新出現 `minWidth`。
- 本規格移除的是應用程式自行設定的最小欄寬；實際拖曳仍由 DevExtreme 原生欄寬引擎處理，不另寫會與表頭排序或 fixed rows touch bridge 衝突的拖曳器。
- 62px 可能讓 4 字姓名換行；保留既有 `wordWrapEnabled: true`，不得裁切姓名或移除姓名連結。

### 行動電話

- `Phone` 的 caption 由「手機」改為「行動電話」。
- `Phone` 設定 `alignment: 'center'`，使表頭與資料內容一致置中。
- 不改變 DTO 欄位名稱、CRM `mobilephone` 映射、搜尋條件或排序資料流。

### 其他欄位

- 性別移到最後一欄，仍使用 `Gender` 原有顯示字串與置中對齊。
- `RelationGoals` 仍是單一「關係目標」欄；一般小組與搜尋結果可本機排序，無小組 remote grid 維持 `allowSorting: !remotePaging`。
- 除頭像外，其餘欄位保留 DevExtreme 原生欄寬調整。
- 所有表格維持 `columnResizingMode: 'widget'`、`sorting: { mode: 'single' }`、`columnAutoWidth: false`、`columnHidingEnabled: false`。
- 不啟用欄位重新排序、adaptive 三點欄位或第二條水平捲軸。

## 後端資料契約

### CRM list 查詢

`MemberInfoController.FetchSmallGroupDescriptors` 的既有 `list` 查詢增加：

- `new_group_time`
- `new_group_place`

兩者使用同一筆既有 list 查詢取得，不增加逐小組額外 CRM request，避免 N+1 查詢與載入時間回歸。

### Descriptor 與 ViewModel

`SmallGroupDescriptor` 增加：

- `GroupTime`
- `GroupPlace`

`GroupNodeViewModel` 增加：

- `GroupTime`
- `GroupPlace`

`DistrictNodeViewModel` 增加：

- `GroupCount`

所有字串預設空字串，避免 null 迫使前端顯示 `undefined`；Builder 寫入前再 trim。

### Builder

`DistrictTreeBuilder` 在建立每個 `GroupNodeViewModel` 時複製並清理 `GroupTime`、`GroupPlace`。完成該區 Groups 建立後，以完整群組集合設定 `GroupCount`，再做既有小組排序。

`MemberCount` 的去重規則、未填區長排序、無小組人數計算、權限與 current-contact 過濾均保持不變。

## 前端資料流

```text
CRM list.new_group_time / new_group_place
    → FetchSmallGroupDescriptors
    → SmallGroupDescriptor
    → DistrictTreeBuilder
    → GroupNodeViewModel / DistrictNodeViewModel.GroupCount
    → LoadDistrictTree / SearchDistrictTree JSON
    → miDistrictHeader / miGroupHeader
```

- 一般瀏覽與搜尋後重建樹狀畫面都使用同一 DTO，不建立第二套摘要邏輯。
- 前端分頁複製 district 時保留 `GroupCount`；只替換當頁 `Groups`。
- 小組成員清單的 `LoadGroupMembers`、無小組清單與會友細節 API 不需新增時間／地點欄位。

## 錯誤與相容處理

- CRM 欄位缺值或空白：序列化為空字串，依核准空值規則隱藏摘要項目。
- CRM 欄位有值：以原始顯示字串呈現，不自行解析日期、星期或地址格式。
- 舊快取或舊 JSON 沒有 `GroupCount`、`GroupTime`、`GroupPlace` 時，前端以 0／空字串安全處理；正式新回應則必須包含正確值。
- 樹載入、搜尋、停止搜尋、返回會友資訊與 Loading overlay 的既有行為不修改。

## TDD 測試設計

正式程式修改前，先建立會失敗的測試：

1. `DistrictTreeBuilderTests`
   - descriptor 的時間／地點會 trim 並傳到 group node。
   - district `GroupCount` 等於完整實際小組數。
   - `GroupCount` 不受會員去重或無小組入口影響。
   - 空白時間／地點輸出空字串，小組名稱、LeaderName、MemberCount 仍保留。
2. `MemberInfoTreeControllerContractTests`
   - list `ColumnSet` 包含 `new_group_time`、`new_group_place`。
   - `SmallGroupDescriptor` mapping 包含 `GroupTime`、`GroupPlace`。
3. `MemberInfoTreeViewContractTests`
   - `FullName` 是 `width: 62` 且 fixed left，並且該欄位區塊不含 `minWidth`。
   - 欄位 dataField 順序精確符合核准的九欄順序。
   - `Phone` caption 是「行動電話」且 `alignment: 'center'`。
   - 區長摘要先呈現 `GroupCount + ' 組'`，再呈現 `本區 MemberCount + ' 人'`。
   - 小組名稱、LeaderName、MemberCount 無條件建立；時間／地點摘要只在至少一項 trim 後非空時建立。
   - 既有 fixed columns、widget resizing、single sorting、remote RelationGoals、單一捲軸、touch bridge 與無 adaptive dots 契約繼續通過。

## 自動驗證

- 針對性 RED：只執行本次新增／修改測試並確認因功能尚未存在而失敗。
- 最小實作後針對性 GREEN。
- 完整 `ChurchReport.MemberInfo.Tests` suite。
- Razor script block 的 `node --check`。
- `ChurchReport.csproj` Debug build；若 VS 鎖住預設輸出，改用隔離的 `BaseOutputPath`，不終止使用者的 VS／ChurchReport 程序。
- 所有本次檔案以 strict UTF-8 解碼，且不得含 U+FFFD。
- `git diff --check` 與變更範圍檢查。
- 依 AGENTS.md 平行重試 Gemini／Claude review；外部服務錯誤只能記錄為 unavailable，不得記為通過。

## 人工驗收

1. 在 320、390／430、640px 與桌機確認姓名預設寬度為 62px，頭像與姓名仍固定。
2. 姓名欄可繼續向左拖小，不再於應用程式設定的 80px 停止。
3. 區長列顯示兩個標籤：小組數在左、本區人數在右；跨分頁時小組數仍為全區總數。
4. 小組時間／地點皆空時，只隱藏時間／地點摘要；小組名稱、小組長與人數仍顯示。
5. 只有時間或只有地點時，只顯示有值項目；兩項都有時依時間、地點順序顯示。
6. 三種會友表格的欄位順序一致，行動電話置中，性別位於最後。
7. 點表頭可正反排序、分隔線可調寬、頭像不可調寬／排序。
8. 只有一條水平捲軸；固定欄與右側資料均可用手機手勢左右滑動，沒有 adaptive 三點按鈕。
9. 搜尋、停止搜尋、返回會友資訊、Loading、開啟姓名細節及頁面垂直滑動沒有回歸。

## 範圍外

- 不修改 CRM schema 或資料內容。
- 不在會友資料列重複顯示小組時間／地點。
- 不保存欄寬或排序偏好。
- 不修改權限、授權過濾、照片、會友細節或 LINE 同步流程。
- 不 Commit、不 merge、不更新主分支。

## 可攜式套件銜接

本規格屬於 2026-07-15（含）後的 MemberInfo 修改。應用程式完成功能與驗證後，暫停中的 portable-kit goal 必須新增本 Spec、後續 Plan、提示詞與增量 patch；在 reference snapshot、manifest 與 ZIP 更新完成前，不得交付舊套件。
