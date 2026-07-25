# Dynamics Access Gateway 架構規格書最終審查報告

## 1. 總體評估 (Summary)

本審查針對 **Dynamics Access Gateway 架構規格書** 進行最終審查，審查範圍包含：
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

### 評估結論：**通過 (PASS)**

該架構設計非常嚴密且完整，針對五至十個產品的 Dynamics 365 存取需求，提出了技術上完全可行且安全的解決方案。設計中徹底排除了對舊有 Dynamics SDK DLL 的依賴，改用純粹的 HTTP/OData v4 Web API，並透過雙主機模式（Gateway/Embedded）兼顧了生產環境的集中管理與開發環境的便利性。

在安全性與效能控制方面，設計引入了基於 `AdmissionEpoch` 與 `RuntimeHostSlotLease` 的組織級准入協調器、持久化等冪帳本（Idempotency Ledger）、嚴格的 `CanonicalKeyV1` 鍵編碼，以及零容忍的資源與憑證洩漏門檻，能有效防止併發超載、憑證洩漏與跨設定檔干擾。

---

## 2. 審查問題回覆 (Review Questions Answers)

### 1. 提案的 Gateway + 私有無 SDK WebApi 程式庫是否為 5-10 個產品提供了技術上健全的解答？是否因具體原因拒絕了「僅限程式庫」和「透明代理」的替代方案？
* **是，技術健全。** 
* **拒絕原因明確：**
  * **僅限程式庫 (Option A)**：在 `design.md` Section 2.2 中被拒絕。因為這會導致每個產品都需要獨立管理 CRM 憑證、設定檔、HTTP 連線狀態、Token 快取、中繼資料快取、重試與相容性邏輯，增加了憑證洩漏與版本漂移的風險。
  * **透明代理 (Option B)**：在 `design.md` Section 2.2 中被拒絕。因為透明代理允許呼叫者傳遞任意的資料表、URL、查詢與標頭，這會洩漏 CRM 綱要控制、擴大攻擊面，並使稽核與授權變得非確定性。
  * **推薦方案 (Option C & D)**：採用集中式 Gateway 作為生產預設，並提供 Embedded 模式作為開發/測試的受控例外，既能集中安全邊界，又保留了開發的靈活性。

### 2. HTTP 處理程序/HttpClient、Windows 憑證、OAuth Token 快取、中繼資料快取、重試/斷路器狀態、佇列/併發狀態以及重新載入生命週期，是否由足夠的不可變設定檔生成鍵 (Profile-generation key) 進行隔離？
* **是。** 
* 在 `design.md` Section 7.1 中，所有憑證相關的執行期狀態均由不可變的 `ProfileRuntimeKey` 進行隔離：
  `ProfileRuntimeKey = tuple(profileId, immutableConfigurationGeneration, apiVersion, normalizedOrganizationBaseUri, authMode, secretVersionFingerprint)`
* 該 Key 不包含明文秘密，且與用戶資訊（如 LINE ID、JWT）完全無關。
* 重新載入生命週期（Section 7.3）採用 `replace-and-drain` 機制，舊的 generation 在 drain 期間停止接收新工作，並在超時後被徹底 dispose，與新建立的 generation 完全隔離。

