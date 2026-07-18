# 會友資訊改版設計：區長 → 小組 → 會友 三層樹狀（Master-Detail）

- 日期：2026-07-15
- 分支：`Sunny_5.1.2.TuneMemberView`
- 影響頁面：`會友資訊`（`/MemberInfo/Index` → `Views/MemberInfo/MemberInfoGrid.cshtml`）

## 1. 背景與目標

現況「會友資訊」是一張**平面 DevExtreme DataGrid**（欄位：照片、姓名、會員身分、手機、小組），全教會為 CRM 伺服器端分頁、牧養名單為記憶體載入。

目標：改成**可折疊的三層樹狀（Master-Detail）**：

1. **第 1 層：區** — 顯示 `區名` ＋ `區長姓名` ＋ `該區總人數`
2. **第 2 層：小組** — 同一列顯示 `小組名稱` ＋ `小組長姓名` ＋ `該組人數`
3. **第 3 層：會友資料表**（點小組才載入）— 欄位：`頭像`、`姓名`、`性別`、`生日`、`手機`、`信仰狀態`、`地址`、`會員身份`、`關係`、`目標`

核心動機：**避免一進頁面就載入所有人的頭像**（效能）。因此第 3 層採「點了那一組才去抓那組成員與頭像」。

觸控需求：不要像現有「組員資訊」那種很小的三角形（手機難點）。**整個區列、整個小組列都可點**開合，同時保留一個較大的三角形當視覺提示。

## 2. 名詞與 CRM 欄位對應

CRM 組織架構為四層：**區牧 → 區長 → 小組 → 成員**（見 `ListManagementController` 註解、`ChurchListDataProcessor`）。本頁只呈現其中**三層（區長 → 小組 → 會友）**，跳過「區牧」這一層。

| 畫面概念 | 來源 | 備註 |
|---|---|---|
| 小組（清單） | `list`，條件 `statecode=0`、`purpose="小組名單"`、`new_app_named=true` | 一筆 = 一個小組 |
| 小組名稱 | `list.listname` | |
| 小組長姓名 | `list.new_contact_family_leader_list`（lookup→contact） | |
| 區長姓名 | `list.new_contact_race_leager_list`（lookup→contact） | 第 1 層分群鍵 |
| 區名 | `list.new_area_name`（值為「{區牧}牧區」，例：曉光牧區） | 若空白，退而用區牧 `new_contact_list_arealeader` 的姓名 ＋「牧區」 |
| 成員↔小組 | `listmember`（`listid`、`entityid`→contact） | |
| 姓名 | `contact.fullname` | 可點 → 開會友細節彈窗 |
| 性別 | `contact.gendercode`（OptionSet→文字：男／女） | |
| 生日 | `contact.birthdate`（格式 yyyy/M/d；年份 ≤1 顯示空白） | |
| 手機 | `contact.mobilephone` | |
| 信仰狀態 | `contact.new_spiriitual_identity`（OptionSet→文字） | |
| 地址 | `contact.address2_line1` | |
| 會員身份 | `contact.customertypecode`（OptionSet→文字） | |
| 關係 / 目標 | `connection`（角色＋對象姓名），沿用 `MemberInfoController.GetRelationGoals` 的來源 | `關係`=角色、`目標`=對象姓名 |
| 頭像 | `contact.entityimage` / LINE 圖 / 性別剪影，沿用 `GetContactImagesBatch` | |

**第 1 層分群鍵＝區長（RaceLeader）**：每個區長為一個第 1 層節點，標題顯示「{區名}　區長：{區長姓名}」。同一牧區下若有多位區長，會出現多個第 1 層節點（各自的區名相同、區長不同）。區長未填者歸到「區長未填」節點。

## 3. 存取範圍（沿用現有權限機制）

`MemberInfoController.GetAccess()` 回傳兩種存取層級，兩者共用同一套樹狀 UI，只是資料範圍不同：

- **全教會（`MemberInfoAccess.Church`）**：涵蓋全教會所有「小組名單」。另含「無小組」節點。
- **牧養名單（`MemberInfoAccess.ShepherdList`）**：只涵蓋登入者所帶的名單（來自 `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData` 的 `ListEntityId` 集合）。通常沒有「無小組」節點（其可見的人都在其名單內）。

## 4. 三層結構、顯示欄位與預設展開

### 4.1 版面示意

```
▼ 曉光牧區   區長：陳志明            （本區 32 人）   ← 第1層（區）：預設展開
    ▶ 以斯帖小組   小組長：王小明     （8 人）        ← 第2層（小組）：名稱看得到、成員收合
    ▶ 小城小組     小組長：李大華     （5 人）
    ▶ SUNNY小組    小組長：張美玲     （6 人）
▼ 恩琦牧區   區長：林恩琦            （本區 18 人）
    ▶ 喜樂小組     小組長：…          （7 人）
    ▶ 活水小組     小組長：…          （11 人）
▶ 無小組                             （120 人）       ← 只有這個預設收合
```

點開某一小組後（第 3 層）：

