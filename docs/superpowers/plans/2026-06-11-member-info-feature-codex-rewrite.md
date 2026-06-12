# 會友資訊功能 - Codex 修正版實踐計畫

- 日期：2026-06-11
- 狀態：重新規劃，尚未實作
- 依據：
  - `docs/superpowers/specs/2026-06-11-member-info-feature-design.md`
  - `docs/superpowers/plans/2026-06-11-member-info-feature.md` 的最新 Claude 修正版
  - 本次對現有 MVC / DevExtreme / CRM 存取程式碼的檢視

## 1. 目標

在前台左側導覽新增「會友資訊」入口。符合條件的使用者可以看到連絡人網格，欄位為「照片、姓名、手機、小組」。點擊「姓名」後開啟唯讀會友細節彈窗，彈窗包含較大的大頭照、手機、地址、信仰狀態、關係目標，並在左側提供「聚會紀錄」「裝備紀錄」兩個子導覽。

核心資料範圍：

- 登入者 `contact.new_church_jobtitle` 包含「牧師傳道」或「牧養主任」：可看全教會現行連絡人。
- 其他帶領牧養小組者，也就是既有 `LoginType == "小組長"`：只能看自己各類名單底下的連絡人。
- 其他使用者：左側不顯示「會友資訊」，且直接呼叫 `/MemberInfo/*` 端點也不得取得資料。

核心安全條件：

- 所有以 `contactId` 查詢的端點都必須在伺服器端重新檢查權限與資料範圍。
- 彈窗內「聚會紀錄」與「裝備紀錄」只顯示被點擊的那一位連絡人資料，不得因為登入者是全教會權限就回傳全教會紀錄。
- 照片也要受同一套範圍保護，不直接暴露任意 `contactId` 圖片讀取。

## 2. 對 Claude 最新修正的評估

Claude 這次修正有幾個方向是正確的，這份新計畫會採納：

- 採納一：不重用 `_GeneralGroupGrids.cshtml` 裡的 `#memberDetailPopup` 與 `openMemberDetailPopup`，新功能使用 `#memberInfoDetailPopup` 與 `openMemberInfoDetailPopup`，避免和既有小組牧養的可編輯點名/探訪/代禱彈窗互相覆蓋。
- 採納二：DevExtreme 欄位模板使用 `.CellTemplate(new JS("memberInfoAvatarCellTemplate"))` 與 `.CellTemplate(new JS("memberInfoNameCellTemplate"))`，避免使用不適合目前 MVC helper 的 inline `<%- %>` 模板。
- 採納三：`SetupMemberInfoViewBag()` 只快取「有權限」的正向結果，不把「目前還取不到登入 contact」誤快取成永久無權限。
- 採納四：讀取牧養名單前先補 `EnsureShepherdListsLoaded()`，處理 `m_MultiGroupList` 還沒載入或被 Session 邊界狀況清空的情形。

這份新計畫在 Claude 版本上再修正幾個仍不夠完整的點：

- 修正一：`/Personal/GetContactImage` 與 `/Personal/GetContactImagesBatch` 目前只檢查 GUID 格式，不檢查呼叫者是否可看該 contact。會友資訊頁不直接使用這兩個端點，而是新增 `/MemberInfo/GetContactImage` 與 `/MemberInfo/GetContactImagesBatch` 受保護代理端點。
- 修正二：`MemberInfoScopeGuard` 不能讓「全教會」只要任意非空 GUID 就通過；仍要確認該 contact 是「現行連絡人」。
- 修正三：全教會清單不可先把全部 contact 載入記憶體再丟給 `DataSourceLoader.Load()`。要用 CRM 分頁、搜尋、排序查詢，只取當頁資料。
- 修正四：`_Layout.cshtml` 的導覽分支很複雜，不能在多個「組員資訊」附近各插一次。建議在 `ViewBag.DisplayNavigation != "不顯示牧養回報項目"` 的一般牧養導覽結尾、奉獻項目前，插入一次即可。
- 修正五：測試計畫要符合現況。`ChurchReport.Tests` 目前沒有 `.csproj`，`ToolUtility.Tests` 只參考 `ToolUtility` 且是 `net8.0`。若要測 Web 專案的純邏輯，應新增獨立 `ChurchReport.MemberInfo.Tests`。

## 3. 已核對的現有程式入口

