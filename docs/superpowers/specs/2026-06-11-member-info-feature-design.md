# 會友資訊（Member Info）功能設計規格

- 日期：2026-06-11
- 狀態：設計已獲使用者口頭核准（待規格書面審閱）
- 作者：Claude（與使用者 mengsunghu 協作）
- 相關技術：ASP.NET Core MVC + DevExtreme 21.2.7 + Microsoft Dataverse/Dynamics 365 CRM

---

## 1. 目標與背景

在左側導覽新增「**會友資訊**」入口，提供：

1. 一個連絡人網格（類似小組回報的「小組牧養」網格）。
2. 點擊網格「姓名」彈出該連絡人的**唯讀**細節頁面（含大頭貼、手機、地址、信仰狀態、關係目標），並可在彈窗左側子導覽切換「聚會紀錄」與「裝備紀錄」兩個子網格。
3. 依角色決定可見性與資料範圍：
   - **牧師傳道 / 牧養主任**（CRM `new_church_jobtitle`）→ 看**全教會現行連絡人**。
   - **帶領牧養小組者**（`LoginType=="小組長"`）→ 只看**自己各類名單底下**的連絡人。

此功能與既有「組員資訊」（`/Personal/MaintainInfomationView`）**並存**，不取代之。

## 2. 已確認的決策（來自使用者）

| 主題 | 決定 |
|---|---|
| 細節彈窗是否可編輯 | **唯讀檢視**（僅顯示，不寫回 CRM） |
| 全教會範圍定義與載入 | `statecode=啟用` 且 `customertypecode≠結案`，DataGrid **伺服器端分頁＋搜尋** |
| 關係目標來源 | Dynamics 365 **Connection / Connection Role（連接／連接角色）**（使用者答覆 *RoleConnection*）。⚠️ 程式碼目前尚未使用此機制，需新接；若實際上是某 `new_xxx` 欄位，於審閱時更正 |
| 與「組員資訊」關係 | 並存，新功能為獨立 controller/view |

## 3. 架構決策

採「**新增獨立 controller + 專屬視圖，所有端點以 `contactId` 參數化**」（評估中的方案 A）。

理由：
- 既有「組員資訊」綁定登入者小組範圍且邏輯複雜，不宜混入全教會＋彈窗邏輯。
- 明確需求是「彈跳細節表單」，排除以 master-detail 展開列取代彈窗的做法。
- 全教會範圍與牧養名單範圍可在同一端點以旗標乾淨分流。

新增 `MemberInfoController : BaseChurchController`，沿用既有 `ToolUtility` / `InMemoryContext` / 連線池 / `HandleError` / `EnsureCorrectUserData` 等基礎設施。

## 4. 元件設計

### 4.1 導覽與權限分流

- 在 `BaseChurchController` 新增 `SetupMemberInfoViewBag()`：
  1. 取登入者 contact（`InMemoryContext.PersonalInfomationModel.m_LoginContact` 或 `ListManager` 之 `m_ContactEntity`）的 `new_church_jobtitle`。
  2. 若字串**包含**「牧師傳道」或「牧養主任」→ `ViewBag.MemberInfoAccess = "全教會"`。
  3. 否則若 `InMemoryContext.ListManager.LoginType == "小組長"` → `ViewBag.MemberInfoAccess = "牧養名單"`。
  4. 皆非 → 不設定（不顯示按鈕）。
- 此方法在會顯示導覽的頁面流程中呼叫（與 `SetupBasicViewBag()` 同處）。
- `Views/Shared/_Layout.cshtml`：在既有導覽清單適當位置加：
  ```cshtml
  @if (ViewBag.MemberInfoAccess == "全教會" || ViewBag.MemberInfoAccess == "牧養名單")
  {
      <li><a href="/MemberInfo/Index"><i class="fas fa-id-card"></i>會友資訊</a></li>
  }
  ```
- 判定參考既有樣板：`QpayManager.cs` 用 `GetEntityStringAttribute(aContact,"new_church_jobtitle")` 判 `IsAOfficeWorker`。

### 4.2 會友資訊網格（`Views/MemberInfo/MemberInfoGrid.cshtml`）

- DevExtreme `DataGrid`，欄位：
  - **照片**：`cellTemplate` 以 `<img src="/Personal/GetContactImage?contactId={ContactId}&size=48">`（可選用 `GetContactImagesBatch` 批次預載，避免 N+1）。
  - **姓名**：`cellTemplate` 渲染可點擊連結，`onClick` 帶 `ContactId` + `FullName` 開啟細節彈窗。
  - **手機**、**小組**。