### 3. 設計是否留有任何跨設定檔路由、秘密洩漏、呼叫者提供的端點/標頭/設定檔逃逸、保留洩漏、陳舊執行期變更或不安全自動重試的途徑？
* **沒有。**
* **防止逃逸**：呼叫者僅能透過 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` 進行呼叫，無法提供自訂的 CRM URL、FetchXML 文字、標頭或憑證。
* **防止秘密洩漏**：設定檔僅包含秘密引用（Secret References），明文秘密僅在執行期從秘密提供者解析，且禁止寫入日誌或遙測。
* **防止陳舊變更**：配置重載使用不可變的 generation，不進行就地修改（no in-place mutation）。
* **防止不安全重試**：非等冪寫入必須使用 CRM 替代鍵/upsert，或透過持久化等冪帳本（Idempotency Ledger）控制。若帳本不可用，則在發送至 Dynamics 前直接失敗，且 `OutcomeUnknown` 的結果絕不自動重播。

### 4. 是否安全地描述了 CE 8.2/9.1 API 版本和驗證限制，而沒有假設 On-premise client-secret 支援或 WS-Trust 回退？
* **是。**
* 在 `design.md` Section 6.3 中，明確指出不承諾或嘗試 CE on-premises 的 client-secret/certificate 驗證（此為 Dataverse-only 功能）。
* 對於 IFD，`AdfsOAuth` 是一個獨立的嚴格結構，僅允許 authority/client-ID/target-specific feasibility-evidence/credential 引用，拒絕密碼、ROPC、client-secret 或憑證私鑰欄位。在 IFD 設定檔被核准前，必須通過 cold-start 服務流程驗證。
* 不使用 WS-Trust/SOAP fallback，完全使用 direct HTTP/OData v4 Web API。

### 5. 效能和高可用性聲明是否具備邊界、可測試，並與 Dynamics 服務保護 (Service Protection) 相容？
* **是。**
* 併發控制：`AggregateMaxInFlight` 和 `MaximumRuntimeHosts` 用於計算每個主機的 `LocalMaxInFlight`。
* 每個主機都必須在 `AdmissionEpoch` 下取得 `RuntimeHostSlotLease` 才能 Ready。
* 這些限制與 Dynamics 的服務保護限制（service protection limits）相容，並在 Phase 4 中設計了故障注入和負載測試來驗證。

### 6. 遷移範圍、無 SDK 強制檢查以及測試/發布門檻是否足夠具體？
* **是。**
* **遷移範圍**：在 `implement.md` Phase 0 和 Phase 6 中，明確列出了要移除的 SDK 依賴（如 `Microsoft.Crm.Sdk.Proxy` HintPath、`Microsoft.Xrm.*`、`Microsoft.PowerPlatform.Dataverse.Client` 等）。
* **無 SDK 強制檢查**：在 `implement.md` Phase 0 和 Phase 6 中，設計了 CI 報告與失敗門檻，並使用 `Verify-NoDynamicsSdk.ps1` 腳本進行掃描。
* **測試/發布門檻**：在 `design.md` Section 7.5 中定義了「零容忍」發布門檻，並在 `implement.md` Phase 4 中列出了詳細的測試案例（包括 fake-server 隔離測試、reload/drain 測試、soak 測試、故障注入測試等）。

### 7. 識別矛盾、缺失的明確決定或危險的假設。
* **無明顯矛盾或危險假設。**
* 設計中對於 `PreAuthenticate` 的啟用門檻（Section 7.2）以及 AD FS OAuth 的可行性驗證（Section 6.3）非常嚴謹，這能有效防止在 Windows/IWA 環境下發生連線綁定驗證的跨設定檔訊號洩漏。

### 8. Gateway/Embedded 主機模式 JSON 設計是否保留了核心安全屬性、允許安全的 Visual Studio 開發、禁止動態/用戶驅動的選擇，並正確協調跨主機模式的產能？
* **是。**
* **禁止動態選擇**：JSON 設計（`design.md` Section 4.1）區分了 `Gateway` 和 `Embedded` 模式，這是在啟動時由部署設定決定的，無法由使用者或請求動態選擇。
* **Visual Studio 開發**：`appsettings.Development.json` 可以選擇 `Embedded` 模式並指向 fake CRM 終端或本地 Gateway，但如果解析到生產環境的秘密或組織識別碼，則啟動會失敗，確保開發環境的安全。
* **產能協調**：不論是 Gateway 還是 Embedded 模式，只要目標是同一個 Dynamics 組織，都必須向同一個 `OrganizationAdmissionKey` 協調器註冊並佔用 `MaximumRuntimeHosts` 中的一個 slot，從而共同遵守 `AggregateMaxInFlight` 預算。

### 9. 安全預熱 (Warm-up) 設計是否加速了冷啟動/登入相鄰路徑，而沒有保留用戶特定的 Dynamics 連線、工作階段、LINE ID 或 Token？
* **是。**
* 在 `design.md` Section 10 中，預熱（warm-up）是服務主體（service-identity-only）的，僅獲取服務文件、CSDL 中繼資料快取和執行唯讀的 `WhoAmI`。
* 預熱是單飛（single-flight）的，且不包含任何使用者特定的資料（如 LINE ID、使用者 Token、密碼或 CRM 工作階段）。
* 登入請求只能加入已在運行的預熱任務，而不能建立使用者專屬的連線池項目或快取。

---

## 3. 迴歸檢查確認 (Regression Checks Confirmation)

經過逐項比對，以下先前審查的 regression checks 均已在修訂後的檔案中得到滿足：

1. **使用 `RuntimeHostSlotLease` 和 `AdmissionEpoch`**：已在 `design.md` Section 7.2.2 中落實，工作僅在 expiry fence 前被准入，逾期工作取消，且 slot 釋放前有隔離期（quarantine）。
2. **僅公開 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`**：已在 `design.md` Section 5 中落實，拒絕呼叫者提供的 CRM 綱要、動作、FetchXML 等。
3. **驗證 `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`**：已在 `design.md` Section 6.1.1 中落實，衍生 `LocalMaxInFlight`，限制 `MaxConnectionsPerServer`，並在生產環境保留至少兩個 Gateway 主機。
4. **共享 `OrganizationAdmissionKey` 容量**：已在 `design.md` Section 7.1 中落實，跨新舊 generation、別名及藍綠/灰度版本共享，防止 reload/rollout 時併發翻倍。
5. **持久化等冪帳本設計**：已在 `design.md` Section 9.3 中落實，使用原子具界限的鍵、固定保留/配額、不儲存原始 body/token/憑證，且 `OutcomeUnknown` 不自動重播。
6. **處理程序/代理/標頭、單飛取消、佇列 drain、解析邊界、遙測去識別化及洩漏門檻**：已在 `design.md` Section 7.2, 7.3, 7.5, 8.1, 9.3 中落實。
7. **`OrganizationAdmissionKey` 作為不可變共享租約命名空間**：已在 `design.md` Section 7.2.2 中落實，要求原子持久協調器，且 Windows 驗證使用嚴格的 `HostIdentity` 與 `SecretReference` 聯合體。
8. **規範元組編碼、基底 URI 驗證、重複感知 JSON 解析及安全滾動交接**：已在 `design.md` Section 7.1.1, 6.1, 6.1.1, 7.2.2 中落實。
9. **禁用呼叫者 FetchXML 且共享 Key 擁有單一設定**：已在 `design.md` Section 5 和 Section 6.1 中落實。
10. **單一 `OrganizationAdmissions` Map 且稽核保留具界限/Fail-safe**：已在 `design.md` Section 6.1 和 Section 9.3 中落實。
11. **使用證據安全的 CE 8.2/9.1 語言**：已在 `design.md` Section 6.2 和 Section 6.3 中落實。
12. **繫結至不可變版本/範本雜湊、限制 generation 數量、HttpClient 所有權可測試及服務主體預熱**：已在 `design.md` Section 7.2, 7.3, 10 中落實。