- `ChurchReport/Controllers/BaseChurchController.cs`
  - `SetupBasicViewBag()` 目前設定 `LoginType`、`LoginFullName`、`FeeType`、`HappyType`，最後呼叫 `SetupFeeDataListCount()`。
  - 新的 `SetupMemberInfoViewBag()` 應在 `SetupBasicViewBag()` 末端呼叫。
- `ChurchReport/Views/Shared/_Layout.cshtml`
  - 左側導覽主要分成 `ViewBag.DisplayNavigation != "不顯示牧養回報項目"` 與簡化奉獻/行事曆分支。
  - 「會友資訊」應只放在一般牧養導覽分支中，避免奉獻或單純行事曆頁出現不一致入口。
- `ChurchReport/Controllers/PersonalController.cs`
  - `LoadMaintainPersonInfomation` 已有牧養名單成員讀取邏輯。
  - 多小組模式使用 `m_MultiGroupList.m_WeeklyReportRecordListData`，逐名單呼叫 `RetrieveMemberListCollectionByListId(listGuid)`，再讀 contact 欄位。
  - 可借用資料取得方式，但不要重用既有「組員資訊」頁面或可編輯流程。
- `ChurchReport/Views/Home/_GeneralGroupGrids.cshtml`
  - 小組牧養網格已有照片欄位、批次圖片預載、點擊開可編輯彈窗。
  - 新功能只沿用命名慣例與 UI 做法，不共用既有可編輯彈窗。
- `ChurchReport/Controllers/PersonalController.ImageUpload.cs`
  - `/Personal/GetContactImage` 與 `/Personal/GetContactImagesBatch` 有快取與縮圖能力，但沒有 per-contact scope authorization。
  - 會友資訊應新增受保護代理端點，內部可複用相同圖片讀取/縮圖想法。
- `ChurchReport/Controllers/EquipmentController.cs`
  - `LoadEquipmentStorLessons` 以 `RetrieveStorLessonsByFetchXml(member.FullName, member.ContactId)` 查學員上課紀錄。
  - 新端點要改為直接由彈窗 contactId 查，不從目前點名 member row 反查。
- `ToolUtility/QueryOperations/FetchXmlQueryService.cs`
  - `RetrieveStorLessonsByFetchXml(contactName, contactId)` 已用 `new_contact_new_stor_lessons` 和 `statecode=0` 查該 contact 的上課紀錄。
- `ToolUtility/QueryOperations/PresentRecordQueryService.cs`
  - 出席/代禱紀錄相關欄位包含 `new_contact_new_present_record`、`new_sunday_present_this_week`、`new_group_present_this_week`、`new_explanation`。
- `ToolUtility/ListOperations/ListService.cs`
  - `RetrieveMemberListCollectionByListId` 查 `listmember`，欄位含 `listmemberid`、`entityid`、`listid`。
  - `RetrieveListByContact` 可用 contact fullname 查所屬小組名單，但 full-church grid 不宜逐列大量呼叫；應只對當頁 contact 批次或小量查詢。
- `ChurchReport/Services/OptionSetMetadataService.cs`
  - 可用 `GetOptionSetText(entity, attribute, value)` 把 `customertypecode`、`new_spiriitual_identity` 轉成顯示文字。
  - 解析「結案」值時要用安全 wrapper，因 `GetOptionSetValue(..., defaultValue: null)` 找不到會丟例外。

## 4. 建議新增與修改檔案

新增：

- `ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoScopeService.cs`
- `ChurchReport/ViewModels/MemberInfoListRowViewModel.cs`
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `ChurchReport/ViewModels/MemberInfoRecordRows.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`
- 選擇性新增 `ChurchReport/wwwroot/css/member-info.css`
- 若要自動化測試：`ChurchReport.MemberInfo.Tests/`

修改：

- `ChurchReport/Controllers/BaseChurchController.cs`
- `ChurchReport/Views/Shared/_Layout.cshtml`
- 若新增測試專案：`ChurchReport.sln`

## 5. 權限與範圍設計

### 5.1 存取等級

新增 `MemberInfoAccess` 常數：

- `Church = "全教會"`
- `ShepherdList = "牧養名單"`

新增 `MemberInfoAccessResolver.Resolve(churchJobTitle, loginType)`：

