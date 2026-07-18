# 會友資訊姓名欄縮窄、欄寬調整與表頭排序設計

## 背景與目標

會友資訊表格已把頭像與姓名固定在左側。現有頭像寬 72px、姓名寬 130px，固定區總寬 202px；在 320px 手機上，右側可滑動資料只露出約 118px，使用者不容易預覽性別、生日等後續資訊。

本設計採用已核准方案：姓名預設縮為 96px，並使用 DevExtreme DataGrid 原生欄寬調整與單欄排序。頭像維持固定尺寸且不可調寬／排序；其餘欄位可從表頭分隔線以滑鼠或單指拖曳調寬，點擊表頭則在升冪與降冪間切換。

## 現況與技術條件

- 三類表格共用 `miMemberColumns(remotePaging)`：一般小組、無小組、搜尋結果。
- 一般小組與搜尋結果使用本機 rows；無小組使用 DevExtreme remote paging／sorting。
- 頭像 `ContactId` 與姓名 `FullName` 已使用 `fixed: true`、`fixedPosition: 'left'`。
- 前端實際載入 DevExtreme 22.1.6；伺服器 NuGet 版本不同，不能只看 csproj 判斷前端行為。
- 表格保留 `columnAutoWidth: false`、`columnHidingEnabled: false` 與單一 DevExtreme 水平 scrollable。
- fixed overlay 的既有 touch bridge 只綁定 rows view，不處理表頭，因此不應攔截表頭調寬或排序。

## 核准方案

### 1. 姓名預設寬度

- `FullName.width` 從 130px 改為 96px。
- `FullName.minWidth` 設為 80px，避免手動縮到無法辨識或無法操作。
- 頭像仍為 72px，因此預設固定區從 202px 降為 168px。
- 320px viewport 的右側可視區約由 118px 增為 152px，增加 34px，約提升 29%。
- 96px 可容納一般 3～4 個中文字、儲存格左右留白與表頭排序提示；較長姓名可依既有 word wrap 換行，或由使用者手動調寬。

### 2. 欄寬調整

所有會友 DataGrid 一致設定：

- `allowColumnResizing: true`
- `columnResizingMode: 'widget'`
- `columnAutoWidth: false`

`widget` 模式在拖寬目前欄位時增加整個 DataGrid 的內容寬度，不壓縮下一欄；後續資訊仍透過既有單一水平捲軸查看。不得使用 `nextColumn`，因為它會把相鄰欄位變窄，可能讓使用者剛調整姓名後又看不清性別或生日。

欄位規則：

- 頭像：`allowResizing: false`，固定 72px。
- 姓名、性別、生日、手機、信仰狀態、地址、會員身份、關係目標：可調整寬度。
- 不加入欄位重新排序；`allowColumnReordering` 維持關閉，避免頭像／姓名固定順序被改動。
- 使用 DevExtreme 原生表頭分隔線與 pointer/touch 辨識，不另寫自訂 header drag handler。
- 滑鼠移到分隔線或手指按住分隔線後左右移動時調整欄寬；拖曳完成不得觸發表頭排序。
- 欄寬只屬目前 DataGrid instance；切換頁面、重新建立表格或重新整理後回到規格預設值。本次不新增 localStorage、伺服器偏好或跨表格同步。

若 DevExtreme 22.1.6 在實際手機上的原生分隔線命中區不足，先記錄裝置、DOM 與事件證據並回到設計階段；不得直接加入會與 fixed rows touch bridge 競爭的第二套全域拖曳器。

### 3. 表頭排序

所有會友 DataGrid 使用 DevExtreme 原生單欄排序：

- `sorting.mode: 'single'`
- 一般點擊某資料欄表頭後升冪排序；再次點擊同一表頭切換為降冪，後續反覆點擊持續切換方向。
- 點擊另一欄時改由該欄排序，不保留多欄組合排序。
- 頭像：`allowSorting: false`。
- 姓名、性別、生日、手機、信仰狀態、地址、會員身份：可排序。
- `RelationGoals` 在一般小組／搜尋本機資料可排序；無小組 remote grid 維持既有 `allowSorting: !remotePaging`，因為關係目標是授權後彙整的計算欄位，不能把不存在的 CRM 欄位送到遠端排序。
- 點擊姓名表頭只排序；點擊資料列中的姓名連結仍開啟會友細節，兩者不得混用。

## 三種表格的一致性

1. 一般小組使用本機資料與完整欄位調寬／排序。
2. 搜尋結果沿用一般小組的 grid mount 與欄位工廠，因此行為完全相同。
3. 無小組使用相同欄位工廠、欄寬調整與單欄排序；實際排序由既有 remote data source 執行，只有 `RelationGoals` 禁止遠端排序。

