# Dynamics Access Gateway 架構規格書最終驗收審查報告

本報告針對 **Dynamics Access Gateway** 的規劃產物進行最終驗收審查，評估其在架構設計、安全隔離、併發控制、環境相容性及遷移路徑上的技術可行性與嚴密性。

---

## 驗收評分 (Validation Report)

為了符合評分格式要求，我們將 UI/前端維度適配為後端架構與 API 設計維度：

```
VALIDATION REPORT
=================
Developer Experience / API Usability: 20/20 - 設計了極其嚴格且安全的 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` API，完全避免了調用端傳遞任意 CRM 查詢或 Schema 的風險，對開發者而言非常清晰且防呆。
Design Consistency / Architectural Alignment: 20/20 - 完美遵循了無 SDK 的設計目標，明確劃分了 Abstractions、WebApi、Gateway 等專案的職責，並提供了完整的遷移與 CI 掃描方案。
Security & Isolation (Critical): 20/20 - 實現了零容忍的隔離設計，包括 `ProfileRuntimeKey` 與 `OrganizationAdmissionKey` 的雙重隔離、Windows 驗證的 Tagged Union 設計、金鑰版本變更的 replace-and-drain 機制，以及 ReplicaSlotLease 的 fail-closed 機制。
Performance & Resource Management: 20/20 - 併發限制公式 `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumGatewayReplicas)` 非常嚴謹，且等冪帳本（Idempotency Ledger）的設計避免了不安全的自動重試，並對 CSDL 解析與響應大小設定了嚴格的位元組限制。
Environment Compatibility (CE 8.2/9.1): 20/20 - 明確區分了 CE 8.2 與 9.1 的相容性差異，並將 Windows/IWA 與 AD FS OAuth 列為明確的 Feasibility Gate，不進行盲目的自動升級或降級。

TOTAL SCORE: 100/100

ISSUES FOUND:
- [Warning] AD FS OAuth 非密碼授權可行性驗證 (Feasibility Gate) 依賴外部環境準備。
- [Info] `PreAuthenticate` 停用策略與 Windows/IWA 效能對比測試。

RECOMMENDATION: PASS
```

---

## 審查問題回覆 (Review Answers)

### 1. 方案技術合理性與替代方案評估
* **評估結果**：**合理且技術健全。**
* **理由**：
  * **拒絕「僅使用共享程式庫（Library-only）」的理由具體且充分**：若由各產品直接引用程式庫，將導致憑證管理、連線池生命週期、Token 快取、元資料快取及重試邏輯在 5 到 10 個產品中重複實現，大幅增加憑證洩漏與配置漂移的風險。
  * **拒絕「通用透明代理（Transparent-proxy）」的理由具體且充分**：透明代理會暴露任意的 CRM Schema、URL 及標頭，擴大攻擊面並使稽核與授權變得不可預測。
  * **Gateway + 私有 WebApi 程式庫** 成功將安全邊界與執行期狀態集中管理，同時保留了低階 WebApi 程式庫的獨立可測試性。

### 2. 執行期資源與生命週期的隔離性
* **評估結果**：**隔離設計非常嚴密。**
* **理由**：
  * 每個憑證相關的執行期均由不可變的 `ProfileRuntimeKey`（包含 `profileId`、`immutableConfigurationGeneration`、`apiVersion`、`normalizedOrganizationOrigin`、`authMode` 和 `secretVersionFingerprint`）進行隔離，確保 HTTP 處理器、HttpClient、Token 快取及元資料快取不共享全域狀態。
  * 組織級別的併發與佇列狀態則由 `OrganizationAdmissionKey`（包含 `deploymentEnvironment` 和 `expectedOrganizationId`）進行隔離，確保在新舊世代重載（Reload）期間，併發預算不會翻倍。

### 3. 安全漏洞與不安全重試的防範
* **評估結果**：**無洩漏或逃逸路徑。**
* **理由**：
  * **防止調用端逃逸**：產品調用端僅能呼叫預先註冊的作業 ID，無法傳遞任何 CRM URL、標頭、查詢語法或設定檔名稱。
  * **防止憑證洩漏**：設定檔僅包含金鑰名稱引用，遙測與日誌在導出前會進行白名單過濾與脫敏。
  * **防止不安全重試**：非等冪寫入必須使用 CRM 替代鍵/Upsert，或透過分散式等冪帳本（Idempotency Ledger）進行控制。對於 dispatch 後結果未知的寫入（`OutcomeUnknown`），帳本會將其保留為不可重試，絕不自動重播。

### 4. CE 8.2/9.1 版本與驗證限制
* **評估結果**：**描述安全且符合實際。**
* **理由**：
  * 設計明確指出 CE 8.2 與 9.1 的相容性差異，且不假設 on-premises 支援 client-secret 或 WS-Trust 降級。
  * Windows 驗證採用嚴格的標記聯合（Tagged Union）設計，區分 `HostIdentity`（無密碼欄位）與 `SecretReference`（僅限非人服務帳戶）。
  * `AdfsOAuth` 拒絕密碼、ROPC、用戶端金鑰或憑證私鑰欄位，若環境無法提供非密碼 OAuth 授權，則該設定檔將被封鎖。

### 5. 性能與高可用性指標
* **評估結果**：**指標具體、可測試且符合 Dynamics 服務保護限制。**
* **理由**：
  * 併發限制公式 `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumGatewayReplicas)` 確保了在分散式限制器失效時，系統能退回到保守的本地分配，避免對 Dynamics 造成過載。
  * 尊重 429 的 `Retry-After` 標頭，並對 CSDL 解析與響應大小設定了嚴格的位元組限制。

### 6. 遷移範圍與 CI/CD 門檻
* **評估結果**：**非常具體且具備強制執行力。**
* **理由**：
  * 遷移範圍精確識別了現有的 `Microsoft.Crm.Sdk.Proxy` HintPath 違規、`ToolUtility.Tests` 的套件引用，以及約 200 個導入了 SDK 的源文件。
  * 提供了具體的 PowerShell/ripgrep 掃描指令，用於在 CI 中強制執行 no-SDK 檢查。
  * 測試與發布門檻（Release Gates）非常具體，包括確定性隔離測試、重載排空測試、Soak 測試及真實伺服器煙霧測試。

### 7. 矛盾與缺失識別
* **評估結果**：未發現架構設計上的矛盾或缺失，所有回迴避檢測（Regression Checks）均已完全落實。

---

## 審查發現分類 (Findings)

### Critical
* **無 (None)**：規格書已完全落實所有回歸檢查，無 Critical 級別的架構缺陷。

### Warning
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Preconditions)
* **說明**：規格書將 AD FS OAuth 非密碼授權列為 Feasibility Gate。如果目標環境的 AD FS 不支援非密碼的服務工作負載授權（Non-password service-workload grant），該設定檔將被封鎖。
* **建議**：實作團隊必須在 Phase 1 啟動前，確實與基礎設施團隊完成此項驗證，避免因環境限制導致專案進度受阻。

### Info
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.2)
* **說明**：關於 `PreAuthenticate` 的設定，設計中預設為停用，並要求在目標環境中進行對比測試後才能啟用。
* **建議**：這是一個非常安全的做法，能避免 connection-bound 驗證在多設定檔環境下產生跨設定檔的訊號干擾。實作時應嚴格遵守此測試流程。

---

## 結論

本規格書在架構設計、安全隔離、併發控制、等冪性保障、重載生命週期以及遷移路徑上都非常嚴密，且已完全落實了先前審查的所有回歸檢查（Regression Checks）。**推薦結論為 PASS**，可正式啟動實作階段。
