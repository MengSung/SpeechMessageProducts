# 2026-06-18 Trace-No-Perf vs Trace-Perf 效能瓶頸及改善計畫

## 一、分析檔案

本報告比對兩份測試 log：

- 無 `[Perf]`：`ChurchReport\Logs\Trace-No-Perf.log`
  - 時間範圍：`2026-06-18 09:42:41` 到 `2026-06-18 10:05:58`
  - 大小：約 `2.12 MB`
  - 特性：只有慢請求、局部 timing、圖片批次 timing、捐獻 timing。
- 有 `[Perf]`：`ChurchReport\Logs\Trace-Perf.log`
  - 時間範圍：`2026-06-18 10:11:52` 到 `2026-06-18 10:16:29`
  - 大小：約 `1.58 MB`
  - 特性：包含 `[Perf]`、`[Perf-Gap]`、`[Perf-Slow]`、`[Perf-N+1]`、`[Perf-Startup]`。

注意：兩份 log 不是完全相同長度的測試樣本。`Trace-No-Perf.log` 時間較長、登入與頁面操作樣本較多；`Trace-Perf.log` 時間較短，但有完整剖析訊號。因此，本報告不把兩份 log 的總量直接視為 profiler 開銷比較，而是用它們交叉驗證瓶頸：無 Perf 看使用者實際慢請求分布，有 Perf 看每個慢點內部是 CRM、Action 還是 Gap。

## 二、核心結論

本次開啟 `[Perf]` 後，瓶頸排序更清楚：

1. 最大「總量」瓶頸是 `/Equipment/LoadEquipmentStorLessons`。有 Perf 測試中它被呼叫 `22` 次，合計 `26.3s`，是測試流程總耗時最高的單一 endpoint 群組。
2. 最大「單次頁面」瓶頸仍包含登入與課程管理：
   - `/Home/ProcessLogin`：`9.98s`
   - `/FeeManagement/LessonList`：`9.61s`
   - `/FeeManagement/Present/{discipleLessonsId}`：`6.19s`
   - `/FeeManagement/Api/FeeData`：最高 `4.98s`，3 次合計 `13.06s`
3. 最明確的 N+1 是 `/Personal/LoadMaintainPersonInfomation`：
   - `[Perf-N+1] crm.n=65`
   - `contact.Retrieve ×22`，合計 `867ms`
   - `RetrieveAttribute.Execute ×43`，合計 `3089ms`
   - 單次總耗時 `4469ms`
4. `[Perf]` 顯示動態 request 的 `gap` 過高：
   - 動態 `[Perf]` 筆數：`135`
   - 動態總時間：`121585ms`
   - 已攔截 CRM 時間：`11068ms`，約 `9.1%`
   - Gap：`94703ms`，約 `77.9%`
   - 這代表大量慢點走在 profiler 目前沒攔到的路徑，尤其是舊式 `ToolUtilityClass`、`m_ToolUtilityClass`、`m_Crm2011OrganizationService`、部分靜態工具呼叫，或同步 CPU / I/O。
5. 靜態檔仍嚴重污染 log：
   - `Trace-Perf.log` 裡有 `345` 筆靜態檔樣式的 `[Perf]`。
   - IdentityAudit 也記錄 `690` 筆 static-like path。
   - 這不一定是主要使用者延遲，但會影響分析準確度與 log 體積。

## 安全前提：Session Leakage 零容忍

本報告所有改善建議的優先順序是「安全 > 正確性 > 體感速度」。先前已發生過 session leakage / session bleeding 類型的嚴重安全問題，因此任何 cache、lazy load、batch API、background warmup，都必須先證明資料隔離正確，否則不得實作或上線。

硬性規則：

1. 不得以 `listId`、`lessonId`、`discipleLessonsId`、`StorLessonsId`、`presentRecordId`、`contactId`、`account`、`lineId` 等單一或少數欄位作為使用者可見資料 cache key。
2. 使用者資料 cache key 必須至少包含 `sessionId + userId/contactId + accountOrLineHash + loginType + activeListId/listScope + selectDate + permissionScope + version`。
3. batch/lazy API 不信任前端傳入 id；後端必須用目前 session 重新計算可存取範圍，再與 request id 取交集。
4. 所有動態 MVC/API response 必須維持 `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`、`Pragma: no-cache` 與 `Vary: Cookie`。
5. 背景預熱或 queue 只能使用建立當下的 immutable user scope，執行前必須重新驗證 scope 有效，禁止讀取可能被其他使用者覆寫的全域 mutable session state。
6. A/B 兩個使用者連續登入、LINE ID 與帳密交錯登入、不同瀏覽器或同瀏覽器切換帳號時，若出現任何跨使用者資料可見，該優化一律視為失敗。

## 三、慢請求 A/B 比對

以下是兩份 log 都能看到的主要慢請求。因樣本數不同，此表用來看「是否重現慢點」，不是直接判斷 profiler 讓系統變快或變慢。

