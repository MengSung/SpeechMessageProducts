# 程式碼審核與效能／生命週期稽核報告 (Commit `e5b7a054`)

## 1. 摘要 (Summary)

本次對 worktree HEAD（commit `e5b7a054`）相對於 HEAD^ 的大規模效能優化與記憶體／Session 洩漏修復進行深入稽核。

**整體評估**：
Claude 在核心效能優化、跨使用者 Session 隔離、`IHttpClientFactory` 安全使用、記憶體邊界控制（Kestrel / MemoryCache / ArrayPool）以及靜態資源快取分流等層面表現極佳，多項關鍵修復成功消除了嚴重的歷史漏洞（如 `CheckSessionOutAttribute` 的靜態實例共享洩漏、`BaseChurchController.Dispose` 對單例 `ToolUtility` 的誤釋放等）。

然而，**在 `DonationPaymentProcessor.Utilities.cs` 的亂碼修復過程中，發生了嚴重的功能回歸（Functional Regression）**：`MoneyToChinese`（金額轉大寫中文）方法中的字元替換被錯誤填入 `?` 與重複的 `壹`，導致金額轉換功能完全毀損。

---

## 2. 存取性與 UI 設計審查 (Accessibility & Design System)

*註：本次變更主要為後端中介層、控制器基底、編譯選項與金流/驗證邏輯優化，不涉及前端 CSS/HTML 視圖結構變更。*

- **存取性 (Accessibility)**：靜態資源快取（P2）開啟了 `immutable` 與 `public, max-age=31536000`，有效消除頁面切換時 CSS/JS 的阻塞與重繪，提升 UI 回應速度。
- **設計一致性 (Design Consistency)**：`ThemeViewDataFilter` 改為 Singleton 註冊後，不再於每次請求重構主題資料，維護 UI 主題流暢性。

---

## 3. 稽核發現與分類 (Findings)

### 🔴 Critical (必須在交付前修正)

#### 1. `MoneyToChinese` 數字轉大寫中文演算法嚴重功能回歸
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs`（第 170 ~ 266 行）
- **重現理由與分析**：
  Claude 嘗試修復該檔案中歷史遺留的中文註解與字串亂碼時，盲目將無法識別的位元組替換為固定字元，導致 `MoneyToChinese` 轉換邏輯完全毀損：
  1. 第 208 行：`"3" => "?"` —— 數字 `3` 被直接轉換成字面問號 `?`。
  2. 第 210、211、213 行：`"5" => "壹"`, `"6" => "壹"`, `"8" => "壹"` —— 數字 `5`、`6`、`8` 全部被映射為 `壹`（正確應分別為 `伍`、`陸`、`捌`）。
  3. 第 220、221、223、227、231 行：`iTemp` 位數單位 `1`（分）、`2`（角）、`6`（佰）、`10`（佰）、`14`（佰）全部被覆寫為 `壹`。
  4. 第 241 ~ 256 行：`Replace` 清除連續零的邏輯中仍保留了大量無效的亂碼替換（如 `.Replace("?嗆", "??")`）。
  5. 第 172 行與第 262 行：預設與異常回傳字串仍為亂碼 `"?嗅???"` 而非 `"零圓整"`。
- **影響範圍**：
  當傳入奉獻金額（例如 `123456`）時，系統產生的收據或感謝訊息會出現 `?` 與大量錯誤的 `壹`，造成財務收據文字錯誤與客戶端信任度危機。

---

### 🟡 Warning (應修正或需明確接受風險)

#### 1. `ContextDictionary._cleanupTimer` 靜態計時器生命週期隱憂
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs`（第 50 ~ 67 行）
- **說明**：
  `ContextDictionary` 宣告了 `static readonly Timer _cleanupTimer`。目前生產環境控制器已改用 Request-Scoped `InMemoryDataContextSmallGroup`，此類別僅剩單元測試使用。
  **風險點**：雖然目前靜態建構函式在生產啟動時不會被執行，但若未來有工程師在啟動或背景服務中意外存取 `ContextDictionary`，將觸發 static ctor 並掛載一個每 5 分鐘喚醒一次且**永遠無法被 `Dispose`** 的長駐計時器。
- **建議**：將 `_cleanupTimer` 改為在容器管理或改用 `IMemoryCache` 內建過期機制機制，移除常駐 `Timer`。

#### 2. 單元測試專案 AssemblyName 命名不符導致 22 個既有測試失敗
- **檔案與行號**：
  `ChurchReport.MemberInfo.Tests`（多個測試檔案中反射呼叫 `Type.GetType(...)` 處）
- **說明**：
  `ChurchReport.MemberInfo.Tests` 中有 22 個測試案例使用 `Type.GetType("ChurchReport.Controllers.PaymentReturnController, ChurchReport")` 尋找控制器型別。因主專案 `.csproj` 的 `<AssemblyName>` 為 `SpeechMessageProducts.ChurchReport`，導致反射回傳 `null` 而測試失敗。
- **建議**：雖然此為 HEAD 變更前即存在的測試基底問題，但應儘速將測試反射字串修正為 `SpeechMessageProducts.ChurchReport` 或 `typeof(...).Assembly`。

---

### 🔵 Info (安全效能優化建議與注意事項)