- `churchJobTitle` 包含「牧師傳道」或「牧養主任」時，回傳 `全教會`。
- 否則 `loginType == "小組長"` 時，回傳 `牧養名單`。
- 否則回傳 `null`。
- 若同時符合全教會與小組長，以全教會優先。

### 5.2 ViewBag 設定

在 `BaseChurchController.SetupBasicViewBag()` 末端加入 `SetupMemberInfoViewBag()`。

`SetupMemberInfoViewBag()` 的規則：

- 先讀 Session `_MemberInfoAccess`，但只接受非空值。
- 若登入者 contact 尚未載入，嘗試呼叫既有 `PersonalInfomationModel.SetPersonalInfomationViewModel()`。
- 若仍取不到 login contact，設定 `ViewBag.MemberInfoAccess = null`，且不寫 Session 快取。
- 若解析出 `全教會` 或 `牧養名單`，寫入 Session 與 ViewBag。
- 若解析結果無權限，不寫 Session，避免資料尚未就緒時把使用者鎖成無權限。

### 5.3 Scope Service

Claude 版本的純 `MemberInfoScopeGuard` 可以保留給單元測試，但 controller 需要更完整的 `MemberInfoScopeService` 或等效私有 helper。建議實作一個服務類別，包以下能力：

- `GetAccess()`：從 ViewBag、Session 或重新解析登入 contact 得到存取等級。
- `GetAllowedShepherdContactIds()`：對 `牧養名單` 使用者，從 `m_MultiGroupList` 與 `RetrieveMemberListCollectionByListId` 建立 HashSet 白名單。
- `EnsureShepherdListsLoaded()`：若 `m_MultiGroupList` 為空且有帳密/日期，沿用 `ListManager.SetupListManager(...)` 重建。
- `IsCurrentContact(Guid contactId)`：查 `contact`，確認 `statecode=0`，且若能解析 `customertypecode` 的「結案」值，則排除結案。
- `CanViewContact(Guid contactId)`：
  - 無 access：false。
  - `全教會`：必須通過 `IsCurrentContact(contactId)`。
  - `牧養名單`：必須在自己的白名單內，並建議也通過 `IsCurrentContact(contactId)`。

所有 `/MemberInfo/*` 端點都使用 `CanViewContact`，不要相信前端傳來的 `contactId`。

## 6. 導覽設計

在 `_Layout.cshtml` 的一般牧養導覽分支中新增一次：

```cshtml
@if (ViewBag.MemberInfoAccess == "全教會" || ViewBag.MemberInfoAccess == "牧養名單")
{
    <li><a href="/MemberInfo/Index"><i class="fas fa-id-card"></i>會友資訊</a></li>
}
```

放置位置：

- 建議放在一般分支內所有 `LoginType` / `FeeType` / `HappyType` 條件結束後、固定的「奉獻」連結之前。
- 不要在每個「組員資訊」附近重複插入，避免不同導覽分支重複出現。
- 不放在 `ViewBag.DisplayNavigation == "不顯示牧養回報項目"` 的簡化奉獻/行事曆分支，除非後續產品明確要求那些頁面也要顯示。

## 7. Controller 設計

新增 `MemberInfoController : BaseChurchController`。

建議端點：

- `GET /MemberInfo/Index`
  - 呼叫 `SetupBasicViewBag()`、`SetMultiGroupLayoutParameter()`。
  - 若無權限，回 403 或導到既有錯誤頁。
  - 回傳 `Views/MemberInfo/MemberInfoGrid.cshtml`。
- `GET /MemberInfo/LoadMemberInfoList`
  - 依 access 分流：
    - `全教會`：CRM server-side paging/search/sort。
    - `牧養名單`：只載入自己的名單成員，回給 DevExtreme。
  - 無權限回空資料。
- `GET /MemberInfo/Detail?contactId=...`
  - 先 `CanViewContact(contactId)`。
  - 通過後讀 contact 詳情，回 `_MemberDetailPopup.cshtml`。
  - 不通過回 403。
- `GET /MemberInfo/LoadContactPresentRecords?contactId=...`
  - 先 `CanViewContact(contactId)`。
  - 通過後只查該 contact 的 `new_present_record`。
  - 不通過回空資料或 403；建議資料 grid 回空資料，Detail 回 403。
- `GET /MemberInfo/LoadContactStorLessons?contactId=...`
  - 同上，只查該 contact 的 `new_stor_lessons`。