| Endpoint | No-Perf 樣本/平均/最高 | Perf 樣本/平均/最高 | 判讀 |
|---|---:|---:|---|
| `/Home/ProcessLogin` | 4 筆 / `14364ms` / `17294ms` | 1 筆 / `9980ms` / `9980ms` | 登入仍是高優先；Perf 顯示 `gap=8804ms` |
| `/FeeManagement/LessonList` | 1 筆 / `6877ms` / `6877ms` | 1 筆 / `9607ms` / `9607ms` | 課程清單重載很慢；Perf 顯示幾乎全是 gap |
| `/FeeManagement/Api/FeeData` | 1 筆 / `1069ms` / `1069ms` | 3 筆 / `4366ms` / `5010ms` | Perf 測試中重現為更大瓶頸 |
| `/FeeManagement/Present/{guid}` | 1 筆 / `3339ms` / `3339ms` | 1 筆 / `6195ms` / `6195ms` | 點名頁重載課程資料成本高 |
| `/FeeManagement/Fee/{guid}` | 1 筆 / `1597ms` / `1597ms` | 1 筆 / `4467ms` / `4467ms` | 繳費頁重載課程資料成本高 |
| `/Equipment/LoadEquipmentStorLessons` | 4 筆 / `1752ms` / `1910ms` | 慢請求 9 筆 / `2101ms` / `3299ms`；Perf 總筆數 22 | 變成總量最大瓶頸，需批次化 |
| `/Personal/LoadMaintainPersonInfomation` | 1 筆 / `4285ms` / `4285ms` | 1 筆 / `4473ms` / `4473ms` | 兩次穩定重現；Perf 明確指出 N+1 |
| `/NewPerson/SaveNewPerson` | 1 筆 / `7815ms` / `7815ms` | 1 筆 / `4382ms` / `4382ms` | 仍慢；需補分段 timing |
| `/Personal/SavePersonalInfomation` | 1 筆 / `4982ms` / `4982ms` | 1 筆 / `2589ms` / `2589ms` | 仍慢；Perf 顯示主要是 gap |
| `/Dedication/SaveQPayDedication` | 1 筆 / `2549ms` / `2549ms` | 1 筆 / `2679ms` / `2679ms` | 穩定約 2.6s，通知/CRM create chain 可優化 |
| `/MemberInfo/UpdateContactInfo` | 2 筆 / `1156ms` / `1182ms` | 1 筆慢請求 / `1003ms` / `1003ms`；Perf 2 筆合計 CRM `1776ms` | contact.Update 是主要成本 |

## 四、Trace-Perf 全域剖析摘要

### 4.1 Startup

| Phase | 時間 |
|---|---:|
| `ConfigureServices` | `44ms` |
| `Configure` | `55ms` |

Startup 本身不是瓶頸。

### 4.2 動態與靜態 `[Perf]`

| 類型 | 筆數 | total 合計 | action 合計 | CRM ms 合計 | CRM n 合計 | gap 合計 |
|---|---:|---:|---:|---:|---:|---:|
| Dynamic | 135 | `121585ms` | `105771ms` | `11068ms` | 141 | `94703ms` |
| Static-like | 345 | `8686ms` | `0ms` | `0ms` | 0 | `0ms` |

重點是動態 request 的 `gap` 佔比高達 `77.9%`。這代表目前 profiler 能證明「慢」，但仍有很多慢的 CRM / 舊工具 / 同步工作沒有被細分成可讀的 operation 名稱。改善計畫除了修業務瓶頸，也要補強 profiler 包裝範圍，否則後續優化會繼續看到大量 gap。

### 4.3 動態 endpoint 總量貢獻

| Endpoint | Hits | TotalSum | MaxTotal | CrmMsSum | GapSum | 判讀 |
|---|---:|---:|---:|---:|---:|---|
| `/Equipment/LoadEquipmentStorLessons` | 22 | `26308ms` | `3298ms` | `0ms` | `19383ms` | per-row AJAX + 未攔截 CRM，總量最大 |
| `/FeeManagement/Api/FeeData` | 3 | `13063ms` | `4982ms` | `0ms` | `12950ms` | 課程繳費資料重複載入 |
| `/Home/ProcessLogin` | 1 | `9976ms` | `9976ms` | `1074ms` | `8804ms` | 登入同步預載 |
| `/FeeManagement/LessonList` | 1 | `9605ms` | `9605ms` | `0ms` | `9539ms` | 課程清單同步重查 |
| `/FeeManagement/Present/{discipleLessonsId}` | 1 | `6192ms` | `6192ms` | `0ms` | `5288ms` | 點名頁同步重查 |
| `/SmallGroup/IntegrateView/{LoginParameter}` | 3 | `5044ms` | `2173ms` | `206ms` | `4629ms` | 整合資料重載 |
| `/Personal/LoadMaintainPersonInfomation` | 1 | `4469ms` | `4469ms` | `3991ms` | `458ms` | 明確 N+1 |
| `/FeeManagement/Fee/{discipleLessonsId}` | 1 | `4464ms` | `4464ms` | `0ms` | `4367ms` | 繳費頁同步重查 |
| `/NewPerson/SaveNewPerson` | 1 | `4379ms` | `4379ms` | `141ms` | `4215ms` | 儲存流程未細分 |
| `/SmallGroup/MultiGroupView/{LoginParameter}` | 3 | `4055ms` | `1834ms` | `303ms` | `3160ms` | 多小組資料重載 |
| `/FeeManagement/Api/SaveBatch` | 2 | `3169ms` | `3001ms` | `0ms` | `3014ms` | 批次儲存其實逐欄位/逐筆更新 |
| `/Equipment/LoadEquipmentContact` | 2 | `2888ms` | `1470ms` | `0ms` | `2863ms` | 切換小組時重載整合資料 |
| `/Dedication/SaveQPayDedication` | 1 | `2676ms` | `2676ms` | `99ms` | `2548ms` | QPay/通知/未攔截 CRM |
| `/Personal/SavePersonalInfomation` | 1 | `2586ms` | `2586ms` | `168ms` | `2403ms` | 個人資料儲存未細分 |
| `/MemberInfo/UpdateContactInfo` | 2 | `1850ms` | `1000ms` | `1776ms` | `45ms` | CRM update 本身慢 |

