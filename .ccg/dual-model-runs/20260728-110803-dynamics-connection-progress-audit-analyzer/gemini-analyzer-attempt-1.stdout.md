# Dynamics 365 連線拆分與無 SDK 閘道工作審計報告

## 1. 執行摘要 (Executive Conclusion)
本報告針對 Dynamics 365 連線拆分與無 SDK 存取閘道（no-SDK access gateway）工作進行唯讀、基於證據的審計。目前 HEAD 已合併 `isolate-connector` 工作分支（commit `f9e544e0`），相關專案（`SpeechMessage.Dynamics.*`）已納入方案中。

目前核心的無 SDK Web API 基礎架構（包含 Gateway、Embedded、WebApi、Abstractions、ProductClient 等專案）已建立，且 47 項單元測試與 4 項 Smoke 測試（在 live CRM 停用下）皆已通過。然而，由於缺乏 ADFS/IFD 實際環境憑證與 ClientId 註冊，目前 live 驗證仍處於受阻狀態。此外，整體方案仍殘留 23 項基準測試失敗（與本次變更無關的歷史債務）以及 `ToolUtility.Tests` 的還原目標框架不匹配問題。

---

## 2. 階段對照與當前狀態表 (Phase Table)

| 評估維度 | 宣告/文檔狀態 | 真實當前狀態 (基於程式碼與驗證證據) |
| :--- | :--- | :--- |
| **Trellis 工作流階段** | `in_progress` | **進行中 (In Progress)**：分支已合併，但任務尚未關閉。 |
| **架構部署階段** | 規劃與基礎建設階段 | **基礎建設已合併，準備進行首個訂閱端遷移與 Live 驗證**。 |
| **實作 Phase 0** (基準與安全清單) | 已完成 | **已完成**：已建立 `no-sdk-source-roots.json` 掃描清單，並完成呼叫覆蓋矩陣的規劃。 |
| **實作 Phase 1** (新專案與合約) | 已完成 | **已完成**：已建立 `SpeechMessage.Dynamics.*` 專案群組並納入方案。 |
| **實作 Phase 2** (Profile 執行期與 Web API 連接器) | 已完成 | **已完成**：已實作 `DynamicsWebApiClient`、`ApprovedWebApiRootFactory`、`ChainedSecretResolver` 等核心邏輯。 |
| **實作 Phase 3** (閘道/嵌入式策略與受控操作) | 進行中 | **部分完成**：已實作 `ControlledOperationExecutor`、`Package01OperationRegistry` 等，但 ADFS/IFD 實際連線與 `Package01` 費用讀取（Fee Reads）因憑證缺失尚未啟用（`Package01FeeReadsEnabled` 仍為 `false`）。 |
| **實作 Phase 4** (遷移前驗證) | 進行中 | **受阻 / 部分完成**：單元測試與模擬測試通過，但 ADFS/IFD 現場驗證、多主機協調（durable multi-host coordination）、Soak/Fault 測試等尚未在真實環境執行。 |
| **實作 Phase 5** (絞殺者遷移) | 未開始 | **未開始**：尚未將 `ChurchReport` 的實際生產流量切換至新閘道。 |
| **實作 Phase 6** (最終 SDK 移除) | 未開始 | **未開始**：`PowerPlatform.Dataverse.Client` 專案仍保留在方案中，且 `no-sdk-source-roots.json` 仍處於 `report-only` 模式（掃描出 1,069 處歷史 SDK 依賴）。 |

---

## 3. 規格與設計文件評估 (Spec Assessment)
- **PRD/設計文件品質**：**優良**。`.trellis/tasks/07-23-dynamics-connection-compatibility/` 下的 `prd.md`、`design.md` 與 `implement.md` 結構嚴密，詳細定義了：
  - 「雙主機、單核心」（Gateway 與 Embedded 共享 WebApi 核心）的架構。
  - 嚴格的組織准入控制（Organization Admission）與租約機制（RuntimeHostSlotLease），防止 Embedded 模式繞過並發限制。
  - 參數化 FetchXML/OData 範本編碼，防止注入攻擊。
  - 冪等性帳本（Idempotency Ledger）與審計意圖（Audit Intent）的狀態機。
- **不足之處**：設計規格非常理想化，但實作計畫中部分高級特性（如分散式協調器 `IRuntimeHostSlotCoordinator` 的 Redis/Durable 實作、HMAC 密鑰輪轉、詳細的佇列公平性演算法）在當前程式碼中多為記憶體內（In-Memory）或簡化版本，與生產級高可用要求仍有距離。

---

