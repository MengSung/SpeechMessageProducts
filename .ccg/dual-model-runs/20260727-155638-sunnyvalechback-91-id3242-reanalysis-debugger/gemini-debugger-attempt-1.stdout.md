## UI Diagnostic Report

### Visual Symptoms
- **使用者可見症狀**：會員登入 ChurchReport 時，系統在查詢會員聯絡人之前呼叫舊版 SOAP 連線池（`CrmConnectionPool`），第一個 CRM 操作即失敗，網頁顯示技術性錯誤訊息：「`ID3242: 無法驗證或授權此安全性權杖。`」。
- **觸發時機**：此錯誤發生在後端 CRM 連線建立與驗證階段，此時網站會員輸入的帳號與密碼尚未被評估。

---

### Hypotheses

1. **ADFS 權杖簽署憑證信任失效或未同步更新 (Token-Signing Certificate Trust/Rollover)** - Likelihood: High
   - **Evidence**：Dynamics 365 CE 9.1 是新部署的環境。若 ADFS 伺服器的權杖簽署憑證（Token-Signing Certificate）曾進行過更新或重建，而 Dynamics 365 伺服器端未同步更新其同盟中繼資料（Federation Metadata），CRM 將無法驗證 ADFS 簽發的 SAML 權杖簽章，進而拋出 `ID3242`。
   - **Check**：檢查 Dynamics 365 伺服器的事件檢視器（Application Log），尋找來源為 `System.IdentityModel` 或 `Microsoft.IdentityModel` 的憑證驗證失敗錯誤。

2. **CRM 信賴憑證者識別碼 (Relying Party Identifier) 或對象 (Audience) 不匹配** - Likelihood: High
   - **Evidence**：組織從 `jesus` 切換為 `sunnyvalechback`，其公用 HTTPS URL 已變更。若 ADFS 中的信賴憑證者信任（Relying Party Trust）識別碼未正確配置為新的 URL，或與 CRM 預期的 Audience URI 不符，CRM 將拒絕該權杖。
   - **Check**：檢查 ADFS 管理主控台中的信賴憑證者識別碼，並比對 `OnPremiseClient` 請求的 URL。

3. **配置的密碼過期或不正確 (Stale/Incorrect Password)** - Likelihood: Medium
   - **Evidence**：雖然 `SPEECHMESSAGE\Administrator` 使用者名稱已證實有效，但設定檔中的密碼可能已過期或與 AD 中的實際密碼不一致。WinRM 探針返回 `Access is denied` 雖非決定性證據，但仍指向密碼錯誤的可能性。
   - **Check**：檢查 ADFS Admin 記錄中的 Event ID 364，確認是否有 `FailedAuthentication` 或密碼錯誤的稽核失敗事件。

4. **ADFS 宣告規則 (Claims Rules) 缺失或不相容** - Likelihood: Medium
   - **Evidence**：新環境的 ADFS 信賴憑證者信任可能未配置正確的宣告規則（例如 UPN 或 Primary SID），導致 CRM 無法將權杖對應至 `SPEECHMESSAGE\Administrator` 使用者。
   - **Check**：檢查 ADFS 信賴憑證者信任的「編輯宣告發行原則」，確認是否包含必要的 UPN 宣告。

5. **伺服器時間差 (Time Skew)** - Likelihood: Low
   - **Evidence**：若 ChurchReport 伺服器、ADFS 伺服器或 Dynamics 365 伺服器之間的時間差超過 5 分鐘，SAML 權杖將被判定為尚未生效或已過期。
   - **Check**：比對三台伺服器的系統時間與時區設定。

---

### Recommended Checks

- **React DevTools / Server Diagnostics (伺服器端診斷)**:
  - **ADFS 伺服器**：檢查事件檢視器 `Applications and Services Logs -> AD FS -> Admin`，尋找 **Event ID 364** 或 **Event ID 111**。
  - **Dynamics 365 伺服器**：檢查事件檢視器 `Windows Logs -> Application`，尋找與 `ID3242` 相關的詳細錯誤堆疊。
- **CSS Inspector / WCF & WSDL Inspection (連線與協定檢查)**:
  - 驗證 `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc?wsdl` 是否能正常下載，且其宣告的同盟簽發者 MEX 地址與 ADFS 實際地址一致。
  - 檢查 WCF 用戶端是否強制啟用 TLS 1.2（`ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12`）。
