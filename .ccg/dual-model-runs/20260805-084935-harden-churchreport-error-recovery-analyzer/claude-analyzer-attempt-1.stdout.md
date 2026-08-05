# 分析報告:ChurchReport 錯誤復原與 CRM 服務生命週期

## 案頭澄清

本分析基於實際原始碼逐行核對(非僅根據任務描述的證據清單推論),核對範圍涵蓋:
`BaseChurchController.cs`、`ToolUtilityFactory.cs`、`ToolUtilityProvider.cs`、`ToolUtilityClass.Core.cs`、`DownloadListManager.cs`、`ListManager.cs`、`AuthenticationController.Private.cs`、`HomeController.cs`、`CrmConnectionPool.cs`。任務描述中列出的六點證據**全數在原始碼中得到確認**,但其中兩點的實際風險等級需要比任務描述更精確的定位——詳見下方分級。

我也發現一件任務描述未提及、但比第 3 點證據本身更關鍵的事實:**`BaseChurchController` 實作了 `IDisposable`,而 ASP.NET Core MVC 的預設 Controller 工廠會在每一次請求結束後對實作 `IDisposable` 的 Controller 呼叫 `Dispose()`**——不只是 `HomeController.cs` 裡那些顯式 `using (var authController = new AuthenticationController(...))` 巢狀建構的分支(如 `ProcessLoginRedirect`、`SaveUserLineIdRedirect`、`ProcessLineBindingRedirect`、`SaveUserIdRedirect`、`ProcessVisitorCard`)。這代表理論上**任何一次成功的 HTTP 請求結束,都會觸發全域單例被 Dispose**,而不僅是巢狀 Controller 案例——這把第 3 點的影響範圍從「某些呼叫路徑」擴大為「整個應用程式生命週期」。

---

## Critical(已證實根因,阻斷性)

### C1. 全域單例被 Controller 生命週期意外 Dispose(Use-After-Dispose)
- **檔案**:`BaseChurchController.cs:1235-1242`、`ToolUtilityFactory.cs:50-95`
- **根因鏈**(逐段驗證):
  `BaseChurchController.ToolUtility` → `_toolUtilityProvider.GetToolUtility()`(`ToolUtilityProvider.cs:32`)→ `ToolUtilityFactory.GetInstance()` → 回傳 process-wide `static ToolUtilityClass _instance`。
  `BaseChurchController.Dispose()` 呼叫 `ToolUtility?.Dispose()`,即直接對該 **static 單例**呼叫 `Dispose(true)`(`ToolUtilityClass.Core.cs:167-217`),釋放其 CRM 連線、trace 檔案流等資源,並設定 `_disposed = true`。
  然而 `ToolUtilityFactory._isInitialized` 在此之後**永遠不會被重設**——`ResetInstance()` 為 `internal`,且原始碼中**沒有任何生產路徑呼叫它**(只出現在文件與測試檔案)。
  結果:單例一旦被 Dispose,`GetInstance()` 仍回傳同一顆「已死亡」的物件,後續所有請求呼叫 CRM 相關方法都會拋 `ObjectDisposedException`。
- **確認的觸發面**:不只是任務描述提到的「Controller 結束後」——由於 `BaseChurchController : Controller, IDisposable`,ASP.NET Core 的預設 Controller 工廠會在**每一次**該類 Controller 服務完請求後呼叫其 `Dispose()`(這是 MVC 內建行為,非本專案自訂)。因此此缺陷的暴露機率遠高於任務描述中「可能」一詞暗示的機率。
- **架構影響**:應用程式極可能在啟動後第一個成功完成的請求即進入「CRM 功能全面癱瘓」狀態,直到程序重啟。這也很可能是「已觀察到的證據」中 CRM 錯誤頻繁出現的**主因**,而不只是伴隨症狀。

### C2. `HandleError` 非 AJAX 分支直接存取 `TempData`,可能以二次例外遮蔽原始錯誤
- **檔案**:`BaseChurchController.cs:361-367`
- **根因**:`TempData["ErrorMessage"] = exception.Message;` 沒有任何 null-safety。`TempData` 依賴 `ITempDataDictionaryFactory` 與可用的 `HttpContext`;若 Controller 在非標準管線中執行(背景工作、未完整初始化的 `ControllerContext`、單元測試宿主),此行會拋出例外,吃掉原始 CRM 例外的呈現機會。
- 與 C1 疊加後風險更高:C1 造成的 `ObjectDisposedException` 若發生在呼叫鏈更深處而被外層 `catch` 導到 `HandleError`,此處若又因 `TempData` 不可用而二次崩潰,維運人員將完全看不到根因,只會看到 IIS/Kestrel 的通用 500。

### C3. 原始例外訊息外洩至瀏覽器
- **檔案**:`BaseChurchController.cs:354-359`(AJAX 分支)、`:365`(非 AJAX 分支寫入 TempData)、`HomeController.cs:750-765`(`DisplayErrorView` 呈現)
- **根因**:AJAX 直接 `message = exception.Message`;非 AJAX 經 TempData 轉一手,最終同樣未經清洗地顯示在 `DisplayErrorView`。CRM 例外訊息可能包含連線字串片段、內部 entity/欄位邏輯名稱、SQL 相關錯誤等內部細節。

