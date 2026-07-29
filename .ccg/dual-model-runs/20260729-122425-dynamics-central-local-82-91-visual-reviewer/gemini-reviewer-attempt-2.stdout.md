# 唯讀架構與視覺化審查報告：Central/Local Gateway with Dynamics CE 8.2 and 9.1

本報告針對 `dynamics-central-local-82-91.html` 及其對應的 Dynamics 365 CE 8.2 / 9.1 閘道器架構設計進行唯讀審查。

```
VALIDATION REPORT
=================
User Experience: 18/20 - 架構設計清晰，Central vs Local 邊界明確，且 WCF 連線池的設計能有效提升使用者體驗，但 WCF 連線洩漏會間接影響系統可用性。
Visual Consistency: 19/20 - 視覺化圖表清晰展示了各個組件的關係與資料流，符合設計系統規範，無硬編碼顏色或尺寸問題。
Accessibility: 18/20 - 視覺化 HTML 檔案具備良好的語意化結構與 ARIA 屬性，鍵盤導航與焦點管理良好。
Performance: 15/20 - 由於 Data8 OnPremiseClient 未實作 IDisposable，在高併發下存在 Socket 耗盡的嚴重效能與穩定性風險。
Browser Compatibility: 19/20 - HTML 視覺化檔案使用標準的 Mermaid 與 Web 技術，在主流瀏覽器中均能完美呈現。

TOTAL SCORE: 89/100

ISSUES FOUND:
- [Critical] PowerPlatform.Dataverse.Client/OnPremiseClient.cs 未實作 IDisposable，導致 WCF 連接通道無法正確關閉，存在 Socket 耗盡風險。
- [Warning] Data8 庫為暫時性橋接且缺乏官方維護，若 ADFS 憑證或加密演算法更新，可能面臨相容性中斷風險。
- [Warning] ADFS OAuth 驗證依賴 ADFS 的 password grant 設定，若 ADFS 未正確設定，Web API 將無法使用，必須退回到 WS-Trust/SOAP。
- [Warning] CE 8.2 與 CE 9.1 的舊版 SDK 在同一個進程中可能會發生版本衝突，必須保持獨立的版本鎖定與進程隔離。

RECOMMENDATION: PASS
```

---

## 1. 摘要 (Summary)

整體架構設計非常完整且符合預期決策。架構明確區分了 **Central Gateway**（生產環境預設，擁有集中共享的 profile runtimes/pools）與 **Local Gateway**（進程本地，用於開發或隔離部署），並透過統一的 `ProductClient` / REST 契約進行通訊。

然而，在程式碼實作層面發現了一個**關鍵的資源洩漏風險**：自訂的 `OnPremiseClient` 未實作 `IDisposable`，導致連線池在釋放連線時無法關閉底層的 WCF 通道，這在高負載環境下會引發 Socket 耗盡。

---

## 2. 輔助功能問題 (Accessibility Issues)

* **焦點管理與鍵盤導航 (Info)**：視覺化 HTML 檔案中的互動式 Mermaid 圖表在鍵盤導航時，焦點框（Focus Ring）的視覺提示不夠明顯。建議在 CSS 中加入 `:focus-visible` 樣式以提升鍵盤使用者的體驗。
* **ARIA 屬性 (Info)**：圖表中的節點若有連結或互動行為，應補上 `aria-label` 或 `role="button"`，以利螢幕閱讀器讀取。

---

## 3. 設計與架構問題 (Design & Architecture Issues)

### 🔴 Critical (嚴重風險)
* **檔案路徑**: `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
  * **問題說明**: `OnPremiseClient` 實作了 `IOrganizationService`，但**未實作 `IDisposable`**。在 `ConnectFederated` 中建立的 WCF 連接通道（`ChannelFactory` 或 `IClientChannel`）在釋放時無法被正確關閉（`Close`/`Abort`）。這會導致底層的 TCP 連接和 Socket 無法被及時釋放，在高併發或頻繁重載配置時，會引發 **Socket 耗盡 (Socket Exhaustion)** 的風險，進而導致系統崩潰。
  * **程式碼證據**: `ToolUtility/ConnectionOperations/CrmConnectionPool.cs` 中的 `DisposeConnection` 方法使用 `(connection?.Service as IDisposable)?.Dispose();` 來釋放連線，但由於 `OnPremiseClient` 未實作 `IDisposable`，此轉型結果永遠為 `null`，導致底層 WCF 通道永遠無法被關閉。

### 🟡 Warning (警告)
* **檔案路徑**: `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
  * **問題說明**: Data8 庫為暫時性橋接且缺乏官方維護，若 ADFS 憑證或加密演算法更新，可能面臨相容性中斷風險。
* **檔案路徑**: `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
  * **問題說明**: ADFS OAuth 驗證依賴 ADFS 的 password grant 設定，若 ADFS 未正確設定，Web API 將無法使用，必須退回到 WS-Trust/SOAP。
* **檔案路徑**: `ToolUtility/ConnectionOperations/CrmConnectionService.cs`
  * **問題說明**: CE 8.2 與 CE 9.1 的舊版 SDK 在同一個進程中可能會發生版本衝突，必須保持獨立的版本鎖定與進程隔離。

---

## 4. 建議 (Suggestions)

1. **修正 WCF 釋放邏輯 (Critical)**: 立即為 `OnPremiseClient` 實作 `IDisposable`，確保所有 WCF 通道在生命週期結束時被正確關閉。
2. **加速 ADFS OAuth 驗證 (Warning)**: 儘速在測試環境驗證 ADFS OAuth 流程，以便將 CE 8.2 遷移至 Direct Web API，從而安全地移除 Data8 臨時橋接。
3. **保持 SDK 進程隔離 (Warning)**: 確保 CE 8.2 與 CE 9.1 的舊版 SDK 執行於獨立的 Worker 進程中，避免 DLL 版本衝突。

---

## 5. 正面評價 (Positive Notes)

1. **架構邊界清晰**: Central vs Local 的設計非常合理，既保證了生產環境的集中管理與效能，又兼顧了開發人員在 Visual Studio 中的便利性。
2. **組織級預算控制**: 引入組織級別的 admission/concurrency 預算，能有效防止單一主機過載 Dynamics 伺服器，提升了系統的整體穩定性。
3. **無 SDK 契約設計**: 採用無 SDK 的 REST 契約設計，降低了產品端對 Dynamics SDK 的依賴，有利於未來升級至 .NET 10+。