- 鍵為 `ContactId`。
- 資料來源 `GET /MemberInfo/LoadMemberInfoList`，依 `ViewBag.MemberInfoAccess` 分流：
  - **全教會**：伺服器端 `DataSourceLoadOptions`（分頁/排序/搜尋下推），CRM `QueryExpression("contact")` 條件 `statecode=0（啟用）` 且 `customertypecode != 結案值`；欄位 `contactid,fullname,mobilephone` + 小組名稱。
  - **牧養名單**：沿用 `PersonalController.LoadMaintainPersonInfomation` 的「逐 `m_MultiGroupList` 名單 `RetrieveMemberListCollectionByListId` → `Retrieve("contact",…)`」邏輯，僅限自己的名單。
- 「小組」欄位來源：牧養名單模式可用名單名稱（`groupRecord.Name`）；全教會模式需決定取得連絡人主要小組之方式（見開放問題 OQ-2）。

### 4.3 細節彈窗（唯讀）（`Views/MemberInfo/_MemberDetailPopup.cshtml`）

- 以 DevExtreme `Popup`（樣板可參考 `Views/DedicationAudit/*`、`Views/Dedication/KeyInDedicationFeeViewWeb.cshtml`）。
- 版面：
  - **左上**：較大、較清晰的大頭貼 `<img src="/Personal/GetContactImage?contactId={id}&size=0">`（`size<=0` 回傳原圖，見 `PersonalController.ImageUpload.cs`）。
  - **基本資訊（唯讀）**：手機、地址、信仰狀態、關係目標。
  - **左側子導覽**：兩顆按鈕「聚會紀錄」「裝備紀錄」，切換右側內容區（預設顯示聚會紀錄）。
- 彈窗內容透過 `GET /MemberInfo/Detail?contactId={id}` 取得（回傳 partial 或 JSON）。

### 4.4 子網格（以 contactId 參數化）

- **聚會紀錄**「個人靈修與聚會紀錄」：
  - 端點 `GET /MemberInfo/LoadContactPresentRecords?contactId={id}`。
  - 來源 `ToolUtility.RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)`。
  - 欄位：**姓名 / 主日 / 小組 / 代禱**（對應 `Member.FullName/Sunday/SmallGroup/PrayItem`）。
- **裝備紀錄**「上課紀錄單」：
  - 端點 `GET /MemberInfo/LoadContactStorLessons?contactId={id}`。
  - 來源 `ToolUtility.RetrieveStorLessonsByFetchXml(fullName, contactId)`，逐筆映射 `new_disciple_lessons` 取階段與日期（複用 `EquipmentController.LoadEquipmentStorLessons` 既有映射邏輯）。
  - 欄位：**課程名稱 / 日期 / 階段名稱 / 是否結業**（對應 `EquipmentStorLessons.DiscipleLessonsName/DiscipleLessonsDateTime/StageName/CurrentComplete`）。
  - 視圖可大量參考既有 `Views/Equipment/EquipmentStorLessonsView.cshtml`。

### 4.5 關係目標（Connection Roles）

- 查 CRM `connection` 實體：以本連絡人為 `record1id`，取 `connectionroleid`（→ 角色名稱）與 `record2id`（→ 對象顯示名稱）。
- 於 `MemberInfoController.Detail` 組裝為清單（角色 + 對象），顯示於細節面板「關係目標」。
- ⚠️ 此為新接機制，需確認 CRM 是否已啟用 Connection；若否則此區塊回傳空清單並安全降級。

### 4.6 照片

- 直接複用 `PersonalController` 既有端點：`GET /Personal/GetContactImage`（縮圖/原圖）、`POST /Personal/GetContactImagesBatch`（批次 Base64）。
- 照片儲存於 CRM `entityimage`，已有 MemoryCache。

### 4.7 伺服器端權限把關（安全要點）

- **不可信任前端傳來的 `contactId`**。所有以 `contactId` 為參數的端點（`Detail`、`LoadContactPresentRecords`、`LoadContactStorLessons`）都要先驗證該 `contactId` 在呼叫者允許範圍內：
  - `MemberInfoAccess=="全教會"` → 允許任一啟用連絡人。
  - `MemberInfoAccess=="牧養名單"` → 僅允許出現在呼叫者自己名單成員集合中的 `contactId`。
  - 皆非 → 一律拒絕（`MemberInfoAccess` 未設定者不得呼叫任何 `/MemberInfo/*`）。