---

## Warning(已證實程式碼結構缺陷,但目前可達性需驗證)

### W1. `DownloadListManager.GetListManager` 將借用連線寫入全域單例欄位
- **檔案**:`DownloadListManager.cs:104-124`(寫入)、`ListManager.cs:58-72`(呼叫鏈起點)、`AuthenticationController.Private.cs:274-376`(`SetupSystemData` 借還)
- **確認的資料流**:
  `BaseChurchController.GetConnection()` → `_connectionPool.AcquireConnection()` → 傳入 `InMemoryContext.ListManager.SetupListManager(..., service)` → `m_DownloadListManager.GetListManager(..., organizationService: service)`。
  在 `GetListManager` 內,若 `organizationService != null` 且 `this.m_ToolUtilityClass.m_Crm2011OrganizationService == null`,則把**這個請求借來、稍後會在 `finally` 歸還池子**的連線寫進 `m_ToolUtilityClass`(這是透過 `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` 取得的**同一顆 process-wide 單例**)的欄位。
  已確認 `CrmConnectionPool.DisposeConnection`(`CrmConnectionPool.cs:409+`)會在連線被判定 idle/不健康時 `Dispose` 底層 `IOrganizationService`。若該連線曾被寫入單例欄位,單例會持有一個之後可能被池子關閉的物件參照,產生跨請求的懸空連線。
- **需要另外驗證的部分(調降嚴重度的關鍵理由)**:
  `m_Crm2011OrganizationService` 在 `ToolUtilityClass` 建構式中就會被 `InitializeCrmConnection()` 賦值(`ToolUtilityClass.Core.cs:97-106, 156-163`),且 `Dispose(bool)` **並未把該欄位設回 null**,只是呼叫其 `Dispose()`。因此在正常存活的單例上,`m_Crm2011OrganizationService == null` 這個條件**通常不成立**,W1 描述的寫入分支在目前可觀察的控制流下多半是死碼。它會被觸發的已知情境包括:(a) 單例建構式中 CRM 連線建立失敗但未拋例外(需確認 `CrmConnectionService.CreateOnPremiseClient` 的失敗語意)、(b) 未來有人呼叫 `ResetInstance()` 或把欄位手動設為 null。
  **這不代表 W1 可以忽略**——它仍是違反「短生命週期資源不可寫入長生命週期共享物件」原則的結構性缺陷,且與 C1 疊加時風險會放大(C1 發生後,單例的連線欄位是「已 Dispose 但非 null」,不會觸發此分支,但如果之後修正 C1 時的方案是「重建單例」而非「移除 Dispose 呼叫」,则重建後的單例欄位會回到 null,此時 W1 就會被真正觸發)。因此修正 C1 與 W1 必須放在同一批變更中一併考量,不能只修其中一個。

### W2. `HomeController.DisplayErrorView` 信任未經清洗的 `TempData` 內容
- **檔案**:`HomeController.cs:747-765`
- 此問題是 C3 的下游呈現點,本身沒有獨立根因,修正 C3(伺服器端產生安全訊息)即可連帶解決。單獨修正這裡(例如加白名單過濾)是防禦深度加分項,但不應是主要修正點。

---

## Info(說明性,非缺陷)

### I1. `SetupSystemData` 的借還模式本身正確
`GetConnection()` / `ReleaseConnection()` 搭配 `try/finally` 的借還節奏沒有問題,問題只出在 W1 那個把借來的連線洩漏進共享欄位的分支。修正時不應動到借還本身的結構。

---

## 最小修正建議

### 修正 1(對應 C1):移除 Controller Dispose 對全域單例的連鎖釋放
`BaseChurchController.Dispose()` 不應呼叫 `ToolUtility?.Dispose()`。`ToolUtility` 的生命週期屬於 `ToolUtilityFactory`(process 層級),不屬於任何單一 Controller 實例;Controller 沒有「建立」這個物件,就不該負責「釋放」它。保留 `IDisposable` 介面本身(因為 `HomeController.cs` 有多處 `using (var xController = new XController(...))` 依賴此介面能編譯),但清空對單例的釋放呼叫。

### 修正 2(對應 C2):`HandleError` 對 `TempData` 做防禦性存取
非 AJAX 分支寫入 `TempData` 前應確認其可用;不可用時改用不依賴 TempData 的降級路徑(例如直接回傳安全訊息的 `ContentResult`),避免二次例外蓋掉原始錯誤的 trace/LINE 通知(這兩者在該行之前已執行,順序上是安全的,只需保護 `TempData` 這一行本身)。

### 修正 3(對應 C3/W2):伺服器端一律以安全訊息取代原始例外文字
`exception.Message`(以及任何衍生的字串)只能寫入伺服器端 log/TraceByLevel/LINE 通知,不可出現在 `Json(...)` 回應或 `TempData["ErrorMessage"]`。改為固定的、不含技術細節的使用者提示字串。