- `GET /MemberInfo/GetContactImage?contactId=...&size=...`
  - 先 `CanViewContact(contactId)`。
  - 通過後回該 contact 圖片或預設圖片。
  - 不通過回預設圖片或 403；建議圖片端點回預設圖片，避免 UI 破圖。
- `POST /MemberInfo/GetContactImagesBatch`
  - 對輸入 contactIds 逐一套用 `CanViewContact`，只查允許的 id。
  - 回傳 `{ success, images }`，未授權 id 不出現在 images 中。

## 8. 資料載入設計

### 8.1 ViewModel

`MemberInfoListRowViewModel`：

- `string ContactId`
- `string FullName`
- `string Phone`
- `string SmallGroupName`

`MemberInfoDetailViewModel`：

- `string ContactId`
- `string FullName`
- `string Phone`
- `string Address`
- `string MembershipStatus`
- `string SpiritualIdentity`
- `IReadOnlyList<RelationGoalItem> RelationGoals`

`RelationGoalItem`：

- `string Role`
- `string TargetName`

`ContactPresentRecordRow`：

- `string PresentRecordId`
- `string FullName`
- `bool Sunday`
- `bool SmallGroup`
- `string PrayItem`

裝備紀錄可以直接使用既有 `EquipmentStorLessons`，或新增更窄的 `MemberInfoStorLessonRow`。若直接使用既有 model，欄位對應為：

- `DiscipleLessonsName` -> 課程名稱
- `DiscipleLessonsDateTime` -> 日期
- `StageName` -> 階段名稱
- `CurrentComplete` -> 是否結業

### 8.2 牧養名單模式

資料來源沿用 `PersonalController.LoadMaintainPersonInfomation` 的核心邏輯，但抽成新的 private helper，不直接呼叫該 action：

1. `EnsureCorrectUserData()`。
2. `EnsureShepherdListsLoaded()`。
3. 逐 `m_MultiGroupList.m_WeeklyReportRecordListData`：
   - 解析 `groupRecord.ListEntityId`。
   - 呼叫 `ToolUtility.RetrieveMemberListCollectionByListId(listGuid)`。
   - 對每筆 `listmember.entityid` 讀 contact 欄位：`contactid, fullname, mobilephone, address2_line1, customertypecode, new_spiriitual_identity, statecode`。
4. `SmallGroupName = groupRecord.Name`。
5. 同一 contact 若出現在多個名單：
   - list grid 可合併成一列，`SmallGroupName` 用 `、` 串接。
   - 白名單只需保留 contactId HashSet。
6. 套用 `DataSourceLoader.Load(rows, loadOptions)`。

注意：

- 牧養名單通常資料量較小，可以先載入到記憶體再交給 DevExtreme。
- 仍要過濾非現行 contact，至少排除 `statecode != 0`；若能解析結案 OptionSet，也排除結案。

### 8.3 全教會模式

全教會模式不能先載入所有 contact。

建議做法：

1. 從 `DataSourceLoadOptions` 取得：
   - `Take`，預設 50，上限 200。
   - `Skip`，預設 0。
   - `SearchValue` 或前端明確傳入的搜尋字串。
   - Sort，第一版只支援 `FullName`、`Phone` 對應到 CRM 欄位；其他排序忽略或落回 `fullname asc`。
2. 建立 `QueryExpression("contact")`：
   - `ColumnSet("contactid", "fullname", "mobilephone", "customertypecode", "statecode")`
   - `statecode = 0`
   - 若能取得「結案」值：`customertypecode != 結案值`
   - 搜尋：`fullname LIKE %term%` 或 `mobilephone LIKE %term%`
   - PageInfo：`Count = take`、`PageNumber = skip / take + 1`
3. 回傳當頁資料。
4. `totalCount`：
   - 第一版可用同條件查詢的 total count helper。
   - 若使用 `QueryExpression.PageInfo.ReturnTotalRecordCount`，要記錄 Dataverse total count 可能受平台限制；若教會資料超過限制，再改 FetchXML aggregate count。
5. 小組欄位：
   - 第一版對當頁 contact 批次查 `listmember` 與 `list`，只取符合 `new_app_named=1`、`purpose="小組名單"` 的名單名稱。
   - 若 CRM 關聯太複雜或效能不佳，先顯示空白或第一筆小組名，並在驗證時確認產品可接受。

