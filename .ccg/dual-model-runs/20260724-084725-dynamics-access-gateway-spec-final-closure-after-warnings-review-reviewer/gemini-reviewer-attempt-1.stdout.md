# Dynamics Access Gateway 架構規格書審查報告

本報告針對 `Dynamics Access Gateway` 的規劃與設計文件進行審查，評估其在架構合理性、安全性、效能限制、遷移路徑以及與 Dynamics 365 CE 8.2/9.1 的相容性設計。

## 1. 總體評估 (Summary)

本次審查的規劃文件（`prd.md`、`design.md`、`implement.md` 及 `2026-07-23-dynamics-access-gateway-design.md`）展現了極高水準的架構設計與嚴謹度。設計完全符合「無 SDK」的最終狀態要求，並針對先前審查中提出的所有迴歸檢查點（Regression Checks）進行了詳盡的修訂與落實。

架構採用 **「雙主機、單核心」（Two-host, One-core）** 的設計，預設以集中式的 `Gateway` 服務作為生產環境邊界，並允許 `Embedded` 模式作為開發、測試或特定隔離部署的替代方案。此設計在確保憑證安全、並行度控制（Admission Control）與開發便利性（Visual Studio 偵錯）之間取得了極佳的平衡。

---

## 2. 審查問題回覆 (Review Questions & Answers)

### Q1: 方案合理性與替代方案拒絕理由
* **評估**：**技術上完全合理。**
* **說明**：設計文件清楚對比並拒絕了 Option A（僅程式庫）與 Option B（透明代理）。Option A 會導致憑證與連線池管理分散在 5-10 個產品中，增加洩漏與漂移風險；Option B 則會暴露 CRM 綱要並擴大攻擊面。Gateway + 私有 WebApi 程式庫能有效收斂安全邊界與相容性邏輯。

### Q2: 資源與生命週期的世代隔離 (Generation Isolation)
* **評估**：**隔離設計非常徹底。**
* **說明**：所有憑證、HttpClient、Token 快取、Metadata 快取與重試狀態皆由 `ProfileRuntimeKey`（包含配置世代與秘密指紋）進行隔離。配置重載採用 `replace-and-drain` 機制，舊世代在排空後會被完全銷毀，不留殘留。

### Q3: 逃逸路徑、憑證洩漏與不安全重試防範
* **評估**：**無逃逸路徑，防護設計嚴密。**
* **說明**：呼叫端僅能傳遞邏輯別名與 `capabilityOperationId`，無法注入自訂標頭、FetchXML 或 CRM 綱要。寫入操作必須透過持久化等冪帳本（Idempotency Ledger）進行原子性校驗，對於 `OutcomeUnknown` 的寫入禁止自動重試，有效避免重複寫入。

### Q4: CE 8.2/9.1 版本與驗證限制
* **評估**：**安全且符合地端限制。**
* **說明**：明確指出不承諾 CE 地端的 client-secret 支援，IFD 模式必須通過非密碼服務工作負載 OAuth 流程可行性驗證（FeasibilityEvidenceId），否則保持不可用，不允許回退到 ROPC 或 WS-Trust。

### Q5: 效能與高可用性限制
* **評估**：**指標明確且具備防禦性。**
* **說明**：定義了明確的延遲指標（p99 授權 < 1ms，Gateway 額外延遲 p99 < 15ms）。並行度控制透過 `OrganizationAdmissions` 限制實體組織的總並行度，且在分散式限制器失效時能安全回退到單機保守分配（LocalMaxInFlight）。

### Q6: 遷移範圍、no-SDK 強制檢查與測試門檻
* **評估**：**具體且具備可執行性。**
* **說明**：遷移計劃明確指出了現有的 HintPath 違規與套件耦合。CI 門檻中包含了 `Verify-NoDynamicsSdk.ps1` 掃描腳本，並在 CI gate matrix 中定義了各階段的失敗條件與產出物。

### Q7: 矛盾、缺失決策或危險假設
* **評估**：**未發現明顯矛盾或缺失。**
* **說明**：設計已將所有先前審查的 regression 項目納入，包含 `RuntimeHostSlotLease`、`AdmissionEpoch`、`CanonicalKeyV1` 等細節，設計非常完備。

### Q8: Gateway/Embedded JSON 設計與容量協調
* **評估**：**安全邊界清晰。**
* **說明**：JSON 僅作為啟動綁定，Embedded 模式必須通過簽章資訊清單或中央登錄表驗證。不論何種模式，只要指向同一個實體組織，都必須使用同一個 `OrganizationAdmissionKey` 進行協調，防止容量加倍。

### Q9: 安全預熱設計 (Safe Warm-up)
* **評估**：**符合無狀態與無使用者資料殘留原則。**
* **說明**：預熱僅針對服務主體（service-identity-only），不包含任何使用者特定的 LINE ID、Token 或 Session，且登入請求只能加入已在運行的預熱任務，不會建立使用者專屬的連線。

### Q10: 遷移前的 Organization-call 覆蓋矩陣
* **評估**：**已作為 Phase 0 的強制門檻。**
* **說明**：矩陣包含參數編碼上下文、v8.2/v9.1 證據、稽核類別等，且 CI 會自動將此矩陣與產生的操作登錄表進行比對。

