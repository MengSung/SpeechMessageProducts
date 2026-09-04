```
VALIDATION REPORT
=================
User Experience: 14/20 - 金流與奉獻收據的大寫金額轉換出現重大錯誤（數字 5, 6, 8 轉成「壹」，數字 3 轉成「?」，單位百與億轉成「壹」），影響使用者收據顯示與對帳體驗。
Visual Consistency: 18/20 - UI 佈局、DevExtreme 靜態資源排除與快取標頭設計良好，未破壞前端視覺。
Accessibility: 18/20 - 符合標準 Razor/DevExtreme 語意結構與鍵盤/視圖運作。
Performance: 19/20 - 大幅優化建置速度、過濾器單例化、反射快取、Kestrel 記憶體上界、Brotli 動態壓縮與 IHttpClientFactory 連線池重用。
Browser Compatibility: 18/20 - 正確將動態頁面 (no-store, Vary: Cookie) 與靜態資源 (public, max-age=1year, immutable) 之 Cache-Control 標頭分流處理。

TOTAL SCORE: 87/100

ISSUES FOUND:
- [Critical] DonationPaymentProcessor.Utilities.cs: MoneyToChinese 亂碼修復時數字與單位對應嚴重損毀
- [Warning] ContextDictionary.cs: 靜態 _cleanupTimer 缺乏停用機制

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

# 審核報告：ChurchReport commit e5b7a0544 效能與生命週期審查

## 1. Summary (整體評估)

對 Commit `e5b7a0544` 相對於 HEAD^ 的變更進行全檔實測稽核：
在**架構效能、Session 隔離、記憶體防護與 HttpClient 重用**方面，Claude 完成了非常高品質的改善：
1. **Session 隔離與安全**：`SessionAttribute.cs` 成功移除了會造成跨使用者 Session 洩漏的實例欄位，改存於 `HttpContext.Session`；`AuthenticationController.LineLoginOAuth.cs` 將 Bearer Token 掛載於單次 `HttpRequestMessage` 標頭，徹底消除 `DefaultRequestHeaders` 造成的跨使用者權杖洩漏風險。
2. **效能與資源邊界**：`SessionValidationMiddleware` 與 `GlobalAuthorizationFilter` 大幅減少反射與 Session 同步阻塞 (`LoadAsync` 異步化)；Kestrel 補上 `RequestHeadersTimeout = 60s` 與 `MaxRequestBufferSize = 1MB` 記憶體硬上界；Brotli 改為 `Fastest` 消除動態壓縮瓶頸。
3. **重大回歸**：在 `DonationPaymentProcessor.Utilities.cs` 的 `MoneyToChinese` 中，將原有的亂碼修復時**誤將中文數字與單位替換錯亂**（例如 3 變成 `?`，5/6/8 變成 `壹`，百/億變成 `壹`），這屬於必須在交付前修正的 **Critical 級別功能回歸**。

---

## 2. 審查發現分類 (Critical / Warning / Info)

### 🔴 Critical (交付前必須修復)

#### 1. `MoneyToChinese` 數字與單位轉換邏輯嚴重損毀 (功能回歸)
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs` (第 170~267 行)
- **重現與理由**：
  在 commit `e5b7a0544` 修復亂碼時，`MoneyToChinese()` 內的 `switch` 映射與字串替換填入了錯誤的字元：
  1. **阿拉伯數字映射錯誤**（第 202-216 行）：
     - `"3" => "?"`（數字 3 被轉為 ASCII 問號 `?`）
     - `"5" => "壹"`（數字 5 被誤轉為 `壹` 而非 `伍`）
     - `"6" => "壹"`（數字 6 被誤轉為 `壹` 而非 `陸`）
     - `"8" => "壹"`（數字 8 被誤轉為 `壹` 而非 `捌`）
     - `"." => "壹"`（小數點被誤轉為 `壹`）
  2. **位數單位映射錯誤**（第 218-235 行）：
     - 第 6 位 (百)、第 10 位 (十萬)、第 12 位 (千萬/百萬)、第 14 位 (億/百) 皆被誤轉為 `"壹"`。
  3. **邊界與替換字串仍存有殘留亂碼**（第 172, 181, 241-265 行）：
     - `return "?嗅???"` (第 172/181 行應為 `"零圓整"`)
     - `Replace("?嗆", "??)` (第 241-256 行有多處未修復的亂碼取代規則)
     - `isNegative ? ("鞎? + result)` (第 265 行應為 `"負"`)
  **影響**：奉獻與金流開立大寫收據時，金額如 $500, $600, $800, $300, $500,000 會被轉成全錯的大寫字串（如 $500 變「壹佰壹圓」、$300 變「?佰圓」），此為重大財務功能回歸。