```
    ▼ 以斯帖小組   小組長：王小明     （8 人）
        ┌───────────────────────────────────────────────┐
        │ 頭像 姓名 性別 生日 手機 信仰狀態 地址 會員身份 關係 目標 │
        │  👤  陳XX 女 1990/8 09xx 基督徒 新店… 小組組員 妻子 王小明 │
        └───────────────────────────────────────────────┘
```

### 4.2 預設展開規則（頁面載入時）

- **有小組的區（區長）→ 全部展開**：一進來就看到每一區底下所有小組的「名稱／小組長／人數」（只顯示小組標題，不載入成員）。
- **每個小組的「成員表格」→ 預設收合**：點小組那一列才向後端載入該組成員與頭像。
  - **例外**：當整個範圍只有**一個小組**時（例：小組長只帶一組）→ 那一組直接連成員一起展開。
- **無小組 → 預設收合**：人數可能很多，點了才展開、伺服器端分頁載入。

### 4.3 互動

- **整個區列、整個小組列皆可點**開合（非只有三角形）。
- 保留一個**較大的三角形**（▼／▶）當視覺提示，手機好按。
- 點第 3 層某位會友的姓名 → 沿用現有 `openMemberInfoDetailPopup(contactId, fullName)` 開「會友細節」彈窗（維持既有體驗）。

## 5. 前端架構

採**自訂折疊列（第 1／2 層）＋ 每個已展開小組掛一個 DevExtreme DataGrid（第 3 層）**。

理由（相對其他方案）：

- DevExtreme 兩層 grouping（現有「組員資訊」做法）：小三角形問題仍在，且 masterDetail 綁在資料列、無法「只載入某一組」→ 會被迫一次載入全部成員與頭像，違反效能目標。
- DevExtreme TreeList 單一元件包三層：群組摘要列與成員豐富欄位（頭像／關係／目標）被迫共用欄位模型，彆扭；大觸控區與逐節點表格樣式也難做。
- **自訂折疊列**：完全掌握「整列可點」的大觸控區、真正的逐組延遲載入、第 3 層可重用現有頭像批次與細節彈窗、分頁容易。

DOM 結構（示意）：

```
#memberInfoTree
  .mi-district (data-race-leader-id)         第1層，整列可點
    .mi-district-header  ▼ 區名 / 區長 / 人數
    .mi-district-body                         預設展開
      .mi-group (data-list-id)               第2層，整列可點
        .mi-group-header  ▶ 小組名 / 小組長 / 人數
        .mi-group-body                        預設收合；展開時掛 DataGrid
          #mi-grid-{listId}                   第3層 DevExtreme DataGrid（點開才建立）
  .mi-ungrouped                               無小組節點，預設收合，展開→成員表格（分頁）
```

## 6. 後端 API 與資料流

於 `MemberInfoController` 新增三個 action（沿用現有連線池／授權／頭像）：

### 6.1 `GET LoadDistrictTree` — 第 1／2 層骨架

回傳整棵樹的骨架（**不含任何成員與頭像**）：

```jsonc
{
  "districts": [
    { "raceLeaderKey": "...", "areaName": "曉光牧區", "raceLeaderName": "陳志明", "memberCount": 32,
      "groups": [ { "listId": "GUID", "groupName": "以斯帖小組", "leaderName": "王小明", "memberCount": 8 } ] }
  ],
  "ungrouped": { "memberCount": 120 },   // 全教會才有
  "scope": "church" | "shepherd"
}
```

建立方式：

- **全教會**：
  1. 查所有小組名單 `list`（帶 `listname`、`new_area_name`、`new_contact_race_leager_list`、`new_contact_family_leader_list`、`new_contact_list_arealeader`）。
  2. 查這些名單的 `listmember`，並以 `LinkEntity` 連 `contact` 帶回 `statecode`、`customertypecode`，用來**只計在籍、排除結案**的成員。以此算「每組人數」與「每區（依區長去重 contact）人數」，並記住「已在某組的 contactId 集合」。
  3. `無小組` = 在籍且不在任何小組名單的 contact。取「在籍 contact 總數」－「已在某組的去重人數」得 `ungrouped.memberCount`。
  4. 骨架不含個資 → 以 3 分鐘快取（比照現有 `member-info-church-rows` 快取模式）。快取鍵標明 `church` 範圍。
- **牧養名單**：範圍限縮為登入者名單的 `ListEntityId` 集合，跑同一套聚合；此範圍屬使用者專屬 → **不進共用快取**（即時計算，量小）或以登入身分綁鍵。

### 6.2 `GET LoadGroupMembers(listId, [search])` — 第 3 層（點開才呼叫）

