# CCG Reviewer 結果：full-code-quality-audit-and-fix-review-rerun

## 執行方式說明
本次審查透過本機檔案系統直接讀取工作目錄 `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree` 的實際原始碼（而非僅憑 diff 文字），逐一核對 diff 中標示的變更點與其上下文邏輯，聚焦於 Request 指定的七個面向。`.ccg/dual-model-runs/**` 底下大量 JSON/MD 檔案的變更僅為 BOM 移除（`﻿{` → `{`），與程式碼品質無關，不列入發現項目。

---

## Critical
（無）

未發現會導致跨使用者資料外洩、憑證外流、或立即性資料損毀的缺陷。

---

## Warning

**1. `GetContactForKeyIn` 以 `TopCount = 1` 且無 `AddOrder` 情況下，重複比對（同編號同姓名）時結果仍不具決定性**
檔案：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:264-288`

方法上方註解提到「同一奉獻編號可能掛在大量會友上」，本次改為 `QueryExpression` + `pager`/`fullname`/`statecode` 三條件 AND 過濾，並加上 `TopCount = 1`。若真的存在多筆 `pager` 與 `fullname` 完全相同、且皆為 active（`statecode=0`）的 contact，CRM 在沒有 `OrderBy` 的情況下回傳的第一筆並無保證順序（同一查詢在不同時間執行可能選到不同筆）。這代表手動輸入奉獻時可能把收據/通知掛到「另一位同名同編號的會友」身上，屬於物件層級的資料歸屬正確性風險，而非新引入的迴歸（舊版 `QueryByAttribute` 同樣未排序也是取 `Entities[0]`），但既然此次特別以「即時找到正確會友」為修復目的，建議補上如 `contactid` 或 `createdon` 的 `AddOrder` 使結果具決定性，並在真正撞到重複時記錄警告而非靜默選一筆。

**2. `CreateLineLoginOAuthHttpClient` 在 `HttpContext` 或 DI 服務缺失時以例外中斷 LINE OAuth 流程**
檔案：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:450-459`

`ExchangeCodeForToken`（第 375 行）與 `GetLineUserProfile`（第 419 行）都在 `try/catch(Exception ex)` 內呼叫 `CreateLineLoginOAuthHttpClient()`，若 `IHttpClientFactory` 未註冊會拋出 `InvalidOperationException`，該例外會被外層 catch 吞掉並回傳 `null`（例如 407 行 `return null;`），對外只呈現「LINE 登入失敗」，不會有明確錯誤碼區分「LINE API 呼叫失敗」與「DI 設定缺陷」。目前 `Startup.cs:165-168` 已正確註冊 `"LineLoginOAuth"` 具名 client，此問題目前不會觸發，但屬於防禦性程式碼氣味：若日後有人抽掉該註冊，例外會被靜默吞掉而不易察覺根因，建議至少加上明確 log。

---

## Info

**1. `QueryListByContactId` 的 `PageInfo.Count = 5000` 為新增上限，且未檢查 `MoreRecords`**
檔案：`ToolUtility/QueryOperations/PresentRecordQueryService.cs:287-326`

此次把 `ColumnSet(true)` 收斂為明確欄位清單（含先前遺漏的 `new_happy_start_date`/`new_happy_end_date`，已由 `ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs:34-40` 的回歸測試驗證），同時新增 `PageInfo { Count = 5000, PageNumber = 1 }`。由於查詢已用 `associationName == contactId` 緊縮條件（單一 contact 關聯的名單），實務上不太可能超過 5000 筆，風險低；但程式碼未讀取回傳的 `MoreRecords`/`PagingCookie`，若未來資料量真的超過上限會靜默截斷而非報錯，建議加上簡單的 `MoreRecords` 檢查或記錄 log 以便未來排查。