---

### 🟡 Warning (建議修正或明確接受風險)

#### 1. `ContextDictionary` 靜態 `_cleanupTimer` 生命週期隱憂
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs` (第 38-66 行)
- **理由**：
  `ContextDictionary` 宣告了 `private static readonly Timer _cleanupTimer` 並於 static ctor 初始化。雖然生產環境 Controller (`BaseChurchController`) 已改用 request-scoped 容器注入而不再存取 `ContextDictionary`，但若有單元測試或診斷邏輯意外觸發該類別，會啟動一個每 5 分鐘喚醒一次且**無法被 Stop/Dispose** 的長駐靜態 Timer。建議若生產環境確定不用，應將該靜態 Timer 徹底移除或封裝為可釋放託管服務。

#### 2. `LineUtilityClass` 註解/碼幹中殘存損毀字串
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs` (第 118, 869 行)
- **理由**：
  `LINE_MENU_PATH` 常數與 `GetLineSender` 註解中仍包含早期 UTF-8/Big5 轉碼遺留的損毀字串（如 `LINE_MENU_PATH = @"D:\Line ?詨\";`）。目前該常數無程式碼引用，但若未來重構啟用此選單路徑時，將導致 `DirectoryNotFoundException`。

---

### 🔵 Info (可安全採用的加速與隔離優化)

#### 1. `SessionValidationMiddleware` & `MiniAppDetectionMiddleware` 異步化與靜態路徑短路
- **檔案**：`SessionValidationMiddleware.cs`, `MiniAppDetectionMiddleware.cs`
- **證明依據**：
  `await context.Session.LoadAsync()` 消除原本在 ThreadPool 執行緒上執行 `GetAwaiter().GetResult()` 的同步阻塞，同時使用 `StaticRequestPathHelper.IsStaticAssetPath` 排除 `/css/`, `/js/`, `/lib/`, `/assets/`, `/images/`, `/fonts/` 靜態檔案，避免無謂的 User-Agent 解析與 Session 載入，高併發下可證明能大幅提升吞吐量且無 Session Leakage。

#### 2. `GlobalAuthorizationFilter` 反射掃描快取
- **檔案**：`GlobalAuthorizationFilter.cs`
- **證明依據**：
  採用 `ConcurrentDictionary<string, bool> AllowAnonymousCache` 以 `descriptor.Id` 快取 `[AllowAnonymous]` 屬性掃描結果。鍵數量嚴格受限於 Route/Action 總數（有界），值僅為純 boolean，不含任何 User/Session/Tenant 狀態，可安全消除每請求 2 次的反射陣列配置。

#### 3. `AuthenticationController.LineLoginOAuth` HttpClient 安全處置
- **檔案**：`AuthenticationController.LineLoginOAuth.cs`
- **證明依據**：
  改用 `IHttpClientFactory.CreateClient("LineLoginApi")` 重用 TCP 連線集區。授權標頭改用 `using var request = new HttpRequestMessage(...)` 並掛載於 `request.Headers.Authorization`，完全不修改 `httpClient.DefaultRequestHeaders`，徹底避免多請求間 Token 交叉覆寫。

