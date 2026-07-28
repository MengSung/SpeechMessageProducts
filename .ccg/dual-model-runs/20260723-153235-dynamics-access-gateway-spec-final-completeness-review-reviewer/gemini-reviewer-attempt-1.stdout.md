# Dynamics Access Gateway 架構規格書完整性審查報告

本報告針對 Dynamics Access Gateway 系統架構規劃文件進行詳細審查，評估其技術可行性、安全性、效能、相容性以及遷移路徑的完整性。

---

## 1. 總體評估 (Summary)

本次審查的規劃文件（包括 PRD、詳細設計、實作計畫及架構規格書）展現了極高水準的嚴謹性與完整性。設計方案成功地在**不使用任何 Dynamics 365 SDK DLL** 的前提下，為 5 至 10 個以上的產品提供了一個安全、可控且高效的 Dynamics 365 存取閘道（Gateway）與嵌入式主機適配器（Embedded Host Adapter）雙模式架構。

設計中對於連線池管理、組織准入控制（Admission Control）、租約機制（RuntimeHostSlotLease）、冪等性帳本（Idempotency Ledger）以及審計意圖（Audit Intent）等關鍵安全與效能機制均給出了具體且可測試的規範，完全滿足了先前審查的所有回歸檢查（Regression Checks）要求。

---

## 2. 審查問題回覆 (Review Answers)

1. **架構合理性**：Gateway + 私有 no-SDK WebApi 程式庫的設計非常合理。設計中明確拒絕了「Library-only」（會導致憑證與連線狀態分散管理）與「Transparent Proxy」（會洩漏 CRM 結構且難以進行細粒度授權與審計）的替代方案，理由具體且充分。
2. **執行階段隔離**：HTTP 處理常式、HttpClient、Windows 憑證、OAuth Token 快取、中介資料快取及重試狀態均由不可變的 `ProfileRuntimeKey`（包含設定世代與金鑰指紋的元組）進行嚴格隔離。
3. **安全防禦**：設計完全封鎖了呼叫端自訂端點、標頭、FetchXML 或設定檔的途徑。設定檔重載採用 replace-and-drain 機制，不允許就地修改。不確定結果（OutcomeUnknown）的寫入被禁止自動重試，有效防止了重複寫入與憑證洩漏。
4. **相容性與驗證**：明確區分了 Web API 路由驗證與 CE 產品版本證明的差異。對於 IFD 部署，將 `AdfsOAuth` 視為獨立的嚴格結構，要求目標環境通過非密碼服務流程的可行性驗證，拒絕 WS-Trust/SOAP 降級。
5. **效能與高可用性**：並行預算由 `OrganizationAdmissions` 集中宣告，`LocalMaxInFlight` 為依據主機上限計算出的保守分配，當分散式限制器失效時會安全退回此分配。定義了明確的延遲指標與 soak 測試規範。
6. **遷移與門檻**：遷移範圍誠實且具體（識別出約 200 個源檔案的耦合）。實作計畫中定義了 `Verify-NoDynamicsSdk.ps1` 指令碼，並在 CI 中加入強制性門檻，防止遺留 SDK 繞過。
7. **技術決策推遲**：將 durable coordinator/ledger/audit 的具體技術選型推遲至 Phase 2 開始前的 ADR，並明確定義了該 ADR 必須滿足的原子性原語，此決策合理且符合架構設計原則。
8. **雙模式 JSON 設計**：產品 JSON 僅作為啟動時的綁定文件，Embedded 模式的綁定與協調器引用必須通過簽章或中央登錄檔驗證。不論何種模式，均計入 `MaximumRuntimeHosts` 並共享同一個 `OrganizationAdmissionKey` 租約空間。
9. **安全預熱**：預熱僅針對服務身分（service-identity-only）進行，且為單一飛行（single-flight）。登入請求僅能加入已在運行的預熱，絕不在連線池或快取中儲存使用者專屬的 Token、LINE ID 或工作階段。
10. **覆蓋矩陣**：Phase 0 明確要求在遷移前建立 Organization-call 覆蓋矩陣，將每個舊有呼叫點映射到核准的 Web API 功能、臨時遺留項目或超出範圍項目，拒絕通用的 Execute 替代方案。
11. **CI/啟動門檻**：已遷移的產品根目錄會在 CI 中被強制檢查，阻止引用 `Microsoft.Xrm*` 等禁用套件，並透過 `no-sdk-source-roots.json` 確保掃描範圍的完整性。
12. **信任邊界**：明確界定了 JSON 非授權來源。Embedded 模式必須載入已簽章的部署資訊清單，本地編輯 JSON 無法越權存取其他組織。
13. **效能與測試具體性**：ADR 准入條件、赤字/加權公平佇列演算法、以及包含擁有者與 IaC 漂移檢測的容量構件均非常具體，確保了效能的可測試性。