**2. LINE OAuth HttpClient 修正與具名註冊已正確落實**
檔案：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:375,419,450-459`；`Startup.cs:165-168`

前次降級審查提出「`CreateClient("LineLoginOAuth")` 缺乏具名註冊」的問題，本次已透過 `services.AddHttpClient("LineLoginOAuth", client => client.Timeout = TimeSpan.FromSeconds(30));` 補上，且 controller 端不再直接 `new HttpClient()`，改由 `IHttpClientFactory` 建立、`using` 內釋放（工廠建立的 client 可安全 dispose，底層 handler 由工廠集中管理，不會造成 socket 耗盡）。確認修復到位。

**3. `TestCachePerformance` 的 sync-over-async 已修正**
檔案：`SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:402,439-442`

原本 `cacheService?.InvalidateAsync(...).Wait()` 屬於同步等待非同步方法、有執行緒池餓死/死結風險的典型反模式，本次改為 `async Task<IActionResult>` + `await cacheService.InvalidateAsync(...)`，屬正確修復。

**4. `StringUtility.DeleteLastComma` 邊界條件修正**
檔案：`ToolUtility/Utilities/StringUtility.cs:38`

`lastIndex > 0` 改為 `lastIndex >= 0`：修正了字串「以逗號開頭」時（`lastIndex == 0`）舊版不會截斷逗號的邊界缺陷，是正確的 bug fix，非迴歸。

**5. `SmallGroupController.LineLogin.cs` 移除 `Task.Run` 平行呼叫，屬於正向修正**
檔案：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs:39,63-67`

原本以三個獨立 `Task.Run` + `Task.WhenAll` 平行呼叫 `InMemoryContext.SetupSmallGroupData`、`SetupViewBagForSmallGroup()`、`EnsureIntegrateDataLoaded(lineUserId)`，三者皆會寫入同一個 per-request 的 `InMemoryContext`（`BaseChurchController.cs:164` 為 `protected readonly IInMemoryDataContext InMemoryContext`，DI scoped 產生，非 static），原本平行寫入同一實例存在資料競爭風險；本次改為循序同步呼叫，消除了該競爭風險，且三者本身皆為同步方法，不存在真正的 I/O 並行效益，是合理簡化。`cancellationToken` 語意與舊版相同（`Task.Run` 傳入的 token 本就不會中斷已開始執行的委派），未造成行為改變。

**6. Secret/config 處理：`.AddEnvironmentVariables()` 覆蓋範圍完整**
檔案：`DonationPaymentManager.cs`、`ChurchReportLineAdminNotificationService.cs`、`PaymentNotificationService.cs`、`DonationFeePaymentProcessor.cs`、`DonationPaymentDebugLogger.cs`、`LineUtilityClass.cs`、`PersonalQrCodeUtility.cs`、`QrCodeUtility.cs`、`RecurringDonationPaymentProcessor.cs`、`SmallGroupQrCodeUtility.cs`、`SundayQrCodeUtility.cs`、`DonationPaymentProcessor.Core.cs`、`LineNotifyUtility.cs`

已對專案內所有 13 處 `new ConfigurationBuilder()...AddJsonFile("appsettings.json")` 呼叫點逐一補上 `.AddEnvironmentVariables()`（以 grep 全專案確認無遺漏），且皆置於 `AddJsonFile` 之後，符合「環境變數覆蓋 appsettings.json」的預期優先順序。`appsettings.json` 內 Sinopac `A1/A2/B1/B2` 等具體金鑰值已清空為空字串，其餘 `[REDACTED]` 為既有佔位字串、非本次新增外洩。

**7. Object-level authorization：CRM 查詢字串為型別化參數，無注入風險**
檔案：`DonationPaymentProcessor.FeeManagement.cs:264-285`、`PresentRecordQueryService.cs:291-325`

改動皆使用 CRM SDK `ConditionExpression`/`FilterExpression` 型別化參數，非字串拼接，不存在注入風險；`AddListMembersListRequest`/`RemoveMemberListRequest` 相關測試（`ListServiceTests.cs`、`ToolUtilityFacadeIntegrationTests.cs`）已對照 `ToolUtility/ListOperations/ListService.cs` 既有生產程式碼實作，驗證邏輯與斷言一致，非空測試（`Assert.True(true)`）。

---

## 結論
本次重跑審查未發現 Critical 等級缺陷；前次降級審查提出的兩項問題（`QueryListByContactId` 缺欄位、`LineLoginOAuth` 缺具名 `HttpClient` 註冊）均已在此 diff 中確認修復並有對應回歸測試佐證。列出的 2 項 Warning 屬於既有風險在此次「修復」意圖下仍未完全消除（重複資料無序選取、DI 缺失時例外被靜默吞掉），建議後續小幅補強；其餘為 Info 等級的正向確認，無需額外動作。

---
SESSION_ID: 56d95806-afe9-449b-b313-abbba06b30c6
