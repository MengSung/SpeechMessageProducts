# VALIDATION REPORT
=================
**整合體驗 (User Experience)**: 20/20 - 提供 Gateway 與 Embedded 雙主機模式，既保證了生產環境的安全與集中管理，又兼顧了開發人員在 Visual Studio 中的偵錯與測試便利性，且兩者使用相同的核心與合約，整合體驗極佳。
**合約一致性 (Visual/Contract Consistency)**: 20/20 - 透過嚴格的 OpenAPI/REST 合約與 `IOrganizationOperationsClient` 抽象，確保所有產品調用 Dynamics 的介面與行為完全一致，杜絕了直接使用 SDK 帶來的混亂。
**安全准入與隔離性 (Accessibility/Security)**: 20/20 - 實作了嚴格的 `RuntimeHostSlotLease`、`AdmissionEpoch`、`OrganizationAdmissionKey` 以及 `CanonicalKeyV1` 編碼，確保多租戶/多設定檔之間的絕對隔離，並有 fail-closed 的安全准入機制。
**效能與容量控制 (Performance)**: 20/20 - 透過連線與 Metadata 快取重用、`LocalMaxInFlight` 衍生計算、分散式與本地雙重限制器、等冪帳本以及服務身分專屬的預熱機制，確保 Dynamics 組織的容量預算不被超限，且效能目標明確可測。
**環境與版本相容性 (Browser/Environment Compatibility)**: 20/20 - 明確區分並支援 CE 8.2 與 9.1 的 API 路由與相容性限制，且針對 Windows/IWA 與 AD FS OAuth (IFD) 提供了嚴格的可行性驗證門檻，不作未經證實的相容性假設。

**TOTAL SCORE**: 100/100

**ISSUES FOUND**:
- 無 (No critical issues found. The specification is exceptionally robust and addresses all regression checks and architectural constraints.)

**RECOMMENDATION**: PASS

---

## 1. Summary (總體評估)
本審查針對 Dynamics Access Gateway 的規劃文件（包含 PRD、詳細設計、實作計劃及架構規格書）進行了全面評估。該設計方案在技術上非常健全，成功解決了五到十個產品在無 SDK 情況下安全、高效訪問 Dynamics 365 CE 8.2/9.1 On-Premises 的架構挑戰。

設計中明確拒絕了「僅使用共用程式庫」與「通用透明代理」的替代方案，並透過「Gateway 預設 + 受控 Embedded 模式」實現了安全邊界與開發便利性的平衡。所有關於連線池隔離、憑證安全、併發控制、等冪寫入、版本相容性以及 CI 門檻的設計均符合最高標準，且完全落實了先前審查的所有回歸檢查點（Regression Checks）。

---

## 2. Review Questions Detailed Answers (審查問題逐項回覆)

### Q1: 方案合理性與替代方案評估
* **評估結果**: 健全且合理。
* **依據**: `design.md` Section 2.2 明確比較並拒絕了 Option A (Library-only) 與 Option B (Transparent proxy)。Option A 會導致憑證、快取與重試邏輯散落於各產品，增加洩漏與漂移風險；Option B 則會暴露任意 CRM 綱要與 URL，擴大攻擊面。Option C (Gateway) 與 Option D (Embedded) 的結合在維持單一核心與安全合約的同時，提供了靈活的部署與開發偵錯支援。

### Q2: 執行期狀態與生命週期的隔離性
* **評估結果**: 隔離性設計非常嚴密。
* **依據**: `design.md` Section 7.1 與 7.2 明確指出，每個憑證承載的執行期均由 `ProfileRuntimeKey`（包含設定檔 ID、配置世代、API 版本、正規化 URI、驗證模式與秘密指紋的 tuple）唯一標識。重新載入時採用 replace-and-drain 機制，舊世代會被完全 dispose，且不允許在原地修改 live profile。

