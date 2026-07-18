# 會友資訊固定頭像與姓名欄位設計

## 背景與目標

會友資訊表格目前可在桌機以水平捲軸、在手機以手指左右滑動查看全部欄位，但滑到右側後，頭像與姓名會一起離開畫面，使用者難以確認目前閱讀的是哪位會友。

本設計採用已核准的方案 A：使用 DevExtreme DataGrid 原生固定欄能力，讓「頭像」與「姓名」永遠固定在表格左側，其餘欄位維持現有水平捲動。

## 根因

`ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml` 的 `miMemberColumns()` 是一般小組、無小組及搜尋結果共用的欄位工廠。現有頭像與姓名欄只有 `width` 與 `cellTemplate`，沒有 `fixed` 設定，因此兩欄會隨水平內容一起移出視窗。

現有單一 DevExtreme 水平捲動架構、`useNative: true`、`scrollByContent: true`、`columnHidingEnabled: false` 與手機 `touch-action` 均已符合需求，不是本次問題來源。

## 核准方案

在 `miMemberColumns()` 的前兩個欄位加入：

- 頭像：`fixed: true`、`fixedPosition: 'left'`，保留 72px 寬度。
- 姓名：`fixed: true`、`fixedPosition: 'left'`，保留 130px 寬度。

兩欄原本已依序位於最前方，因此固定後的視覺順序仍是「頭像 → 姓名 → 其餘可滑動欄位」。不新增第二個表格、不複製資料列，也不使用 CSS `position: sticky` 模擬固定欄。

目前前端實際載入 DevExtreme 22.1.6。此版本會把固定欄渲染成絕對定位覆蓋層；專案先前曾因固定區攔截手機橫向手勢而移除 fixed。為同時滿足本次固定需求與既有「在資料列上用手指滑動」契約，固定欄需搭配一個限定在固定資料列覆蓋層的觸控轉接器，而不是只加入兩個 `fixed` 屬性。

## 適用範圍

由於三類表格都呼叫相同的 `miMemberColumns()`，設定必須一致套用到：

1. 一般小組展開後的會友表格。
2. `無小組` 會友表格。
3. 搜尋到一筆或多筆資料時，直接取代瀏覽表格的搜尋結果表格。

零筆搜尋仍顯示既有訊息，不建立 DataGrid，因此沒有固定欄行為。

## 捲動與手機互動

- 外層 `.mi-grid-host` 繼續隱藏自身水平 overflow，避免出現第二條捲軸。
- 唯一水平捲動責任仍由 DevExtreme 內部 `dx-scrollable` 負責。
- 保留手機原生慣性、`pan-x pan-y`、`useNative: true` 與 `scrollByContent: true`。
- 固定資料列區使用 `touch-action: pan-y`：垂直手勢維持瀏覽器原生行為，橫向手勢交由轉接器處理。
- 轉接器只在單指移動超過小門檻且橫向位移大於垂直位移時啟動，透過同一個 DataGrid `getScrollable().scrollBy(...)` 捲動右側欄位。
- 橫向滑動完成後短暫抑制合成 click，避免使用者滑動姓名時誤開會友細節；沒有移動的普通點擊仍照常開啟。
- 每個固定覆蓋層只綁定一次事件；DataGrid 重繪後由共用 `onContentReady` 對新的覆蓋層重新檢查，不累積重複 handler。
- 其餘欄位寬度、響應式字級、列高及 48px 觸控目標不變。
- 保留 `columnHidingEnabled: false`，不得出現 adaptive 三點欄位或隱藏右側欄位。

固定欄總寬維持 202px。即使在 320px 窄螢幕，右側仍保留可滑動區域；本次不額外壓縮頭像或姓名，以免降低辨識與點擊能力。

## 資料、錯誤與無障礙

- 不改動後端 API、DTO、CRM 查詢、權限、照片載入或搜尋狀態機。
- 頭像與姓名的既有 cell template、姓名點擊開啟會友細節及替代文字行為全部保留。
- Loading、錯誤、重試、停止搜尋、返回會友資訊及零筆結果流程不變。
- DevExtreme 原生固定欄負責同步固定區與可捲動區的表頭及資料列，不自行維護兩套 DOM；觸控轉接器只轉送橫向位移，不複製資料或介入載入流程。

## 測試與驗收

先擴充 `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`，建立會失敗的契約，要求：

- `ContactId` 頭像欄同時具備 `fixed: true` 與 `fixedPosition: 'left'`。
- `FullName` 姓名欄同時具備 `fixed: true` 與 `fixedPosition: 'left'`。
- 共用 `miMemberColumns()` 仍被一般小組、無小組與搜尋結果使用。
- `columnHidingEnabled: false`、單一原生水平捲動及禁止 `hidingPriority` 的既有契約繼續通過。
- 性別、生日、手機、信仰狀態、地址、會員身份與關係目標不設定為固定欄。
- 固定資料列區具備單次綁定的觸控轉接器、6px 方向判定門檻、水平 `scrollBy`、垂直手勢保留及滑動後 click 抑制。

自動驗證包含 MemberInfo 完整測試、Razor JavaScript 語法檢查、相關建置、UTF-8／U+FFFD 與 diff 範圍檢查。實際操作需在桌機及 320px、390／430px、640px 寬度確認：從固定的頭像區、姓名區及右側可捲動區起手都能左右滑動；滑到最右側後頭像與姓名仍可見；垂直滑動、姓名點擊及單一水平捲軸均正常。

## 不在本次範圍

- 不調整欄位順序、欄寬或字級。
- 不修改會友細節彈窗內的聚會／裝備紀錄表格。
- 不加入使用者自行固定／解除固定欄位的選單。
- 不修改任何後端、資料庫、CRM schema 或權限行為。
- 不 Commit；由使用者完成實際驗收後自行提交。