## 4. 實作進度評估 (Implementation Assessment)
- **已完成 (Complete)**：
  - 專案結構建立：`Abstractions`、`WebApi`、`Gateway`、`Embedded`、`ProductClient`、`Tests`、`SmokeTests`。
  - 核心 Web API 客戶端與 ADFS OAuth 權杖提供者（`AdfsOAuthTokenProvider`）。
  - 47 個單元測試與 4 個 Smoke 測試。
  - 靜態 SDK 掃描器配置（`no-sdk-source-roots.json`）。
- **部分完成 (Partial)**：
  - 准入控制：目前僅有 `InMemoryRuntimeHostSlotCoordinator`，尚未實作跨多主機的持久化協調器。
  - 密鑰解析：已實作 `ChainedSecretResolver`，但生產環境的 Key Vault/Secret Provider 整合尚未完全對接。
- **受阻 (Blocked)**：
  - ADFS/IFD 現場驗證：因缺乏真實環境的 ClientId 與憑證，無法進行 live 測試。
- **未開始 (Not Started)**：
  - 實際業務流量遷移（Phase 5）。
  - 徹底移除 SDK 依賴與專案（Phase 6）。

---

## 5. 文檔與追蹤一致性問題 (Documentation/Traceability Issues)
- **狀態矛盾**：
  - `.trellis/.../task.json` 與 `.ccg/.../task.json` 均標記為 `in_progress`，但 `.ccg/.../task.json` 的 `currentPhase` 寫為 `implementation`，而 `.ccg/tasks/dynamics-connection-compatibility/review.md` 卻宣稱「Planning artifacts are ready for user/spec review. No production implementation has started.」（規劃產出已準備好評審，尚未開始生產實作）。
  - **影響**：這反映了文件更新的滯後。事實上，`isolate-connector` 的程式碼實作已經合併至 `1.0.0.3` 分支，實作早已開始並完成第一階段，但部分規劃文件仍停留在「規劃完成、等待實作」的描述，容易誤導審查人員。

---

## 6. 阻礙與風險分析 (Blockers and Risks)

### Critical (嚴重阻礙)
- **ADFS/IFD 憑證與 ClientId 缺失**：無法在真實環境驗證 `WhoAmI` 與 Web API 連線，這是進入 Phase 4/5 的關鍵阻礙。
- **歷史憑證洩漏風險**：目標分支上有 9 份歷史追蹤文件包含舊的明文憑證字串。雖然新程式碼已清理，但若該憑證尚未在 Dynamics 端輪轉，存在極大安全隱患。

### Warning (警告)
- **方案測試未完全綠燈**：方案中仍有 23 個基準測試失敗（`ChurchReport.MemberInfo.Tests` 22 個，`RichMenus.Tests` 1 個），且 `ToolUtility.Tests` 因 .NET 8.0 vs .NET 10.0 框架不匹配無法還原。這會干擾 CI/CD 流程，無法建立乾淨的驗證基準。
- **分散式協調器尚未落地**：目前僅有記憶體內（In-Memory）的准入協調器，若部署多個 Gateway 實例或使用 Embedded 模式，將無法有效限制 aggregate 併發，可能導致 Dynamics 端觸發服務保護限制。

### Info (提示)
- `no-sdk-source-roots.json` 目前為 `report-only` 模式，掃描出 1,069 處 SDK 依賴，符合 Phase 0 預期，後續遷移時需逐步收緊為強制作業。

---

## 7. 優先後續步驟 (Prioritized Next Steps)
1. **憑證輪轉與環境準備**：在 Dynamics 365 端輪轉歷史洩漏的憑證，並向運維團隊申請非生產環境的 ADFS ClientId 與測試帳號憑證。
2. **執行 Live Smoke 測試**：啟用 `LiveDynamicsWebApiSmokeTests`，完成真實環境的 ADFS/IFD 驗證與 `WhoAmI` 測試。
3. **修復方案基準債務**：解決 `ToolUtility.Tests` 的還原框架不匹配問題，並修復或隔離 `ChurchReport.MemberInfo.Tests` 的 22 個歷史失敗測試，確保 CI 流程暢通。
4. **實作持久化准入協調器**：根據 ADR 規劃，實作基於 Redis 或資料庫的 `IRuntimeHostSlotCoordinator`，以支援多實例部署。
5. **啟動 Strangler 遷移 (Phase 5)**：選擇一個唯讀、低風險的 ChurchReport 欄位或操作，透過 Feature Flag 導流至新閘道進行驗證。

---

## 8. 整體評估與評級 (Overall Evaluation)
- **整體評級**：**B-**
- **評語**：架構設計優異，核心程式碼已就緒並通過單元測試；但受限於外部憑證阻礙無法進行 Live 驗證，且方案殘留較多歷史測試債務，需在進入下一階段前優先清理。