### Q3: 安全漏洞與逃逸路徑防範
* **評估結果**: 無安全逃逸路徑。
* **依據**: 調用端僅能發送邏輯別名與核准的 `capabilityOperationId`，禁止發送任何 CRM 綱要、原始 OData、FetchXML 文本或自訂 Header。`nextLink` 追蹤必須嚴格限制在 `ApprovedWebApiRoot` 之下。Telemetry 與稽核日誌均經過 allowlisted/redacting 轉接器過濾，防止敏感資訊與憑證洩漏。

### Q4: CE 8.2/9.1 版本與驗證限制
* **評估結果**: 描述安全且符合實際。
* **依據**: `design.md` Section 6.3 明確指出不應承諾或默默嘗試 CE on-premises 的 client-secret 支援。Windows/IWA 採用嚴格的 tagged union（`HostIdentity` 與 `SecretReference` 互斥且無明文）；IFD 則要求 target-specific 的非密碼服務工作流可行性驗證，否則設定檔保持不可用，杜絕了 WS-Trust/SOAP 降級。

### Q5: 效能與高可用性指標
* **評估結果**: 指標具體且與 Dynamics 服務保護相容。
* **依據**: 併發限制由 `OrganizationAdmissions` 的 `AggregateMaxInFlight` 與 `MaximumRuntimeHosts` 決定，每個主機的 `LocalMaxInFlight` 為衍生計算值，且 `MaxConnectionsPerServer` 不得超過此限制。當分散式限制器不可用時，會退回到保守的本地分配，確保總併發不超限。

### Q6: 遷移範圍與 CI 門檻的具體性
* **評估結果**: 非常具體且可執行。
* **依據**: `implement.md` Section 14 定義了完整的 CI gate matrix，包含 Legacy SDK inventory、Product JSON contract、Runtime isolation、Capacity and lease safety、Web API compatibility、Performance/leak soak 以及 Final no-SDK enforcement。

### Q7: 矛盾、遺漏決策或危險假設
* **評估結果**: 未發現矛盾或危險假設。
* **依據**: 方案將 Linux 上的 Kerberos/keytab 支援與 IFD 的 AD FS OAuth 均列為實作前置的可行性驗證門檻（feasibility gate），這在架構上是非常安全的防禦性設計，避免了未經證實的技術承諾。

### Q8: Gateway/Embedded 模式的 JSON 設計與容量協調
* **評估結果**: 設計安全且協調正確。
* **依據**: 產品 JSON 僅在啟動時解析，不允許動態或請求驅動的選擇。在 Embedded 模式下，綁定與准入協調器引用必須通過已簽署的資訊清單（signed manifest）或中央登錄表驗證，否則 startup 會 fail closed。兩者均使用相同的 `OrganizationAdmissionKey` 進行容量協調。

### Q9: 安全預熱設計與用戶資料隔離
* **評估結果**: 預熱設計安全。
* **依據**: 預熱是低優先權、服務身分專屬的單一飛航（single-flight）動作，僅預熱服務文件、CSDL 快取與進行唯讀的 `WhoAmI` 探測。登入只能加入已在運行的單一飛航預熱，不能建立用戶專屬的預熱或連線池項目，亦不保留任何用戶帳戶、LINE ID、用戶 Token、Cookie 或工作階段。

### Q10: 遷移前的 Organization-call 覆蓋矩陣
* **評估結果**: 已明確要求。
* **依據**: Phase 0 明確要求在遷移任何工作負載前，必須建立 Organization-call coverage matrix，且列出了所需的 12 個欄位，不允許使用通用的 "Execute" 代理。

### Q11: 遷移產品的 CI/啟動門檻強度
* **評估結果**: 強度足夠。
* **依據**: Phase 0 和 Phase 6 明確要求使用 `Verify-NoDynamicsSdk.ps1` 進行 CI 掃描，且該腳本會使用 `no-sdk-source-roots.json`（包含所有生產與測試專案目錄）進行掃描，禁止任何 legacy SDK/pool 的繞過。

### Q12: 產品 JSON 的信任邊界與簽署驗證
* **評估結果**: 邊界明確。
* **依據**: 在 Embedded 模式下，`ProductProfileBinding` 和 `OrganizationAdmissionCoordinatorRef` 必須通過已簽署的資訊清單（signed manifest）或中央登錄表驗證，否則 startup 會 fail closed 並保持 NotReady。