---

## 3. 審查發現 (Findings)

### Critical (嚴重問題)
*無。技術架構設計嚴密，無安全性或功能性嚴重缺陷。*

### Warning (警告/需修正項目)

#### 1. 規格書與規劃文件存在字元損壞（亂碼）
* **檔案路徑**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`
  * **位置**：第 1 行、第 184 行、第 324 行、第 395 行、第 452 行。
  * **問題**：
    * 第 1 行：`# Dynamics 365 Access Gateway ??Architecture SPEC` -> 應修正為 `Architecture SPEC`。
    * 第 184 行：`process/store boundaries?ever direct string concatenation.` -> 應修正為 `, never`。
    * 第 324 行：`Retry-After behavior?ot unlimited concurrency.` -> 應修正為 `, not`。
    * 第 395 行：`redacted outcome/reference?ever a raw body` -> 應修正為 `, never`。
    * 第 452 行：`D:\?唾?蝘??Ｗ?\蝟餌絞撟喳\Dynamics 365 SDK DLL` -> 應修正為 `D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL`。
  * **理由**：字元損壞會降低規格書的嚴謹性，特別是路徑和關鍵字句的損壞可能導致實作時的誤解。

#### 2. 設計文件中的樹狀圖字元損壞
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
  * **位置**：第 81-90 行。
  * **問題**：樹狀圖中的分支字元損壞（例如 `???€`、`???€`）。
  * **理由**：這會影響方案拓撲結構圖的可讀性，應修正為標準的 ASCII 樹狀圖字元（如 `├──` 和 `└──`）。

#### 3. 實作計畫中的標題分隔符號損壞
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
  * **位置**：第 52 行、第 83 行、第 123 行、第 299 行、第 359 行、第 451 行、第 472 行。
  * **問題**：標題中的分隔符號顯示為 `??`（例如 `## Phase 0 ??Baseline and safety inventory`）。
  * **理由**：應修正為 `-` 或 `:` 以維持 Markdown 格式的正確性與美觀。

### Info (提示資訊)
* **技術選型推遲**：將 durable coordinator、idempotency ledger 與 audit-retention 的具體技術選型推遲至 Phase 2 開始前的 ADR 是合理的，因為實作計畫中已明確定義了該 ADR 必須滿足的原子性交易原語與測試要求，這為後續實作提供了清晰的指導方針。

---

## 4. 建議 (Suggestions)

1. **修正字元編碼**：在啟動 Phase 1 之前，請統一修正上述文件中因字元編碼轉換導致的 `??` 與亂碼字元，確保規格書的文字嚴謹度。
2. **自動化 CI 門檻指令碼測試**：在 Phase 0 中，建議先在現有的 CI 流程中以「警告模式」執行 `Verify-NoDynamicsSdk.ps1`，以驗證該指令碼在 Windows 與 Linux 代理程式上的相容性與效能，避免在 Phase 6 啟用強制門檻時造成建置管線非預期的中斷。

---

## 5. 肯定之處 (Positive Notes)

* **回歸檢查完全滿足**：修訂後的檔案完美融入了先前審查的所有回歸檢查要求，特別是 `RuntimeHostSlotLease` 的隔離期（quarantine）與過期邊界（expiry fence）設計，極具技術深度。
* **防禦性設計徹底**：金鑰的長度前綴規範（`CanonicalKeyV1`）、重複屬性感知的 JSON 解析、以及對 FetchXML 與 OData 參數的嚴格限制，展現了極高的安全防禦意識。
* **遷移路徑務實**：實作計畫沒有迴避現有的複雜耦合，而是要求先建立呼蓋矩陣並採取 Strangler 逐步遷移，這大幅降低了系統重構的風險。