任何一種表格都不得重新啟用 adaptive 三點欄位、第二條水平捲軸或欄位自動隱藏。

## 手機與固定欄互動

- 預設固定區為 168px；在 320px 螢幕仍保留約 152px 的右側可滑動區。
- 調整姓名欄後，頭像與姓名繼續固定在左側；右側仍由同一個 `getScrollable()` 水平捲動。
- rows view 的固定欄 touch bridge、6px 判向、垂直手勢保留、350ms 防誤點與普通姓名點擊行為不變。
- 表頭分隔線拖曳由 DevExtreme 原生 resizing 處理；固定 rows touch bridge 不得綁到 headers。
- 表頭點擊排序、分隔線拖曳調寬與資料列姓名點擊是三種不同操作，不能互相誤觸。

## 無障礙與操作回饋

- 保留 DevExtreme 原生排序狀態、表頭 focus 與排序方向提示，不自行建立只有滑鼠可用的表頭元件。
- 可調整欄位在滑鼠 hover 時使用 DevExtreme 原生 resize cursor／separator；觸控操作以實際 320、390／430、640px 裝置驗證。
- 欄位被縮窄時允許既有資料列換行增高，不以固定高度裁切姓名或地址。
- 頭像欄沒有排序／調寬假提示，避免使用者嘗試無效操作。

## 測試設計

先擴充 `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs` 建立失敗契約，再修改 Razor：

1. `FullName` 必須是 `width: 96`、`minWidth: 80`，並繼續 fixed left。
2. `ContactId` 必須是 `width: 72`、`allowResizing: false`、`allowSorting: false`。
3. 兩個 DataGrid 初始化入口都必須設定 `allowColumnResizing: true`、`columnResizingMode: 'widget'`、`sorting: { mode: 'single' }`。
4. `columnAutoWidth: false`、`columnHidingEnabled: false`、單一水平 scrollable 與禁止 adaptive dots 的既有契約繼續通過。
5. `RelationGoals` 繼續使用 `allowSorting: !remotePaging`，避免無小組 remote sorting 回歸。
6. 除頭像外不得出現 `allowResizing: false`；不得啟用 `allowColumnReordering` 或新增自訂 header touchmove drag handler。
7. 固定欄 rows touch bridge、姓名普通點擊、防誤點與三種 grid 共用欄位工廠的既有測試繼續通過。

自動驗證包含：針對性 RED→GREEN、完整 MemberInfo test suite、Razor JavaScript `node --check`、Debug build、strict UTF-8／U+FFFD、`git diff --check` 與差異範圍檢查。

## 人工驗收

在一般小組、無小組及搜尋結果各驗證一次：

1. 320、390／430、640px 與桌機初始載入時姓名欄為 96px，頭像＋姓名固定區明顯縮窄。
2. 從姓名、性別、生日、手機、信仰狀態、地址、會員身份、關係目標表頭分隔線，以滑鼠及手指左右拖曳，可調整目前欄位寬度。
3. 頭像分隔線不可調整，頭像寬度維持 72px。
4. 拖曳分隔線不觸發排序；輕點表頭才排序。
5. 同一資料欄連續點擊會在升冪／降冪間切換，點另一欄改為該欄排序。
6. 無小組的姓名、生日等 remote 排序正確；關係目標沒有可用的遠端排序操作。
7. 調寬或縮窄姓名後，頭像與姓名仍固定；表格仍只有一條水平捲軸，右側內容可由資料列與固定區手勢查看。
8. 點資料列姓名仍開啟正確明細；表頭拖曳與排序不會誤開明細。
9. 垂直滑動頁面、搜尋、停止搜尋、返回會友資訊、Loading 與 adaptive dots 禁止契約沒有回歸。

## 範圍外

- 不修改 Controller、API、DTO、CRM schema、權限、照片或搜尋資料流。
- 不讓使用者重新排列、固定或解除固定欄位。
- 不保存個人欄寬／排序偏好，不跨頁或跨裝置同步。
- 不修改會友細節 popup 內的其他 DataGrid。
- 不 Commit；由使用者實際驗收後自行提交。

## 可攜式套件銜接

本功能屬 2026-07-15（含）後的 MemberInfo 規格。完成實作與驗證後，目前進行中的可攜式部署 goal 必須再把本 Spec、後續 Plan、提示詞與參考增量納入權威清單，從 8 Specs／8 Plans 更新為 9／9，再重建 Manifest 與 ZIP；不得交付只含前一版固定欄的舊套件。
