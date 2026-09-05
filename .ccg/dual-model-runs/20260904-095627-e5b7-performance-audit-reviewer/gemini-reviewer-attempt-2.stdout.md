對提交 `e5b7a0544`（相對於 `HEAD^`）的完整程式碼審查報告如下。本審查透過讀取實際 git diff 與相關原始碼檔案，方針著重於 Session Leakage、Memory Leakage、跨租戶隔離、靜態資源管理與功能回歸。

---

### 一、 摘要 (Summary)

本次提交在建置設定（`Directory.Build.props`、`csproj`）、Kestrel/ThreadPool 參數調優、中間件非同步 I/O 化（`SessionValidationMiddleware`）以及 `SessionAttribute` 併發漏洞修復上皆表現優異。
**但是，在修復亂碼的過程中，`DonationPaymentProcessor.Utilities.cs` 中的 `MoneyToChinese` 金額轉中文大寫演算法遭受嚴重的功能回歸（Functional Regression）**。大量數字與單位映射被錯誤改寫為 `"壹"` 與 `"?"`，導致金額轉大寫結果完全錯誤。

---

### 二、 審查發現分類 (Findings Classification)

#### 🔴 Critical (必須在任何交付前修正)

##### 1. `MoneyToChinese` 大寫金額轉換邏輯嚴重毀損與回歸
- **檔案**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs`
- **行號**：第 170-266 行 (特別是 202-235 行及 241-262 行)
- **問題說明與可重現理由**：
  在 commit `e5b7a0544` 中，原本旨在修復註解與字串亂碼的變更，錯誤地將 `MoneyToChinese` 數字與單位的 switch 映射表覆蓋為重複的 `"壹"` 與 `"?"`。
  **程式碼截圖與事實**：
  ```csharp
  // 行 202-210
  "3" => "?",
  "5" => "壹",
  "6" => "壹",
  "8" => "壹",

  // 行 218-235 (小數點與單位權位 mapping)
  1 => "壹", // 分
  2 => "壹", // 角
  6 => "壹", // 佰
  10 => "壹",// 佰
  12 => "壹",// 佰
  14 => "壹",// 佰
  ```
  並且在小數處理與替換邏輯中：
  ```csharp
  // 行 241-256
  .Replace("?嗆", "??)
  
  // 行 172
  if (string.IsNullOrWhiteSpace(lowerMoney)) return "?嗅???";
  ```
  **影響結果**：
  當傳入任何捐款金額（例如 `123456789.12`），`MoneyToChinese` 會輸出包含問號 `?` 及連續重複 `壹` 的無效中文字串，嚴重影響奉獻金收據、銀行報表與支付對帳單的產出品質。
- **修復建議**：
  請將 `MoneyToChinese` 恢復為標準的中文大寫金額轉換演算法（0-9 對應 零壹貳參肆伍陸柒捌玖，位數對應 分角圓拾佰仟萬億整），並確認原始檔儲存格式為 UTF-8 with BOM 以避免註解/字串編碼毀損。

---

#### 🟡 Warning (應修正或需明確接受風險的問題)

##### 1. `ContextDictionary.cs` 靜態 `Timer` 缺乏明確的 Dispose/Stop 機制
- **檔案**：`SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs`
- **行號**：第 20-30 行
- **問題說明**：
  在 `ContextDictionary` 的靜態建構子中初始化了 `_cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5))`。雖然目前生產線 Controller 已經棄用此類別，但若此類別在專案任何生命週期中被讀取，該靜態 Timer 將持續在背景運作且無法被釋放。
- **修復建議**：
  若該類別已過時（[Obsolete]），建議改用框架託管的 `IHostedService` 或清理時手動觸發，避免保留無界限的背景 Timer。

---

#### 🟢 Info (可安全採用的額外效能改善建議與條件)

##### 1. `ChurchReport.MemberInfo.Tests` 測試專案組件名稱反射錯誤修復
- **檔案**：`ChurchReport.MemberInfo.Tests` 中的各個 Controller/Service 測試檔案
- **說明**：
  測試套件中有 22 個測試項目失敗，原因為測試基底使用了 `Type.GetType("ChurchReport.Controllers.PaymentReturnController, ChurchReport")`，但主專案組件名稱已更改為 `SpeechMessageProducts.ChurchReport`。
- **建議**：
  將測試程式碼中的硬編碼組件名稱改為 `typeof(PaymentReturnController).AssemblyQualifiedName`，可使 22 個單元測試全數通過，達成 100% 測試通過率。

##### 2. CRM 查詢欄位集（ColumnSet）由全欄位改為精確欄位限制
- **條件與洩漏防範**：
  在 `BaseChurchController` 與 `ContactService` 中，部分 CRM FetchXml/QueryExpression 使用了 `new ColumnSet(true)` 抓取所有實體屬性。
- **加速建議**：
  在確定 API 回應或頁面需要的欄位後，改為明確列出欄位名稱（例如 `new ColumnSet("contactid", "fullname", "emailaddress1")`）。這能降低 CRM SQL 查詢負擔與網路傳輸大小，但必須確定沒有動態視圖依賴未擷取的欄位（避免 `KeyNotFoundException`）。

---

### 三、 無障礙 (Accessibility) 與 介面設計 (Design Consistency) 檢視

- **Semantic HTML & ARIA**：本提交變更主要集中於 Backend / Infrastructure 效能調優，未修改前端 Razor View / HTML structure。
- **靜態資源優化影響**：`csproj` 排除 `*.bak` (73MB) 與 `*.debug.js` (50MB) 的 Content 打包，已驗證視圖 (Views) 與 JS 檔案並未引用 `.debug.js`，對前端 UI 功能與載入速度無負面影響。

---

### 四、 亮點與肯定 (Positive Notes)

1. **Session 安全隔離與防修復**：
   - 修正了 `CheckSessionOutAttribute` (`SessionAttribute.cs`) 靜態/實體欄位 `String SessionId = ""` 被跨使用者共享的重大漏洞！改為將 Baseline SessionId 儲存在各自使用者的 `HttpContext.Session` 中，完全防止跨租戶與跨使用者 Session 覆蓋。
   - 將 `async void OnActionExecuting` 改為 `override void OnActionExecuting`，解決了未存獲例外會直接導致整體 Application Process Crash 的隱患。
2. **Kestrel 與內存保護**：
   - 在 `Program.cs` 中將 `RequestHeadersTimeout` 從 30 分鐘修飾為 60 秒，有效防範 Slowloris DDoS 攻擊。
   - 設定 `MaxRequestBufferSize = 1024 * 1024` (1MB)，防止巨大請求佔用無限記憶體。
3. **無狀態 Filter 最佳化**：
   - `GlobalAuthorizationFilter` 將 `[AllowAnonymous]` 反射結果依照 `ActionDescriptor.Id` 進行 `ConcurrentDictionary` 快取，且 Filter 註冊為 Singleton，完全無使用者狀態殘留，有效減少反射 CPU 開銷。
   - `StrictNoCacheFilter` 提供靜態單例 `Instance` 減少垃圾回收負擔。
4. **Build 與輸出打包大幅瘦身**：
   - `Directory.Build.props` 設定 `SatelliteResourceLanguages=en` 成功剔除 14 個非必要語系資料夾。
   - 產物總大小由 300MB 降至 149MB (-50%)，建置時間由 12.07s 降至 7.59s (-37%)。

---

### 五、 結論與建議處置 (Recommendation)

- **審查結論**：**NEEDS_IMPROVEMENT** (因存在 Critical 級別的 `MoneyToChinese` 金額演算法損壞功能回歸)。
- **行動方案**：
  1. 請立即修復 `DonationPaymentProcessor.Utilities.cs` 中的 `MoneyToChinese` 映射表與字串替換邏輯。
  2. 針對 `MoneyToChinese` 補上單元測試（包含小數、零元、萬位億位跨度測試）。
  3. 完成修正後重新驗證建置與測試。