- 牧養名單成員集合可由 `m_MultiGroupList` 名單成員快取／重查取得，作為白名單。

## 5. 資料來源／CRM 方法對照

| 用途 | 方法 / 端點 | 來源檔 |
|---|---|---|
| 教會職稱 | `GetEntityStringAttribute(contact,"new_church_jobtitle")` | `QpayManager.cs:660` |
| 名單成員 | `RetrieveMemberListCollectionByListId(listGuid)` | `PersonalController.cs:258` |
| 連絡人欄位 | `Retrieve("contact", id, ColumnSet(...))` | `PersonalController.cs:287` |
| OptionSet 文字 | `OptionSetMetadataService.GetOptionSetText/Mapping` | `PersonalController.cs:542` |
| 聚會紀錄 | `RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)` | `WebServiceConnector/NewPerson.cs:1306` |
| 裝備紀錄 | `RetrieveStorLessonsByFetchXml(fullName, contactId)` | `EquipmentController.cs:306` |
| 照片 | `/Personal/GetContactImage`、`/Personal/GetContactImagesBatch` | `PersonalController.ImageUpload.cs` |
| 連接角色 | `RetrieveMultiple(QueryExpression("connection"))` | （新接） |

**CRM contact 欄位**：`contactid, fullname, mobilephone, address2_line1, customertypecode（會員身分）, new_spiriitual_identity（信仰狀態，注意拼字）, new_equipment_status, new_church_jobtitle（教會職稱）, statecode, entityimage`。

## 6. 影響的檔案

**新增**
- `Controllers/MemberInfoController.cs`（`Index`、`LoadMemberInfoList`、`Detail`、`LoadContactPresentRecords`、`LoadContactStorLessons` + 範圍白名單輔助）。
- `Views/MemberInfo/MemberInfoGrid.cshtml`（網格頁，對應 `Index`）。
- `Views/MemberInfo/_MemberDetailPopup.cshtml`（彈窗與兩個子網格）。
- 視需要：`ViewModels/MemberInfoDetailViewModel.cs`（細節 + 關係目標清單）。

**修改**
- `Controllers/BaseChurchController.cs`：新增 `SetupMemberInfoViewBag()`。
- `Views/Shared/_Layout.cshtml`：新增「會友資訊」`<li>`（依 `ViewBag.MemberInfoAccess`）。
- 各會顯示導覽的進入點視需要呼叫 `SetupMemberInfoViewBag()`（與 `SetupBasicViewBag()` 同步）。

## 7. 開放問題 / 假設

- **OQ-1（關係目標）**：確認採 Connection/Connection Role；或提供 `new_xxx` 欄位名。CRM 是否已啟用 Connection 待查。
- **OQ-2（全教會「小組」欄位）**：全教會模式下，連絡人的「小組」如何取得（主要牧養小組？以連絡人對名單的多對多關係第一筆？）。實作時確認查詢方式與效能。
- **假設**：`new_church_jobtitle` 為單一字串欄位，以「包含」判斷多個職稱字樣（如同 `Contains("會計")` 的既有寫法）。
- **假設**：「現行」＝ `statecode` 啟用且 `customertypecode≠結案`。

## 8. 不在範圍（YAGNI）

- 細節彈窗的任何**編輯/寫回 CRM**（本期唯讀）。
- 全教會範圍的批次匯出、列印、進階篩選器（除基本搜尋外）。
- 變更或重構既有「組員資訊」功能。
- 連接角色的**新增/維護** UI（僅唯讀顯示既有連接）。

## 9. 測試策略（概要）

- **角色分流**：以三類登入者（牧者／小組長／一般）驗證導覽是否正確顯示「會友資訊」及資料範圍。
- **權限把關**：以「小組長」身分嘗試查詢不在自己名單的 `contactId`，應被拒絕（IDOR 防護）。
- **子網格**：對有/無聚會紀錄與裝備紀錄的連絡人，驗證欄位映射與空資料處理。
- **效能**：全教會模式於大量連絡人下確認伺服器端分頁/搜尋運作正常。
- **降級**：CRM 未啟用 Connection 時，關係目標區塊安全顯示空白。
- 既有 `ChurchReport.Tests` 專案可放置 controller 層的範圍/權限單元測試。
