# 程式碼品質稽核 – 最終審查報告

## 審查範圍
本次審查針對目前分支的 git diff（涵蓋 ChurchReport 主專案、ToolUtility 函式庫及測試專案），聚焦下列六大類別：Session 隔離／跨使用者資料外洩、MemoryCache 生命週期、LINE/HTTP Client 所有權、同步阻塞非同步呼叫、物件層級授權、CRM 查詢效能／無界讀取、密鑰與設定管理。

---

## Critical
**未發現 Critical 等級缺陷。**

本次 diff 中的變更（HttpClientFactory 導入、appsettings 密鑰改用環境變數 fallback、CRM 查詢改用具名欄位＋確定性排序）均屬修復性質，未發現會導致跨使用者資料外洩、可利用的授權繞過或明文密鑰外流的問題。

---

## Warning

### 1. `ToolUtility/QueryOperations/PresentRecordQueryService.cs:291-325`（`EntityName = "list"` 查詢，含新增的 `PageInfo`）
新增的 `PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }`（約第 310-314 行）只呼叫一次 `RetrieveMultiple`（第 325 行），沒有檢查回傳 `EntityCollection.MoreRecords` / `PagingCookie` 並迴圈取下一頁。若符合條件的 `list`（行銷清單）筆數超過 5000 筆，超出部份會被**靜默截斷**，且呼叫端無從得知資料不完整。就教會 CRM 的規模而言目前風險機率低，但這是本次 diff 主動加入 `PageInfo` 觸及到「CRM 查詢效能與無界讀取」這個審查項目的具體位置，建議至少記錄／告警 `MoreRecords == true` 的情況，而非直接吞掉。

（註：檔案中另一個查詢 `QueryWeeklyReportBeforeTwoMonthOfSunday`（第 260-282 行）本來就沒有分頁邏輯，此問題並非本次 diff 新引入，而是既有模式的延伸，嚴重度僅列 Warning 而非 Critical。）

---

## Info（含已確認修復 / 非缺陷之觀察）

### 1. `SmallGroupController.LineLogin.cs:38-39, 64-67` — 平行改序列，屬於正確修復而非退化
Diff 將原本以 `Task.Run` + `Task.WhenAll` 平行執行的 `SetupSmallGroupData` / `SetupViewBagForSmallGroup` / `EnsureIntegrateDataLoaded` 改為循序同步呼叫。經追查 `InMemoryContext`（`BaseChurchController.cs:164`，DI 註冊為 Scoped，`Startup.cs:642`）底層的 `ListManager` 實際是透過 `GetOrCreateSessionCacheValue`（`InMemoryDataContextSmallGroup.cs:179-202`，以 Session ID 為 key）存取共用 `IMemoryCache` 物件。先前三條執行緒同時對同一份 session 快取物件寫入，屬於未同步的資料競爭（race condition）；改為循序執行後消除了此請求內的競爭，是合理的正確性修復，非效能退化的疏漏。剩餘的「同一 Session 多分頁/重試並發存取同一快取物件」風險為既有架構、非本次 diff 造成，故不列入本次缺陷。

### 2. `AuthenticationController.LineLoginOAuth.cs:375, 419, 450-459` + `Startup.cs:165-168` — HttpClient 所有權修復正確
`new HttpClient()` 已全面改為透過 `CreateLineLoginOAuthHttpClient()` 取得具名 `IHttpClientFactory` 用戶端（`"LineLoginOAuth"`），並在 `Startup.cs` 完成對應具名註冊（含 30 秒逾時）。已掃描全專案生產程式碼，未發現殘留的 `new HttpClient()`（僅文件 `.md` 檔內有歷史範例）。`using` 包裹 factory 建立的 client 是安全模式（僅釋放 wrapper，不影響底層 handler 共用），無誤。

