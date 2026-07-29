# Dynamics CE 9.1 AD FS OAuth 最小權限審查報告

## 1. 審查決策
**決策：KEEP_WITH_GATES (暫時保留，但必須落實安全閘口)**

在開發階段（Development E2E），考量到 `Package01FeeReadsEnabled=false`、固定 localhost 回呼地址、以及 Token 僅在記憶體中由 profile-generation owner 保管並確定性清理（無 Session Leakage）的條件下，暫時保留此權限是可接受的。然而，由於該權限實際上作用於整個共用 Relying Party Trust (RP)，必須在進入測試與生產環境前落實嚴格的隔離與安全閘口。

---

## 2. 安全發現與風險分級 (Findings)

### 🔴 Critical: 共用 Relying Party Trust 導致的權限擴張風險
* **路徑/範圍**：AD FS Relying Party Trust: `Dynamics 365 IFD External`
* **原理與風險**：
  由於 Dynamics 365 IFD 使用共用的 Relying Party Trust，其 Identifiers 包含了多個組織的網址（如 `david`, `elijah`, `solomon`, `sunnyvalechback` 等）。當執行 `Grant-AdfsApplicationPermission` 時，AD FS 會將 `ServerRoleIdentifier` 正規化為該 RP 的主識別碼 `https://auth.speechmessage.com.tw/`。
  這意味著：**此 Public ClientId (`2ad88395-b77d-4561-9441-d0e40824f9bc`) 實際上已被允許向該共用 RP 下的所有其他組織識別碼請求並取得 Access Token**。OAuth 協議層面此時無法對單一組織進行細粒度隔離。

### 🟡 Warning: Public Client 缺乏 Client Secret 驗證
* **路徑/範圍**：AD FS Client: `SpeechMessage-ChurchReport-LocalDev`
* **原理與風險**：
  該 Client 註冊為 `Public` 且無 Client Secret，其安全性完全依賴於 Redirect URI (`http://localhost:43371/diagnostics/adfs-callback`) 的控制。若本機開發環境的 Port 遭其他惡意處理程序監聽，或 Redirect URI 遭劫持，將存在 Authorization Code 被竊取的風險。

### 🟢 Info: 縱深防禦機制 (Dynamics 權限與 Gateway Policy)
* **路徑/範圍**：`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
* **原理與風險**：
  雖然 Client 能夠取得其他組織的 Token，但實際存取仍受到以下兩層防禦保護：
  1. **Dynamics 使用者權限層**：登入的使用者必須在目標 Dynamics 組織中擁有合法的帳號與安全角色（Security Roles），否則 Dynamics 會回傳 HTTP 401/403。
  2. **Gateway Policy 層**：Gateway 透過 `ConfigurationGatewayOperationAuthorizer` 進行嚴格的 Windows Principal/SID 與 Workload 綁定，限制其只能呼叫特定的 Profile Alias 與 Operation ID，從最前端進行 Fail-Closed 攔截。

---

## 3. 精確 AD FS 命令與驗證命令

### A. 修改 AD FS Client 描述 (明確記錄權限邊界)
為避免後續維運人員誤判，應更新該 Client 的 Description：
```powershell
Set-AdfsClient `
  -TargetClientId "2ad88395-b77d-4561-9441-d0e40824f9bc" `
  -Description "SpeechMessage-ChurchReport-LocalDev: Permission bound to shared Dynamics IFD RP. Actual routing and operation access are strictly restricted by Gateway policy."
```

### B. 回滾命令 (Rollback)
若需完全撤銷此開發授權並清理環境，請執行以下命令：
```powershell
# 1. 撤銷該 Client 對共用 RP 的授權
Revoke-AdfsApplicationPermission `
  -ClientRoleIdentifier "2ad88395-b77d-4561-9441-d0e40824f9bc" `
  -ServerRoleIdentifier "https://auth.speechmessage.com.tw/"

# 2. 刪除 AD FS Client 註冊
Remove-AdfsClient -TargetClientId "2ad88395-b77d-4561-9441-d0e40824f9bc"
```

### C. 回滾後驗證命令
```powershell
# 驗證 Client 是否已刪除 (應回傳空值或錯誤)
Get-AdfsClient -ClientId "2ad88395-b77d-4561-9441-d0e40824f9bc"

# 驗證權限是否已撤銷 (應確認無該 ClientId 與共用 RP 的關聯)
Get-AdfsApplicationPermission | Where-Object { $_.ClientRoleIdentifier -eq "2ad88395-b77d-4561-9441-d0e40824f9bc" }
```

---

## 4. Browser E2E 前置 Gate 清單

在進行 Local Gateway 瀏覽器 E2E 測試前，必須確認並完成以下安全與生命週期檢查點：

- [ ] **Gate 1: Token 儲存與記憶體安全**
  * 本機產生的 `adfs-local-token.json` 必須被 `.gitignore` 嚴格排除，絕不能提交至 Git。
  * `AdfsOAuthTokenProvider` 讀取 Token 響應時，必須限制 `MaxTokenResponseBytes = 32 KB`，且在讀取完畢後呼叫 `CryptographicOperations.ZeroMemory(buffer)` 抹除記憶體。
- [ ] **Gate 2: 確定性資源清理 (Zero Session Leakage)**
  * 驗證 `AdfsOAuthTokenProvider` 的 `DisposeAsync` 確實被呼叫，且能完整釋放 `HttpClient`、`SocketsHttpHandler` 與 `SemaphoreSlim`，無殘留的 Socket 連線或記憶體洩漏。
- [ ] **Gate 3: Gateway 嚴格路由與操作授權**
  * 啟用 `ConfigurationGatewayOperationAuthorizer`，且 `DynamicsGateway:WorkloadBindings` 設定中不得使用任何 `*` 或 `?` 通配符，必須精確指定允許的 Profile Alias（如 `sunnyvalechback-prod`）與 Operation ID。
- [ ] **Gate 4: 傳輸層安全配置**
  * 確保 `SocketsHttpHandler` 已關閉 Cookie (`UseCookies = false`)、關閉自動轉址 (`AllowAutoRedirect = false`)、關閉代理伺服器 (`UseProxy = false`) 且關閉預先驗證 (`PreAuthenticate = false`)。

---

## 5. 需實機 Token／WhoAmI 才能證明的結論

以下安全與功能性結論，無法單靠靜態程式碼審查或模擬測試證明，**必須透過實機取得 Token 並呼叫 Dynamics WhoAmI 接口才能證實**：

1. **AD FS Token 簽發與 Audience 相容性**：
   證明使用該 Public ClientId 透過 `authorization_code` 或 `refresh_token` 流程向 AD FS 請求的 Access Token，其 Audience 與格式能被 Dynamics 365 CE 9.1 Web API 接受並成功通過驗證。
2. **跨組織權限隔離有效性**：
   必須實機測試：使用該 ClientId 取得的 Token 嘗試存取共用 RP 內的其他組織（例如 `https://david.speechmessage.com.tw/api/data/v9.1/WhoAmI`）。若該登入使用者在 `david` 組織中無權限，Dynamics 必須回傳 HTTP 401 或 403，以此證明 Dynamics 組織層級的權限隔離依然有效。
3. **Gateway 政策攔截與 Fail-Closed 驗證**：
   實機模擬 Workload 試圖透過 Gateway 呼叫未授權的 Alias 或 Operation，驗證 Gateway 是否確實回傳 HTTP 403 Forbidden，且後端 Dynamics 接口完全沒有收到任何請求呼叫。