## 五、瓶頸一：Equipment 課程明細總量最大

### 觀測

`/Equipment/LoadEquipmentStorLessons` 在 `Trace-Perf.log` 中：

- `[Perf]` hits：`22`
- 平均 total：`1195.8ms`
- P50：`732ms`
- P90：`1787ms`
- Max：`3298ms`
- TotalSum：`26308ms`
- GapSum：`19383ms`
- `crm{n=0,ms=0}`，但程式碼明顯有 CRM 查詢，代表此路徑沒有被 profiler 完整攔截。

程式位置：

- `ChurchReport\Controllers\EquipmentController.cs`
  - `LoadEquipmentContact()`
  - `LoadEquipmentStorLessons()`
- `LoadEquipmentStorLessons()` 目前流程：
  - 每個展開列發一個 request。
  - 每個 request 執行 `EnsureCorrectUserData()`。
  - 用 `PresentRecordId` 找 member。
  - 呼叫 `ToolUtility.RetrieveStorLessonsByFetchXml(member.FullName, member.ContactId)`。
  - 每筆 lesson 再 `ToolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId)` 取課程階段與日期。

### 瓶頸判斷

這是典型「前端 master-detail 每列 AJAX + 後端每人查 CRM + 每課再查關聯課程」的總量瓶頸。單次看可能只有 0.3 到 3.3 秒，但測試一次展開或載入會打出 22 次，總量就達 26 秒以上。

### 改善計畫

1. 新增批次端點，例如 `/Equipment/LoadEquipmentStorLessonsBatch`。
2. 前端進入 Equipment 頁或展開第一層時，一次送出所有 visible `PresentRecordId` / `ContactId`。
3. 後端用一個 FetchXML 或 `ConditionOperator.In` 查所有 contact 的 stor lessons。
4. FetchXML 直接 link `new_disciple_lessons`，把 `new_class_start_date`、`new_now_stage_name`、lesson name 一次帶回，避免逐筆 `RetrieveEntity()`。
5. 結果依 `PresentRecordId` 或 `ContactId` 分組，前端展開時讀本地資料。
6. 同一 list/date 的裝備課程結果用短 TTL cache，更新課程或切換小組時 invalidation。
7. `EnsureCorrectUserData()` 在批次 request 只執行一次，且修正 credential cache 命中問題。

### 預估效益

- 目前：22 次合計 `26.3s`。
- 批次查詢後：同一頁目標降到 `2-5s` 冷啟，warm cache `<1s`。
- 後端 CRM 呼叫量預估減少 `70-90%`。
- 使用者展開明細感知速度預估提升 `50-80%`。

## 六、瓶頸二：FeeManagement 課程/點名/繳費資料重複載入

### 觀測

FeeManagement 相關 endpoint 在 `Trace-Perf.log` 中合計非常高：

| Endpoint | Hits | TotalSum | MaxTotal | GapSum |
|---|---:|---:|---:|---:|
| `/FeeManagement/LessonList` | 1 | `9605ms` | `9605ms` | `9539ms` |
| `/FeeManagement/Api/FeeData` | 3 | `13063ms` | `4982ms` | `12950ms` |
| `/FeeManagement/Present/{discipleLessonsId}` | 1 | `6192ms` | `6192ms` | `5288ms` |
| `/FeeManagement/Fee/{discipleLessonsId}` | 1 | `4464ms` | `4464ms` | `4367ms` |
| `/FeeManagement/Api/SaveBatch` | 2 | `3169ms` | `3001ms` | `3014ms` |
| 合計 | 8 | `36493ms` | - | `35158ms` |

程式位置：

- `ChurchReport\Controllers\FeeManagementController.cs`
  - `LessonList()` 每次呼叫 `InMemoryContext.FeeList.SetupLessonList(...)`
  - `Fee()` 每次呼叫 `SetupPresentFeeList(discipleLessonsId)`
  - `Present()` 每次呼叫 `SetupPresentFeeList(discipleLessonsId)`
  - `GetFeeData()` 有 `discipleLessonsId` 時也呼叫 `SetupPresentFeeList(discipleLessonsId)`
  - `SaveBatch()` 呼叫 `CommitPendingChanges()`
- `ChurchReport\Models\FeeList.cs`
  - `SetupLessonList()` 直接呼叫 `m_FeeDownUpLoader.GetLessonList(...)`
  - `CommitPendingChanges()` 對每筆 change、每個 field 逐一呼叫 `UpdateFeeDataList(...)`
- `ChurchReport\WebServiceConnector\FeeDownUpLoader.cs`
  - `GetLessonList()` 會 `FindLoginUser()`，並查講員/收費點名/助理三種課程關係。
  - `SetPresentFeeList()` / `ProcessDiscipleLesson()` 會查課程、查 stor lessons、處理所有上課紀錄。
  - `UpdateFeeDataList()` 每個欄位都先 `RetrieveEntity("new_stor_lessons")`，再依欄位呼叫一到多次 `UpdateEntity()`。

### 瓶頸判斷

FeeManagement 的問題不是單一 API，而是整個頁面流程反覆載入同一份課程資料：

- 進 `/LessonList` 載課程清單。
- 進 `/Fee/{id}` 或 `/Present/{id}` 又載一次課程相關學員資料。
- DevExtreme DataGrid 呼叫 `/Api/FeeData?discipleLessonsId=...` 時又再次 `SetupPresentFeeList()`。
- 儲存時雖然叫 `SaveBatch`，但內部仍是逐筆、逐欄位 CRM update。

