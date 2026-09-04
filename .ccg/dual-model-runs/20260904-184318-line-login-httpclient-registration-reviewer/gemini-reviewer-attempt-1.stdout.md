# LINE 奉獻收費清單登入與 HttpClient 生命週期審查報告

## 審查總結 (Executive Summary)

針對 HEAD 相對於目前變更（含 `Startup.cs` 的 `LineLoginApi` 註冊、`DedicationController` 的 `VerifyLineIdTokenAsync` LINE LIFF ID Token 驗證流程、`DonationPaymentManager` / `DonationDedicationFeeFormService` 的 Session 隔離與個資防禦、以及單元測試）進行整體審查。

審查結果確認：
1. **HttpClient 註冊與生命週期正確**：在 `Startup.cs` 中正確使用 `IHttpClientFactory` 註冊名為 `"LineLoginApi"` 的 named client，設有明確有界的 10 秒 Timeout。在 `DedicationController` 中每次由 `IHttpClientFactory.CreateClient("LineLoginApi")` 產生 transient 實例，且 `FormUrlEncodedContent` 與 `HttpResponseMessage` 均透過 `using var` 進行確定性釋放（deterministic dispose），完全無 socket、memory 或 stream leakage。
2. **跨使用者/跨 Session 資料隔離落實 Fail-Closed 原則**：LINE LIFF Token 驗證嚴格核對 `iss` (https://access.line.me)、`sub` (目前請求 User ID)、`aud` (Channel ID) 與 `exp` 到期時間。一旦驗證失敗或找不到對應 CRM Contact，即立即執行 `ClearLineDonationState` 清空所有敏感個資與 Session key。
3. **無資源洩漏風險**：呼叫點完整傳遞 `CancellationToken`，HTTP response body 亦被正確讀取處置，未發現常駐 Timer、懸空 Task 或未釋放 Socket。

---

## 審查發現 (Findings & Classifications)

### 🔴 Critical (必須修正才能交付的問題)
> **無 (None)**：經驗證，未發現會導致連線洩漏、跨租戶/跨使用者個資洩漏、死鎖或 HTTP 500 當機的 Critical 問題。

---

### 🟡 Warning (建議修正或需明確限制的風險)

#### 1. `VerifyLineIdTokenAsync` 中 `PostAsync` 使用絕對路徑忽略了 `BaseAddress`
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs` (第 1063 - 1064 行)
  `SpeechMessageProducts.ChurchReport/Startup.cs` (第 229 行)
- **程式碼片段**：
  ```csharp
  // Startup.cs (Line 229)
  client.BaseAddress = new Uri("https://api.line.me/");

  // DedicationController.cs (Line 1063-1064)
  using var response = await factory.CreateClient("LineLoginApi")
      .PostAsync("https://api.line.me/oauth2/v2.1/verify", content, cancellationToken)
      .ConfigureAwait(false);
  ```
- **判斷依據與影響**：
  在 `Startup.cs` 已將 `BaseAddress` 設定為 `https://api.line.me/`。但在 `DedicationController.cs` 呼叫 `PostAsync` 時傳入包含了完整 Domain 的絕對 URL `https://api.line.me/oauth2/v2.1/verify`。根據 `HttpClient` 規格，傳入絕對 URI 時會覆寫並無視 `BaseAddress`。雖然功能正常，但失去了在 `Startup` 統一管理與轉發端點（如代理伺服器或測試網域）的彈性。
- **改善建議**：
  將 `PostAsync` 的路徑改為相對路徑 `"oauth2/v2.1/verify"`：
  ```csharp
  using var response = await factory.CreateClient("LineLoginApi")
      .PostAsync("oauth2/v2.1/verify", content, cancellationToken)
      .ConfigureAwait(false);
  ```

---

### 🔵 Info (觀察與可選優化)

#### 1. LINE ID Token 驗證流程與 Session 安全隔離驗證通過
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs` (第 967-1083 行)
  `SpeechMessageProducts.ChurchReport/Payments/DonationPaymentSessionKeys.cs`
- **判斷依據**：
  - **安全性核對**：`VerifyLineIdTokenAsync` 完整驗證了 LINE 官方規定的四項標準欄位 (`iss` == `"https://access.line.me"`、`sub` == `expectedUserId`、`aud` == `channelId`、`exp` > 當前 Unix 時間)，防止了重放攻擊與跨 Channel 偽造 User ID 的情境。
  - **個資清空**：`ClearLineDonationState` 清空了 `m_Contact`、`m_LoginContact`、`FullName`、`Mobile`、`DedicationNumber`、`NationId`、`LastSixDigit`，並將 `DedicationFeeList` 與 `SameNameList` 執行 `Clear()`。同時移除 `Session[WebLoginContactId]` 與 `Session[LineUserId]`，徹底阻止跨請求個資殘留。

#### 2. HttpClient 生命週期與記憶體管理驗證通過
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Startup.cs` (第 227-231 行)
  `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (第 340 行)
- **判斷依據**：
  - `Startup.cs` 使用 `AddHttpClient("LineLoginApi")` 註冊，並設定 `Timeout = TimeSpan.FromSeconds(10)`。
  - `DedicationController.cs` 未將 `HttpClient` 保存為 `static` 或成員變數，亦未修改 `DefaultRequestHeaders`（避免多請求同時存取觸發 Race Condition 或 Token 蓋寫）。
  - 所有 HTTP 請求與回應物件（`FormUrlEncodedContent` 與 `HttpResponseMessage`）均以 `using var` 控制作用域，無底層 Socket Exhaustion 或 Memory Leak。

---

## 結論 (Decision)

**審查結論：PASS (建議包含 Warning 建議進行微調)**

目前 HEAD 與 working tree 的程式碼在安全性、連線池生命週期、跨 Session 隔離與測試防禦上均符合高標準要求。Warning 項屬於維護性建議，不影響功能正確性。