不要在全教會模式對所有 contact 跑 `RetrieveListByContact`。若要使用既有 `RetrieveListByContact`，只限對當頁 50 筆以內做小量 fallback，且要加快取。

### 8.4 Detail

`Detail(contactId)` 只讀欄位：

- `fullname`
- `mobilephone`
- `address2_line1`
- `customertypecode`
- `new_spiriitual_identity`
- `statecode`

顯示文字：

- `customertypecode` -> `MembershipStatus`
- `new_spiriitual_identity` -> `SpiritualIdentity`
- 使用 `OptionSetMetadataService.GetOptionSetText(...)`，失敗時顯示空字串或原始值，不讓彈窗失敗。

### 8.5 關係目標

使用 Dynamics 365 `connection`：

- 查詢 `connection`，條件建議同時支援：
  - `record1id = contactId`
  - 或 `record2id = contactId`
- 欄位：
  - `record1id`
  - `record2id`
  - `record1roleid`
  - `record2roleid`
  - `connectionroleid` 若環境使用此欄位也保留兼容
- 顯示「角色：對象姓名」。
- 查詢失敗或環境未啟用 connection 時回空清單，不中斷 Detail。

Claude 計畫只查 `record1id` 與 `connectionroleid`，可能漏掉 contact 在 `record2id` 的關係；本版計畫補成雙向查詢。

### 8.6 聚會紀錄

`LoadContactPresentRecords(contactId)`：

1. `CanViewContact(contactId)`。
2. 讀 contact fullname。
3. 呼叫 `ToolUtility.RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactId)`，或以 `QueryExpression("new_present_record")` 直接查：
   - `new_contact_new_present_record = contactId`
   - `statecode = 0`
4. 映射：
   - 姓名：contact fullname
   - 主日：`new_sunday_present_this_week > 0`
   - 小組：`new_group_present_this_week > 0`
   - 代禱：`new_explanation`
5. key 使用 `new_present_recordid`，不要用姓名。

### 8.7 裝備紀錄

`LoadContactStorLessons(contactId)`：

1. `CanViewContact(contactId)`。
2. 讀 contact fullname。
3. 呼叫 `ToolUtility.RetrieveStorLessonsByFetchXml(fullName, contactId)`。
4. 逐筆映射：
   - `new_new_disciple_lessons_new_stor_les` lookup display -> 課程名稱
   - 關聯 `new_disciple_lessons.new_class_start_date` -> 日期
   - 關聯 `new_disciple_lessons.new_now_stage_name` -> 階段名稱
   - `new_current_complete` -> 是否結業
5. key 使用 `new_stor_lessonsid`。

## 9. 圖片安全設計

不要讓會友資訊頁直接使用：

- `/Personal/GetContactImage?contactId=...`
- `/Personal/GetContactImagesBatch`

原因：這兩個端點目前只檢查 GUID 與圖片存在，不檢查登入者是否可以查看該 contact。

新增受保護代理：

- `/MemberInfo/GetContactImage`
- `/MemberInfo/GetContactImagesBatch`

代理端點流程：

1. 解析 contactId。
2. 套用 `CanViewContact(contactId)`。
3. 不通過時回預設圖或略過該 id。
4. 通過時讀 `contact.entityimage`，沿用同樣的 thumbnail/caching 策略。

實作選擇：

- 第一版可在 `MemberInfoController` 內複製必要的 thumbnail helper，避免大範圍重構 `PersonalController.ImageUpload.cs`。
- 後續可抽成 `ContactImageService`，讓 Personal 與 MemberInfo 共用。

## 10. 前端設計

### 10.1 網格頁

`Views/MemberInfo/MemberInfoGrid.cshtml`：

- 使用 DevExtreme DataGrid。
- DataSource 指向 `/MemberInfo/LoadMemberInfoList`。
- Key：`ContactId`。
- 欄位：
  - 照片：`memberInfoAvatarCellTemplate`，圖片 src 使用 `/MemberInfo/GetContactImage?contactId=...&size=48`。
  - 姓名：`memberInfoNameCellTemplate`，點擊開 `openMemberInfoDetailPopup(contactId, fullName)`。
  - 手機：`Phone`
  - 小組：`SmallGroupName`
- SearchPanel 開啟，PageSize 預設 50。
- full church 開 RemoteOperations；shepherd list 也可以相容同一 grid。