`[Perf]` 顯示 `crm{n=0,ms=0}`，但這些方法實際上大量使用 `m_ToolUtilityClass`，代表 profiler 沒攔到舊工具類中的 CRM 呼叫。這也是 gap 高的主因。

### 改善計畫

1. `LessonList()` 加 per-user/per-session/date cache。
   - 安全 cache key：`FeeLessons:{sessionId}:{userId}:{accountOrLineHash}:{loginType}:{selectDate}:{permissionScopeHash}:v1`。
   - 不得使用明文帳號、明文密碼、LINE ID、姓名、手機。
   - TTL：`5-15` 分鐘。
   - 課程、點名、繳費資料有修改時 invalidation。
2. `SetupPresentFeeList(discipleLessonsId)` 加 per-user/per-session/per-lesson cache。
   - 安全 cache key：`FeeData:{sessionId}:{userId}:{accountOrLineHash}:{loginType}:{selectDate}:{discipleLessonsId}:{permissionScopeHash}:v1`。
   - 不得使用只有 `discipleLessonsId` 的 cache key；同一課程對不同登入者的可見範圍可能不同。
   - `Fee()` / `Present()` / `GetFeeData()` 共用同一份 cache。
   - 同一頁面內不要 view action 載一次、DataGrid API 再載一次。
3. `GetFeeData()` 若 `FeeDataList` 已是同一 `discipleLessonsId` 且未過期，直接 `DataSourceLoader.Load()`，不要重新 `SetupPresentFeeList()`。
4. `CommitPendingChanges()` 改成「每筆 StorLessonsId 合併所有欄位」後再更新。
   - 目前 3 筆 pending、4 個 field 耗時 `3001ms`。
   - 對同一 `StorLessonsId` 的多欄位合併為一個 `Entity("new_stor_lessons", id)` update。
   - PayDate / Amount / PayWay 若需要同步 Fee entity，也合併成一個 Fee update。
5. 進一步使用 CRM `ExecuteMultiple` 或等效 batch update，一次送多筆 update。
6. 將 `FeeDownUpLoader` 的 CRM service 換成 profiler 可包裝的 `IOrganizationService`，讓下次 `[Perf]` 能看到真正 CRM n/ms。
7. `GetFeeData()` 與 `SaveBatch()` 必須用目前 session 重新授權 `discipleLessonsId`、`StorLessonsId`，不得只相信前端傳入 id。
8. cache hit 後必須比對目前 session scope 與 cache scope；不一致時 fail closed，回 401/403 或重新要求登入，不得 fallback 回舊資料。

### 預估效益

- `LessonList` warm cache：`9.6s` 降到 `<1s`。
- `FeeData` 同課程重複載入：`4-5s` 降到 `<1.5s`，warm cache `<500ms`。
- `Present/Fee` 頁面：`4-6s` 降到 `1-2s`。
- `SaveBatch` 3 筆/4 欄位：`3.0s` 降到 `0.8-1.5s`。
- FeeManagement 測試段總等待可減少 `60-85%`。

## 七、瓶頸三：登入仍有同步預載與 credential cache 失效

### 觀測

`Trace-Perf.log`：

- `/Home/ProcessLogin total=9976ms`
- `action=9878ms`
- `crm{n=7,ms=1074}`
- `gap=8804ms`
- slowest：`contact.RetrieveMultiple:720ms`
- `[Perf-Slow] RetrieveAttribute.Execute 126ms`

ProcessLogin 時間戳：

- `10:11:59` 開始登入。
- `10:12:00` 進入 `SetupSystemData()`。
- `10:12:08` 完成 `SetupSystemData()`。
- `10:12:09` 返回登入結果。

`Trace-No-Perf.log`：

- `/Home/ProcessLogin` 4 筆。
- 平均 `14364ms`，最高 `17294ms`。

程式位置：

- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Login.cs`
  - `ProcessLogin()` 同步呼叫 `SetupSystemData()`。
- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Private.cs`
  - `SetupSystemData()` 同步執行：
    - `ListManager.SetupListManager(...)`
    - `EnsureCorrectUserData()`
    - `AppointmentsListManager.SetupAppointmentList()`
    - `QpayManager.SetQpayModel(...)`
    - `FeeList.SetupLessonList(...)`
  - `DetermineDisplayViewType()` 在 `IntegrateView` 時還可能呼叫 `SetupIntegrateData(...)`。
- `ChurchReport\Controllers\BaseChurchController.cs`
  - `EnsureCorrectUserData()` 有快取，但 log 中仍反覆出現「憑證不一致，重新載入 ListManager 資料」。

### 瓶頸判斷

登入的問題不是單一 CRM 呼叫。已攔截 CRM 只有 `1.074s`，但 gap 有 `8.804s`，代表同步預載、舊工具類 CRM、未攔截服務、或多個初始化流程串在登入等待路徑上。

`EnsureCorrectUserData()` 的快取沒有有效阻止重載。`Trace-Perf.log` 中 `EnsureCorrectUserData` 出現 `64` 次，且多次記錄憑證不一致。這會造成後續 AJAX 反覆重建 ListManager。

### 改善計畫

1. 登入只保留必要步驟：
   - 驗證帳密/LINE。
   - 建立 session。
   - 設定最小登入資訊：姓名、登入類型、ActiveListId、DisplayViewType。