- **Console / Application Logs (應用程式端日誌)**:
  - 在應用程式端加入最小化安全診斷程式碼，捕獲完整的 Exception 鏈（包括所有 `InnerException`），並過濾敏感資訊（如密碼與 Token）。

---

### Probable Root Cause
最可能的根本原因為 **ADFS 權杖簽署憑證未在 Dynamics 365 中更新** 或 **Relying Party Identifier 不匹配**。
- **推理**：新證據顯示 `SPEECHMESSAGE\Administrator` 在瀏覽器中可成功登入，因此該帳號格式在當前組織中是有效的。`ID3242` 是典型的 WIF 權杖驗證失敗錯誤，通常發生在 ADFS 成功簽發權杖後，CRM 伺服器在驗證該權杖的簽署憑證或 Audience 時失敗。

---

## Detailed Technical Answers

### 1. Plausible Causes Ranking (ID3242 誘因排序)
基於 `SPEECHMESSAGE\Administrator` 使用者名稱已證實有效的最新證據，重新評估並排序如下：
1. **ADFS 權杖簽署憑證信任失效 (Token-Signing Cert Trust/Rollover)** - **Confidence: High**
   - ADFS 憑證更新後，CRM 伺服器未同步更新同盟中繼資料，導致無法驗證權杖簽章。
2. **CRM 信賴憑證者識別碼不匹配 (Relying-Party Identifier Mismatch)** - **Confidence: High**
   - 切換至 `sunnyvalechback` 後，ADFS 中的信賴憑證者識別碼與 CRM 預期的 Audience 不一致。
3. **配置的密碼過期或不正確 (Stale/Incorrect Password)** - **Confidence: Medium**
   - 設定檔中的密碼可能與 AD 實際密碼不符（WinRM 拒絕存取可能為此徵兆）。
4. **宣告規則缺失 (Claims Rules Mismatch)** - **Confidence: Medium**
   - ADFS 未向 CRM 發送必要的 UPN 或 Primary SID 宣告，導致 CRM 無法識別使用者。
5. **伺服器時間差 (Time Skew)** - **Confidence: Low**
   - 伺服器間時間不同步導致權杖失效。
6. **自訂 WCF 繫結不相容 (Custom WCF Binding Incompatibility)** - **Confidence: Low**
   - CE 9.1 對安全協定（如 TLS 1.2）或加密演算法有更嚴格的要求。

### 2. Distinguishing ADFS vs CRM Failures (如何區分 ADFS 與 CRM 階段失敗)
- **ADFS 階段失敗（未核發權杖）**：
  - **用戶端表現**：異常發生在 WCF 請求權杖階段（呼叫 ADFS STS 端點時）。異常類型通常為 `MessageSecurityException` 或 `FaultException`，錯誤訊息包含 ADFS 拒絕原因（如 `FailedAuthentication`）。
  - **伺服器端日誌**：ADFS 伺服器的 `AD FS -> Admin` 日誌中會出現 **Event ID 364**（Error），且 Security 稽核日誌中無該使用者的成功權杖發行記錄。
- **CRM 階段失敗（CRM 拒絕已核發的權杖）**：
  - **用戶端表現**：異常發生在呼叫 CRM 服務操作（如 `Execute`）時。異常訊息明確指出 `ID3242: 無法驗證或授權此安全性權杖。`。
  - **伺服器端日誌**：ADFS 伺服器顯示權杖發行成功（Event ID 501），但 Dynamics 365 伺服器的 Application 日誌中會出現 WIF 驗證失敗的警告或錯誤。

### 3. Smallest Safe Application-Side Diagnostic (最小化安全診斷提案)
在應用程式端部署以下診斷程式碼，以安全地捕獲異常鏈而不洩漏敏感資訊：

```csharp
public static void DiagnoseCrmConnection(string url, string username, string password)
{
    try
    {
        // 1. 測試 WSDL 可達性
        var wsdlUrl = url + "?wsdl";
        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(wsdlUrl);
        using (var response = request.GetResponse()) { }

        // 2. 建立用戶端
        var client = new PowerPlatform.Dataverse.Client.OnPremiseClient(url, username, password);

        // 3. 執行輕量級請求以觸發驗證
        var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
        client.Execute(whoAmI);
    }
    catch (Exception ex)
    {
        int depth = 0;
        Exception current = ex;
        while (current != null)
        {
            string msg = current.Message;
            if (!string.IsNullOrEmpty(password))
            {
                msg = msg.Replace(password, "********"); // 安全過濾
            }
            System.Diagnostics.Trace.WriteLine($"[CRM Diag Lvl {depth}] {current.GetType().FullName}: {msg}");
            System.Diagnostics.Trace.WriteLine($"[CRM Diag Stack] {current.StackTrace}");
            current = current.InnerException;
            depth++;
        }
        throw;
    }
}
```