#### 1. `Directory.Build.props` 集中建置設定驗證
- **檔案**：`Directory.Build.props`
- **稽核結果**：✅ **安全**。
  - `SatelliteResourceLanguages=en` 成功排除 14 個非英文框架附屬組件目錄，不影響應用程式內部的繁體中文內容與 `CultureInfo`。
  - `EnableNETAnalyzers=false` 關閉不影響決策的 Roslyn 分析器，減少編譯負載。
  - Release Optimization 旋鈕（`TieredPGO`, `TieredCompilation`）為 .NET 10 官方推薦組合。

#### 2. `AuthenticationController.LineLoginOAuth` HttpClient 與 Token 隔離驗證
- **檔案**：`AuthenticationController.LineLoginOAuth.cs`
- **稽核結果**：✅ **安全且完全無洩漏**。
  - 成功以 `IHttpClientFactory` 取代 `new HttpClient()` using 區塊，解決 socket TIME_WAIT 通訊埠耗盡。
  - 關鍵安全處理：存取權杖（AccessToken）改掛載於單次請求的 `HttpRequestMessage.Headers.Authorization`，**絕不寫入 `HttpClient.DefaultRequestHeaders`**，徹底消除了共用 Client 時的跨使用者身分洩漏。

#### 3. `SessionValidationMiddleware` 與 `GlobalAuthorizationFilter` 效能與隔離驗證
- **檔案**：`SessionValidationMiddleware.cs` / `GlobalAuthorizationFilter.cs` / `SessionAttribute.cs`
- **稽核結果**：✅ **安全**。
  - `SessionValidationMiddleware` 於讀取 Session 前加入 `await context.Session.LoadAsync()`，消除原本同步阻塞等待分散式快取的 ThreadPool 飢餓隱患。
  - `GlobalAuthorizationFilter` 以 `ActionDescriptor.Id` 快取 `[AllowAnonymous]` 判定結果（只存 bool，無任何 Request/User 狀態），大幅降低每請求反射開銷。
  - `SessionAttribute.cs` (CheckSessionOutAttribute)：成功移除危險的實例欄位 `String SessionId = ""`，改存於 `HttpContext.Session`，且將 `async void` 修正為 `override void`，消除了跨使用者 Session 覆寫漏洞與未處理解除例外造成行程崩潰的風險。

#### 4. Kestrel 記憶體上界保護驗證
- **檔案**：`Program.cs`
- **稽核結果**：✅ **安全**。
  - `RequestHeadersTimeout` 由 30 分鐘降至 60 秒，有效抵禦 Slowloris 連線佔用攻擊。
  - `MaxRequestBufferSize` 由 `null`（無上限）設為 `1MB`（1024*1024），建立背壓機制，防止惡意連線推送大資料累積於記憶體中。

#### 5. 可採用的進一步加速建議 (與無 Leakage 條件)
1. **CRM 查詢精準欄位過濾（取代 `ColumnSet(true)`）**：
   - *現狀*：部份 Dataverse 查詢（如 `ContactService` / `MemberInfoController`）仍使用 `ColumnSet(true)` 抓取實體所有欄位。
   - *加速方案*：明確列出所需欄位名稱（如 `new ColumnSet("fullname", "emailaddress1", ...)`），減少 Dataverse API 傳輸量與記憶體配置。
   - *無洩漏條件*：必須嚴格核對 View/Model 所使用的欄位，確保不會因為少載入欄位而引發 `NullReferenceException`。

---

## 4. 驗證記錄 (Verifiable Execution Results)

在本機 worktree 環境實際執行之驗證結果：

| 驗證項目 | 執行命令 / 方式 | 結果 | 備註 |
|---|---|---|---|
| **專案編譯** | `dotnet build -c Release` | **成功** (0 Error) | 警告 1 筆 (Line.Messaging CS1572) |
| **單元測試 (MemberInfo)** | `dotnet test ChurchReport.MemberInfo.Tests` | 316 通過, 22 失敗 | 失敗原因確定為舊有反射 AssemblyName 不符 (`ChurchReport`) |
| **單元測試 (Dataverse)** | `dotnet test ToolUtility.Dataverse.Tests` | 74 通過, 0 失敗 | 完全通過 |
| **單元測試 (Workflows)** | `dotnet test LineMessagingProcessor.Workflows.Tests` | 33 通過, 0 失敗 | 完全通過 |
| **單元測試 (Payments)** | `dotnet test SpeechMessage.Payments.Tests` | 55 通過, 0 失敗 | 完全通過 |
| **發佈產物驗證** | `dotnet publish -c Release` | 產物大小 149 MB | 無 `*.bak` (73MB) 與 `*.debug.js` (50MB)，無 `refs\` 目錄 |

---

## 5. 結論與建議行動 (Recommendation)

**結論：NEEDS_IMPROVEMENT（需要修正後方可交付）**

Claude 在系統級效能、記憶體洩漏與 Session 隔離上展現了極高的工程品質，但因亂碼修復導致 `MoneyToChinese` 產生致命功能回歸。

### 立即行動清單：
1. **修正 `DonationPaymentProcessor.Utilities.cs`**：還原 `MoneyToChinese` 演算法，補齊繁體中文大寫數字（`零壹貳參肆伍陸柒捌玖`）與單位（`分角圓拾佰仟萬億`），新增對應單元測試以防再次回歸。
2. **修正 `ChurchReport.MemberInfo.Tests`**：將測試中反射字串從 `"ChurchReport"` 替換為 `"SpeechMessageProducts.ChurchReport"`，使全站單元測試達到 100% 通過。
