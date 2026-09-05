# LINE 奉獻收費清單登入與 HttpClient 生命週期審查報告

對 `HEAD` 相對於目前工作樹（含 `Startup.cs` `LineLoginApi` 註冊、`DedicationController` 的 `VerifyLineIdTokenAsync` LIFF ID Token 驗證流程、`DedicationAuditController` null 防衛機制、`DonationDedicationFeeFormService` Session 隔離與單元測試）進行整體程式碼審查。

---

## 綜合審查結論 (Decision)

**審查結果：PASS（審查通過，包含建議優化項目）**

變更已完整解決從 Layout 選單進入奉獻稽核頁面時可能產生的 `ArgumentNullException`，並實現了健全的 named `HttpClient` 生命週期管理、Fail-Closed 的跨 Session/跨使用者個資防禦機制，且無資源或 Socket 洩漏疑慮。

---

## 審查要點驗證與分析

### 1. `Startup.cs` 之 `LineLoginApi` Named HttpClient 註冊與生命週期
- **Timeout 邊界與 BaseAddress 註冊**：`Startup.cs` (Line 227-231) 使用 `services.AddHttpClient("LineLoginApi", client => { client.BaseAddress = new Uri("https://api.line.me/"); client.Timeout = TimeSpan.FromSeconds(10); });` 正確註冊，且設有明確有界的 10 秒 Timeout 上界。
- **與 `IHttpClientFactory` 使用方式一致**：在 `DedicationController.cs` (Line 1063) 每次以 `factory.CreateClient("LineLoginApi")` 產生 transient `HttpClient` 實例，未將其儲存於全域靜態或長生命週期物件中。
- **標頭與隔離**：`VerifyLineIdTokenAsync` 透過 `FormUrlEncodedContent` 以 POST 攜帶 `id_token` 與 `client_id`，完全未存取或修改 `DefaultRequestHeaders`，徹底避免多 Request/多執行緒間標頭覆寫或 Token 污染風險。

### 2. LINE LIFF ID Token 驗證與資料隔離（跨使用者 / 跨 Session / 跨租戶）
- **Fail-Closed 嚴格 Token 驗證**：`VerifyLineIdTokenAsync` (Line 1040-1083) 嚴格校驗：
  1. `iss` 是否為 `https://access.line.me`
  2. `sub` 是否與目前請求之 `expectedUserId` 完全相符（防止攻擊者傳入其他人有效的 Token 綁定當前 Session）
  3. `aud` 是否匹配伺服器設定之 `ChannelId`（防止跨應用 App Token 重放）
  4. `exp` 到期時間。
- **隔離與個資徹底清空**：當驗證失敗或 CRM 查無對應 Contact 時，即呼叫 `ClearLineDonationState` (Line 533-575) 與 `ClearModelForMissingContact`，將 `m_Contact`、`m_LoginContact`、身分欄位 (`FullName` `Mobile` `NationId` `LastSixDigit` 等)、`DedicationFeeList` 與 `SameNameList` 完整清空重置，並清除 Session 中的 `LineUserId` 與 `WebLoginContactId`。

### 3. 資源生命週期與洩漏防護
- **Socket 與 Connection 集區**：由 `IHttpClientFactory` 管理底层 `HttpMessageHandler` 生命週期與 TCP Socket 重用，消除傳統 `new HttpClient()` 的 Socket 耗盡問題。
- **Stream / Response 確定性釋放**：`using var content = ...` 與 `using var response = ...` 確保 HTTP 請求與回應串流在非同步方法結束時確定性 Dispose。
- **Cancellation 與 Task 控制**：非同步 API（如 `VerifyLineIdTokenAsync` 與 `SetupUserLineId`）皆有正確接收 `CancellationToken`，且非同步呼叫使用 `.ConfigureAwait(false)`，無 Deadlock 風險。

