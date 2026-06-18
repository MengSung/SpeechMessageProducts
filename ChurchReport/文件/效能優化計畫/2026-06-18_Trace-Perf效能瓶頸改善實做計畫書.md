# 2026-06-18 Trace-Perf 效能瓶頸改善實做計畫書

## 一、計畫來源

本實做計畫書依據以下分析報告整理：

- `ChurchReport\文件\效能優化計畫\2026-06-18_Trace-No-Perf_vs_Trace-Perf_效能瓶頸及改善計畫.md`

分析報告指出目前主要瓶頸如下：

| 優先級 | 區域 | 主要問題 | 目前觀測 | 改善方向 |
|---|---|---|---:|---|
| P0 | Profiler / Log | 大量 gap 與靜態檔噪音 | 動態 gap `77.9%`，static-like `[Perf]` 345 筆 | 補強 CRM 包裝、排除靜態檔、補 phase timing |
| P1 | Equipment | master-detail 每列 AJAX + 後端逐人查 CRM | `/Equipment/LoadEquipmentStorLessons` 22 次合計 `26.3s` | 批次 API、批次 CRM 查詢、短 TTL cache |
| P2 | FeeManagement | 課程/點名/繳費資料重複載入，SaveBatch 逐欄位更新 | FeeManagement 相關合計約 `36.5s` | per-user/per-lesson cache、避免重複載入、合併更新 |
| P3 | Personal | 明確 N+1 | `/Personal/LoadMaintainPersonInfomation` `crm.n=65` | contact 批次查詢、option label cache |
| P4 | Login | 登入同步預載過多 | `/Home/ProcessLogin` `10-17s` | 第一頁最小載入、click-triggered lazy load |
| P5 | Save / Image / Dedication | 儲存流程與圖片 outlier 缺少分段 | `SaveNewPerson`、`SavePersonalInfomation`、`SaveQPayDedication` 仍慢 | 補 phase timing、圖片獨立化、通知背景化 |

## 二、總體實做目標

### 2.1 使用者體感目標

1. 登入後先看到第一頁，不再等待全系統資料預載完成。
2. Equipment、FeeManagement、Personal 維護資料不再因 N+1 或重複載入造成長時間等待。
3. 使用者點擊功能時才載入該功能資料，且同一資料在短時間內不重複打 CRM。
4. 儲存與捐獻流程回應更快，通知、大圖處理等非必要等待改成背景或獨立流程。

### 2.2 量化目標

| 項目 | 目前觀測 | 第一階段目標 | 完整目標 |
|---|---:|---:|---:|
| `/Home/ProcessLogin` | `10-17s` | `<8s` | `<4s` |
| 第一頁可互動時間 | 未量測 | `<5s` | `<3s` |
| `/Equipment/LoadEquipmentStorLessons` | 22 hits / `26.3s` | `<5s` | warm cache `<1s` |
| `/FeeManagement/LessonList` | `9.6s` | `<2s` | warm cache `<1s` |
| `/FeeManagement/Api/FeeData` | 最高 `5.0s` | cold `<1.5s` | warm cache `<500ms` |
| `/FeeManagement/Api/SaveBatch` | 3 筆修改 `3.0s` | `<1.5s` | `<1s` |
| `/Personal/LoadMaintainPersonInfomation` | `4.47s`, CRM n=65 | `<1.5s` | `<1.2s`, CRM n=2-4 |
| static-like `[Perf]` | 345 筆 | 接近 0 | 0 |
| IdentityAudit static-like path | 690 筆 | 接近 0 | 0 |

## 三、實做原則

### 3.1 第一頁最小載入

無論是帳密登入或 LINE ID 登入，登入成功後都只載入「即將進入的第一頁」需要的最小資料。

登入階段不應載入：

- 課程完整清單之外的課程明細。
- FeeData / PresentData。
- Equipment 課程明細。
- 所有小組完整成員與完整 IntegrateData。
- 圖片批次。
- 捐獻歷史。
- 非第一頁需要的 Appointment / QPay / Personal 明細。

### 3.2 Click-triggered lazy load

第一頁以外的資料全部由使用者點擊觸發：

- 點擊課程才載入該課程 FeeData。
- 展開 Equipment 明細才載入該頁可見成員課程資料。
- 進入 Personal 維護頁才批次載入 contact 欄位。
- 進入圖片區塊才載入圖片批次。
- 進入捐獻歷史才載入捐獻列表。

### 3.3 批次化優先於單點微調

本次瓶頸主要是 N+1、重複載入、逐欄位更新。優先做：

1. 多筆資料一次查。
2. 多欄位一次更新。
3. 同一頁資料短時間快取。
4. 同一 request 只驗證一次。

### 3.4 先讓 profiler 看得到，再優化深層邏輯

目前大量 `gap` 代表 profiler 尚未完整攔截舊式 CRM 路徑。P0 必須先完成，否則後續優化難以驗證。

### 3.5 安全第一：Session Leakage 零容忍

本計畫所有效能改善都必須以「不重現 Session Leakage / Session Bleeding」為最高優先。任何 cache、lazy load、batch API、background warmup，只要無法證明資料隔離正確，就不得上線。

絕對禁止：

1. 使用只包含 `listId`、`lessonId`、`discipleLessonsId`、`StorLessonsId`、`presentRecordId`、`contactId`、`account`、`lineId` 的 cache key 儲存使用者可見資料。
2. 把 A 使用者的 `InMemoryContext`、`ListManager`、`FeeList`、`Equipment`、`Personal`、`QPay` 資料放進全域 singleton 或 static 狀態後讓 B 使用者讀取。
3. batch API 信任前端傳入的 `contactId`、`presentRecordId`、`listId`、`discipleLessonsId`，未經伺服器端授權檢查就查詢或回傳資料。
4. background warmup 在 request 結束後繼續使用可能已變更的全域使用者狀態。
5. 對含個資、課程、奉獻、點名、裝備、牧養資料的動態 response 啟用公共快取或可被 proxy 共用的快取。
6. 為了效能跳過 `SessionValidationMiddleware`、`EnsureCorrectUserData()`、權限檢查、CSRF/Anti-forgery 檢查。

所有使用者資料 cache key 必須至少包含：

```text
sessionId + userId/contactId + accountOrLineHash + loginType + activeListId/listScope + selectDate + dataPurpose + version
```

若資料與課程或清單有關，還必須包含：

- `discipleLessonsId`
- `StorLessonsId` 或 `presentRecordId`
- `listId`
- `permissionScope`
- `cacheVersion` 或 `lastInvalidatedAt`

安全規則：

