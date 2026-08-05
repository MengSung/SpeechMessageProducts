# ChurchReport 錯誤復原與 CRM 服務生命週期分析報告

本報告針對 `ChurchReport` 系統中已觀測到的錯誤處理機制、TempData 存取異常、以及 CRM 服務生命週期洩漏等問題進行深度 UI/UX 與前端架構分析，並提出最小安全修正範圍與回歸測試規劃。

---

## 1. UX Analysis (使用者影響評估)

### 使用者體驗影響 (User Experience Implications)
* **二次崩潰遮蔽真實錯誤**：當系統發生 CRM 連線或業務邏輯錯誤時，`BaseChurchController.HandleError` 試圖處理該錯誤，卻因 `TempData` 或 `HttpContext` 為 `null` 而觸發第二個 `NullReferenceException`。這會導致瀏覽器端直接呈現 IIS 的「500 Internal Server Error」黃頁或空白畫面，而非友好的錯誤提示頁面，嚴重打擊使用者對系統的信任度。
* **敏感資訊洩漏風險**：AJAX 分支直接將 `exception.Message` 回傳至前端，非 AJAX 分支亦將原始錯誤訊息寫入 `TempData` 並呈現在 `DisplayErrorView`。若錯誤訊息包含資料庫連線字串、ADFS 憑證、CRM 內部欄位名稱或 SQL 語法錯誤，將會被惡意使用者利用，構成資安漏洞。

### 使用者旅程與無障礙考量 (User Journey & Accessibility)
* **錯誤復原中斷**：在正常的錯誤復原旅程中，使用者應看見清晰、不含技術術語的引導說明（例如：「系統暫時無法連線，請稍後再試」），並提供「返回首頁」或「重新整理」的按鈕。目前的機制會導致頁面卡死或崩潰，中斷了使用者的操作流。
* **無障礙輔助技術相容性**：直接回傳原始 Exception 堆疊或未格式化的 JSON 錯誤，會導致螢幕閱讀器（Screen Reader）朗讀出無意義的程式碼片段，對身心障礙使用者極不友善。

---

## 2. Design Evaluation (設計系統評估)

### 一致性與模式 (Consistency & Patterns)
* **錯誤回饋不一致**：AJAX 請求與一般頁面請求的錯誤處理邏輯分散且不一致。AJAX 回傳的 JSON 格式未經過統一的 API Response 封裝，而一般頁面則依賴不穩定的 `TempData` 進行跨 Action 傳遞。
* **元件生命週期職責混淆**：`BaseChurchController` 作為 UI 控制器，卻主動去 `Dispose` 全域單例 `ToolUtility`。這違反了設計系統中「誰創建，誰釋放」的生命週期管理原則，導致全域共享的 UI 輔助元件在單次請求結束後即失效。

---

## 3. Technical Considerations (技術考量與架構影響)

以下依據嚴重程度，將已證實的根因與潛在風險進行分級分類：

### 【Critical】嚴重缺陷

#### 1. BaseChurchController.HandleError 中的 TempData 空值引發二次崩潰
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs`
* **已證實根因**：在非 AJAX 分支中，程式碼直接執行 `TempData["ErrorMessage"] = exception.Message;`。在單元測試環境、背景排程呼叫、或 ControllerContext 未完全初始化的請求中，`TempData` 為 `null`，這會拋出 `NullReferenceException`，徹底遮蔽原始的 CRM 錯誤。
* **架構影響**：破壞了錯誤處理機制的強健性（Robustness），使系統失去自我診斷能力。

#### 2. 敏感資訊直接暴露至前端瀏覽器
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs`
* **已證實根因**：AJAX 分支直接回傳 `exception.Message`；非 AJAX 分支將原始訊息放入 `TempData` 後由 `HomeController.DisplayErrorView` 呈現。
* **架構影響**：違反安全編碼規範，存在資訊洩漏（Information Disclosure）漏洞。

#### 3. 跨請求的靜態單例被 Controller 錯誤釋放 (Use-After-Dispose)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs` (第 1235-1242 行)
* **已證實根因**：`BaseChurchController.Dispose()` 呼叫了 `ToolUtility?.Dispose()`。然而 `ToolUtility` 是透過 `ToolUtilityFactory.GetInstance()` 取得的 Process-wide 靜態單例。一旦某個 Controller 執行完畢被釋放，該單例即被銷毀，導致後續所有請求在呼叫 `ToolUtility` 時皆會拋出 `ObjectDisposedException`。
* **架構影響**：導致整個應用程式在運行一段時間後，所有依賴 CRM 與工具類別的功能集體癱瘓。

