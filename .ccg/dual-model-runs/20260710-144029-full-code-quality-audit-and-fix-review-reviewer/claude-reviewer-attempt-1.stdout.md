## 審查結果（Traditional Chinese）

已針對本次 diff 逐一核對下游消費端（非僅看變更本身），聚焦於 Session 隔離、快取生命週期、LINE/HTTP client 所有權、同步阻塞、物件層授權、CRM 查詢效能/無界讀取、機密設定管理。以下為存活的具體缺陷。

---

### 🔴 Critical

**1. `ToolUtility/QueryOperations/PresentRecordQueryService.cs:294-313` — `QueryListByContactId` 新的 ColumnSet 遺漏 `new_happy_start_date` / `new_happy_end_date`，導致「小組效期篩選」邏輯被靜默繞過**

- 本次把 `ColumnSet(true)` 收窄為明確欄位清單，但清單中沒有 `new_happy_start_date`、`new_happy_end_date`。
- 這兩個欄位實際會被下游多處讀取：
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs:281-282`
  - `SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:334-335`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs:602-603`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:197-198`
  - 這些呼叫鏈全部經由 `ChurchListDataProcessor.QueryListByContactIdWithCache()` → `ToolUtilityClass.QueryListByContactId()` → 本次修改的 `PresentRecordQueryService.QueryListByContactId()`，包含 `SmallGroupController` LINE 登入路徑的熱路徑。
- `GetEntityDateTimeAttribute`（`ToolUtility/ToolUtilityStaticGlobal.cs:204-210`）對「Entity 未包含此欄位」是安全處理，直接回傳 `DateTime(1,1,1)`（Year==1），**不會拋例外**。
- 結果：每一筆名單都會被判定成「開始/結束日期皆未填」，落入 `ChurchListDataProcessor.cs:318-321` 的分支，**無條件把該名單加入 `m_Lists`**——原本用來限制「僅顯示效期內小組」的日期窗篩選整條失效。
- 影響：靜默資料正確性回歸，無編譯錯誤、無執行期例外、測試不會發現，會影響所有透過此查詢載入小組資料的登入/報表流程。
- 建議：把 `new_happy_start_date`、`new_happy_end_date` 加回 ColumnSet，並在合入前針對 `ChurchListDataProcessor`/`WeeklyReportProcessor`/`DownloadListManager`/`DownloadEquipment` 四個消費端做一次欄位使用交叉核對，避免同類窄化再次漏欄位。

**2. `appsettings.json` 舊機密值仍留存於 Git 歷史，且本次 review 輸入本身也含未遮罩明文**

- Diff 中 Sinopac（`A1`/`A2`/`B1`/`B2`，約 297-343 行一帶）、MyPay `Key`、CRM `Password`（`"[REDACTED]"`）等被清空的「舊值」在提交給我的 diff 內容中是**未加 `[REDACTED]` 的明文**，與其餘已遮罩的 `ChannelAccessToken`/`ChannelSecret` 處理方式不一致。
- 單純把 `appsettings.json` 改成空字串，並不會讓已經明文提交進 Git 歷史（`git log -p`/`git blame` 仍可還原）的舊金鑰失效。
- 建議：(a) 對已外洩的機敏值（Sinopac 沙盒金鑰、CRM 帳密等）立即輪替/撤銷；(b) 評估是否需要對 Git 歷史做 secret purge；(c) 之後產生審查輸入時，統一對所有「即將被移除」的舊機密值也套用遮罩，不要讓被刪除的明文原樣出現在 diff 文字中。

---

### 🟡 Warning

**`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:450-459` — 具名 HttpClient `"LineLoginOAuth"` 未在 DI 註冊專屬設定**

- `CreateLineLoginOAuthHttpClient()` 呼叫 `httpClientFactory.CreateClient("LineLoginOAuth")`，但 `Startup.cs:164` 只註冊了無名稱的 `services.AddHttpClient()`，沒有 `services.AddHttpClient("LineLoginOAuth", ...)` 這個具名項目。
- 未註冊的具名 client 不會丟例外（會退回預設 `HttpClientFactoryOptions`），所以功能上可用、也確實解決了原本 `new HttpClient()` 逐次配置 socket 的風險，這點是正向修正。
- 但目前這個名稱只是「掛名」，沒有取得任何專屬逾時/BaseAddress/重試設定的好處；未來若有人假設它已有 LINE OAuth 專屬逾時設定（例如比對 `Startup.cs`）會誤判為已配置。建議在 `Startup.cs` 補上 `services.AddHttpClient("LineLoginOAuth", ...)` 明確設定逾時等參數，或乾脆改用無名稱 `CreateClient()` 避免誤導。

---

### 🔵 Info

**`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs:38-67` — 移除 `Task.Run`/`Task.WhenAll` 是正向修正，非回歸**

- 原本 `Task.Run` 讓 `SetupViewBagForSmallGroup()`（存取 `ViewBag`）與 `InMemoryContext`/`EnsureIntegrateDataLoaded` 在背景執行緒上並行修改同一組 controller/session 綁定的可變狀態，屬於典型 ASP.NET Core 反模式（跨執行緒存取 `ViewBag`，且三個並行工作互相沒有同步機制去存取同一個 `InMemoryContext.ListManager`）。
- 改成直接在請求執行緒依序同步呼叫後，消除了這個競態風險。雖然損失原本三個工作平行等待的延遲優化，但相較於潛在的跨執行緒狀態毀損，此取捨合理，且與既有稽核文件（`.ccg/tasks/full-code-quality-audit-and-fix/research/crm-query-static-hotspot-audit.md`）建議一致。不需再處理。

---

### 未發現新增缺陷的區塊（已核對）

- `DonationPaymentProcessor.FeeManagement.cs:264-286` 的 `GetContactForKeyIn` 改用 `QueryExpression` + 明確欄位 + `TopCount=1` + `statecode=0`：已交叉核對 `CreateFee`、`ResolveDedicationNotificationLineId`（`:397`,`:403`，讀 `new_lineid`/`new_lineid_backup`，皆在新 ColumnSet 內）、`BuildSuccessMessage` 沒有存取被移除的欄位，屬於正向修正。
- `HomeController.cs:402-444` 由 `.Wait()` 改為 `await`：屬正向修正，消除該除錯端點的同步阻塞死結風險；此端點依賴 `HttpContext.Session` 取得目前登入者自己的 ContactID（`:459`），不存在跨使用者查詢他人資料的物件層授權問題。
- `ToolUtility.Tests/TestHelpers/MockOrganizationServiceFactory.cs` 及各測試檔改用 `IOrganizationService` mock、`ToolUtilityFacade(IOrganizationService, object logger = null)` 建構參數順序與實際簽章相符，未發現編譯期或邏輯缺陷。

---
SESSION_ID: 94048b87-6576-4bf6-9f87-9e7d53a0dfeb