1. **Fail closed**：任何 session、user id、login type、active list、permission scope 無法確認時，回傳 401/403 或空資料，不得回傳 cache。
2. **Server-side ownership check**：所有 lazy/batch API 都必須由後端用目前 session 重新計算可存取範圍，再與前端傳入資料取交集。
3. **Per-user isolation first**：除非資料被證明是匿名且公共，否則 cache 一律以 user/session scope 隔離。即使同一 `lessonId`，也不能預設所有登入者可看相同資料。
4. **No shared mutable user state**：若必須使用 singleton service，service 不得持有目前使用者資料；使用者資料必須來自 `HttpContext.Session`、scoped context 或明確傳入的 immutable request scope。
5. **No-store dynamic response**：所有 MVC/API 動態 response 必須維持 `Cache-Control: no-store` 與 `Vary: Cookie`，不得被 Phase 0 靜態檔排除邏輯誤排除。
6. **Audit security events**：cache scope mismatch、batch request 越權、session mismatch、background warmup scope mismatch 都要寫安全警告 log。
7. **Two-user regression required**：每個 Phase 都必須通過 A/B 兩個不同使用者、同一瀏覽器/不同瀏覽器/同 Wi-Fi 情境測試，確認不會看到對方資料。

## 四、階段總覽

| 階段 | 名稱 | 目的 | 預估風險 | 可獨立驗收 |
|---|---|---|---|---|
| Phase 0 | Profiler 與 log 噪音修正 | 讓下一輪量測可信 | 低 | 是 |
| Phase 1 | Login 第一頁最小載入 | 改善登入體感 | 中 | 是 |
| Phase 2 | Equipment 批次化 | 砍最大總量瓶頸 | 中高 | 是 |
| Phase 3 | FeeManagement cache 與批次更新 | 砍最大頁面流程瓶頸 | 高 | 是 |
| Phase 4 | Personal N+1 修正 | 修明確 N+1 | 中 | 是 |
| Phase 5 | 儲存/圖片/捐獻流程優化 | 降低剩餘慢點 | 中 | 是 |
| Phase 6 | 回測與調校 | 驗證整體改善 | 低 | 是 |

建議實做順序：

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4
6. Phase 5
7. Phase 6

原因：Phase 0 讓資料可信；Phase 1 先改善登入體感；Phase 2 和 Phase 3 解決最大總量與最大頁面瓶頸；Phase 4 修掉明確 N+1；Phase 5 再處理剩餘儲存流程。

## 五、Phase 0：Profiler 與 Log 噪音修正

### 5.1 目標

1. 靜態檔不再產生 `[Perf]`。
2. 靜態檔不再進入 IdentityAudit。
3. FeeManagement / Equipment 等舊式 CRM 路徑不再全部落在 gap。
4. 報表能看出真正 CRM operation 名稱、次數與耗時。

### 5.2 修改範圍

主要檔案：

- `ChurchReport\Middleware\PerfProfilingMiddleware.cs`
- `ChurchReport\Middleware\IdentityAuditMiddleware.cs`
- `ChurchReport\Middleware\PerformanceMonitoringMiddleware.cs`
- `ChurchReport\Diagnostics\Profiling\TimedOrganizationService.cs`
- `ChurchReport\Diagnostics\Profiling\TimedToolUtilityProvider.cs`
- `ChurchReport\Diagnostics\Profiling\RequestProfiler.cs`
- `ChurchReport\Controllers\BaseChurchController.cs`
- 可能新增：`ChurchReport\Middleware\StaticRequestPathHelper.cs`
- 可能新增：`ChurchReport\Diagnostics\Profiling\ProfilingServiceWrapper.cs`

### 5.3 任務拆分

#### 5.3.1 建立靜態檔判斷 helper

新增共用 helper：

- 方法：`IsStaticAssetPath(PathString path)`
- 判斷路徑：
  - `/css`
  - `/js`
  - `/lib`
  - `/assets`
  - `/images`
  - `/img`
  - `/fonts`
  - `/_framework`
- 判斷副檔名：
  - `.css`
  - `.js`
  - `.map`
  - `.png`
  - `.jpg`
  - `.jpeg`
  - `.gif`
  - `.svg`
  - `.ico`
  - `.woff`
  - `.woff2`
  - `.ttf`

安全注意：

- helper 只能用於「真正靜態資產」的 profiler/audit 噪音排除。
- 不得讓 `/Home/xxx.css`、`/FeeManagement/xxx.js` 這類動態路由偽裝靜態副檔名而跳過 Web Cache Deception 或 session 驗證。
- Phase 0 修改後必須確認 `WebCacheDeceptionMiddleware` 仍在 `UseStaticFiles()` 之前執行。
- 動態 MVC/API response 仍必須保留 no-store 與 `Vary: Cookie`，不得因 helper 誤判而被 public cache。

#### 5.3.2 PerfProfilingMiddleware 排除靜態檔

在 `Invoke()` 一開始加入：

```csharp
if (StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
{
    await _next(context);
    return;
}
```

#### 5.3.3 IdentityAuditMiddleware 排除靜態檔

在寫 audit log 前檢查靜態檔，靜態檔直接 pass-through。

#### 5.3.4 PerformanceMonitoringMiddleware 排除或降級靜態檔

靜態檔不輸出慢請求 warning，避免干擾慢請求排名。

#### 5.3.5 補強舊式 CRM service 包裝

目前 `[Perf]` 已能攔到部分 `IOrganizationService`，但 FeeManagement / Equipment 大量 `crm{n=0,ms=0}`，需要把以下 service 統一包裝：

- `ToolUtilityClass.m_Crm2011OrganizationService`
- `ToolUtilityClass.m_OrganizationService`
- `BaseChurchController.GetConnection()` 回傳值
- 透過 `m_ToolUtilityClass` 持有的 service
- 透過 manager / loader 內部持有的 service

建議新增共用方法：

```csharp
IOrganizationService WrapIfProfilingEnabled(IOrganizationService service, IHttpContextAccessor http)
```

並在建立或取得 `ToolUtilityClass` 後呼叫：

```csharp
ProfilingServiceWrapper.WrapToolUtility(toolUtility, httpAccessor);
```

#### 5.3.6 新增 Phase timing 能力

在 `RequestProfiler` 加上 named phase：

- `RecordPhase(string name, long ticks)`
- `BuildSummaryLine()` 可選擇輸出 top phases。
- 或輸出獨立 `[Perf-Phase]`：

```text
[Perf-Phase] path=/FeeManagement/LessonList phase=SetupLessonList ms=9530
```

優先補 phase 的路徑：

- `FeeManagementController.LessonList`
- `FeeManagementController.GetFeeData`
- `FeeManagementController.SaveBatch`
- `EquipmentController.LoadEquipmentStorLessons`
- `NewPersonController.SaveNewPerson`
- `PersonalController.SavePersonalInfomation`
- `AuthenticationController.SetupSystemData`

### 5.4 驗收標準