#### 4. Kestrel 記憶體邊界與 Brotli 壓縮優化
- **檔案**：`Program.cs`, `Startup.cs`
- **證明依據**：
  - Kestrel `RequestHeadersTimeout = 60s` 防止 Slowloris 慢速連線攻擊。
  - Kestrel `MaxRequestBufferSize = 1MB` 建立明確背壓與記憶體上限。
  - Dynamic Response Compression 改為 `CompressionLevel.Fastest`（Brotli Quality 1），解決 `Optimal`（Quality 11）造成 CPU 飽和與回應延遲飆高問題。

---

## 3. Review Checklist 檢核回應

### Accessibility
- [x] Semantic HTML 結構維持完整。
- [x] ARIA 標籤與角色在 Razor 視圖與 DevExtreme 元件中維持既有設定。
- [x] 鍵盤導覽與 Focus 狀態未受後端效能調整影響。

### Design Consistency
- [x] 靜態資產排除 (`wwwroot` 排除 `*.bak` 與 `*.debug.js`) 不影響正式發布網頁之 CSS/JS 載入與 Token 設定。

### Code Quality
- [x] TypeScript / C# 型別完整。
- [x] `CheckSessionOutAttribute` 成功移除實例欄位 `SessionId`，改存於 `HttpContext.Session`，完全解決過濾器單例造成的跨使用者會話洩漏問題。

### Performance
- [x] 靜態檔案與動態 Responses 之 `Cache-Control` 標頭實現安全分流 (`no-store` 用於動態頁面，`public, max-age=31536000, immutable` 用於靜態資源)。
- [x] 無多餘 re-renders 或 ThreadPool 同步阻塞。

### Responsive
- [x] 無橫向捲軸或行動端跑版問題。

---

## 4. Suggestions (進一步安全加速建議)

1. **靜態路徑判定快取 (`StaticRequestPathHelper`)**：
   - 目前每次請求皆呼叫 `Path.Value` 進行多重前綴與副檔名比較。雖然運算輕量，但可在 `StaticRequestPathHelper` 內部對常見的靜態檔案 PathValue 加上 `ConcurrentDictionary<string, bool>`（限制最大容量如 2048 筆），達到 O(1) 字典查表加速。
2. **`MoneyToChinese` 單元測試覆蓋**：
   - 建議針對 `DonationPaymentProcessor.Utilities.MoneyToChinese` 補齊全範圍單元測試（包含 $0, $3, $5, $6, $8, $100, $500,000, $10,000,000 等各級金額與負數），確保未來修正亂碼時不會再度發生轉碼或字元對應錯亂。

---

## 5. Positive Notes (值得肯定之處)

1. **Session 隔離稽核徹底**：修正了 `CheckSessionOutAttribute` 中深藏多年的 Attribute 實例欄位問題，且 `LineLoginOAuth` 的 Token 處理無洩漏痕跡。
2. **建置與發佈瘦身明顯**：排除 123 MB 無用 `*.bak` / `*.debug.js` 檔案與 `refs/` 目錄，使 `dotnet publish` 時間大幅縮短約 37%。
3. **代碼註解與架構說明極其詳盡**：在 `BaseChurchController`、`GlobalAuthorizationFilter` 與 `SessionValidationMiddleware` 中寫入了非常清晰的生命週期與安全不變量說明，大幅降低未來維護者誤破壞安全機制的風險。

---

## 6. 驗證執行紀錄與命令結果

執行下列 commands 驗證現有 worktree head 狀態：
1. `git status` -> 確認目前分支為 `1.0.0.7.JsutComsumeClaude(SpeedUp).worktree`，HEAD 位於 `e5b7a0544`。
2. `git diff HEAD^..HEAD` -> 逐一審查 16 個受變更檔案之程式碼與變更內容。
3. `search_code` -> 搜尋 `AddHttpClient`、`StaticRequestPathHelper`、`ContextDictionary` 等關鍵字，確認 DI 註冊與生命週期無破點。

**結論**：除了 `MoneyToChinese` 的 Critical 亂碼修復功能回歸必須修正外，其餘效能與安全優化均具備明確依據且符合生產規範。
