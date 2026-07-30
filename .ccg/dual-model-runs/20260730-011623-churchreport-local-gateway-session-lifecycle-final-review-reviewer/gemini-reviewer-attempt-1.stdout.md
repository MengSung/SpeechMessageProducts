# 審查結果：PASS

本審查針對 `1.0.0.3.Gateway&Embedded.Worktree` 分支中，關於 ChurchReport Local Gateway 主機擁有權（Host Ownership）與 Session 資源生命週期（Session Resource Lifecycle）的變更進行了完整且深入的程式碼與架構核對。所有 Required Invariants 與核准的架構規格均已確實遵守，未發現任何 Critical 或 Warning 級別的資源洩漏或安全隔離缺陷。

---

## 驗證報告 (VALIDATION REPORT)

*註：由於本任務為後端生命週期、安全邊界與系統整合審查，評分維度已對應調整為系統整合體驗、架構一致性、安全防護性、效能優化度與系統相容性。*

* **系統整合體驗 (System Integration Experience)**: 20/20
  * **評語**：登入前與登出時的資源清理順序設計極為嚴謹。系統強制先透過主 DI 單例協調器（Coordinator）撤銷舊 Donation 世代的請求租約，再執行 `Session.Clear`。若撤銷過程發生任何異常，例外會直接向上傳遞以阻止後續操作，達成 Fail-Closed 安全防線，有效防止 Session Fixation 攻擊與跨使用者資源污染。
* **架構一致性 (Architectural Consistency)**: 20/20
  * **評語**：完全符合核准的架構設計。舊有的 static facade 類別（`DonationDynamicsAccessBootstrap`）已不再擁有 `ServiceProvider` 或連線池，僅作為過渡路由轉送至主 DI 容器註冊的 `IDonationDynamicsAccessProcessHost` 單例。這確保了所有 Dynamics 傳輸層資源（HttpClient、Timer、Socket Pool）的生命週期均被嚴格限制在 Generic Host 內，並在主機關閉時進行確定性釋放。
* **安全防護性 (Security & Isolation Hardening)**: 20/20
  * **評語**：安全邊界劃分清晰。Gateway 成功移除了 `WhoAmI` 回應中原本主動序列化的 `approvedWebApiRoot` 內部路由中繼資料，防止敏感的 CRM 主機名稱與 API 路徑跨越信任邊界洩漏給產品端。此外，API 授權驗證（403）被正確置於 Content-Type 媒體型別驗證（415）之前，防止未授權呼叫者利用媒體型別進行合約探測。
* **效能優化度 (Performance & Resource Management)**: 20/20
  * **評語**：`ControlledOperationExecutor` 採用了非 async 的準備階段（`TryPrepare`），在進入 async 狀態機前即完成參數白名單過濾與規範化（Canonicalization），並在排隊等待期間僅持有輕量化的 `PreparedOperationDispatch` 標量狀態，避免將整個請求物件圖（Request Graph）提升為 long-lived 狀態，顯著降低了高併發下的記憶體壓力。
* **系統相容性 (System Compatibility)**: 20/20
  * **評語**：完美相容於現有的 legacy SOAP 模式，且為未來的 Local Gateway 與 Web API 模式奠定了安全的基礎。

**總分 (TOTAL SCORE): 100/100**

**發現的問題 (ISSUES FOUND):**
* 無（未發現任何 Critical 或 Warning 級別的程式碼缺陷）。

**審查建議 (RECOMMENDATION): PASS**

---

## 審查發現分類

### Critical
* **無**。所有關於 Session 資源生命週期、併發清理競爭、Fail-Closed 啟動中斷與安全邊界防護的實作均符合規格。

### Warning
* **無**。

### Info
* **`LineMessagingClient` 既有 HTTP 資源釋放問題確認**：
  * **說明**：先前審查指出的 `LineMessagingClient` 部分既有方法未確定性釋放 `HttpRequestMessage` 與 `HttpResponseMessage` 之問題，經確認本次變更僅對 `MarkAsReadByTokenAsync` 進行了 XML 註解與空白調整，並未新增或擴大該問題。此問題已記錄為獨立的儲存庫層級生命週期重構任務，不在此 Local Gateway 增量中混合修復。
* **`EstimatedEnvelopeBytes` 與 `CanonicalEnvelopeBytes` 的相容性設計**：
  * **說明**：`DispatchEnvelope` 中保留了 `EstimatedEnvelopeBytes` 屬性作為 `CanonicalEnvelopeBytes` 的別名，以相容於既有的測試與管理邏輯，此設計安全且未引入 Last-Write-Wins 風險。

---

## 尚未完成的驗證差距 (Verification Gaps)

以下為進入真實 Local Gateway 生產環境或 Strangler 遷移前，仍需在後續階段完成的驗證步驟（本切片已在文件中正確聲明這些閘門保持開啟）：
1. **真實環境 E2E 驗證**：尚未在真實的 `localhost` 環境下啟動 Local Gateway 並執行 ChurchReport 瀏覽器端端到端（E2E）整合測試。
2. **ADFS / PKCE 與真實組織驗證**：尚未在真實的 Dynamics 365 CE 8.2 / 9.1 環境下完成 AdfsOAuth 權杖取得與 WhoAmI 驗證。
3. **效能與負載測試**：尚未進行跨行程容量限制、併發排隊、故障恢復與長期運行（Soak）的效能基準測試。
4. **Strangler 遷移與舊 SDK 移除**：Phase 5 產品流量遷移與 Phase 6 Data8 / 舊 SDK 移除的準備工作尚未開始。

---

## 關鍵不變性與架構保留確認

本審查特別針對以下三項關鍵架構約束進行了現場核對，確認均已確實保留：

1. **`Package01FeeReadsEnabled = false` 確實保留**：
   * 經核對 `SpeechMessageProducts.ChurchReport/appsettings.json` 第 559 行，該旗標確實維持為預設值 `false`，確保未經驗證的 Web API 新路徑不會在生產環境中被意外啟用，所有流量仍安全走舊有 SOAP 管道。
2. **Embedded 模式保留但延遲**：
   * 雖然 `ExecutionMode` 設為 `"Embedded"`，但因 `Package01FeeReadsEnabled` 為 `false`，新路徑完全短路，Embedded 模式的實作程式碼被安全保留以供後續 VS 本機偵錯使用。
3. **Data8 專案與舊 SDK 保留**：
   * 位於 `PowerPlatform.Dataverse.Client` 的 Data8 專案依然存在，未被提前移除，符合 Phase 6 移除閘門尚未滿足的架構規劃。