### 10.2 彈窗

同頁加入 DevExtreme Popup：

- id：`memberInfoDetailPopup`
- title：`{FullName} - 會友細節`
- content 透過 AJAX 載入 `/MemberInfo/Detail?contactId=...`
- 不使用既有 `#memberDetailPopup`。

`_MemberDetailPopup.cshtml`：

- 左側：
  - 大頭照 `/MemberInfo/GetContactImage?contactId=...&size=0`
  - 子導覽按鈕「聚會紀錄」「裝備紀錄」
- 右側：
  - 手機、地址、信仰狀態、關係目標
  - 子網格容器：
    - `#member-subgrid-present`
    - `#member-subgrid-equip`

前端函式命名：

- `memberInfoAvatarCellTemplate`
- `memberInfoNameCellTemplate`
- `openMemberInfoDetailPopup`
- `memberInfoDetailSwitch`
- `initMemberInfoSubGrid`

不要使用 `memberDetailSwitch` 這種可能與既有彈窗混淆的泛名。

### 10.3 UI 原則

- 彈窗唯讀，不提供儲存、編輯、刪除。
- 子導覽使用明確按鈕狀態，不用頁面說明文字。
- 手機尺寸下彈窗寬度應改為接近全螢幕，左側導覽可改為水平或置頂。
- 圖片失敗要顯示預設頭像，不讓 grid 出現破圖。

## 11. 測試與驗證

### 11.1 自動化測試

若要加入測試，新增 `ChurchReport.MemberInfo.Tests`：

- TargetFramework 使用 `net10.0`，與 `ChurchReport.csproj` 一致。
- 測 `MemberInfoAccessResolver`：
  - 職稱包含「牧師傳道」 -> 全教會。
  - 職稱包含「牧養主任」 -> 全教會。
  - 同時是牧者與小組長 -> 全教會優先。
  - 小組長但非牧者 -> 牧養名單。
  - 一般使用者 -> null。
- 測純白名單判定：
  - 無 contactId -> false。
  - 無 access -> false。
  - 牧養名單 id 在 HashSet -> true。
  - 牧養名單 id 不在 HashSet -> false。

注意：

- `CanViewContact` 涉及 CRM，不做純單元測試，改做整合或手動驗證。
- 若 Web 專案引用造成測試專案建置困難，將純邏輯類保持無 ASP.NET 相依，並調整測試只引用可編譯範圍。

### 11.2 建置驗證

每個主要階段後執行：

```powershell
dotnet build ChurchReport/ChurchReport.csproj
```

若新增測試專案：

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

### 11.3 手動驗證

用三種實際帳號驗證：

- 牧師傳道或牧養主任：
  - 左側顯示「會友資訊」。
  - 進入後可看全教會現行連絡人。
  - 搜尋與分頁正常。
- 小組長：
  - 左側顯示「會友資訊」。
  - 只看到自己各類名單底下的連絡人。
  - 不在自己名單的人不能透過直接 URL 看 Detail、紀錄、圖片。
- 一般組員：
  - 左側不顯示「會友資訊」。
  - 直接呼叫 `/MemberInfo/Index` 或資料端點不得取得資料。

資料正確性：

- 點 A 連絡人後，聚會紀錄只出現 A 的資料。
- 點 B 連絡人後，聚會紀錄切換為 B 的資料，不沿用 A 的資料。
- 裝備紀錄同樣只對應彈窗 contactId。
- 有照片者顯示照片，無照片者顯示預設圖。
- 關係目標有 connection 者顯示，無 connection 者顯示空狀態。

安全驗證：

- 小組長直接呼叫 `/MemberInfo/Detail?contactId=<非自己名單GUID>` -> 403。
- 小組長直接呼叫 `/MemberInfo/LoadContactPresentRecords?contactId=<非自己名單GUID>` -> 空資料或 403。
- 小組長直接呼叫 `/MemberInfo/LoadContactStorLessons?contactId=<非自己名單GUID>` -> 空資料或 403。
- 小組長直接呼叫 `/MemberInfo/GetContactImage?contactId=<非自己名單GUID>` -> 預設圖或 403，不回真照片。

## 12. 實作順序

### Phase 1：純邏輯與導覽