### 3. `HomeController.cs:402, 439-442` — 消除同步阻塞非同步（sync-over-async）
`TestCachePerformance()` 改為 `async Task<IActionResult>`，並將原本 `cacheService?.InvalidateAsync(...).Wait()` 改為 `await`。原寫法在 ASP.NET Core 環境雖不易死結但仍屬不必要的執行緒阻塞，此修復正確且無副作用。

### 4. `DonationPaymentProcessor.FeeManagement.cs:264-294` — 欄位集合已核實足夠覆蓋下游用途
已追蹤 `GetContactForKeyIn` 回傳的 `Entity` 在 `CreateFee` → `SetFeeParameter`（第 124、126、138 行讀取 `fullname`）、`SetFeeAdditionalInfo`（第 599 行讀取 `parentcustomerid`）、`AssignFeeOwner` → `GetOwnerId`（讀取 `ownerid`）、`BuildSuccessMessage`（第 636-637 行讀取 `pager`、`new_personal_id`）等全部下游存取路徑，均落在新 `ColumnSet` 明確列出的 8 個欄位內，未發現因由 `ColumnSet(true)` 改為顯式欄位而缺漏欄位導致執行期例外的風險。`TopCount = 1` 搭配 `AddOrder("contactid", OrderType.Ascending)` 亦已達成先前 CCG 審查要求的確定性排序。

### 5. 密鑰與設定管理（appsettings.json 及約 12 個組態建置點）
`AddEnvironmentVariables()` 已一致地加在 `AddJsonFile(...)` 之後（環境變數具較高優先權，可正確覆寫 appsettings 中已清空的密鑰），涵蓋 `DonationPaymentManager.cs:46`、`ChurchReportLineAdminNotificationService.cs:39`、`PaymentNotificationService.cs:48`、`DonationFeePaymentProcessor.cs:59`、`DonationPaymentDebugLogger.cs:34`、`LineUtilityClass.cs:58`、`PersonalQrCodeUtility.cs:66`、`QrCodeUtility.cs:72`、`RecurringDonationPaymentProcessor.cs:45`、`SmallGroupQrCodeUtility.cs:76`、`SundayQrCodeUtility.cs:66`、`DonationPaymentProcessor.Core.cs:54`、`LineNotifyUtility.cs:51`。appsettings.json 中的 Sinopac `A1/A2/B1/B2` 加密金鑰已清空為空字串，需仰賴環境變數提供實際值，屬合理設計，但建議確認部署環境已設定對應的 `Sinopac__A1` 等環境變數，否則正式環境的加解密會因空金鑰而失敗（此為部署面確認事項，非程式碼缺陷）。

### 6. `ToolUtility/Utilities/StringUtility.cs:38`（`DeleteLastComma`）
`lastIndex > 0` 改為 `lastIndex >= 0` 修正了逗號位於字串索引 0（如整串僅為單一逗號）時無法正確刪除的 off-by-one 邊界錯誤，屬獨立、正確的修復，未發現連帶副作用。

### 7. 測試相關變更（`MockOrganizationServiceFactory.cs` 等）
測試由 `MockCrmClientFactory` 統一改為 `MockOrganizationServiceFactory`，直接 mock `IOrganizationService`，並針對 `AddListMembersListRequest` / `RemoveMemberListRequest` 補上明確斷言。純測試基礎設施變更，未觸及生產程式碼路徑，不影響本次審查的六大類別。

---

## 結論
本次 diff 屬於針對既有 Critical／Warning 問題的**收斂性修復**（HttpClient 所有權、CRM 查詢欄位/排序、sync-over-async、密鑰環境變數 fallback），未發現新引入的 Critical 缺陷。僅一項 Warning（`PresentRecordQueryService.cs` 的單頁 5000 筆上限缺乏 `MoreRecords` 續頁邏輯）建議跟進，其餘均為已核實修復或低風險、資訊性觀察。

---
SESSION_ID: 712f6699-cf2c-4bf1-9aba-36cc98e36aef