- **授權**：`listId` 必須落在使用者可見範圍（全教會＝全部小組名單；牧養＝自己帶的名單）；否則 `403`。再對成員以 `CanViewContactsBatch` 把關。
- 撈該名單在籍、排除結案的成員，帶 `fullname`、`gendercode`（轉文字）、`birthdate`、`mobilephone`、`new_spiriitual_identity`（轉文字）、`address2_line1`、`customertypecode`（轉文字）。
- **關係／目標**：以**一次** `connection` 查詢（`record1id`／`record2id` `In` 該組成員）批次帶回，避免逐人 N+1（現有 `GetRelationGoals` 為單筆版，需新增批次版）。
- 回傳資料列（**不含頭像 bytes**）。前端渲染後，沿用現有 `GetContactImagesBatch` 批次補頭像。
- 有 `search` 時，只回符合的成員。

### 6.3 `GET LoadUngroupedMembers(loadOptions, [search])` — 無小組（分頁）

- 僅全教會。撈在籍、排除結案、且不在任何小組名單的 contact，**伺服器端分頁**（比照現有全教會分頁做法）。欄位同 6.2。

## 7. 特殊情況與規則

- **無小組**：第 1 層的特殊節點（與各牧區同層），預設收合；展開後**直接**顯示成員表格（不再多一層假的「無小組小組」），並伺服器端分頁。
- **人數**：一律計「在籍、排除結案」的成員，讓標題人數與點開後看到的列數一致。
- **關係／目標**：拆兩欄。一人多筆時，兩欄以**相同順序**並排（關係：`妻子、門徒`／目標：`王小明、李大華`）。
- **搜尋（保留）**：輸入關鍵字 → 後端回「含符合成員的小組清單（＋是否命中無小組）」。前端只顯示這些區／組並自動展開，第 3 層帶 `search` 只顯示符合的人；清空搜尋 → 回到完整樹（預設展開狀態）。
- **分頁（小組 50／頁）**：整棵樹以「每頁最多 50 個小組」呈現，跨頁時該區的標題會在下一頁重覆出現；以上一頁／下一頁切換。實務上小組多半不到 50，鮮少觸發。
- **工具列**：保留「搜尋」與「重新同步LINE」（僅全教會管理者）；**移除「顯示照片／顯示全部」按鈕**（樹狀下照片本就只在展開某組時才載入，該篩選意義不大）。

## 8. 權限與安全（沿用既有教訓）

- 範圍由 `GetAccess()` 決定；第 3 層與搜尋端點都**再次**以 `CanViewContactsBatch` 把關，**絕不信任前端傳來的 `listId`／`contactId`**。
- **不把使用者專屬資料放進共用快取**（沿用 2026-01-13 session 外洩教訓）：全教會骨架不含個資可共用快取；牧養名單骨架即時計算或綁登入身分。
- 重用既有頭像端點（`GetContactImagesBatch` 內含批次授權 `CanViewContactsBatch`）。

## 9. 會動到的檔案

- `Controllers/MemberInfoController.cs`：新增 `LoadDistrictTree`、`LoadGroupMembers`、`LoadUngroupedMembers` 與批次關係／目標查詢；沿用現有頭像／細節／授權／OptionSet 快取。
- `Views/MemberInfo/MemberInfoGrid.cshtml`：改寫為樹狀 UI（第 1／2 層折疊列 ＋ 第 3 層 DataGrid）；保留搜尋與重新同步LINE、移除顯示照片鈕。（提醒：`.cshtml` 於 net10.0 編入 DLL，改動需重新發佈＋重啟 app pool 才生效。）
- `ViewModels/`：新增 `DistrictNodeViewModel`、`GroupNodeViewModel`、`GroupMemberRowViewModel`。

保留可重用：`GetContactImagesBatch`、`openMemberInfoDetailPopup` 與細節彈窗、`CanViewContactsBatch`、OptionSet 共用快取服務。

## 10. 待實作時驗證的假設

1. `list` 的 `new_contact_race_leager_list`、`new_contact_family_leader_list`、`new_area_name` 於現有資料多數有值；`new_area_name` 空白時以區牧姓名＋「牧區」補上。
2. `listmember` 以 `LinkEntity` 連 `contact` 可同時帶回 `statecode`／`customertypecode`（供人數過濾），一次查詢完成。
3. `connection` 批次查詢（`record1id`／`record2id` `In` 一組成員）效能可接受（單組通常 5–30 人）。
4. 全教會「小組名單」數量在數十～上百之間（骨架查詢便宜、可快取）。

## 11. 決策記錄（已與需求方定案）

- 三層＝區長 → 小組（小組名＋小組長同列）→ 會友；跳過「區牧」層。
- 第 1 層顯示「區名（`new_area_name`）＋區長姓名＋人數」。
- 全教會與牧養名單都改樹狀。
- 有小組的區預設全展開（露出小組名稱）；小組成員表格預設收合、點了才載入；範圍只有一組時該組直接展開。
- 無小組預設收合，展開直接顯示成員、分頁。
- 成員表 10 欄（一般小組與無小組共用）：頭像、姓名（可點開細節）、性別、生日、手機、信仰狀態、地址、會員身份、關係、目標。
- 關係／目標拆兩欄、同順序並排。
- 每頁最多 50 個小組、跨頁區長標題重覆。
- 保留搜尋與重新同步LINE、移除顯示照片按鈕。