1. 重跑測試一後：
   - static-like `[Perf]` 從 `345` 降到 `0` 或接近 `0`。
   - IdentityAudit static-like path 從 `690` 降到 `0` 或接近 `0`。
2. FeeManagement / Equipment 的 `[Perf]` 不再全部 `crm{n=0,ms=0}`。
3. `parse-perf-log.ps1` 能看到更準確的 CRM n/ms。
4. 新增 phase timing 可以指出至少：
   - `SetupLessonList`
   - `SetupPresentFeeList`
   - `CommitPendingChanges`
   - `LoadEquipmentStorLessons` query 階段
5. 動態路由偽裝靜態檔測試必須被阻擋，例如：
   - `/Home/ProcessLogin/fake.css`
   - `/FeeManagement/LessonList/fake.js`
   - `/Equipment/LoadEquipmentStorLessons/fake.png`
6. 任一 MVC/API response 不得出現 `Cache-Control: public`。

### 5.5 回退策略

1. 靜態檔 helper 若造成誤判，只回退 middleware path guard。
2. CRM wrapper 若造成 service release 問題，先只在 Debug + Profiling enabled 時啟用。
3. Phase timing 若輸出過多，可加 threshold，例如 `phase >= 100ms` 才輸出。

## 六、Phase 1：Login 第一頁最小載入

### 6.1 目標

1. 帳密登入與 LINE ID 登入都只載入第一頁需要的最小資料。
2. `/Home/ProcessLogin` 第一階段低於 `8s`。
3. 完整 minimal landing page loading 後低於 `4s`。
4. 第一頁可互動時間目標 `<3-5s`。

### 6.2 修改範圍

主要檔案：

- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Login.cs`
- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Private.cs`
- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.LineLogin.cs`
- `ChurchReport\Controllers\AuthenticationController\AuthenticationController.LineLoginOAuth.cs`
- `ChurchReport\Controllers\HomeController.cs`
- `ChurchReport\Controllers\BaseChurchController.cs`
- `ChurchReport\Models\ListManager.cs`
- 可能新增：`ChurchReport\Services\Login\LandingPageResolver.cs`
- 可能新增：`ChurchReport\Services\Login\LandingPageMinimumDataService.cs`
- 可能新增：`ChurchReport\Models\LoginLandingResult.cs`

### 6.3 設計

登入流程拆成四層：

1. `Authenticate`
   - 帳密登入驗證帳密。
   - LINE ID 登入驗證 LINE ID / LIFF 資訊。
   - 只取得 user id、full name、login account、login credential。
2. `InitializeSession`
   - 建立 session。
   - 寫入 `_LoginAccount`、`_LoginPassword`、user id、display name。
3. `ResolveLandingPage`
   - 判斷第一頁 route。
   - 回傳 `displayViewType`、`activeListId`、`loginType`、`nextRoute`。
4. `LoadLandingPageMinimumData`
   - 只載入第一頁首屏資料。
   - 不做全系統預載。

### 6.4 Landing page 資料邊界

| Landing page | 允許登入時載入 | 延後載入 |
|---|---|---|
| `MultiGroupView` | 多小組清單摘要、圖表摘要、登入者資訊 | 每組完整成員、IntegrateData、Equipment、FeeData、圖片 |
| `IntegrateView` | active list 的週報首屏、必要成員摘要 | 其他小組、歷史資料、圖片、Equipment、FeeData |
| `Personal` | 目前登入者基本資料 | 全小組維護清單、contact 批次圖片、option metadata |
| `QPay` / `Dedication` | 付款頁必要 contact / qpay model | 捐獻歷史、課程、裝備、小組資料 |
| `FeeManagement` | 課程清單摘要 | 指定課程 FeeData / PresentData |

### 6.5 任務拆分

#### 6.5.0 Session 安全前置要求

Phase 1 開始前必須先確認並保留現有 Session Bleeding 防線：

1. 全站動態 response 保持：
   - `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`
   - `Pragma: no-cache`
   - `Expires: 0`
   - `Vary: Cookie`
2. `SessionValidationMiddleware` 仍位於 `UseSession()` 之後、`UseAuthentication()` 之前。
3. `StrictNoCacheFilter` 與全域 `ResponseCache(NoStore=true)` 不得被移除。
4. 登入成功後必須重新建立/綁定 session identity，避免繼承前一個使用者 session。
5. 帳密登入與 LINE ID 登入必須都寫入一致的 session identity 欄位：
   - `_SessionUserId`
   - `_SessionUserAgent`
   - `_SessionRealIp`
   - `_LoginAccount`
   - `_LoginPassword` 或等價 credential token
   - login type
   - active list scope
6. 若新設計導入 `LandingPageResolver` 或 `LandingPageMinimumDataService`，服務生命週期必須是 scoped 或 stateless singleton；不得在 service 欄位保存目前使用者資料。

#### 6.5.1 拆分 `SetupSystemData()`

目前 `SetupSystemData()` 同步執行：

- `SetupListManager`
- `EnsureCorrectUserData`
- `SetupAppointmentList`
- `SetQpayModel`
- `SetupLessonList`

改成：

- `SetupLoginMinimumContext()`
- `ResolveLandingPage()`
- `LoadLandingPageMinimumData()`
- `QueueOptionalWarmup()`

#### 6.5.2 移除登入同步 `SetupLessonList()`

登入階段不呼叫：

```csharp
InMemoryContext.FeeList.SetupLessonList(viewModel.Account, viewModel.Password);
```

改由 `/FeeManagement/LessonList` 第一次進入時載入，並使用 Phase 3 cache。

#### 6.5.3 修正 `EnsureCorrectUserData()` cache miss

目前 log 中反覆出現：

```text
[BaseChurch.EnsureCorrectUserData] 憑證不一致，重新載入 ListManager 資料
```

需檢查：

- session `_LoginPassword`
- `ListManager.m_Password`
- LINE ID 登入時的 password 代表意義
- 帳密登入與 LINE ID 登入是否混用相同 cache key

建議 cache key：

```text
sessionId + userId + accountOrLineHash + credentialHash + loginType + activeListId + selectDate + permissionScopeHash
```

#### 6.5.4 第一頁之外資料改 lazy API

需要確認第一頁 view 是否在初始化時自動打以下 API：

- `FeeManagement/Api/FeeData`
- `Equipment/LoadEquipmentStorLessons`
- `Personal/LoadMaintainPersonInfomation`
- `MemberInfo/GetContactImagesBatch`

若有，調整成：

- 頁籤被點擊才呼叫。
- master-detail 展開才呼叫。
- 首屏以下資料延後。

#### 6.5.5 背景預熱

第一頁顯示後才允許背景預熱，例如：

- 預熱多小組 chart summary。
- 預熱使用者最常點擊的第一個功能。

背景預熱規則：

- 不阻塞 response。
- 失敗不影響 UI。
- 可設定關閉。
- 不預熱大量資料。
- 必須捕捉 immutable user scope，例如 userId、sessionId、loginType、activeListId、selectDate、permission scope。
- 執行前必須重新驗證該 scope 仍有效；若 session 已登出、使用者切換、activeListId 改變，立即取消。
- 背景預熱結果只能寫入同一 user/session scope 的 cache key，不得寫入只有 `listId`、`lessonId`、`discipleLessonsId`、`StorLessonsId`、`contactId` 的共用 key。
- 不得在背景執行緒讀寫會被其他使用者共用的 `InMemoryContext` 可變狀態。

### 6.6 驗收標準

1. 帳密登入：
   - `/Home/ProcessLogin < 8s`。
   - 完整第一頁 minimal load `<4s`。
   - 第一頁可互動 `<3-5s`。
2. LINE ID 登入：
   - 使用同樣 minimal-data 規則。
   - 不因 LINE ID 登入預載額外全系統資料。
3. 登入 log 不再出現大量：
   - `SetupLessonList`
   - `LoadEquipmentStorLessons`
   - `LoadMaintainPersonInfomation`
   - 非第一頁必要圖片批次。
4. 同一 session 後續 AJAX 中 `EnsureCorrectUserData` credential mismatch 明顯下降。
5. 兩個不同使用者連續登入測試：
   - A 登入後進第一頁。
   - 登出或開新瀏覽器 B 登入。
   - B 不得看到 A 的姓名、ActiveListId、小組、課程、圖片、奉獻或任何個資。
6. 同一台機器、同一 Wi-Fi、相同 IP 的 A/B 使用者測試必須通過。
7. LINE ID 登入與帳密登入切換測試必須通過，不能沿用前一次登入的 session scope。

### 6.7 回退策略

1. 若第一頁資料不足，先只把 `SetupLessonList()` 與非必要資料延後，不立即大改 route resolver。
2. LINE ID 登入若有特殊路徑，先保留原 route 判斷，但仍禁止載入非第一頁資料。
3. 背景預熱若不穩定，先關閉，只保留 click-triggered lazy load。

## 七、Phase 2：Equipment 批次化

### 7.1 目標

1. `/Equipment/LoadEquipmentStorLessons` hits 從 `22` 降到 `1-3`。
2. Equipment 課程明細總耗時從 `26.3s` 降到 cold `<5s`。
3. warm cache `<1s`。
4. 後端不再每個 contact、每個 lesson 個別查 CRM。

### 7.2 修改範圍

主要檔案：

- `ChurchReport\Controllers\EquipmentController.cs`
- Equipment 對應 view，需搜尋：
  - `Views\Equipment\*.cshtml`
  - `LoadEquipmentStorLessons`
- `ToolUtilityClass` 相關查詢方法所在檔案。
- 可能新增：`ChurchReport\Services\Equipment\EquipmentLessonQueryService.cs`
- 可能新增：`ChurchReport\Models\EquipmentStorLessonsBatchRequest.cs`
- 可能新增：`ChurchReport\Models\EquipmentStorLessonsBatchResponse.cs`

### 7.3 API 設計

新增 endpoint：

```text
POST /Equipment/LoadEquipmentStorLessonsBatch
```

安全要求：

1. 此 API 必須要求已登入 session，不得加入 `SessionValidationMiddleware` 排除清單。
2. 後端不得信任 request 的 `listId`、`presentRecordId`、`contactId`。
3. 後端必須用目前 session 的 user scope 重新取得使用者可存取的 list/contact 集合。
4. request items 必須與後端可存取集合取交集；不在授權集合內的項目直接忽略或回 403。
5. response 不得包含未授權 contact 的存在與否資訊，避免 side-channel 洩漏。
6. 所有錯誤回應不得回傳其他使用者的 list/contact 名稱。

Request：

```json
{
  "listId": "active-list-guid",
  "items": [
    { "presentRecordId": "...", "contactId": "..." }
  ]
}
```

Response：

```json
{
  "success": true,
  "lessonsByPresentRecordId": {
    "present-record-id": [
      {
        "storLessonsEntityId": "...",
        "discipleLessonsName": "...",
        "stageName": "...",
        "currentComplete": true,
        "discipleLessonsDateTime": "2026-06-18"
      }
    ]
  }
}
```

### 7.4 後端查詢策略

#### 7.4.1 第一步：批次 contactId 查詢

將多個 contactId 用 `IN` 條件一次查 `new_stor_lessons`。

#### 7.4.2 第二步：link `new_disciple_lessons`

查 `new_stor_lessons` 時直接 link 課程資料，帶回：

- lesson name
- `new_class_start_date`
- `new_now_stage_name`
- classification
- current complete

避免：

```csharp
ToolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId)
```

#### 7.4.3 第三步：分組

以 `contactId` 或 `PresentRecordId` 分組，回傳給前端。

### 7.5 前端調整

目前 master-detail likely 每列展開呼叫一次 `LoadEquipmentStorLessons`。

調整策略：

1. 第一階段：進入 Equipment contact list 後，批次載入 visible contacts 的 lessons。
2. 第二階段：使用者展開 row 時，先查本地 map。
3. 若本地 map 沒資料，再 fallback 呼叫單筆 API。
4. 保留舊單筆 API 作為回退。

### 7.6 Cache 策略

Cache key：

```text
EquipmentLessons:{sessionId}:{userId}:{accountOrLineHash}:{loginType}:{selectDate}:{listId}:{permissionScopeHash}:{contactIdsHash}:v1
```

TTL：

- cold data：`5` 分鐘。
- 若課程資料更新，清除該 list cache。

安全要求：

1. 不得使用 `EquipmentLessons:{listId}` 或 `EquipmentLessons:{contactIdsHash}` 這類跨使用者 key。
2. cache hit 後仍需確認目前 session 的 `userId/loginType/activeListId/selectDate` 與 cache scope 完全一致。
3. 使用者登出、切換帳號、LINE ID 重新綁定、active list 改變時，必須清除或自然隔離舊 cache。
4. 若 cache scope mismatch，必須 fail closed，不得 fallback 回傳舊資料。

### 7.7 驗收標準

1. `Trace.log` 中 `/Equipment/LoadEquipmentStorLessons` hits 降到 `1-3`。
2. 新增 `/Equipment/LoadEquipmentStorLessonsBatch` total `<5s`。
3. warm cache `<1s`。
4. 展開裝備明細資料正確：
   - 課程名稱正確。
   - 階段正確。
   - 日期正確。
   - 無課程者顯示空清單。
5. A/B 使用者交叉測試：
   - A 可看到的 Equipment contact，不得被 B 的 batch request 透過 contactId 猜出或取得。
   - B 傳入 A 的 `presentRecordId/contactId/listId` 時必須回 403 或忽略，且不得回傳 A 的資料。
6. cache key log 不得含明文帳號、明文密碼、LINE ID 原文、手機、姓名；只能記錄 hash 或 scope id。

### 7.8 回退策略

1. 保留舊 `/Equipment/LoadEquipmentStorLessons`。
2. 前端 batch 失敗時 fallback 單筆載入。
3. Cache 可由設定關閉。

## 八、Phase 3：FeeManagement Cache 與批次更新

### 8.1 目標

1. `/FeeManagement/LessonList` warm cache `<1s`。
2. `/FeeManagement/Api/FeeData` cold `<1.5s`，warm cache `<500ms`。
3. `/FeeManagement/Api/SaveBatch` 3 筆修改 `<1.5s`。
4. 避免同一課程資料被 view action 與 DataGrid API 重複載入。

### 8.2 修改範圍

主要檔案：

- `ChurchReport\Controllers\FeeManagementController.cs`
- `ChurchReport\Models\FeeList.cs`
- `ChurchReport\WebServiceConnector\FeeDownUpLoader.cs`
- FeeManagement views，需搜尋：
  - `Views\FeeManagement\*.cshtml`
  - `Api/FeeData`
  - `Api/SaveBatch`
- 可能新增：`ChurchReport\Services\FeeManagement\FeeManagementCacheService.cs`
- 可能新增：`ChurchReport\Services\FeeManagement\FeeBatchUpdateService.cs`

### 8.3 LessonList cache

新增 per-user/date cache：

```text
FeeLessons:{sessionId}:{userId}:{accountOrLineHash}:{loginType}:{selectDate}:{permissionScopeHash}:v1
```

流程：

1. `LessonList()` 先查 cache。
2. cache hit：直接使用 `LessonList`。
3. cache miss：呼叫 `SetupLessonList()`，並寫 cache。
4. 課程或繳費資料修改後 invalidation。

安全要求：

1. 課程清單不可只用 `account` 或 `lessonId` 當 key。
2. 同一課程若不同使用者權限不同，cache 必須依 permission scope 分開。
3. cache hit 後必須確認目前 session identity 與 cache scope 一致。
4. cache debug log 不得輸出明文帳密、LINE ID、奉獻或個資。

### 8.4 FeeData / PresentData cache

新增 per-lesson cache：

```text
FeeData:{sessionId}:{userId}:{accountOrLineHash}:{loginType}:{selectDate}:{discipleLessonsId}:{permissionScopeHash}:v1
```

流程：

1. `Fee()` / `Present()` 不直接重載完整 `FeeDataList`，只設定 `DiscipleLessonsId` 與 ViewBag。
2. `GetFeeData()` 負責載入資料。
3. `GetFeeData()` 若同一 `discipleLessonsId` 已載入且未過期，直接回傳。
4. `SaveBatch()` 成功後清除該 lesson cache。

安全要求：

1. `discipleLessonsId` 由前端傳入時，後端必須確認目前使用者有該課程的點名/繳費權限。
2. 未授權時回 403，不得回傳空資料偽裝成功，避免使用者以為資料被清空。
3. FeeData / PresentData 屬於使用者可見業務資料，cache 不得跨使用者共用；每次讀取都必須先確認目前 session scope 與 cache scope 完全一致。
4. `SaveBatch()` invalidation 必須只清除目前 user/scope 或該課程所有相關安全 scope，不得誤刪其他使用者 session 狀態。

### 8.5 避免 view 與 API 重複載入

目前風險：

- `Fee()` 呼叫 `SetupPresentFeeList()`
- `Present()` 呼叫 `SetupPresentFeeList()`
- DataGrid 進來又呼叫 `GetFeeData()`，再次 `SetupPresentFeeList()`

調整：

- View action 只準備頁面殼與參數。
- DataGrid API 才載入資料。
- 若 view 必須顯示筆數，使用 cache metadata 或前端載入後更新。

### 8.6 SaveBatch 合併更新

目前 `CommitPendingChanges()`：

- foreach record
- foreach field
- 每欄位呼叫 `UpdateFeeDataList()`
- `UpdateFeeDataList()` 內可能 retrieve + update stor lesson + update fee

改為：

1. 以 `StorLessonsId` 分組。
2. 每筆建立一個 `storLessonUpdate`。
3. 每筆必要時建立一個 `feeUpdate`。
4. 多欄位合併到同一個 Entity。
5. 最後一次 `Update` 或 `ExecuteMultiple`。

新增方法建議：

```csharp
UpdateFeeDataRecord(string storLessonsId, IReadOnlyDictionary<string, string> fields)
CommitPendingChangesBatch()
```

安全要求：

1. `SaveBatch` 必須重新驗證每個 `StorLessonsId` 屬於目前使用者可修改的 `discipleLessonsId`。
2. 不得只依 `ChangeHistory` 或前端 key 信任修改範圍。
3. 對同一筆資料合併多欄位前，必須確認所有欄位都在允許修改白名單。
4. 金額、繳費日期、退費、點名欄位應分別定義允許角色，避免多小組或非收費人員越權。
5. `ExecuteMultiple` 的每筆失敗都要記錄安全 scope 與 record id hash，不得輸出姓名、手機、明文帳號。

### 8.7 CRM batch update

若目前 CRM SDK 支援，使用 `ExecuteMultipleRequest`：

- 每筆 update 加入 request collection。
- 設定 continue on error。
- 回傳每筆結果。

若不支援，至少同一 `StorLessonsId` 多欄位合併成單次 update。

### 8.8 驗收標準

1. `/FeeManagement/LessonList`：
   - 第一次 cold 可接受。
   - 第二次同 user/date `<1s`。
2. `/FeeManagement/Api/FeeData`：
   - 第一次同 lesson `<1.5s`。
   - 第二次同 lesson `<500ms`。
3. `/FeeManagement/Api/SaveBatch`：
   - 3 筆修改 `<1.5s`。
4. `Trace.log` 不再看到同一 `discipleLessonsId` 在 view action 與 API 連續重複 `SetupPresentFeeList()`。
5. 修改後資料正確寫入 CRM。
6. 未授權使用者呼叫 `/FeeManagement/Api/FeeData?discipleLessonsId=...` 必須回 403。
7. 未授權使用者呼叫 `/FeeManagement/Api/SaveBatch` 修改不屬於自己的 `StorLessonsId` 必須失敗，且 CRM 不得被更新。
8. A/B 使用者連續登入後，B 不得讀到 A 的 FeeData cache。

### 8.9 回退策略

1. Cache service 可用設定關閉。
2. SaveBatch 可保留舊 `CommitPendingChanges()`，新方法失敗時回退舊方法。
3. ExecuteMultiple 若不穩定，先保留「同筆多欄位合併」版本。

## 九、Phase 4：Personal N+1 修正

### 9.1 目標

1. `/Personal/LoadMaintainPersonInfomation` 從 `4.47s` 降到 `<1.2s`。
2. CRM n 從 `65` 降到 `2-4`。
3. `[Perf-N+1]` 不再出現。

### 9.2 修改範圍

主要檔案：

- `ChurchReport\Controllers\PersonalController.cs`
- 可能新增：`ChurchReport\Services\Personal\PersonalMemberQueryService.cs`
- 可能新增：`ChurchReport\Services\CrmMetadata\CrmOptionLabelCache.cs`

### 9.3 任務拆分

#### 9.3.0 Personal API 安全要求

`LoadMaintainPersonInfomation()` 改批次後仍必須遵守：

1. 不得信任前端傳入的 `id`、`MULTIGROUP_MODE`、contactId、listId。
2. 多小組清單必須由目前 session 的 `ListManager.m_MultiGroupList` 或重新授權查詢取得。
3. 批次 contactId 必須由後端授權清單產生，不得直接使用前端提供的 contactId 清單。
4. 每一筆回傳 member 都必須屬於目前使用者可見的小組/list scope。
5. 若 `InMemoryContext.ListManager` 與 session user scope 不一致，必須 fail closed 並要求重新登入，不得嘗試使用舊資料補救。

#### 9.3.1 批次收集 contactId

在 `LoadMaintainPersonInfomation()` 中：

1. 多小組模式先收集所有 group 的 member list。
2. 收集所有 contactId。
3. 去重。

#### 9.3.2 批次查 contact

用一個 `QueryExpression("contact")`：

- `ConditionOperator.In`
- 欄位：
  - `contactid`
  - `fullname`
  - `mobilephone`
  - `address2_line1`
  - `birthdate`
  - `customertypecode`
  - `new_spiriitual_identity`
  - `new_equipment_status`

建立：

```csharp
Dictionary<Guid, Entity> contactsById
```

#### 9.3.3 Option label cache

目前 `RetrieveAttribute.Execute ×43` 合計 `3089ms`。

新增 metadata label cache：

```text
OptionLabels:contact:customertypecode
OptionLabels:contact:new_spiriitual_identity
```

TTL：

- `24` 小時。
- 或應用程式啟動期間常駐。

安全說明：

- Option label metadata 本身不含使用者個資，可以跨使用者快取。
- 但 member/contact 資料不可放進同一個全域 metadata cache。
- option label cache key 不需要 session，但 contact/member data cache 必須 session/user scoped。

#### 9.3.4 減少逐成員 Debug log

目前每個成員輸出大量 log。改成：

- 開始：小組數、預計 contact 數。
- 結束：成功筆數、缺漏筆數、耗時。
- 單筆錯誤才記錄 member id。

### 9.4 驗收標準

1. `parse-perf-log.ps1` 不再出現 `/Personal/LoadMaintainPersonInfomation` 的 `[Perf-N+1]`。
2. `crm.n <= 4`。
3. total `<1.2s`。
4. UI 欄位正確：
   - 姓名
   - 電話
   - 地址
   - 生日
   - 會員身分文字
   - 信仰狀態文字
   - 裝備狀態
5. B 使用者不得透過 Personal 維護頁看到 A 使用者小組成員。
6. 前端手動傳入其他 list/contact scope 時必須回 403 或空資料，且不得回傳該資料是否存在。

### 9.5 回退策略

1. 保留舊逐筆查詢方法為 private fallback。
2. 批次查詢失敗時回退舊方法並記錄 warning。
3. Option label cache miss 時可回源 CRM，但只允許整欄位一次回源，不允許逐成員回源。

## 十、Phase 5：儲存流程、圖片與捐獻優化

### 10.1 目標

1. 儲存類 endpoint 至少改善 `30-50%`。
2. 圖片批次不再出現 `>1s` CRM outlier。
3. 捐獻通知不阻塞使用者 response。

### 10.2 修改範圍

主要檔案：

- `ChurchReport\Controllers\NewPersonController.cs`
- `ChurchReport\Controllers\PersonalController.cs`
- `ChurchReport\Controllers\PersonalController.ImageUpload.cs`
- `ChurchReport\Controllers\MemberInfoController.cs`
- `ChurchReport\Controllers\DedicationController.cs`
- `ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.FeeManagement.cs`
- `ChurchReport\WebServiceConnector\PersonalInfomatioManager.cs`
- `ChurchReport\WebServiceConnector\NewPerson.cs`
- 可能新增：`ChurchReport\Services\BackgroundTasks\BackgroundTaskQueue.cs`
- 可能新增：`ChurchReport\Services\Images\ContactImageProcessingService.cs`

### 10.3 SaveNewPerson

補 phase timing：

- validate input
- `UploadNewPersonToCrm`
- `HandleSuccessfulNewPersonCreation`
- image decode / auto orient
- image encode
- CRM image update

改善：

1. 新人資料 create 成功後先回應。
2. 圖片改獨立 `/NewPerson/UploadNewPersonImage`。
3. 前端壓縮圖片最大邊長。
4. 後端儲存縮圖，避免每次讀原圖 resize。

### 10.4 SavePersonalInfomation

補 phase timing：

- model validate
- contact update
- present record update
- relation update
- reload data

改善：

1. 只送變更欄位。
2. 合併同 entity 多欄位 update。
3. 儲存後不重新載入整頁資料，只回傳更新後必要欄位。

### 10.5 Dedication

目前 timing：

- `GetContact`
- `CreateFee`
- `SendDedicationNotificationAsync`
- `CreateFee.SetFeeParameter`
- `CreateFee.CreateEntity`
- `CreateFee.RetrieveEntity`
- `CreateFee.AssignFeeOwner`

改善：

1. 通知送出改 background queue。
2. `CreateEntity` 後避免不必要 `RetrieveEntity`。
3. 若可行，在 create 時設定 owner，避免後續 assign。
4. session 中已有 contact 資料時，不重複 `GetContact`。

安全要求：

1. background queue item 不得只存 session id 後再回頭讀可變 session 狀態。
2. queue item 必須存 immutable correlation id、dedication id、contact id、建立當下的授權結果摘要。
3. 執行通知前重新查詢該 dedication 是否仍屬於同一 contact / payment scope。
4. 通知內容不得從其他使用者 cache 取資料。
5. queue 失敗重試不得重複建立奉獻或重複扣款。

### 10.6 圖片 batch

改善：

1. Personal / MemberInfo 圖片 batch 保留。
2. 若 `crm > 1000ms`，輸出 slow image query log。
3. 預產縮圖或 cache thumbnail bytes。
4. 無照片者優先 LINE picture URL / fallback avatar。

安全要求：

1. 圖片 cache key 必須包含 user/session 或明確的 permission scope；不得使用 `contactId:size` 作為所有使用者共用 key。
2. batch image API 必須先檢查目前使用者可看哪些 contact，再查圖片。
3. 未授權 contact 不得回傳 fallback avatar 來暗示該 contact 存在；應直接省略該 key 或回 403。
4. LINE picture URL 若來自 contact 欄位，也視為個資，必須受相同權限檢查保護。
5. 若未來要做跨使用者 binary/object cache，只能以不可反推個資的 content hash 儲存原始 bytes；每次回傳前仍必須先通過目前 session 的 contact 可見性檢查，HTTP response 仍維持 no-store。

### 10.7 驗收標準

1. `SaveNewPerson` `<4s`。
2. `SavePersonalInfomation` `<3s`。
3. `SaveQPayDedication` `<1.8s`。
4. `SaveKeyInDedication` `<650ms`。
5. 圖片 batch 一般 `<500ms`，不得再出現 `7s` outlier。
6. B 使用者不得透過圖片 batch 取得 A 使用者 contact 的 entityimage 或 LINE picture URL。
7. background queue 不得在 A 登出後，把通知內容錯送到 B 使用者資料。

### 10.8 回退策略

1. Background queue 可用設定關閉。
2. 圖片獨立上傳若失敗，不影響新人資料新增。
3. Dedication 通知 queue 失敗時記錄補償 log，可人工重送。

## 十一、Phase 6：整體回測與調校

### 11.1 回測前準備

1. 確認 Debug build。
2. 確認：

```json
"Profiling": {
  "Enabled": true
}
```

3. 清空或備份 `ChurchReport\Logs\Trace.log`。
4. 確認瀏覽器 cache 狀態一致。
5. 使用同一組測試帳號與同一組操作流程。

### 11.2 測試流程

依序操作：

1. 帳密登入。
2. LINE ID 登入。
3. MultiGroupView 第一頁。
4. SmallGroup IntegrateView。
5. Personal 維護資料。
6. NewPerson 新增與圖片上傳。
7. FeeManagement 課程清單。
8. Fee / Present / FeeData。
9. SaveBatch。
10. Equipment contact list。
11. Equipment lessons detail。
12. Dedication / QPay。
13. MemberInfo update 與圖片。

### 11.3 分析命令

```powershell
.\ChurchReport\Tools\parse-perf-log.ps1 -Log .\ChurchReport\Logs\Trace.log -Top 50
```

額外檢查：

```powershell
rg -n "\[Perf-N\+1\]|\[Perf-Gap\]|\[Perf-Slow\]" .\ChurchReport\Logs\Trace.log
```

### 11.4 驗收報表

修正後需產出新報告：

```text
2026-06-xx_Trace-Perf_效能改善驗收報告.md
```

報告需包含：

- 修正前後慢請求比較。
- 修正前後 `[Perf]` endpoint summary。
- CRM n/ms 變化。
- gap 變化。
- N+1 是否消失。
- static-like `[Perf]` 是否消失。
- 使用者第一頁可互動時間。

## 十二、資料正確性測試

### 12.0 Session Leakage 安全回歸測試

每個 Phase 完成後，都必須先跑 Session Leakage 安全回歸。若失敗，不得進入效能驗收。

測試帳號：

- 使用者 A：小組/課程/奉獻/個資資料與 B 不重疊。
- 使用者 B：不同小組、不同課程權限、不同 LINE ID。
- 使用者 C：權限較低，只能看部分或不能看相關資料。

測試矩陣：

| 測試 | 步驟 | 預期 |
|---|---|---|
| 同瀏覽器連續登入 | A 登入看資料，登出，B 登入看同頁 | B 不得看到 A 的任何資料或 cache |
| 不登出直接切換登入 | A 登入後直接走 B 登入流程 | session 必須重新綁定，A 資料不可殘留 |
| 同 Wi-Fi / 同 IP | A、B 在相同 IP 操作 | 不因 IP 相同混淆 session |
| 不同瀏覽器 | A 在 Chrome，B 在 Edge | cache 不得跨瀏覽器共享使用者資料 |
| LINE ID -> 帳密 | A 用 LINE ID 登入，再用 B 帳密登入 | LINE session scope 不得污染帳密登入 |
| 帳密 -> LINE ID | A 帳密登入，再用 B LINE ID 登入 | 帳密 session scope 不得污染 LINE 登入 |
| 手動竄改 listId | B 手動呼叫 A 的 listId | 回 403 或空資料，不得回傳 A 資料 |
| 手動竄改 contactId | B 手動呼叫 A 的 contactId 圖片/Personal/Equipment API | 回 403 或省略，不得暗示資料存在 |
| 手動竄改 discipleLessonsId | B 呼叫 A 課程 FeeData / SaveBatch | 回 403，CRM 不更新 |
| background warmup | A 觸發 warmup 後立刻 B 登入 | warmup 結果不得寫到 B scope |
| response cache | 動態頁檢查 headers | 必須 no-store 且 Vary Cookie |

必查 headers：

```text
Cache-Control: no-store, no-cache, must-revalidate, max-age=0
Pragma: no-cache
Vary: Cookie
X-Content-Type-Options: nosniff
```

安全驗收指標：

1. 沒有任何頁面或 API 讓 B 看到 A 的姓名、電話、照片、小組、課程、奉獻、點名、裝備資料。
2. 沒有 cache key 使用明文帳號、明文密碼、LINE ID、手機、姓名。
3. 沒有 API 因未授權資料回傳 fallback avatar、筆數、名稱等可推測存在性的資訊。
4. 安全警告 log 可追蹤越權嘗試，但不洩漏敏感資料。

### 12.1 Login

需測：

- 帳密登入。
- LINE ID 登入。
- 小組長。
- 多小組使用者。
- QPay / Dedication route。
- session 過期後重新登入。

確認：

- 第一頁正確。
- display view type 正確。
- ActiveListId 正確。
- 不會看到前一個使用者資料。
- 登入後 session identity 與 `_LoginAccount/_LoginPassword/_SessionUserId` 一致。
- 使用者切換後舊 `InMemoryContext` 不可被新使用者讀取。

### 12.2 Equipment

需測：

- 有課程的人。
- 無課程的人。
- 多課程的人。
- 多小組切換。
- 展開/收合多次。

確認：

- 資料筆數正確。
- 課程名稱、階段、日期正確。
- fallback 單筆 API 正常。
- 傳入未授權 contact/list id 不回傳資料。
- Equipment cache 不跨使用者命中。

### 12.3 FeeManagement

需測：

- 無課程。
- 單一課程。
- 多課程。
- Fee view。
- Present view。
- DataGrid paging/sorting/filtering。
- 修改一欄位。
- 同一筆修改多欄位。
- 多筆修改。
- SaveBatch 成功與失敗。

確認：

- cache 不回舊資料。
- 儲存後 cache invalidation 正確。
- CRM 資料正確更新。
- 未授權 lesson/storLessons id 不可讀取或修改。
- FeeData cache 不跨使用者命中。

### 12.4 Personal

需測：

- 單一小組。
- 多小組。
- 0 人。
- 20 人以上。
- 會員身分 option label。
- 信仰狀態 option label。
- 欄位缺漏 contact。

確認：

- 無 N+1。
- option label 正確。
- 欄位資料完整。
- Personal member/contact 批次資料不跨小組或跨使用者洩漏。

### 12.5 Save / Image / Dedication

需測：

- 新人無圖片。
- 新人有圖片。
- 大圖。
- 旋轉照片。
- Personal 修改單欄位。
- Personal 修改多欄位。
- QPay dedication。
- KeyIn dedication。
- 通知成功。
- 通知失敗。

確認：

- 使用者 response 不被通知阻塞。
- 背景錯誤可追蹤。
- 圖片顯示正確。
- 圖片與 LINE picture URL 權限檢查正確。
- background queue 不使用錯誤 session scope。

## 十三、風險與對策

| 風險 | 影響 | 對策 |
|---|---|---|
| Cache 回傳舊資料 | 使用者看到過期資料 | 每個寫入點明確 invalidation，短 TTL，Debug log cache key |
| Cache 跨使用者命中 | 嚴重 Session Leakage / 個資外洩 | 所有使用者資料 cache key 必須包含 sessionId、userId、accountOrLineHash、loginType、permissionScope；scope mismatch fail closed |
| Lazy load 造成畫面空白 | 使用者誤以為沒資料 | 首屏只顯示必要資料，區塊載入中使用既有 loading state |
| 批次 API 回傳資料對應錯誤 | Equipment / Personal 顯示錯誤 | 使用 contactId / PresentRecordId 雙 key 對照，加入資料筆數驗證 |
| 批次 API 被竄改 id | 越權讀取其他使用者資料 | 後端重新計算授權範圍，與前端傳入 id 取交集；未授權回 403 或省略 |
| LINE ID 登入路徑特殊 | 登入改造破壞 LINE flow | 帳密與 LINE 共用最小載入原則，但保留各自 authenticate |
| ExecuteMultiple 不支援或不穩 | SaveBatch 失敗 | 先做同筆多欄位合併，ExecuteMultiple 作第二階段 |
| 背景通知遺失 | 捐獻通知未送 | queue 持久化或失敗補償 log |
| 背景工作使用錯誤 session | A 的背景資料寫入 B scope | queue item 使用 immutable scope，執行前重驗授權，結果只寫入 scoped cache |
| Profiler wrapper 影響 Release | 正式環境風險 | 僅 `#if DEBUG` 且 `Profiling:Enabled=true` 時啟用 |

