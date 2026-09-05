# LINE 奉獻收費清單與登入流程審查報告

本審查方針對目前 working tree 與近期修復相對於 `HEAD` 的變更進行全方位的 UI / Backend / 安全性與記憶體資源洩漏檢驗。

---

## 審查總結 (Executive Summary)

* **整體評估**：**PASS / 通過**
* **Critical 風險**：**無 Critical**
* **重點核對結果**：
  1. **LINE LIFF 登入資料流**：LIFF ID Token 驗證完整實作，包含 `iss` (https://access.line.me)、`sub` (LINE User ID)、`aud` (Channel ID) 與 `exp` Unix 時間戳記檢查，驗證成功後正確綁定 CRM Contact 與 ASP.NET Session。
  2. **Null Model 防禦與 Session 隔離**：`DedicationAuditController` 與 `DedicationController` 均已加入全路徑 null 防禦機制 (`BuildAuditWebFormModel` 與 `EnsureAuditFormModel`)，無身分/未登入時決定性清空敏感個資與奉獻明細，絕不殘留或跨 Session 洩漏資料。
  3. **記憶體與資源洩漏 (Resource Lifecycle)**：`VerifyLineIdTokenAsync` 採用 `IHttpClientFactory` 並透過 `using` 確切處置 `FormUrlEncodedContent` 與 `HttpResponseMessage`；包裝 Controller 轉址均有適當處置，無背景 Task / Timer / EventHandler 懸空洩漏。
  4. **前端保護規範**：`DediationLineLoginView.cshtml` 在後端 `status != 1` 時停止運作並關閉 Loading Panel 且不執行轉址；console 日誌已遮蔽 ID Token (`[REDACTED]`)，防止敏感憑證外洩。
  5. **專案標準**：代碼與 View 檔案均符合 UTF-8 without BOM、CRLF 換行規範與正體中文註解。

---

## 核心要點逐項審查 (Requirement Verification)

### 1. LINE LIFF 登入到奉獻收費清單資料流
- **ID Token 驗證邏輯** (`DedicationController.cs` Line 1013–1076):
  - 透過 `IHttpClientFactory.CreateClient("LineLoginApi")` 發送 POST 請求至 LINE 官方驗證端點 `https://api.line.me/oauth2/v2.1/verify`。
  - 完整校驗 4 項關鍵 Claim：`Issuer` (`https://access.line.me`)、`Subject` (`expectedUserId`)、`Audience` (`channelId`) 以及 `ExpiresAt > CurrentUnixTimestamp`。
  - HTTP 請求與回應資源 (`FormUrlEncodedContent`, `HttpResponseMessage`) 皆使用 `using var` 宣告，確保資源確定性處置 (Deterministic Disposal)。
- **Session 與身分綁定** (`DedicationController.cs` Line 972–989):
  - 驗證成功後將 `LineUserId` 寫入 Session (`DonationPaymentSessionKeys.LineUserId`)，並移除 Web 登入之舊 Session，防止身分邊界混淆。
  - 成功從 CRM 查詢 Contact 後始賦值 `m_Contact` 與 `m_LoginContact`，失敗時呼叫 `ClearLineDonationState` 徹底重置清空。

### 2. Null 防禦、崩潰防護與個資隔離
- **稽核頁 Web 入口當機防禦** (`DedicationAuditController.cs` Line 84–96, Line 143–165):
  - 由 Layout 導覽選單進入 `AuditViewWeb` 時，如當前 Request 無登入 Contact (`m_LoginContact == null`)，會呼叫 `BuildAuditWebFormModel()` 自動走 Safe Fallback 流程。
  - 顯式清空 8 大欄位：`FullName` (`""`)、`Mobile` (`""`)、`DedicationNumber` (`""`)、`NationId` (`""`)、`LastSixDigit` (`""`)、`DedicationFeeList` (`.Clear()`)、`SameNameList` (`.Clear()`)、`TotalAmount` (`0`)。避免上一使用者或跨 Session 舊資料殘載。
- **DataGrid / AJAX 入口保護** (`DedicationAuditController.cs` Line 175–180, Line 253, Line 283):
  - `EnsureAuditFormModel` 保證 `m_DonationPaymentFormModel` 及內部 List (`DedicationFeeList`, `SameNameList`) 恆不為 `null`，避免 AJAX 請求發起時觸發 `NullReferenceException` (HTTP 500)。

### 3. 記憶體與資源生命週期 (Resource & Memory Lifecycle)
- **HttpClient 使用**：未實例化長壽命或未處置的 `HttpClient`，統一由容器 `IHttpClientFactory` 管理連線池。
- **Response & Content dispose**：JSON 序列化與 Response Content 均在 async scope 內即時釋放。
- **Controller 轉址包裝** (`HomeController.cs` Line 222–231):
  - `SetupUserLineIdRedirect` 使用 `using (var dedicationController = ...)` 包裝轉址控制器實例，確保 Request 結束時適當 dispose。
- **無背景懸空 Task / Timer**：未建立背景常駐 Timer 或 CancellationTokenSource 遺失處理。

### 4. 前端防護驗證 (`DediationLineLoginView.cshtml`)
- **非成功 status 控制** (Line 536–552):
  - 當後端回應 `data.status !== "1"` 時，呼叫 `getLoadPanelInstance().hide()` 關閉載入動畫，呈現錯誤提示文字與 Toast，且**絕對不發起轉址 (`window.location.href`)**。
- **ID Token 安全性** (Line 496–502, Line 522):
  - AJAX 請求前的日誌紀錄 `requestLogData` 顯式宣告 `IdToken: "[REDACTED]"`，瀏覽器 Console 不會印出明文 ID Token。

### 5. 代碼規範與維護性
- 檔案編碼均維護 **UTF-8 without BOM** 與 **CRLF** 斷行。
- 註解與 XML 文件均為標準正體中文。
- 單元測試 (`DonationPaymentViewDefaultsTests.cs`) 已使用 `[assembly: InternalsVisibleTo]` 進行測試暴露，避免脆弱的私有字串反射呼叫。

---

## 審查發現與分類 (Findings)

### 🔴 Critical
**無 Critical**

---

### 🟡 Warning

#### 1. `SetupAuditViewBag` 在 LINE 入口分支存取 `m_DonationPaymentFormModel` 之 null 安全導覽已修復，但需注意相關 View 相依性
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 124)
- **原因**：Line 124 已套用 safe-navigation 運算子 `InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.IsAOfficeWorker == true`，防止 null 崩潰。
- **建議**：維持此防禦性語法，確保 LINE View 渲染時不會因 Model 預設值調整而引發 View 視圖層級例外。

---

### 🔵 Info

#### 1. 舊有檔案標頭包含 Big5 / 歷史轉碼亂碼註解
- **檔案**：
  - `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs` (Line 1–13)
  - `SpeechMessageProducts.ChurchReport/Views/Dedication/DediationLineLoginView.cshtml` (Line 19, 143, 165 等)
- **原因**：檔案頂部與部分 HTML 註解為過去歷史遺留之 Big5 轉碼字元 (如 `AI-蝜?銝剜?瑼?` / `憟賜鈭?`)。
- **說明**：此為歷史遺留問題，不影響 C# 編譯與 HTML 瀏覽器渲染。新撰寫之程式碼與 XML Doc 均為乾淨正體中文。建議於後續例行維護時統一清理。

---

## 審查結論 (Decision)

**審查結果：PASS (無 Critical 缺陷)**

目前的變更完全符合 LINE LIFF 登入驗證規範、個資隔離原則、null 邊界保護以及前端憑證安全要求，可安全進行合併與部署。