### 4. Server-Side Logs & Decision Tree (伺服器端日誌與決策樹)

#### 檢查對象與指令：
1. **ADFS 伺服器**：
   - 檢查 `AD FS -> Admin` 事件日誌中的 **Event ID 364**。
   - 檢查信賴憑證者信任的識別碼是否包含 `https://sunnyvalechback.speechmessage.com.tw/`。
2. **Dynamics 365 伺服器**：
   - 檢查 `Application` 日誌中的 WIF 錯誤。
   - 使用 PowerShell 更新同盟中繼資料：
     ```powershell
     Update-MSOCrmClaimsPublishedFederationMetadata -DeploymentId <DeploymentId>
     ```

#### 決策樹：
```
                                  [開始診斷]
                                      │
                     檢查 ADFS Admin 日誌 (Event ID 364)
                                      │
                  ┌───────────────────┴───────────────────┐
                  ▼                                       ▼
            [有 Event ID 364]                       [無 ADFS 錯誤]
                  │                                       │
    ┌─────────────┴─────────────┐                         ▼
    ▼                           ▼                 檢查 CRM Application 日誌
[密碼錯誤/帳號鎖定]         [信賴憑證者未找到]       尋找 ID3242 詳細錯誤
    │                           │                         │
    ▼                           ▼                         ▼
更新設定檔密碼/             在 ADFS 中將 CRM URL      ┌───┴───────────────────┐
在 AD 中解鎖帳號            加入信賴憑證者識別碼      ▼                       ▼
                                                [憑證驗證失敗]          [宣告/對象不匹配]
                                                      │                       │
                                                      ▼                       ▼
                                                在 CRM 中更新           檢查 ADFS 宣告規則
                                                ADFS 權杖簽署憑證       與信賴憑證者識別碼
```

### 5. Username Format Recommendation (使用者名稱格式建議)
- **建議**：`CrmConnection:Username` 應**保持**為 `SPEECHMESSAGE\Administrator`。
- **理由**：瀏覽器已成功使用該格式登入 `https://sunnyvalechback.speechmessage.com.tw/main.aspx`，證實該格式在當前 AD 與 CRM 組織中是完全有效且被接受的。除非 ADFS 事件日誌（Event ID 364）明確指出該格式無法被解析，否則不應盲目修改為 UPN 或其他格式，以避免引入新的對應問題。

### 6. Weaknesses in Custom `OnPremiseClient` (舊版用戶端在 CE 9.1 下的潛在弱點)
- **TLS 1.2 強制要求**：CE 9.1 預設停用 TLS 1.0/1.1。若用戶端未顯式啟用 TLS 1.2，連線將被拒絕。
- **WS-Trust 1.3 終端節點相容性**：`OnPremiseClient` 寫死尋找 WS-Trust 1.3 策略。若新 ADFS 僅發布或啟用了 WS-Trust Feb 2005 終端節點，解析將失敗。
- **SHA-256 簽署要求**：CE 9.1 要求更強的加密套件。若自訂 WCF 繫結預設使用 SHA-1，將導致驗證失敗。

---

## Findings Classification

### 🔴 Critical
- **ADFS 權杖簽署憑證未同步**：若 ADFS 憑證已更新但 CRM 未同步，將直接導致 `ID3242` 錯誤，癱瘓所有後端 CRM 連線。
- **信賴憑證者識別碼不匹配**：切換環境後，若 ADFS 中的識別碼未精確對齊新的公用 HTTPS URL，將導致權杖驗證失敗。

### ⚠️ Warning
- **TLS 1.2 相容性風險**：若未在應用程式全域強制啟用 TLS 1.2，將無法與 CE 9.1 伺服器建立安全通道。
- **帳號鎖定風險**：WinRM 測試失敗提示密碼可能存在問題，應避免重複嘗試以防 `SPEECHMESSAGE\Administrator` 帳號被 AD 鎖定。

### ℹ️ Info
- **使用者名稱格式有效性**：已證實 `SPEECHMESSAGE\Administrator` 為有效格式，暫無須變更為 UPN。