## 十四、交付物

### 14.1 程式交付

依階段交付：

1. Phase 0 PR：Profiler / log 噪音修正。
2. Phase 1 PR：Login 第一頁最小載入。
3. Phase 2 PR：Equipment 批次 API 與前端整合。
4. Phase 3 PR：FeeManagement cache 與 SaveBatch 合併更新。
5. Phase 4 PR：Personal N+1 修正。
6. Phase 5 PR：儲存、圖片、捐獻優化。
7. Phase 6 PR：回測報告與調校。

### 14.2 文件交付

1. 更新效能改善實作紀錄。
2. 新增改善後驗收報告。
3. 若新增設定，更新 README 或維運說明。
4. 若新增背景 queue，補充錯誤處理與重送方式。

## 十五、建議里程碑

| 里程碑 | 內容 | 預期結果 |
|---|---|---|
| M1 | Phase 0 完成 | profiler 報表可信，靜態檔噪音消失 |
| M2 | Phase 1 完成 | 登入體感明顯改善，第一頁更快可互動 |
| M3 | Phase 2 完成 | Equipment 明細總量瓶頸下降 |
| M4 | Phase 3 完成 | FeeManagement 頁面與儲存明顯改善 |
| M5 | Phase 4 完成 | Personal N+1 消失 |
| M6 | Phase 5 + Phase 6 完成 | 儲存/圖片/捐獻慢點收斂，產出驗收報告 |