### Q11: 產品 CI/啟動門檻防範繞過
* **評估**：**防護強度足夠。**
* **說明**：已遷移的產品原始碼根目錄會啟用強制 CI 門檻，禁止引用 `Microsoft.Xrm*`、`Microsoft.CrmSdk*` 等，除非該檔案在暫時遺留矩陣中。

### Q12: 產品 JSON 信任邊界與簽章驗證
* **評估**：**邊界明確。**
* **說明**：Embedded 模式的綁定與協調器引用必須在解析任何 CRM 秘密或槽位前，通過簽章資訊清單或中央登錄表驗證，否則保持 NotReady。

### Q13: ADR、佇列公平性與容量擁有者工件
* **評估**：**具體且可測試。**
* **說明**：Phase 2 開始前必須撰寫 ADR 選定後端。佇列公平性採用每工作負載佇列上限與赤字/加權公平調度。`OrganizationAdmissions` 包含擁有者、測量日期等，且 CI 會比對 IaC/HPA 設定。

### Q14: 跨環境設定檔容量預算合併
* **評估**：**設計正確。**
* **說明**：若兩個不同環境標籤的設定檔指向同一個實體組織，啟動將會失敗，除非有明確核准的跨環境 `OrganizationAdmissions` 項目將它們合併，防止預算加倍。

### Q15: Embedded 簽章資訊清單與登錄表信任模型
* **評估**：**模型完整。**
* **說明**：詳細規範了簽章資訊清單與登錄表回應的欄位，並規範了金鑰輪轉、單調版本防回滾、快取 TTL、超時與失敗關閉（fail-closed）行為。

### Q16: 實作計劃的 CI 門檻矩陣
* **評估**：**非常具體。**
* **說明**：CI gate matrix 詳細列出了各個階段的門檻、指令、失敗條件與產出物，涵蓋了所有要求的檢查項目。

---

## 3. 發現與建議 (Findings & Suggestions)

### 3.1 嚴重問題 (Critical)
* **無嚴重問題。** 設計文件已完美解決了所有硬性非功能性需求與安全性邊界。

### 3.2 警告事項 (Warning)
* **無警告事項。** 所有的邊界條件、錯誤處理、超時與失敗關閉（fail-closed）機制皆已在設計中明確規範。

### 3.3 資訊提示 (Info)
* **【Info】ADR 撰寫時程確認**：
  * **位置**：`implement.md` Preconditions / Phase 2
  * **說明**：設計中要求在 Phase 2 開始前必須完成選擇持久化協調器、等冪帳本與稽核後端的 ADR。建議在 Phase 1 結束時即啟動此 ADR 的撰寫與評估，以確保 Phase 2 的實作能無縫接軌。
* **【Info】Linux 環境下的 Kerberos 測試**：
  * **位置**：`design.md` Section 6.3
  * **說明**：對於 Windows/IWA 設定檔，若部署於 Linux 容器環境，必須依賴 Linux Kerberos/keytab。由於此部分涉及複雜的基礎設施設定，建議在 Phase 3 的環境驗證中，儘早安排 target-like 環境的冒煙測試。

---

## 4. 驗證報告與評分 (Validation Report)

針對 `/ccg:bugfix` 驗證與架構合規性進行評分：

```
VALIDATION REPORT
=================
User Experience (Developer Experience): 20/20 - 雙主機設計（Gateway/Embedded）完美兼顧了生產環境的安全邊界與開發人員在 Visual Studio 中的偵錯便利性，且 JSON 模式切換規則清晰。
Visual Consistency (API & Schema Design): 20/20 - 統一的 POST /v1/organizations/{alias}/operations/{capabilityOperationId} 介面，且 JSON Schema 具備重複鍵檢查與嚴格的 Tagged Union 驗證，設計高度一致。
Accessibility (Security & Isolation): 20/20 - 具備零容忍的憑證與 Session 隔離設計，Embedded 模式引入簽章資訊清單驗證，且跨環境指向相同實體組織時有強制的預算合併檢查，安全性極高。
Performance (Concurrency & Resource Management): 20/20 - 透過 OrganizationAdmissions 進行全域並行度控制，具備單機保守回退機制，且預熱設計為 service-identity-only，避免了資源洩漏與並行度超載。
Browser Compatibility (Protocol & Standards Compliance): 20/20 - 採用標準的 OData v4 HTTP 協定，明確區分 CE 8.2/9.1 的相容性差異，且預設關閉壓縮以防範安全漏洞，符合現代 Web 安全標準。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無嚴重或警告級別的問題。

RECOMMENDATION: PASS
```

## 5. 總結 (Positive Notes)

* **設計嚴謹度極高**：設計文件不僅滿足了「無 SDK」的要求，更在並行度控制、等冪性帳本、稽核意圖（Audit Intent）以及 Embedded 模式的信任模型上，提出了非常具體且具備防禦性的架構設計。
* **測試與 CI 規劃完善**：在 `implement.md` 中規劃了極為詳盡的測試矩陣（包含單元、整合、Soak、故障注入與真實伺服器冒煙測試），並將 no-SDK 掃描與覆蓋率矩陣比對納入 CI 強制門檻，確保了架構設計在實作階段不會發生漂移。