2. 移除登入同步 `FeeList.SetupLessonList()`，課程頁第一次需要才載入。
3. `AppointmentsListManager.SetupAppointmentList()` 與 QPay model 初始化改 lazy load 或背景預熱。
4. `EnsureCorrectUserData()` 快取 key 需包含：
   - session id
   - account
   - password hash
   - LoginType
   - ActiveListId 或 select date
   並修正登入成功後 `ListManager.m_Password` 與 session `_LoginPassword` 不一致的來源。
5. `SetupSystemData()` 不要主動呼叫 `EnsureCorrectUserData()` 造成二次 `SetupListManager()`。
6. 若仍需要預熱，改成登入 response 回傳後由背景 task 或前端 lazy request 觸發。

### 登入後第一頁最小載入策略

無論是帳密登入或 LINE ID 登入，都應採用同一個設計原則：登入成功後，只取得「使用者即將打開的第一個頁面」所需的最小資料；其他頁面、其他頁籤、master-detail 明細、圖片、課程、裝備、捐獻歷史等資料，等使用者實際點擊到該功能時再向後端取得。

建議登入流程改成：

1. `Authenticate`
   - 帳密登入：驗證帳號密碼。
   - LINE ID 登入：驗證 LINE ID / LIFF 身分。
   - 兩者都只建立可信 session 與最小使用者識別資料。
2. `ResolveLandingPage`
   - 只判斷登入後第一個要進入的頁面，例如 `MultiGroupView`、`IntegrateView`、`Personal`、`QPay`、`Dedication`。
   - 回傳 `nextRoute`、`displayViewType`、`activeListId`、`loginType`、`fullName` 等最小資訊。
3. `LoadLandingPageMinimumData`
   - 只載入第一頁首屏立即需要的資料。
   - 不在登入 request 中載入課程清單、裝備課程、所有小組成員完整資料、所有圖片、捐獻紀錄、點名繳費明細。
4. `Lazy Load On Click`
   - 使用者點擊「課程」、「裝備」、「個人資料維護」、「捐獻」、「小組明細」、「圖片」時，才呼叫對應 API。
   - 每個 API 再依自己的 cache / batch 策略載入資料。
5. `Optional Background Warmup`
   - 第一頁已顯示後，可以用低優先背景 request 預熱可能會點擊的資料。
   - 背景預熱不可阻塞第一頁顯示，也必須可取消、可忽略失敗。

第一頁資料邊界建議如下：

| Landing page | 登入時允許載入 | 登入時不應載入 |
|---|---|---|
| `MultiGroupView` | 多小組清單摘要、圖表摘要、目前使用者基本資訊 | 每個小組完整成員、每個小組 IntegrateData、Personal 維護資料、Equipment 明細、FeeData |
| `IntegrateView` | 目前 active list 的首屏週報資料與必要成員摘要 | 其他小組資料、課程清單、裝備課程、歷史捐獻、圖片批次 |
| `Personal` | 目前登入者或目前頁面需要的個人基本欄位 | 全小組 `LoadMaintainPersonInfomation`、所有 contact 批次圖片、option metadata 逐筆查詢 |
| `QPay` / `Dedication` | 付款頁需要的 contact / qpay model 最小資料 | 課程清單、裝備資料、小組整合資料、捐獻歷史列表 |
| `FeeManagement` | 若第一頁就是課程頁，只載入課程清單摘要 | 特定課程的 FeeData / PresentData，等選課後再載入 |

這個設計的重點不是單純把工作延後，而是把使用者等待點切開：登入 request 只負責「可以進入系統並看到第一頁」，其他資料載入改由使用者操作觸發。這會讓使用者更早看到可互動畫面，即使總資料量不變，體感速度也會明顯改善。

### 預估效益

- 第一階段：登入 `10-17s` 降到 `5-8s`。
- 完整 lazy load：登入降到 `2-4s`。
- 後續 AJAX 因 `EnsureCorrectUserData()` 命中快取，整體再少 `0.5-2s` 的重複開銷。

## 八、瓶頸四：Personal 維護資料明確 N+1

### 觀測

`/Personal/LoadMaintainPersonInfomation`：

- total：`4469ms`
- action：`4449ms`
- CRM：`n=65`、`3991ms`
- gap：`458ms`
- `[Perf-N+1]` 明細：
  - `contact.Retrieve ×22 (Σ867ms)`
  - `RetrieveAttribute.Execute ×43 (Σ3089ms)`

log 顯示多小組模式：

- 小組數量：`2`
- 第 1 小組：`9` 人
- 第 2 小組：`13` 人
- 總人數：`22`

程式位置：

- `ChurchReport\Controllers\PersonalController.cs`
  - `LoadMaintainPersonInfomation()`
  - 多小組模式每個 group 呼叫 `RetrieveMemberListCollectionByListId(listGuid)`。
  - 每個 member 再 `toolUtility.m_Crm2011OrganizationService.Retrieve("contact", contactId, columnSet)`。
  - 每個 member 再透過 `GetMembershipStatusText()`、`GetSpiritualIdentityText()` 間接觸發大量 `RetrieveAttribute.Execute` 取得 option label。

### 瓶頸判斷

這是本次 profiler 最明確指出的 N+1。22 位成員卻產生 65 次 CRM/metadata 呼叫，主要成本是欄位選項文字查詢 `RetrieveAttribute.Execute`，其次是逐成員 `contact.Retrieve`。

### 改善計畫

1. 一次收集所有 contactId。
2. 用 `QueryExpression("contact")` + `ConditionOperator.In` 一次取回：
   - `contactid`
   - `fullname`
   - `mobilephone`
   - `address2_line1`
   - `birthdate`
   - `customertypecode`
   - `new_spiriitual_identity`
   - `new_equipment_status`
