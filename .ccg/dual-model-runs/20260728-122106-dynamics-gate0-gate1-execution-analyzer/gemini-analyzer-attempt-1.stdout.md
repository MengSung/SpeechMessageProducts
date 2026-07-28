# Dynamics 365 Gate 0 / Gate 1 執行分析報告

本報告針對現有高風險任務 `.trellis/tasks/07-23-dynamics-connection-compatibility` 的下一步執行步驟進行 UI/UX、設計系統與前端/後端架構評估。本分析為純唯讀評估，未修改任何儲存庫檔案或遠端系統。

---

## 1. UX Analysis (使用者影響評估)

- **使用者體驗影響**：此變更屬於後端架構重構（移除舊有 Dynamics SDK 並改用 direct Web API），對終端使用者（如 LINE 訂閱用戶或 ChurchReport 操作員）在正常情況下應為無感。
- **使用者旅程影響**：若驗證機制或連線池發生故障，將導致 ChurchReport 無法讀取奉獻數據，進而中斷操作員查詢奉獻記錄的旅程。因此，驗證機制的穩定性直接關係到核心業務流程的可用性。
- **行動端與桌面端體驗**：無直接 UI 影響，但後端 API 的延遲與連線池效率會間接影響前端頁面的載入速度與響應時間。

---

## 2. Design System Evaluation (設計系統評估)

- **一致性與模式**：新設計採用 `Gateway`（共用服務）與 `Embedded`（進程內適配器）雙主機模式，透過 JSON 設定檔進行切換，與現有的配置驅動模式保持一致。
- **組件重用性**：`AdfsOAuthTokenProvider` 作為獨立的權杖提供者，被 `DynamicsWebApiClient` 重用，符合單一職責原則。
- **Token 與主題使用**：此處涉及安全 Token（OAuth Access Token）的生命週期管理。設計中包含了記憶體快取與單飛重新整理（Single-flight refresh）機制，以防止並發請求導致重複獲取 Token，這符合安全與效能設計規範。

---

## 3. Technical Considerations (技術考量)

- **組件結構影響**：`AdfsOAuthTokenProvider` 負責權杖獲取與快取，但目前實作僅支援 `refresh_token` 與 `password` 授權，尚未實作 `client_credentials` 流程。
- **狀態管理與快取**：權杖快取於記憶體中（`_cachedToken`），並使用 `SemaphoreSlim` 進行同步鎖定。此設計無每使用者或每工作階段的狀態儲存，符合無狀態設計要求。
- **效能與 Socket 耗盡風險**：
  - 在 `AdfsOAuthTokenProvider.cs` 中，若未注入 `IHttpClientFactory`，系統會自行建立 `SocketsHttpHandler` 與 `HttpClient` 並在 `finally` 區塊中處置。
  - **風險**：在高並發場景下，頻繁建立與處置 `HttpClient` 會導致大量 Socket 處於 `TIME_WAIT` 狀態，進而引發 Socket 耗盡。
- **測試考量**：由於沙盒環境的安全性隔離限制，代理人進程無法獲取 TLS 用戶端憑證上下文，導致 HTTPS 連線失敗。這意味著自動化 CI/CD 無法執行 live smoke 測試，必須依賴操作員在具備適當 Windows 認證的環境中手動執行。

---

## 4. Options (替代方案與權衡)

| 方案 | 優點 | 缺點 / 阻礙 | 決策 |
| --- | --- | --- | --- |
| **A. 繼續嘗試在 ADFS 上啟用 `client_credentials`** | 符合非使用者、無重新整理權杖持續性的安全要求。 | Dynamics 365 On-Premises (IFD) 官方不支援此流程，需要極高風險的 ADFS 與 CRM 內部配置修改，違反「不破壞現有信賴憑證者信任」的限制。 | 拒絕 |
| **B. 使用 ADFS `authorization_code` 流程並持久化 `refresh_token`** | 這是 Dynamics 365 On-Premises (IFD) 官方支援的 OAuth 流程。 | 需要操作員進行一次性互動式登入以獲取初始 `refresh_token`，且需要安全的持久化儲存（如 `LocalDevTokenStore`），存在權杖過期或洩漏風險。 | 暫緩 |
| **C. 停止並返回架構選擇** | 避免在不支援的技術路徑上浪費資源，確保系統安全性與穩定性。 | 需要重新評估 Dynamics 365 的整合方式（例如是否保留部分 SOAP 接口，或改用 Windows 驗證）。 | **推薦** |

---

## 5. Recommendation (推薦方案)

**執行方案 C (停止並返回架構選擇)**。
由於 CE 9.1 IFD 目標無法在不進行破壞性修改的情況下支援 `client_credentials` 流程，且目前 ADFS 停用了 password grant，繼續執行 Gate 0/1 將面臨無法逾越的技術阻礙。應立即觸發停止條件，重新評估架構。

---

## 6. Verdict (判定)

### **VERDICT: FAIL**

**判定理由**：
1. **觸發停止條件**：目標環境（CE 9.1 IFD）無法支援所需的非使用者、非重新整理權杖持續性流程（`client_credentials`）。
2. **驗證阻礙**：沙盒環境存在 TLS 連線阻礙，無法在代理人端完成自動化驗證。

---

## 7. Findings (具體發現)

### **Critical Findings**

1. **ADFS/CRM `client_credentials` 不相容性**
   - **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-tier-a-ifd-auth-blocker.md`
   - **說明**：Dynamics 365 On-Premises (IFD) 官方不支援 `client_credentials` 流程（S2S 驗證）。ADFS 發行的 client credentials token 無法對應到 CRM 內部的 SystemUser，會導致 HTTP 401 Unauthorized 錯誤。
2. **Password Grant 停用與 ClientId 未註冊**
   - **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-tier-a-ifd-auth-blocker.md`
   - **說明**：ADFS 伺服器已停用 password grant（回傳 `unsupported_grant_type`），且臨時的 ClientId 未在 ADFS 上註冊，導致 `authorization_code` 流程無法完成。

### **Warning Findings**

1. **沙盒環境 TLS 阻礙**
   - **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-live-smoke-attempt.md`
   - **說明**：代理人進程在沙盒環境（`codexsandboxoffline`）中執行時，因缺乏憑證上下文，HTTPS 連線到 `jesus.speechmessage.com.tw` 會發生 `SEC_E_NO_CREDENTIALS (0x8009030e)` 錯誤。
2. **Socket 耗盡風險**
   - **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 364-386 行)
   - **說明**：在未注入 `IHttpClientFactory` 的情況下，每次請求新 Token 都會建立並處置新的 `HttpClient` 與 `SocketsHttpHandler`。在高並發下，這會導致大量 Socket 殘留在 `TIME_WAIT` 狀態，引發 Socket 耗盡風險。

### **Info Findings**

1. **產品流量隔離**
   - **檔案路徑**：`SpeechMessage.Dynamics.Gateway/Program.cs`
   - **說明**：`Package01FeeReadsEnabled` 目前保持為 `false`，確保在所有驗證閘門通過前，不會有實際的產品流量切換至新路由。