#### 4. 短生命週期 CRM 連線洩漏至長生命週期全域單例
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\WebServiceConnector\DownloadListManager.cs` (第 104-124 行)
* **已證實根因**：`DownloadListManager.GetListManager` 將傳入的短生命週期 `IOrganizationService`（從 Pool 借出且會在 `finally` 歸還）寫入共用的 `m_ToolUtilityClass.m_Crm2011OrganizationService` 與 `m_OrganizationService` 欄位。
* **架構影響**：當該連線被歸還至 Pool 或被關閉後，全域單例仍持有其無效參照，造成跨請求的連線污染與 Use-After-Free 風險。

---

### 【Warning】警告事項

#### 1. HomeController.DisplayErrorView 依賴不安全的 TempData 內容
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\HomeController.cs` (第 748-766 行)
* **已證實根因**：直接讀取並呈現 `TempData["ErrorMessage"]`，缺乏對錯誤訊息內容的安全過濾與遮蔽。
* **架構影響**：若上游未做好錯誤訊息淨化，此處將成為敏感資訊洩漏的最終呈現點。

---

### 【Info】說明資訊

#### 1. SetupSystemData 的連線借還生命週期
* **已證實行為**：從 `ICrmConnectionPool` 借用 `IOrganizationService`，並在 `finally` 區塊中歸還。此設計模式本身是正確且安全的，但必須確保借出的連線絕不外洩至任何欄位或靜態變數中。

---

## 4. Options (替代方案與權衡)

### 方案 A：全面重構為標準 ASP.NET Core 異常處理中間件 (Middleware)
* **優點**：將錯誤處理與 Controller 完全解耦，統一在 Pipeline 最外層進行錯誤遮蔽與日誌記錄。
* **缺點**：修改範圍過大，會影響現有的 AJAX 回傳格式與舊版路由跳轉邏輯，回歸測試成本極高。

### 方案 B：最小安全範圍修正 (本報告推薦)
* **優點**：僅針對 `BaseChurchController` 的 `HandleError` 與 `Dispose`、以及 `DownloadListManager` 的連線賦值進行局部修正。不改變現有架構，風險極低，且能完全解決二次崩潰與連線洩漏問題。
* **缺點**：Controller 仍保有部分錯誤處理職責，但可透過嚴格的單元測試確保其安全性。

---

## 5. Recommendation (偏好方案與執行細節)

### 最小修正建議 (Minimal Safe Fixes)

#### 修正 1：安全存取 TempData 與實施錯誤遮蔽
在 `BaseChurchController.HandleError` 中，進行 `TempData` 的 null 檢查，並將回傳給前端的訊息替換為安全、通用的錯誤提示。原始錯誤則記錄於伺服器端日誌。

```csharp
// 檔案路徑：SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs
// 確保檔案編碼為 UTF-8 no BOM, CRLF 結尾

protected IActionResult HandleError(Exception exception, string methodName)
{
    // 1. 安全地將詳細錯誤記錄於伺服器端日誌與 LINE 通知
    string detailedErrorMessage = $"[系統錯誤] FullName = {GetType().FullName}, Method = {methodName}, Time = {DateTime.Now}, Details = {exception}";
    
    try
    {
        // 僅在伺服器端 Trace 詳細錯誤，避免洩漏給瀏覽器
        ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, detailedErrorMessage);
    }
    catch (Exception traceEx)
    {
        System.Diagnostics.Debug.WriteLine($"TraceByLevel 失敗: {traceEx.Message}");
    }

    SendLineErrorNotification(detailedErrorMessage);

    // 2. 定義安全、不含敏感資訊的通用錯誤訊息
    string safeUserMessage = "系統處理您的請求時發生錯誤，請稍後再試或聯絡系統管理員。";

    bool isAjaxRequest = false;
    try
    {
        isAjaxRequest = Request?.Headers != null && Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }
    catch
    {
        isAjaxRequest = false;
    }

    if (isAjaxRequest)
    {
        // AJAX 僅回傳安全訊息
        return Json(new
        {
            status = "error",
            message = safeUserMessage,
            timestamp = DateTime.Now
        });
    }
    else
    {
        // 非 AJAX 分支：安全檢查 TempData 是否可用
        if (TempData != null)
        {
            TempData["ErrorMessage"] = safeUserMessage;
            return RedirectToAction("DisplayErrorView", "Home");
        }
        else
        {
            // 若 TempData 不可用，直接回傳 ContentResult 避免二次崩潰
            return Content(safeUserMessage, "text/html; charset=utf-8");
        }
    }
}
```

