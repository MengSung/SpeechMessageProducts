# Sunnyvalechback Dynamics 365 9.1 ID3242 整合與驗證分析報告

本報告針對 ChurchReport 系統在切換 Dynamics 組織至 `sunnyvalechback` (CE 9.1 IFD) 後，發生 `ID3242: 無法驗證或授權此安全性權杖` 錯誤進行深度分析，並提出相應的修復與驗證方案。

---

## 1. UX Analysis (使用者影響評估)

- **使用者體驗影響**：
  - **登入功能完全癱瘓**：由於後端 CRM 連線無法建立，所有網頁會員（如帳號 `zz`）在嘗試登入時，皆會看到「`驗證過程發生錯誤: ID3242: 無法驗證或授權此安全性權杖。`」的錯誤訊息，導致使用者無法進入系統。
  - **錯誤訊息混淆**：前端顯示的錯誤訊息為技術性的 ADFS 錯誤代碼（ID3242），一般使用者無法理解，且容易誤以為是自己的帳號密碼輸入錯誤，導致重複嘗試或向客服抱怨。
- **使用者旅程影響**：
  - 會員登入是系統的入口點。此故障阻斷了所有後續的使用者旅程（如奉獻查詢、個人資料修改等）。
- **行動端與桌面端體驗**：
  - 兩端皆受相同後端服務影響，呈現一致的阻斷性故障。

---

## 2. Design Evaluation (設計系統與一致性評估)

- **配置一致性**：
  - 系統目前存在配置漂移（Configuration Drift）。雖然 `CrmConnection` 的 `Organization` 和 `ServerUrl` 已切換至 `sunnyvalechback`，但 `DynamicsAccess` 區段仍殘留 `jesus` 8.2 的設定（如 `ProfileAlias`、`CeVersion`、`AuthorityUri` 等）。這違反了設計系統中「單一事實來源（Single Source of Truth）」與「環境配置一致性」的原則。
- **憑證管理安全性**：
  - 系統設計中明確禁止將 `CrmConnection:Password` 複製到 `DynamicsAccess` JSON 中，密碼僅能透過 `SecretReference` 或環境變數傳遞。此設計原則在本次調整中必須繼續嚴格遵守。

---

## 3. Technical Considerations (技術與架構考量)

- **組件架構影響**：
  - `ICrmConnectionPool` 作為單例（Singleton）註冊於 `Startup.cs`，負責管理與 CRM 的 SOAP 連線。
  - `CrmConnectionService.CreateOnPremiseClient` 內部使用 `PowerPlatform.Dataverse.Client.OnPremiseClient`，其建構子僅接收 `url`、`username`、`password`，並不接受獨立的 `domain` 參數。
- **狀態管理與效能**：
  - 由於 `CrmConnectionPool` 在啟動時會嘗試建立最小連線數（`MinPoolSize`），若配置錯誤，會在啟動期就不斷嘗試連線並失敗，雖然有 Semaphore 限制，但仍會造成不必要的資源浪費與日誌干擾。
- **測試考量**：
  - 必須區分「後端系統連線憑證」與「前端會員帳密」。測試時應先確保後端連線（WhoAmI）成功，再進行前端會員登入測試。

---

## 4. Options (替代方案評估)

### 方案 A：僅調整配置 (Configuration-only Fix) - **推薦**
- **作法**：修改 `appsettings.json`，將 `CrmConnection:Username` 從 `"SPEECHMESSAGE\\Administrator"` 修改為符合 9.1 環境的 `"DYNAMICS-365\\Administrator"` (或該環境接受的 UPN，如 `"Administrator@dynamics-365.local"`)。
- **優點**：無需修改程式碼，無編譯與部署風險，影響範圍最小且最安全。
- **缺點**：若未來再次切換環境，仍需手動確保 Username 中的網域前綴與 Domain 配置一致。

