# 系統與 UI 審查報告：Sunnyvalechback 9.1 服務憑證修復

本報告針對 `SpeechMessageProducts.ChurchReport` 專案中，為修復 Dynamics 365 CE 9.1 On-Premises IFD 登入失敗所進行的一行高風險配置變更進行審查。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 成功修復了前端會員無法登入的致命阻擋問題，恢復了 zz/zz 帳號的登入流程。
Visual Consistency: 20/20 - 此為後端連線配置變更，未對前端 UI 視覺樣式與設計系統 Token 造成任何負面影響。
Accessibility: 19/20 - 登入表單（Login.cshtml）維持既有的語意化 HTML 與錯誤提示機制，無障礙性未受損。
Performance: 15/20 - 透過 SDK 同步呼叫 CRM 進行驗證（ValidateUserCredentials）是已知的效能瓶頸，未來需進一步優化。
Browser Compatibility: 20/20 - 配置變更不影響瀏覽器相容性，Playwright 測試已證實 Chrome/Chromium 下運作正常。

TOTAL SCORE: 92/100

ISSUES FOUND:
- [Warning] appsettings.json 中以明文儲存 CrmConnection:Password，存在敏感資訊外洩風險。
- [Warning] 使用 SPEECHMESSAGE\Administrator 高權限帳號進行 CRM 連線，違反最小權限原則。
- [Info] scratch/d365-login-probe/ 目錄下殘留診斷暫存檔案，應於正式部署前清理。

RECOMMENDATION: PASS
```

---

## 審查問題回覆

### 1. 診斷是否在邏輯上受到證據支持？
**是的，完全支持。**
* **驗證流程分析**：根據 `AuthenticationController.Private.cs` 中的 `ValidateUserCredentials` 實作，當前端會員輸入帳密（如 `zz/zz`）進行登入時，系統會先呼叫 `GetConnection()` 建立與 Dynamics 365 `Organization.svc` 的連線，再查詢 `contact` 實體來比對會員帳密。
* **失敗原因**：由於之前的 `CrmConnection:Password` 為過期的 6 字元密碼，導致後端無法成功建立 CRM 連線，進而使所有前端會員登入失敗。
* **驗證結果**：更新密碼後，本機 Kestrel 測試 `http://localhost:43371/Authentication/ProcessLogin` 傳回成功（`message=login success`），證實了此診斷與修復的正確性。

### 2. 此最小配置變更是否為解決即時前端登入失敗的可接受修復方案？
**是的，這是目前最直接且風險最低的緊急修復方案。**
* 此變更僅修正了過期的憑證配置，未改動任何程式碼邏輯，能立即恢復生產環境的登入功能。

### 3. 應指出哪些殘留風險（特別是明文金鑰、服務帳號權限、暫存產物及未來的去 SDK/OAuth 工作）？
* **明文金鑰風險 (Plaintext Secrets)**：密碼以明文形式儲存在 `appsettings.json` 中。若程式碼庫外洩，該密碼將直接曝光。
* **服務帳號權限過大 (Service Account Privilege)**：目前連線使用的是 `SPEECHMESSAGE\Administrator`（網域系統管理員/CRM 系統管理員），違反最小權限原則。
* **殘留診斷檔案 (Scratch Artifacts)**：`scratch/d365-login-probe/` 目錄下有許多診斷產物（如 `result.json`、螢幕截圖等），可能包含敏感的網域資訊，應在部署前清理。
* **未來去 SDK/OAuth 轉型 (Future no-SDK/OAuth work)**：Dynamics 2011 SOAP 端點已被微軟棄用，未來應規劃轉向 Web API (OData v4) 並採用 OAuth 2.0 驗證。

### 4. 是否有任何 Critical 級別的阻擋性問題？
**沒有。**
* 雖然明文密碼與高權限帳號是顯著的安全隱患（列為 Warning），但這屬於既有架構的技術債，並非本次修復引入的新問題，不應阻擋此修復的發佈。

---

## 詳細審查意見

### 1. 摘要 (Summary)
本次配置變更成功修復了 ChurchReport 前端會員登入失效的問題。經確認，前端登入依賴後端以服務帳號連線至 Dynamics 365 進行憑證比對，更新過期密碼為必要且正確的處置。

### 2. 安全與配置問題 (Security & Configuration Issues)
* **[Warning] 明文密碼儲存**
  * **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json`
  * **說明**：`CrmConnection:Password` 以明文儲存。
  * **建議**：未來應將敏感憑證移至環境變數或使用加密金鑰管理服務。
* **[Warning] 服務帳號權限過大**
  * **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json`
  * **說明**：使用 `SPEECHMESSAGE\Administrator` 進行 CRM 連線。
  * **建議**：應建立一個專用的、僅具備 `contact` 讀取權限的低權限服務帳號。
* **[Info] 殘留診斷檔案**
  * **檔案路徑**：`scratch/d365-login-probe/`
  * **說明**：目錄下殘留 Playwright 測試產生的截圖與 JSON 檔案。
  * **建議**：在正式發佈前，應將 `scratch/` 目錄排除或清理，避免提交至版本控制系統。

### 3. 建議 (Suggestions)
* **效能優化**：`ValidateUserCredentials` 內部的 `RetrieveMultiple` 查詢是同步進行的，且可能因 CRM 回應慢而導致登入延遲。建議未來評估加入適當的快取機制或改為非同步查詢。
* **架構現代化**：規劃將 Dynamics 365 SDK 連線重構為 Web API，並結合 ADFS OAuth 2.0 驗證，以提升系統安全與擴充性。

### 4. 肯定之處 (Positive Notes)
* **診斷詳實**：開發團隊建立了完整的 Playwright 探針工具（`login-probe.js`）進行自動化模擬，並留下了詳細的 HTTP 請求歷程記錄，使問題定位非常精準。
* **影響範圍小**：採用最小配置變更，未改動任何 Controller 邏輯，確保了系統的穩定性。
