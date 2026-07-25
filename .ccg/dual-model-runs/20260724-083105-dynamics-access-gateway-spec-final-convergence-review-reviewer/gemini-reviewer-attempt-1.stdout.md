以下為針對 **Dynamics Access Gateway** 架構規格書的審查報告：

# 1. 摘要 (Summary)

本審查報告針對 Dynamics Access Gateway 架構規格書（包含 PRD、設計文件、實作計畫及架構規格書）進行全面評估。整體而言，該規格書設計極為嚴謹，針對無 SDK 限制、連線池隔離、並行控制、租約管理、等冪性帳本、審計追蹤以及防洩漏機制等硬性品質要求，均給出了具體且技術上可行的方案。

---

# 2. 評估與回覆 (Review Answers)

針對 16 個審查問題與回歸檢查點，本規格書的設計均已妥善處理：
1. **架構合理性**：採用 Gateway + 私有無 SDK WebApi 程式庫的「雙主機、一核心」設計，並在 `design.md` 第 2.2 節中明確駁回了「僅程式庫」與「透明代理」的替代方案，理由充分。
2. **隔離性**：使用包含配置世代、API 版本、組織 URI、驗證模式與金鑰指紋的 `ProfileRuntimeKey` 進行完整隔離，生命週期管理清晰。
3. **安全性與防洩漏**：嚴格限制產品端僅能呼叫預先註冊的 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`，禁止傳遞自訂 URL、標頭或 FetchXML，並透過 replace-and-drain 機制防止 stale runtime 突變。
4. **版本與驗證約束**：明確區分 CE 8.2/9.1 路由，不假設 on-premise client-secret 支援，且將 IFD 驗證視為可行性閘門，不使用 WS-Trust 降級。
5. **效能與高可用性**：定義了 `AggregateMaxInFlight` 與 `MaximumRuntimeHosts`，並在分散式限制器失效時退回到保守的單機分配，確保 Dynamics 服務保護。
6. **遷移與測試閘門**：實作計畫中包含了 Phase 0 至 Phase 6 的具體步驟，並在 CI 閘門矩陣中定義了具體的檢測指令（如 `Verify-NoDynamicsSdk.ps1`）。
7. **矛盾與遺漏**：未發現明顯的架構矛盾。
8. **主機模式 JSON 設計**：`Gateway` 與 `Embedded` 模式的 JSON 結構嚴格對稱，且 Embedded 模式必須通過簽章資訊清單或中央註冊表驗證，無法透過修改本地 JSON 越權。
9. **預熱設計**：採用服務主體身份進行單 flight 預熱，不保留任何使用者特定連線或 LINE ID 等敏感資訊。
10. **呼叫覆蓋矩陣**：Phase 0 要求建立完整的 Organization-call 覆蓋矩陣，並作為 CI 的完整性檢查閘門。
11. **CI 啟動閘門**：CI 掃描會強制阻斷任何引入舊版 SDK 或 raw 連線字串的提交。
12. **信任邊界**：Embedded 模式採用簽章資訊清單與中央註冊表雙重驗證，具備 fail-closed 特性。
13. **協調器與等冪性帳本**：要求在 Phase 2 前完成 ADR，且等冪性帳本採用 `CanonicalKeyV1` 編碼，狀態機支援 `OutcomeUnknown` 處理。
14. **跨環境預算**：相同實體組織的跨環境 Profile 強制合併至單一 `OrganizationAdmissions` 預算中。
15. **Embedded 信任模型**：詳細定義了簽章金鑰輪轉、TTL、撤銷、防回滾等 fail-closed 行為。
16. **CI 閘門矩陣**：在 `implement.md` 第 14 節中提供了完整的 CI 閘門矩陣表格。

---

# 3. 審查發現 (Findings)

### **Critical (嚴重)**
*無。*
*原因*：規格書已完全覆蓋所有硬性安全與架構要求，包含 `RuntimeHostSlotLease` 租約隔離、`AdmissionEpoch` 世代控制、等冪性帳本的 `OutcomeUnknown` 狀態處理，以及 CI 閘門的 PowerShell 掃描腳本設計，設計邏輯閉環且無安全漏洞。

### **Warning (警告)**
1. **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Phase 0.3)
   * **問題描述**：覆蓋矩陣中要求包含 "XML/OData encoding context for each parameter"，但未明確說明 CI 閘門如何自動驗證程式碼中的參數是否確實使用了對應的上下文編碼器（Context-specific encoder），這可能導致開發人員在實作時不小心使用字串拼接而繞過安全編碼。
   * **建議修正**：在實作計畫中，明確要求 CI 靜態分析或單元測試必須掃描所有註冊的 Operation 範本，驗證其參數綁定均有宣告對應的編碼上下文，否則拒絕建置。

2. **檔案路徑**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` (Section: Performance and release gates)
   * **問題描述**：規格書中提到 "Automatic decompression and ambient `Accept-Encoding` are disabled in the initial release; any received `Content-Encoding` is rejected before JSON/XML parsing."。雖然這能有效防止 Zip Bomb 等安全威脅，但在 CE 8.2/9.1 的大數據量查詢或中繼資料（CSDL）下載時，停用壓縮會顯著增加網路頻寬消耗與傳輸延遲，可能影響 p99 延遲目標。
   * **建議修正**：建議在 Phase 3 效能評估時，將「啟用受限的 GZip 壓縮（限制解壓後最大位元組數）」列為效能調優的備選方案，並在規格書中預留此擴充說明的可行性評估。

