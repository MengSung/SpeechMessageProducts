# Dynamics Phase 4 Isolation Hardening 審查報告

本審查針對當前未提交的 Phase 4 本地隔離強化變更集進行唯讀事實核對與程式碼品質評估。

---

## 一、 審查判定 (Verdict)

### **PASS**
本次 Phase 4 本地隔離強化變更在單機範疇內的設計與實作完全正確。本地准入控制（Admission Control）與記憶體內協調器（Host-Slot Coordinator）的原子性與生命週期管理處理妥當，無資源洩漏或 race condition。HTTP 傳輸層已正確套用無 Session 強化設定。測試覆蓋率高且設計合理。

---

## 二、 關鍵發現與分類 (Findings & Classifications)

### Critical 🔴
* **無**。本次變更在單機範疇內未發現嚴重的程式碼缺陷、資源洩漏或 race condition。

### Warning 🟡

1. **分散式協調器缺失 (Durable Coordinator Missing)**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs`
   * **說明**：目前僅實作了 `InMemoryRuntimeHostSlotCoordinator`，其 `IsDurable` 屬性為 `false`。這意味著在多主機（multi-host）部署環境下，無法進行跨機的容量限制與租約管理。
   * **緩解措施**：在多機生產環境部署前，必須實作基於 Redis 或資料庫的持久化協調器（`IRuntimeHostSlotCoordinator`）。

2. **Gateway 缺乏生產級驗證 (Scaffolding Workload Authentication)**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (以及 Gateway 相關進入點)
   * **說明**：目前 Gateway 仍處於 scaffolding 階段，直接信任請求體中的 `WorkloadSubjectId`，缺乏生產級的 JWT/mTLS 驗證中間件。
   * **緩解措施**：在將 Gateway 暴露給真實流量前，必須補齊生產級的工作負載驗證機制。

3. **ADFS/IFD 外部驗證阻塞 (ADFS OAuth Blocker)**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
   * **說明**：由於 ADFS 伺服器端尚未完成 ClientId 註冊，導致 OAuth 流程無法取得有效 Access Token，此為外部阻塞點。
   * **緩解措施**：協調 ADFS 管理員完成用戶端註冊。

### Info 🟢

1. **功能旗標安全隔離**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 559 行)
   * **說明**：`DynamicsAccess:Package01FeeReadsEnabled` 確實保持為 `false`，確保未經驗證的 Web API 路徑不會在生產環境中被意外啟用。

2. **HTTP 傳輸層安全強化**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs` (第 92-103 行)
   * **說明**：`SocketsHttpHandler` 已正確禁用 cookies、redirects、proxies、decompression 和 pre-authentication，防止 session 洩漏。

3. **原子性本地准入控制**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 168-182 行)
   * **說明**：使用 `_totalAdmission` 訊號量在 `lock` 內進行原子性預留，有效防止 concurrent burst 超過 queue capacity。

---

## 三、 剩餘 Release Blockers (本增量範疇外之發布阻礙)

以下項目仍需在後續階段實作並提供驗證證據，方可啟用任何 Dynamics 生產流量：

1. **分散式協調器 (Durable Coordinator)**：需實作具備 epoch/fencing/quarantine 語義的跨主機協調器。
2. **設定檔生命週期隔離 (Profile Lifecycle Isolation)**：需實作 profile 產生隔離、replace-and-drain 以及確定性的非同步執行期銷毀。
3. **回應串流限制與去識別化 (Bounded Response Streaming & Redaction)**：需在所有 ADFS 和 CRM 路徑上限制回應大小並對 token/body 進行去識別化。
4. **Gateway 工作負載驗證 (Gateway Workload Auth)**：需實作 JWT/mTLS 驗證，移除呼叫端控制的 workload subject 資料。
5. **完整 Soak/Fault 測試套件**：需執行 socket/timer/heap soak 測試以及 Gateway-plus-Embedded 聚合容量測試。
6. **Live Smoke 驗證矩陣**：需在真實 CE 8.2 和 CE 9.1 環境下完成雙副本容量與 parity 驗證。