1. 新增 `MemberInfoAccess`、`MemberInfoAccessResolver`。
2. 在 `BaseChurchController` 加 `SetupMemberInfoViewBag()`，並由 `SetupBasicViewBag()` 呼叫。
3. 修改 `_Layout.cshtml`，一般牧養導覽只插入一次「會友資訊」。
4. 建置驗證。

### Phase 2：Controller 骨架與 scope

1. 新增 `MemberInfoController`。
2. 實作 `GetAccess()`、`EnsureShepherdListsLoaded()`、`GetAllowedShepherdContactIds()`、`IsCurrentContact()`、`CanViewContact()`。
3. 實作 `Index()`，無權限時擋掉。
4. 建置驗證。

### Phase 3：清單網格

1. 新增 `MemberInfoListRowViewModel`。
2. 實作 `LoadMemberInfoList` 的牧養名單模式。
3. 實作 `LoadMemberInfoList` 的全教會分頁模式。
4. 新增 `MemberInfoGrid.cshtml`，完成照片/姓名/手機/小組欄位。
5. 圖片欄先接 `/MemberInfo/GetContactImage`，不是 `/Personal/GetContactImage`。
6. 建置與手動驗證。

### Phase 4：受保護圖片端點

1. 新增 `/MemberInfo/GetContactImage`。
2. 新增 `/MemberInfo/GetContactImagesBatch`。
3. 確認未授權 contactId 不回真照片。
4. 視效能需要再把 grid 改為 batch preload。

### Phase 5：唯讀細節彈窗

1. 新增 `MemberInfoDetailViewModel` 與 `_MemberDetailPopup.cshtml`。
2. 實作 `Detail(contactId)`。
3. 實作 `openMemberInfoDetailPopup`，AJAX 載入 partial。
4. 確認與既有 `_GeneralGroupGrids.cshtml` 的 `#memberDetailPopup` 無衝突。

### Phase 6：子網格

1. 實作 `LoadContactPresentRecords(contactId)`。
2. 在 partial 中初始化「聚會紀錄」grid。
3. 實作 `LoadContactStorLessons(contactId)`。
4. 在 partial 中初始化「裝備紀錄」grid。
5. 確認切換 A/B 連絡人時子網格資料重新綁定該 contactId。

### Phase 7：關係目標

1. 實作 connection 雙向查詢。
2. 未啟用 connection 或查詢失敗時安全回空清單。
3. 用至少一筆有 connection 的 contact 驗證顯示。

### Phase 8：測試與收斂

1. 視需要新增 `ChurchReport.MemberInfo.Tests`。
2. 補角色解析與白名單純邏輯測試。
3. 執行 build/test。
4. 做三角色手動驗證與 IDOR 驗證。

## 13. 驗收條件

- 牧師傳道/牧養主任登入後，看得到左側「會友資訊」，且清單是全教會現行連絡人。
- 小組長登入後，看得到左側「會友資訊」，且清單只包含自己各類名單底下的現行連絡人。
- 一般使用者看不到入口，也不能直接打 API 取得資料。
- 網格欄位為照片、姓名、手機、小組。
- 點姓名後彈窗顯示該 contact 的大頭照、手機、地址、信仰狀態、關係目標。
- 聚會紀錄 grid 欄位為姓名、主日、小組、代禱，且只顯示該 contact 的資料。
- 裝備紀錄 grid 欄位為課程名稱、日期、階段名稱、是否結業，且只顯示該 contact 的資料。
- 任意非授權 contactId 不能透過 Detail、子紀錄、圖片端點外洩資料。
- `dotnet build ChurchReport/ChurchReport.csproj` 通過。

## 14. 仍需實作時確認的問題

- 「現行連絡人」的 `customertypecode = 結案` 是否一定存在於 OptionSet。若無法解析，第一版只套用 `statecode=0`，並記錄 warning。
- 全教會清單的「小組」欄位要顯示第一個小組、全部小組串接，還是主要小組。若 CRM 沒有主要小組欄位，建議第一版顯示當頁查到的所有小組名稱串接。
- Connection Role 在此 CRM 環境實際使用 `record1roleid` / `record2roleid` 還是 `connectionroleid`。實作時要用真實資料驗證。
- 圖片代理端點第一版是複製 helper 還是先抽 `ContactImageService`。若只為本功能快速落地，先複製最小必要程式碼較穩；後續再抽共用服務。