### **Info (提示)**
1. **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.2.2)
   * **說明**：規格書中明確指出 "Windows gMSA is a Windows-host identity option, not a synonym for Linux Kerberos/keytab hosting."，這對於跨平台部署（Windows IIS vs Linux Container）的驗證邊界釐清非常有幫助，有助於避免部署時的環境配置混淆。
2. **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.2.1)
   * **說明**：設計中將 `CanonicalOrganizationCapacityKey` 與 `RuntimeHostSlotLeaseNamespace` 區分開來，防止因環境標籤（Environment Label）不同而導致對同一實體 Dynamics 組織的預算加倍，此設計非常精準。

---

# 4. 建議 (Suggestions)
- **CI 靜態分析強化**：建議在 Phase 6 引入 Roslyn Analyzer，自動偵測產品專案中是否含有任何對 `Microsoft.Xrm` 或 `IOrganizationService` 的直接引用，以程式碼層級的強型別檢查代替單純的檔案字串掃描。
- **等冪性帳本效能調優**：等冪性帳本在寫入前進行 Redis/資料庫的原子性讀寫，建議在 Phase 4 壓測時重點評估此步驟對整體寫入延遲的影響。

---

# 5. 肯定之處 (Positive Notes)
- **防洩漏設計極為徹底**：規格書中對 `ProfileRuntimeKey` 的設計、replace-and-drain 機制、以及弱引用哨兵（weak-reference sentinels）的測試要求，展現了對記憶體與連線洩漏的零容忍態度。
- **邊界清晰**：明確區分了 `CanonicalOrganizationCapacityKey` 與 `RuntimeHostSlotLeaseNamespace`，從根本上杜絕了因部署環境標籤不同而導致 Dynamics 組織並行預算加倍的風險。

---

# 6. 評分報告 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 19/20 - 提供了 Embedded 模式供 Visual Studio 本地開發與測試，且設定檔完全不包含生產環境機密，開發者體驗良好。
Visual Consistency: 20/20 - 規格書結構完整，術語一致，圖表與實作步驟對應清晰。
Accessibility: 20/20 - 作為後端架構規格書，其 API 權限控制、主機模式綁定與安全邊界設計非常完善，系統間整合的相容性與安全性極佳。
Performance: 19/20 - 連線池重用、中繼資料快取與單 flight 預熱設計優良，唯獨停用壓縮可能在傳輸大體積 CSDL 時帶來些許延遲。
Browser Compatibility: 20/20 - 採用標準的 OData v4 REST API，不依賴任何特定瀏覽器或 SDK，相容性極佳。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Warning] 覆蓋矩陣的編碼上下文缺乏 CI 自動化驗證機制的具體說明。
- [Warning] 停用自動解壓縮可能影響大體積資料傳輸的效能，需預留調優評估。

RECOMMENDATION: PASS
```