## 十六、最小可行實作順序

若要用最短時間得到最大改善，建議先做以下最小集合：

1. 建立 Session Leakage 安全測試矩陣，並先跑 baseline。
2. 靜態檔排除與 profiler phase timing，但不得讓動態路由偽裝靜態檔跳過安全防線。
3. 登入移除同步 `SetupLessonList()`，且確認帳密/LINE ID 登入都重新綁定 session identity。
4. `FeeManagement.GetFeeData()` 避免同一 `discipleLessonsId` 重複 `SetupPresentFeeList()`，cache key 必須 user/session scoped。
5. `Personal.LoadMaintainPersonInfomation()` 批次查 contact 與 option label cache，contact 資料不可跨使用者 cache。
6. `Equipment.LoadEquipmentStorLessonsBatch` 後端先完成，前端再接；後端必須重新計算授權 contact/list scope。

這五項完成後，即使尚未完成所有 cache 與 batch update，也應該能明顯降低登入、Personal 與 Equipment 的等待時間，並讓下一輪 profiler 報表更精準。

## 十七、完成定義

本計畫完成需同時符合：

1. 所有 Phase 的主要驗收標準通過。
2. Session Leakage 安全回歸測試全部通過，且必須優先於效能驗收。
3. A/B/C 不同使用者、同 IP、同瀏覽器切換、LINE ID/帳密交叉登入測試均未發生資料串用。
4. 所有新增 cache key 均已審查，不含明文帳號、明文密碼、LINE ID、手機、姓名，且使用者資料 cache 均包含 user/session/permission scope。
5. 所有新增 lazy/batch API 都有 server-side ownership check，不信任前端 id。
6. 所有動態 MVC/API response 維持 no-store 與 `Vary: Cookie`。
7. 新的 `Trace.log` 中沒有主要 `[Perf-N+1]`。
8. static-like `[Perf]` 接近 `0`。
9. `/Home/ProcessLogin` 達到第一頁最小載入目標。
10. Equipment hits 大幅下降。
11. FeeManagement 重複載入下降。
12. Personal CRM n 降到 `2-4`。
13. 新增 `2026-06-xx_Trace-Perf_效能改善驗收報告.md`，報告必須包含安全回歸結果。
14. 使用者操作測試一流程時，第一頁進入與主要功能點擊的體感速度明顯改善。

安全否決條款：

- 只要發現任何可能讓 B 使用者看到 A 使用者資料的情境，即使效能達標，也視為本計畫未完成。
- 任何為了效能而降低 session 驗證、no-store、權限檢查、ownership check 的修改，一律不得合併。