3. 建立 `Dictionary<Guid, Entity>` 後回填 member。
4. `customertypecode` 與 `new_spiriitual_identity` 的 label map 啟動時或第一次使用時載入一次，快取 24 小時。
5. 若只是顯示文字，優先使用本地 option value -> label map，不要每個成員查 metadata。
6. 移除這個方法內大量逐成員 Debug log，改成 summary log。

### 預估效益

- 目前：`4469ms`。
- 批次 contact + option label cache 後：`600-1200ms`。
- CRM 呼叫數：`65` 降到 `2-4`。
- 改善幅度：`70-85%`。

## 九、瓶頸五：小組整合與多小組頁仍有重載

### 觀測

| Endpoint | Hits | TotalSum | MaxTotal | CRM ms | GapSum |
|---|---:|---:|---:|---:|---:|
| `/SmallGroup/IntegrateView/{LoginParameter}` | 3 | `5044ms` | `2173ms` | `206ms` | `4629ms` |
| `/SmallGroup/MultiGroupView/{LoginParameter}` | 3 | `4055ms` | `1834ms` | `303ms` | `3160ms` |
| `/SmallGroup/LoadIntegrate` | 4 | `1776ms` | `633ms` | `0ms` | `1271ms` |
| `/SmallGroup/GetMultiGroupChartDataList` | 3 | `1036ms` | `599ms` | `0ms` | `828ms` |

程式位置：

- `ChurchReport\Controllers\SmallGroupController\SmallGroupController.IntegrateView.cs`
  - `ShouldLoadIntegrateData()` 在 `displayViewType == "MultiGroupView"` 時直接 `return true`。
  - 這使 MultiGroupView 每次進頁都有高機率重新 `SetupIntegrateData()`。
- `ChurchReport\Models\ListManager.cs`
  - `SetupIntegrateData()` 呼叫 `m_DownloadIntegrateData.SetupIntegrateData(...)`。

### 改善計畫

1. `ShouldLoadIntegrateData(loginParameter)` 改成檢查：
   - `weeklyReport == null`
   - `!weeklyReport.LoadFlag`
   - `ActiveListId != requestedListId`
   - select date / account 是否變更
2. MultiGroupView 不應無條件 reload。
3. 將 `SetupIntegrateData()` 拆成：
   - 小組基本資料與成員資料：可快取。
   - 出席/回報可編輯資料：短 TTL 或明確 invalidation。
4. `LoadIntegrate` / chart data 可以直接從 `m_MultiGroupChartDataList` 或快取讀，避免重算。

### 預估效益

- 同一 list 重複進入：`1.4-2.2s` 降到 `<800ms`。
- 小組頁整段切換：減少 `40-70%`。

## 十、瓶頸六：儲存類 endpoint 缺少分段，但 gap 指向舊 CRM/同步工作

### 觀測

| Endpoint | No-Perf | Perf | Perf 內部 |
|---|---:|---:|---|
| `/NewPerson/SaveNewPerson` | `7815ms` | `4379ms` | `crm=141ms`、`gap=4215ms` |
| `/Personal/SavePersonalInfomation` | `4982ms` | `2586ms` | `crm=168ms`、`gap=2403ms` |
| `/Dedication/SaveQPayDedication` | `2549ms` | `2676ms` | `crm=99ms`、`gap=2548ms` |
| `/MemberInfo/UpdateContactInfo` | 最高 `1182ms` | 2 筆合計 `1850ms` | `contact.Update` 兩次：`771ms`、`913ms` |

判讀：

- `MemberInfo/UpdateContactInfo` 已被 profiler 證明主要是 CRM update 本身。
- `NewPerson`、`Personal Save`、`Dedication QPay` 的慢大多是 gap，代表還需要補業務分段 timing 或包裝舊 service。

### 改善計畫

1. 對 `SaveNewPerson` 補以下 timing：
   - `UploadNewPersonToCrm`
   - `HandleSuccessfulNewPersonCreation`
   - `TryUploadNewPersonImageAsync`
   - 圖片 decode / resize / CRM update
2. `SaveNewPerson` 不要在新增成功 request 內同步處理大圖：
   - 新人資料 create 先回應。
   - 圖片上傳用獨立 endpoint 或背景 queue。
   - 前端上傳前先壓縮到最大邊長，例如 1024 或 1280。
3. `SavePersonalInfomation` 補分段 timing，確認是 `PersonalInfomatioManager` 還是 `UploadIntegrateData` 造成 gap。
4. `Dedication/SaveQPayDedication`：
   - 通知送出改 queue。
   - `CreateFee` 後避免立即不必要 retrieve。
   - owner assign 可嘗試 create 時一次指定，避免後續 assign。
5. `MemberInfo/UpdateContactInfo`：
   - 只送有變更欄位。
   - 避免更新後重新查整筆資料。
   - 若同一操作會更新多欄位，合併成單一 `contact.Update`。

### 預估效益

- `SaveNewPerson`：`4.4-7.8s` 降到 `2-4s`。
- `SavePersonalInfomation`：`2.6-5.0s` 降到 `1.5-3s`。
- `SaveQPayDedication`：`2.6s` 降到 `1.2-1.8s`。
- `MemberInfo/UpdateContactInfo`：視 CRM update 延遲，目標降 `20-40%`。

## 十一、圖片批次分析

### 觀測

No-Perf：