### 修正 4(對應 W1):移除寫入共享欄位的分支,但需先確認下游呼叫者的連線來源
單純刪除 `GetListManager` 內把 `organizationService` 寫入 `m_ToolUtilityClass` 欄位的區塊(如 Gemini 分析所建議)在語法上安全,但要注意:`GetSmallGroupMemberNumber`、`ProcessListEntity` 等私有方法目前並**不**接受 `organizationService` 參數,它們只讀取 `m_ToolUtilityClass.m_Crm2011OrganizationService` / `m_OrganizationService` 這兩個欄位。若只刪除寫入而不做其他調整,這些下游查詢會**永遠改用單例自身建構時的固定連線身分**,完全忽略呼叫端(`SetupSystemData`)特意從連線池借來的連線——這是一個**行為變更**,而非單純的防護加固。在確認單例自身連線的憑證/權限範圍與連線池借出的連線在業務上等價之前,不應視為零風險的最小修正;若不等價,正確做法應是讓 `organizationService` 參數透過方法簽章往下傳遞給實際發出 CRM 呼叫的私有方法,而不是寫入共享欄位或直接丟棄。

---

## 應先撰寫且預期先失敗的 xUnit 回歸測試

1. **`ControllerDispose_DoesNotDisposeSharedToolUtilitySingleton`**
   驗證:對一個 `BaseChurchController` 衍生類別呼叫 `Dispose()` 後,`ToolUtilityFactory.GetInstance()` 回傳的物件仍可正常呼叫(例如 `TraceByLevel` 不拋 `ObjectDisposedException`)。
   修正前預期失敗原因:C1 會使該單例被 Dispose 且永不重建。

2. **`ControllerDispose_CalledTwiceAcrossSequentialRequests_SecondRequestStillWorks`**
   模擬兩次「建立 Controller → 執行 action → Dispose」的序列(對應 ASP.NET Core 對每個請求建立新 Controller 實例的行為),驗證第二次請求仍能透過 `ToolUtility` 存取 CRM 相關方法而不拋例外。
   修正前預期失敗原因:第一次 Dispose 後,`_isInitialized` 仍為 true 但 `_instance` 已死。

3. **`HandleError_WhenTempDataUnavailable_DoesNotThrow`**
   在缺乏可用 `TempData`(或 `ControllerContext` 不完整)的情境下呼叫非 AJAX 分支的 `HandleError`,驗證不拋 `NullReferenceException` 且回傳非 500 的 `IActionResult`。
   修正前預期失敗原因:C2 描述的直接存取。

4. **`HandleError_Ajax_DoesNotReturnRawExceptionMessage`** / **`HandleError_NonAjax_DoesNotPersistRawExceptionMessageToTempData`**
   驗證兩個分支回傳/寫入的字串都不包含原始 `exception.Message` 或 `exception.ToString()` 的任何片段。
   修正前預期失敗原因:C3。

5. **`DownloadListManager_GetListManager_DoesNotMutateSharedToolUtilityConnectionFields`**
   建構一個 `m_Crm2011OrganizationService` 為 null 的測試情境(需要能注入或反射設定,因為該欄位目前來自 process-wide 單例——測試需先確認能否以 `ResetInstance()` 或依賴注入取得乾淨實例),傳入一個 mock `IOrganizationService`,呼叫後驗證共享物件的欄位未被該 mock 覆寫。
   修正前預期失敗原因:W1。
   **附註**:此測試需要先解決「如何在測試中取得未被污染的單例」這個前置問題本身就佐證了 process-wide static singleton 對可測試性的負面影響——這點可記錄為架構債,但不在本次最小修正範圍內處理。

---

## 應明確拒絕的建議

- **拒絕:在每個 Controller 結束後呼叫 `ToolUtilityFactory.ResetInstance()`。**
  理由:並行請求會共用同一顆單例;請求 A 結束時重設,會讓仍在執行中的請求 B 的 CRM 連線瞬間失效,屬於新引入的 race condition,且違反任務限制「不可引入可跨請求保留的可變 CRM service」的精神(reset 本身就是一種跨請求副作用)。

- **拒絕:把 `IOrganizationService` 改為請求層級或使用者層級的靜態/快取變數來"修好"生命週期。**
  理由:直接違反任務明訂限制——不可引入可跨使用者、跨組織或跨請求保留的可變 CRM service。C1/W1 的正確修法是「不要洩漏」,不是「換一個地方繼續洩漏」。

- **拒絕:為了修 C2/C3 而把 `HandleError` 整個改寫成中介軟體(Exception Handling Middleware)等大範圍架構調整。**
  理由:任務要求「最小且安全的修正範圍」,且改為 middleware 會影響現有 AJAX 回傳格式與既有路由跳轉邏輯(`RedirectToAction("DisplayErrorView", "Home")`),回歸測試成本與本次任務範圍不成比例。應留待獨立的架構重構任務處理。

- **拒絕:在前端用 JavaScript 過濾或隱藏已傳輸的錯誤訊息。**
  理由:訊息一旦送達瀏覽器即視為外洩,前端遮蔽不具防禦效果,必須在伺服器端(C3 修正)阻止外洩發生。

---
SESSION_ID: bcf078b9-3ec2-4bae-92ae-34524a85230a