---

## 4. 發現與建議 (Findings & Suggestions)

### Critical (嚴重)
* **無。** 設計方案非常嚴謹，未發現任何架構缺陷或安全漏洞。

### Warning (警告)
* **無。**

### Info (提示)
1. **檔案路徑**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` (Section 7.2)
   * **原理說明**：設計中對於 `PreAuthenticate` 的啟用門檻以及 AD FS OAuth 的可行性驗證非常嚴謹，這能有效防止在 Windows/IWA 環境下發生連線綁定驗證的跨設定檔訊號洩漏。
2. **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Phase 4)
   * **原理說明**：Phase 4 的測試計劃非常詳盡，特別是針對 `RuntimeHostSlotLease` 的隔離區（quarantine）機制、`CanonicalKeyV1` 的長度前綴編碼驗證，以及等冪帳本在 `OutcomeUnknown` 狀態下的 fail-safe 行為，這些測試能確保系統在極端故障下的穩定性。

---

## 5. 肯定之處 (Positive Notes)

* **雙主機模式的統一核心**：將 Gateway 與 Embedded 模式的底層 Web API 核心統一，既保證了開發時的便利性（可在 Visual Studio 中直接偵錯），又確保了兩者在生產環境中遵循完全相同的安全、准入與等冪規範。
* **嚴格的容量與准入控制**：透過 `AdmissionEpoch` 與 `RuntimeHostSlotLease` 強制限制最大主機數，並在協調器端引入隔離期（quarantine），能有效防止因容器自動伸縮（HPA）或滾動更新（Rolling Update）導致的 Dynamics 併發超載。
* **Fail-safe 的等冪與稽核設計**：稽核意圖（Audit Intent）與等冪帳本的狀態轉移設計非常嚴密，確保了在網路中斷或進程崩潰時，系統不會遺失稽核記錄，也不會盲目重播不確定的寫入作業。