| Endpoint | Hits | AvgTotal | MaxTotal | ReqSum | CrmSum | ImgSum |
|---|---:|---:|---:|---:|---:|---:|
| Personal | 12 | `1318.2ms` | `7246ms` | 58 | `14634ms` | `1056ms` |
| MemberInfo | 6 | `238.7ms` | `503ms` | 169 | `444ms` | `325ms` |

Perf：

| Endpoint | Hits | AvgTotal | MaxTotal | ReqSum | CrmSum | ImgSum |
|---|---:|---:|---:|---:|---:|---:|
| Personal | 6 | `182.8ms` | `357ms` | 30 | `430ms` | `396ms` |
| MemberInfo | 1 | `560ms` | `560ms` | 50 | `208ms` | `208ms` |

判讀：

- No-Perf 曾出現 Personal 圖片批次異常：`7246ms`、`7206ms`，CRM 約 `7s`。
- Perf 測試未重現該異常，Personal 最高 `357ms`。
- MemberInfo 50 人批次在 Perf 中 `560ms`，仍可接受，但比前次 `283ms` 高。

### 改善計畫

1. 保留目前 batch image API，這不是優先 P0。
2. Personal 圖片查詢加超時/慢查 log，若 `crm > 1000ms` 記錄 contact 數量與 CRM query 條件。
3. 預先儲存縮圖，request 時不要對大圖動態 resize。
4. 對 `entityimage` 載入做欄位最小化；沒有照片者優先使用 LINE picture URL 或 fallback avatar。

預估效益：

- 一般情況只省 `100-300ms`。
- 可避免偶發 `7s` 圖片 CRM outlier 影響使用者。

## 十二、Profiler 自身要補強

本次 `[Perf]` 最大價值是指出大量 gap，但也暴露 profiler 尚未完整覆蓋舊式 CRM 呼叫。

### 問題

1. `FeeManagement`、`Equipment` 明顯有 CRM 操作，但 `[Perf]` 顯示 `crm{n=0,ms=0}`。
2. `Trace-Perf.log` 有 `345` 筆 static-like `[Perf]`，IdentityAudit 有 `690` 筆 static-like path。
3. Gap 佔動態總時間 `77.9%`，過高。

### 改善計畫

1. 包裝所有舊式 CRM service：
   - `ToolUtilityClass.m_Crm2011OrganizationService`
   - `ToolUtilityClass.m_OrganizationService`
   - `GetConnection()` 回傳 service
   - `m_ToolUtilityClass` 內部建立或持有的 service
2. 在 `FeeDownUpLoader`、`EquipmentController`、`NewPerson`、`PersonalInfomatioManager` 補 named phase timing。
   - 例如 `[Perf-Phase] path=/FeeManagement/LessonList phase=SetupLessonList ms=...`
   - 或整合進 `RequestProfiler.RecordPhase(...)`。
3. `PerfProfilingMiddleware` 加靜態檔過濾，即使 middleware pipeline 理論上應該跳過，也用 path guard 防止污染：
   - `/css`
   - `/js`
   - `/lib`
   - `/assets`
   - `/images`
   - `.css/.js/.png/.jpg/.svg/.ico/.woff/.woff2/.map`
4. `IdentityAuditMiddleware` 同樣排除靜態檔。
5. `PerformanceMonitoringMiddleware` 對靜態檔降級或排除。

### 預估效益

- 不一定直接提升使用者速度，但會讓下一次剖析從「gap 很大」進步成「哪一個 CRM 方法慢」。
- Trace log 體積可減少約 `60-75%`。
- 優化迭代速度會明顯提高。

## 十三、建議實作優先順序

### P0：先修 profiler 覆蓋與 log 噪音

目的：避免後續修正後仍看不到真正 CRM 明細。

1. `PerfProfilingMiddleware`、`IdentityAuditMiddleware` 排除靜態檔。
2. 包裝 `m_ToolUtilityClass` 使用的所有 CRM service。
3. 為 FeeManagement 與 Equipment 補 phase timing。

驗收：

- 靜態檔 `[Perf]` 從 `345` 筆降到接近 `0`。
- IdentityAudit static-like path 從 `690` 筆降到接近 `0`。
- FeeManagement / Equipment 的 `crm{n,ms}` 不再是 `0`。

### P1：Equipment 批次化

目的：先砍最大總量瓶頸。

1. 新增 `LoadEquipmentStorLessonsBatch`。
2. FetchXML / QueryExpression 一次查多個 contact。
3. link `new_disciple_lessons`，避免逐 lesson retrieve。
4. 前端 master-detail 改用預載 map。

驗收：

- `/Equipment/LoadEquipmentStorLessons` hits 從 `22` 降到 `1-3`。
- Equipment 課程總耗時從 `26.3s` 降到 `<5s`，warm cache `<1s`。

### P2：FeeManagement cache 與批次更新

目的：處理最大頁面延遲群組。

1. `LessonList` 加 user/session/permission scoped cache。
2. `FeeData` / `Present` / `Fee` 共用 per-user/per-session/per-lesson scoped data cache。
3. `GetFeeData()` 避免同一 `discipleLessonsId` 重複 `SetupPresentFeeList()`。
4. `CommitPendingChanges()` 合併同筆多欄位更新。
5. 使用 CRM batch update。
6. 上線前跑 A/B 使用者安全回歸：B 不得讀到 A 的 FeeData cache；未授權 `discipleLessonsId` / `StorLessonsId` 必須回 403，且 CRM 不可被更新。

驗收：

- `/FeeManagement/LessonList` warm cache `<1s`。
- `/FeeManagement/Api/FeeData` warm cache `<500ms`，cold `<1.5s`。
- `/FeeManagement/Api/SaveBatch` 3 筆修改 `<1.5s`。
- 動態 MVC/API response 必須維持 `no-store` 與 `Vary: Cookie`。

