# UI 與架構審查報告：Phase 0 Dynamics 365 no-SDK 移除閘門更新

## 1. 總體評估 (Summary)
本次審查針對 Phase 0 Dynamics 365 no-SDK 移除閘門更新的相關規劃與設計文件進行了完整評估。整體設計非常嚴密，針對 Dynamics 365 舊版 SDK 的移除路徑規劃清晰，並建立了完善的防禦性架構（包含連線池隔離、分散式並行控制、冪等性帳本、以及硬性審計閘門）。

本階段（Phase 0）的目標是建立資產清單與報告機制，不影響現有的建置與運行。設計文件明確指出 `PowerPlatform.Dataverse.Client` 為臨時遺留依賴，並規劃在後續階段將其完全移出建置源，符合「無 SDK」的終極目標。

---

## 2. 輔助功能與開發者體驗 (Accessibility & Developer Experience)
*註：由於本任務為後端整合與架構設計，傳統的 UI 網頁輔助功能（a11y）並不適用。此處評估著重於「開發者體驗 (Developer Experience)」與「配置可讀性」。*

* **無障礙與語意化配置 (Info)**：
  * **檔案位置**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`
  * **評估**：產品配置 JSON 採用了明確的 `$schema` 宣告，這能讓開發人員在 Visual Studio / VS Code 中編輯配置時獲得自動補全與語意驗證，提升了開發者體驗並減少配置錯誤的機率。

---

## 3. 設計一致性與架構審查 (Design Consistency & Architecture Review)

### 3.1 SDK 參考圖完整性驗證
* **評估結果**：**通過**。
* **說明**：Phase 0 的 `phase0-organization-call-matrix.json` 完整記錄了現有的 SDK 依賴關係（SDK-001 至 SDK-007），包含 `ToolUtility.Tests` 對 `Microsoft.CrmSdk.CoreAssemblies` 的引用，以及 `SpeechMessageProducts.sln` 對 `PowerPlatform.Dataverse.Client` 的包含。這與已知的 SDK 圖譜完全一致。

### 3.2 終極無 SDK 狀態之明確性
* **評估結果**：**通過**。
* **說明**：設計文件（`prd.md`、`design.md`、`implement.md`）中多次強調 `PowerPlatform.Dataverse.Client` 必須在消費者遷移後被完全刪除或移出可建置源，且明確禁止將其作為新連接器的包裝（wrapper）保留。這消除了任何模糊空間。

### 3.3 避免過早中斷建置
* **評估結果**：**通過**。
* **說明**：Phase 0 的 CI 掃描模式設定為 `report-only`，且明確將「刪除現有 SDK 依賴」列為 Phase 0 的非目標（Out of Scope），確保了現有建置不會在替代方案完成前被破壞。

### 3.4 方案拓撲一致性
* **評估結果**：**通過**。
* **說明**：設計明確要求將新的 Dynamics 專案組加入現有的 `SpeechMessageProducts.sln`，並將獨立的 `SpeechMessage.Dynamics.sln` 列為非強制性的選用過濾器，避免了多個專案參考源頭不一致的衝突。

---

## 4. 具體發現與建議 (Findings & Suggestions)

### ⚠️ 警告 (Warning)

#### 1. 排除清單配置可能導致 SDK 掃描漏洞
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (第 591-596 行)
* **原因分析**：`Verify-NoDynamicsSdk.ps1` 依賴 `no-sdk-source-roots.json` 來決定掃描範圍，並允許排除歷史與規劃文檔目錄。如果排除清單配置過於寬鬆，可能導致測試專案（如 `ToolUtility.Tests`）或複製的程式碼範例被意外排除，從而繞過無 SDK 閘門。
* **建議修復**：在 `Verify-NoDynamicsSdk.ps1` 中加入硬性斷言，確保所有在 `SpeechMessageProducts.sln` 中註冊的 production 與 test 專案目錄都必須被包含在掃描範圍內，排除清單僅能限制於非程式碼目錄（如 `docs`、`.trellis`、`.ccg`）。

#### 2. Embedded 模式下的 WorkloadSubjectId 偽造與重放風險
* **檔案路徑**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` (第 100-113 行)
* **原因分析**：在 `Embedded` 模式下，產品服務在本地進程內運行。如果本地 JSON 配置中的 `WorkloadSubjectId` 或 `ProductProfileBinding` 可以被輕易篡改，且本地驗證機制不夠嚴密，可能會導致越權存取。
* **建議修復**：在 Phase 2 實現 `Embedded` 模式的簽名資訊清單（signed manifest）驗證時，必須確保：
  1. 簽名驗證的公鑰是透過安全管道分發且唯讀的。
  2. 資訊清單中必須包含單調遞增版本號（monotonic version）與過期時間（expiry），且驗證邏輯必須防範重放攻擊（replay attacks）。

---

### ℹ️ 資訊 (Info)

#### 1. HostRoleWeights 未來擴展的接口預留
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (第 727-732 行)
* **原因分析**：設計中提到 Phase 1 預設使用等權重分配（equal-weight allocation），未來可能引入 `HostRoleWeights`。
* **建議**：在 Phase 1 設計限流器與配額分配邏輯時，應將權重（weight）作為一個可選的屬性預留於代碼結構中，避免未來引入權重分配時需要對 `OrganizationAdmissionManager` 進行大規模重構。

#### 2. Telemetry 敏感資訊過濾的單元測試
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (第 373-383 行)
* **原因分析**：設計中要求 Telemetry 必須移除 `url.full`、查詢字串、標頭與主體。
* **建議**：在 Phase 3 實現時，應針對 Telemetry 輸出管道編寫專門的單元測試，模擬包含敏感資訊（如 Access Token、PII 欄位）的異常物件，驗證過濾器是否能 100% 進行紅線遮蔽（redaction）。

---

## 5. 優秀設計亮點 (Positive Notes)
* **防禦性設計極為嚴密**：設計中對於 `PreAuthenticate` 的禁用考量非常周詳，有效避免了連線綁定驗證（connection-bound authentication）在多 profile 環境下可能導致的跨租戶 Session 洩漏風險。
* **生命週期管理清晰**：採用 `replace-and-drain` 的配置重載機制，並配合弱引用哨兵（weak-reference sentinels）進行記憶體洩漏測試，能有效防止舊 generation 的資源殘留。
* **Fail-Closed 原則貫徹徹底**：無論是審計存儲達到硬配額、KMS 無法存取、或是租約過期，系統皆選擇 fail-closed 停止發送請求至 Dynamics，這在高風險的企業整合場景中是非常正確且安全的決定。