### Q13: 持久協調器/帳本/稽核 ADR 與佇列公平性
* **評估結果**: 具體且可測試。
* **依據**: 要求在 Phase 2 開始前撰寫 ADR，選定持久協調器、等冪帳本和稽核保留後端。佇列公平性明確要求實作每工作負載佇列上限、赤字/加權公平調度（deficit/weighted fair dispatch）與老化/飢餓限制。

### Q14: 跨環境設定檔的容量預算合併
* **評估結果**: 已強制合併。
* **依據**: 若兩個不同 `deploymentEnvironment` 標籤的設定檔指向同一個物理 Dynamics 組織，啟動將失敗，除非有一個明確核准的跨環境 `OrganizationAdmissions` 條目合併其預算。

### Q15: Embedded 簽署資訊清單與登錄表信任模型
* **評估結果**: 具體且安全。
* **依據**: 詳細定義了已簽署資訊清單的 schema。過期和單調版本檢查可防止回滾（anti-rollback）。超出 TTL 的逾時、明確撤銷、無效簽章或策略拒絕都會使 Embedded 保持 NotReady（fail-closed）。

### Q16: 實作計劃的 CI 門檻矩陣
* **評估結果**: 具體且完整。
* **依據**: `implement.md` Section 14 (Validation commands) 中提供了一個非常具體的 CI gate matrix 表格，列出了各個 Gate、對應的指令/工作流、失敗條件以及產出的 Artifact。

---

## 3. Accessibility Issues (安全准入與隔離性問題)
* **無 Critical 或 Warning 級別問題**。
* **Info**: 關於 `AdfsOAuth` 的可行性驗證，設計中明確指出不支援 ROPC 或 end-user 密碼儲存，這在安全性上是非常正確的決定，但可能會對某些現有的 IFD 整合帶來挑戰（需要基礎設施配合調整為非密碼服務工作流）。這在實作前置條件中已被列為阻礙點（blocker），這是非常負責任的設計。

---

## 4. Design Issues (設計一致性問題)
* **無 Critical 或 Warning 級別問題**。
* **Info**: 在 `design.md` 中，`CanonicalKeyV1` 的定義中使用了 `bytes = ASCII(kind) + 0x00 ...`，這在實作時需要確保所有欄位名稱和值都正確編碼，且在 C# 中有對應的結構化 equality 實作。這在設計中已經有明確的測試要求。

---

## 5. Suggestions (改進建議)
* **建議 1 (Info)**: 在實作 `Verify-NoDynamicsSdk.ps1` 時，建議在 CI 環境中加入對該腳本本身的單元測試，確保其在 Windows (PowerShell) 與 Linux (pwsh) 環境下的行為完全一致，特別是對於 `Select-String` 與 `ripgrep` 的回退邏輯。
* **建議 2 (Info)**: 由於 `PreAuthenticate` 預設為禁用，建議在 Phase 2 的測試中，建立一個專門的基準測試案例，對比啟用與禁用 `PreAuthenticate` 在 Windows/IWA 下的 TCP 連線建立次數與延遲，以作為未來是否啟用的數據支持。

---

## 6. Positive Notes (優秀設計點)
* **雙主機模式的對等性**: 設計成功實現了「Two-host, One-core」的理念，使得開發人員可以在 Visual Studio 中使用 Embedded 模式進行完整的整合測試，而不需要部署複雜的 Gateway 微服務，同時又保證了兩者使用完全相同的 Web API 核心與准入控制。
* **嚴格的容量與租約控制**: 引入 `RuntimeHostSlotLease`、`AdmissionEpoch` 與 `OrganizationAdmissionKey`，並強制要求在租約失效時立即 fail-closed，這極大地保護了 Dynamics 365 伺服器免受併發風暴的衝擊。
* **等冪帳本與稽核意圖的原子性**: 將等冪帳本的 `Pending` 狀態與稽核意圖的 `Reserved` 狀態在同一個持久交易中建立，確保了寫入操作的確定性，避免了因網路中斷或進程崩潰導致的重複寫入或稽核遺失。