### P3：Personal N+1

目的：修掉 profiler 明確指出的 N+1。

1. `LoadMaintainPersonInfomation()` contact 查詢改 `IN` 批次。
2. option label map 快取。
3. 移除逐成員 metadata retrieve。

驗收：

- `[Perf-N+1]` 不再出現。
- CRM n 從 `65` 降到 `2-4`。
- endpoint 從 `4.47s` 降到 `<1.2s`。

### P4：登入瘦身

目的：降低第一個使用者等待點。

1. 統一帳密登入與 LINE ID 登入流程，只建立 session、使用者識別、landing page 判斷所需資料。
2. 移除登入同步 `SetupLessonList()`、`SetupPresentFeeList()`、Equipment、完整 IntegrateData、圖片批次、捐獻歷史等非第一頁必要載入。
3. `SetupSystemData()` 只做第一頁最小資料，不做全系統預載。
4. 第一頁之外的資料全部改成 click-triggered lazy load。
5. 第一頁顯示後才允許低優先背景預熱，而且不可阻塞 UI。
6. 修正 `EnsureCorrectUserData()` cache miss / credential mismatch。

驗收：

- `/Home/ProcessLogin` 第一階段 `<8s`。
- 完整第一頁最小載入後 `<4s`。
- 第一頁可互動時間作為新指標，目標 `<3-5s`。
- 帳密登入與 LINE ID 登入都符合相同的 landing page minimal-data 規則。
- `EnsureCorrectUserData` 的「憑證不一致，重新載入」在同一 session 測試中大幅下降。

### P5：儲存流程與圖片 outlier

1. `SaveNewPerson`、`SavePersonalInfomation`、`SaveQPayDedication` 補 phase timing。
2. 圖片處理背景化或獨立化。
3. 通知背景 queue。
4. MemberInfo update 合併欄位。

驗收：

- 儲存類端點至少下降 `30-50%`。
- Personal 圖片批次不再出現 `>1s` CRM outlier。

## 十四、預估整體改善

若依 P0 到 P4 完成，依本次 `Trace-Perf.log` 估算：

| 區域 | 目前 | 修正後目標 | 預估改善 |
|---|---:|---:|---:|
| Equipment 課程明細 | 22 hits / `26.3s` | cold `<5s`，warm `<1s` | `70-90%` |
| FeeManagement 課程/繳費/點名 | 合計 `36.5s` | cold `8-12s`，warm `<3s` | `60-85%` |
| Personal 維護資料 | `4.47s`，CRM n=65 | `<1.2s`，CRM n=2-4 | `70-85%` |
| 登入 | `10-17s` | 第一階段 `<8s`，完整 `<4s` | `40-75%` |
| SmallGroup 切換 | 合計約 `9.1s` | `<3-5s` | `40-70%` |
| 儲存與捐獻 | `2.6-7.8s` | 下降 `30-50%` | `30-50%` |

測試一整體體感速度，保守可提升 `50%` 以上；若 Equipment 與 FeeManagement 都完成快取/批次化，使用者最明顯的等待點可望減少 `60-75%`。

## 十五、下一輪驗證方式

修正後請保留同樣測試流程，並輸出新 log：

1. 清空或另存 `Trace.log`。
2. 確認 `Profiling:Enabled=true`。
3. 用 Debug build 重啟站台。
4. 依同樣順序操作測試一。
5. 執行：

```powershell
.\ChurchReport\Tools\parse-perf-log.ps1 -Log .\ChurchReport\Logs\Trace.log -Top 50
```

驗證重點：

- 先跑 Session Leakage A/B 安全回歸，安全通過後才看效能數字。
- A 登入後登出、B 登入同一頁，B 不得看到 A 的課程、點名、裝備、奉獻、個資或圖片資料。
- LINE ID 登入與帳密登入交錯測試時，session scope、cache scope、active list scope 不得沿用前一位使用者。
- 手動竄改 `discipleLessonsId`、`StorLessonsId`、`listId`、`contactId` 時，後端必須回 401/403 或省略未授權資料，且 CRM 不可被更新。
- `[Perf-N+1]` 是否消失。
- `/Equipment/LoadEquipmentStorLessons` hits 是否下降。
- FeeManagement 的 `gap` 是否被拆成可讀的 CRM operation，且 total 是否下降。
- `/Home/ProcessLogin` 是否低於 8 秒。
- 靜態檔 `[Perf]` / IdentityAudit 是否接近 0。

## 十六、結論

開啟 `[Perf]` 後，原本只能推論的瓶頸已經被拆得更清楚：

- Equipment 是最大總量瓶頸，必須批次化。
- FeeManagement 是最大頁面流程瓶頸，必須 cache 與合併更新。
- Personal 維護資料有明確 N+1，必須批次查 contact 並快取 option labels。
- 登入仍有同步預載與 credential cache 失效問題，必須瘦身。
- Profiler 目前仍有大量 gap，需優先補強舊式 ToolUtility / CRM service 包裝，否則後續很難精準驗證。

建議先做 P0 + P1 + P2。這三項完成後，下一輪測試應該就能看到最大等待時間明顯下降，且 `[Perf]` 報表會更能指出剩餘瓶頸。

安全否決條款：只要任一測試能讓 B 使用者看到 A 使用者的資料、cache、圖片、課程、點名、奉獻或個資，無論效能數字改善多少，該修正都不得上線；必須先修復 session/user/permission scope 隔離後再重新驗收。