### 4. 控制器、服務、模型、Razor View 與測試相容性
- **`DedicationAuditController` Null 防衛**：`BuildAuditWebFormModel` 與 `EnsureAuditFormModel` 確保直接自 Layout 導覽或 DataGrid AJAX 進入時，`m_DonationPaymentFormModel` 均有安全初始值，徹底防禦崩潰。
- **單元測試有效性**：`DonationPaymentViewDefaultsTests.cs` 精準測試了預設模型建立與無 Login Contact 時的資料清空邏輯。

---

## 審查發現與修正建議 (Findings)

### 🔴 Critical Issues
- **無 (None)**：目前變更符合安全性與資源生命週期規範，無阻擋交付之 Critical 問題。

---

### 🟡 Warning Issues

#### 1. `VerifyLineIdTokenAsync` 傳入 `PostAsync` 之 URL 可優化為相對路徑
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs` (Line 1063-1065)
- **判斷依據**：
  在 `Startup.cs` 中已設定 `client.BaseAddress = new Uri("https://api.line.me/");`，但在 `DedicationController.cs` (Line 1064) 中使用完整絕對路徑：
  ```csharp
  using var response = await factory.CreateClient("LineLoginApi")
      .PostAsync("https://api.line.me/oauth2/v2.1/verify", content, cancellationToken)
      .ConfigureAwait(false);
  ```
  雖然 .NET `HttpClient` 在傳入絕對 URI 時能自動解析並覆寫 `BaseAddress`，但傳入相對路徑更符合 named client 的 `BaseAddress` 設定原則與程式碼一致性。
- **修正建議**：
  建議將 POST 目標改為相對路徑：
  ```csharp
  using var response = await factory.CreateClient("LineLoginApi")
      .PostAsync("oauth2/v2.1/verify", content, cancellationToken)
      .ConfigureAwait(false);
  ```

#### 2. `SetupAuditViewBag` 在 LINE 入口分支存取 `m_DonationPaymentFormModel` 缺乏安全 Null 導向
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 112)
- **判斷依據**：
  ```csharp
  ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker ? "是的" : "否";
  ```
  在 `SetupAuditViewBag(false)` (走 LINE 視圖 `DedicationFeeAuditViewLine`) 路徑中，若此時 `m_DonationPaymentFormModel` 尚未被初始化（為 `null`），將在此處拋出 `NullReferenceException`。
- **修正建議**：
  改用 null-safe 導向運算子：
  ```csharp
  ViewBag.IsAOfficeWorker = (InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.IsAOfficeWorker == true) ? "是的" : "否";
  ```

---

### 🔵 Info Issues

#### 1. `LoadDedicationFeeList` 可比照 `LoadSameNameList` 加入 Safe Navigation 防衝擊
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 200)
- **判斷依據**：
  `LoadSameNameList` (Line 230) 採用了 null 合併運算子防護 (`?? new List<SameNameElement>()`)，而 `LoadDedicationFeeList` (Line 200) 直接存取 `.DedicationFeeList`：
  ```csharp
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.DedicationFeeList;
  ```
  雖然前置的 `EnsureAuditFormModel` 已維護 non-null 模型，但比照 Line 230 補上 safe navigation (`?.`) 與 fallback 可提升程式碼防禦強度：
  ```csharp
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.DedicationFeeList
      ?? new System.Collections.Generic.List<DedicationFee>();
  ```

#### 2. 單元測試改用 `internal` 替代 private 反射呼叫
- **檔案與行號**：
  `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 240, Line 274)
- **判斷依據**：
  單元測試中使用了 `GetMethod("BuildAuditWebFormModel", BindingFlags.Instance | BindingFlags.NonPublic)` 反射呼叫私有方法。此寫法若未來 Controllers 重構或方法重命名，無法在編譯時期發現。
- **修正建議**：
  可將 `BuildAuditWebFormModel` 設為 `internal`，並在控制器專案中加入 `[assembly: InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]`，改以強型別直接呼叫。