#### 修正 2：移除 BaseChurchController 對全域單例的 Dispose 呼叫
避免 Controller 銷毀時連帶釋放全域單例。

```csharp
// 檔案路徑：SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs

public new void Dispose()
{
    // 🔴 移除 ToolUtility?.Dispose(); 避免釋放全域共享單例
    
    // 僅釋放 Controller 基礎資源
    base.Dispose();
}
```

#### 修正 3：禁止將短生命週期連線快取至全域單例欄位
修改 `DownloadListManager.GetListManager`，移除將 `organizationService` 寫入 `m_ToolUtilityClass` 欄位的邏輯。

```csharp
// 檔案路徑：SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs

public void GetListManager(String Account, String Password, DateTime aDownloadDate, ref MultiGroupList aMultiGroupList, ref MultiGroupChartDataList aMultiGroupChartDataList, ref String LoginType, ref String UserType, ref String LoginFullName, ref String ActiveListId, IOrganizationService organizationService = null)
{
    try
    {
        // 🔴 移除以下將短生命週期 service 寫入全域單例欄位的程式碼：
        // if (organizationService != null && this.m_ToolUtilityClass != null) { ... }
        
        // 🟢 改為：在方法內部需要使用服務時，直接使用傳入的 organizationService 參數，
        // 或在未傳入時，透過安全管道即時獲取，絕不快取至 m_ToolUtilityClass。
        
        // ... 續接原業務邏輯 ...
    }
    catch (Exception ex)
    {
        // 錯誤處理
    }
}
```

---

### 應先撰寫且預期失敗的 xUnit 回歸測試清單 (xUnit Regression Tests)

在套用上述修正前，必須先撰寫以下測試案例，並確認其在修正前會失敗（Red），修正後通過（Green）：

1. **`HandleError_WhenTempDataIsNull_ShouldNotThrowNullReferenceException`**
   * **測試目的**：驗證當 `Controller.TempData` 為 `null` 時，呼叫 `HandleError` 不會引發二次崩潰，且能安全回傳錯誤結果。
   * **預期失敗原因**：修正前會因為直接存取 `TempData["ErrorMessage"]` 而拋出 `NullReferenceException`。

2. **`HandleError_ShouldNotExposeOriginalExceptionMessageToClient`**
   * **測試目的**：驗證不論是 AJAX 還是非 AJAX 分支，回傳給前端的錯誤訊息皆不包含原始 Exception 的 `Message` 或 `StackTrace`。
   * **預期失敗原因**：修正前會直接將 `exception.Message` 寫入 JSON 或 `TempData`。

3. **`ControllerDispose_ShouldNotDisposeSharedToolUtilityInstance`**
   * **測試目的**：驗證當 Controller 實例被 `Dispose` 後，全域的 `ToolUtilityFactory.GetInstance()` 仍能正常運作，且其內部的 CRM 服務未被釋放。
   * **預期失敗原因**：修正前呼叫 Controller 的 `Dispose` 會導致後續呼叫 `ToolUtility` 時拋出 `ObjectDisposedException`。

4. **`DownloadListManager_ShouldNotLeakPassedServiceToSharedToolUtility`**
   * **測試目的**：驗證傳入 `DownloadListManager.GetListManager` 的 `IOrganizationService` 實例，在方法執行完畢後，不會被保留在 `ToolUtilityFactory.GetInstance()` 的任何欄位中。
   * **預期失敗原因**：修正前傳入的短生命週期連線會被寫入全域單例，導致連線被非法保留。

---

### 應明確拒絕的建議 (Explicitly Rejected Proposals)

* **❌ 拒絕「在每次 Controller 結束後呼叫 `ToolUtilityFactory.ResetInstance()`」**：
  * *理由*：這會導致並行請求（Concurrent Requests）互相干擾。當請求 A 的 Controller 結束並重設單例時，正在執行中的請求 B 會突然失去連線，造成嚴重的 Race Condition。
* **❌ 拒絕「將 `IOrganizationService` 改為靜態或全域快取變數」**：
  * *理由*：違反「不可引入可跨使用者、跨組織或跨請求保留的可變 CRM service」之限制，會導致多租戶資料交叉污染與連線逾時崩潰。
* **❌ 拒絕「在前端 DisplayErrorView 中透過 JavaScript 解析並隱藏敏感錯誤」**：
  * *理由*：敏感資訊一旦傳輸至瀏覽器，即屬資安洩漏，在前端進行隱藏毫無防禦效果。必須在伺服器端進行錯誤遮蔽。