### 方案 B：修改程式碼自動組合 Domain 與 Username
- **作法**：修改 `Startup.cs` 或 `CrmConnectionPool`，在傳遞給 `CreateOnPremiseClient` 之前，若 `Domain` 有值且 `Username` 中不包含 `\`，則自動組合成 `Domain\Username`。
- **優點**：程式碼層面自動處理，減少配置出錯的機率。
- **缺點**：
  1. `OnPremiseClient` 的 `userName` 參數若已包含網域前綴（如 `SPEECHMESSAGE\Administrator`），程式碼需做複雜的解析與取代邏輯，容易引入新 Bug。
  2. 修改生產環境程式碼風險較高，且可能影響其他正常運作的環境。

---

## 5. Recommendation (建議方案與根因分析)

### 根因分析 (Root Cause Ranking)
1. **網域不匹配 (Primary Cause)**：`sunnyvalechback` (CE 9.1 IFD) 的 ADFS 網域為 `DYNAMICS-365`，而配置中寫死的 `CrmConnection:Username` 為 `"SPEECHMESSAGE\\Administrator"`。ADFS 無法驗證非該網域的安全性權杖，因而拋出 `ID3242`。
2. **參數未傳遞**：`Startup.cs` 在初始化 `CrmConnectionPool` 時，未將 `CrmConnection:Domain` 傳遞給連線客戶端，導致客戶端完全依賴 `Username` 中寫死的前綴。
3. **配置漂移 (Secondary Cause)**：`DynamicsAccess` 區段未同步更新至 9.1，雖然目前因 `Package01FeeReadsEnabled` 為 `false` 未直接報錯，但會阻礙後續的 Web API 驗證。

### 建議修復方案 (Recommended Minimal Fix)
1. **配置修正**：在 `appsettings.json` 中，將 `CrmConnection:Username` 修改為 `"DYNAMICS-365\\Administrator"` (或該環境接受的 UPN)。
2. **對齊 DynamicsAccess 設定**：同步更新 `DynamicsAccess` 區段以匹配 9.1 環境，但保持 `Package01FeeReadsEnabled` 為 `false`。

---

## 6. Findings (關鍵發現分類)

### 🔴 Critical (危急)
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json`
- **問題描述**：`CrmConnection:Username` 中的網域前綴 (`SPEECHMESSAGE`) 與 `sunnyvalechback` 9.1 環境的實際網域 (`DYNAMICS-365`) 不匹配，且 `Domain` 參數未被傳遞給 `OnPremiseClient`，導致 ADFS 驗證失敗 (ID3242)，系統登入功能完全癱瘓。
- **修復建議**：將 `CrmConnection:Username` 修改為 `"DYNAMICS-365\\Administrator"`。

### ⚠️ Warning (警告)
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json`
- **問題描述**：`DynamicsAccess` 區段仍殘留舊的 `jesus` 8.2 設定（如 `CeVersion: "8.2"`、`AuthorityUri` 指向舊 ADFS 等），這會導致未來啟用 `Package01FeeReadsEnabled` 時發生連線與路由錯誤。
- **修復建議**：將 `DynamicsAccess` 相關設定更新為 9.1 對應值（詳見驗證清單）。

### ℹ️ Info (提示)
- **檔案路徑**：`ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
- **問題描述**：`CrmConnectionPool` 在初始化時若連線失敗，僅在 Debug 模式下輸出日誌，缺乏 Fail-Fast 機制，導致配置錯誤只能在使用者嘗試登入時才被發現。
- **建議**：未來可考慮在啟動時加入 WhoAmI 測試連線的 Fail-Fast 驗證，並對日誌中的使用者名稱進行遮罩處理（如 `DYN***\Adm***`），確保安全。

---

## 7. Verification Checklist (驗證清單)

### 配置對齊檢查 (appsettings.json)
- [ ] `CrmConnection:Username` 已改為 `"DYNAMICS-365\\Administrator"` (或對應 UPN)。
- [ ] `DynamicsAccess:CeVersion` 已改為 `"9.1"`。
- [ ] `DynamicsAccess:ProfileAlias` 已改為 `"sunnyvalechback-prod"`。
- [ ] `DynamicsAccess:Embedded:CeVersion` 已改為 `"9.1"`。
- [ ] `DynamicsAccess:Embedded:OrganizationWebApiBaseUri` 已改為 `"https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/"`。
- [ ] `DynamicsAccess:Embedded:SecretReference` 已改為 `"dynamics-sunnyvalechback-prod-credential"`。
- [ ] `DynamicsAccess:Embedded:UserNameSecretName` 已改為 `"DYNAMICS_SUNNYVALECHBACK_PROD_USERNAME"`。
- [ ] `DynamicsAccess:Embedded:PasswordSecretName` 已改為 `"DYNAMICS_SUNNYVALECHBACK_PROD_PASSWORD"`。
- [ ] `DynamicsAccess:Embedded:DomainSecretName` 已改為 `"DYNAMICS_SUNNYVALECHBACK_PROD_DOMAIN"`。
- [ ] `DynamicsAccess:Embedded:AuthorityUri` 已改為 `"https://adfsdev91.speechmessage.com.tw/adfs"`。
- [ ] `DynamicsAccess:Embedded:ResourceUri` 已改為 `"https://sunnyvalechback.speechmessage.com.tw/"`。
- [ ] `DynamicsAccess:Package01FeeReadsEnabled` 保持為 `false`。

### 驗證步驟
1. **步驟 1：啟動驗證**
   - 啟動 ChurchReport 應用程式，確認無啟動異常。
2. **步驟 2：ADFS Token 探測**
   - 執行 `docs/scripts/Invoke-AdfsTokenProbe.ps1`，確認能成功從 `https://adfsdev91.speechmessage.com.tw/adfs/services/trust/mex` 取得 Token。
3. **步驟 3：後端連線驗證**
   - 觸發系統健康檢查或 WhoAmI 請求，確認 `CrmConnectionPool` 能成功與 CRM 建立連線。
4. **步驟 4：前端會員登入測試**
   - 使用網頁會員帳號 `zz` 進行登入，確認能順利通過 `ValidateUserCredentials` 並成功登入系統。
