```diff
--- a/.trellis/tasks/08-14-p7-current-state-rebaseline/task.json
+++ b/.trellis/tasks/08-14-p7-current-state-rebaseline/task.json
@@ -5,1 +5,1 @@
-  "description": "靘銵?撘Ⅳ??摮???撱?P7 甈?撌株??拚嚗甇?parent 頝舐???task metadata嚗??甇瑕 CE cycle??霈 feature gate ???€?,
+  "description": "依現行程式碼與儲存庫狀態重建 P7 剩餘工作矩陣，校正 parent 規劃與 task metadata，不授權啟用 CE cycle、不啟用 feature gate 或流量。",
--- a/.trellis/tasks/08-14-p7-current-state-rebaseline/prd.md
+++ b/.trellis/tasks/08-14-p7-current-state-rebaseline/prd.md
@@ -5,1 +5,1 @@
-靘銵?撘Ⅳ??摮???撱?P7 甈?撌株??拚嚗甇?parent 頝舐???task metadata嚗??甇瑕 CE cycle??霈 feature gate ???€?
+依現行程式碼與儲存庫狀態重建 P7 剩餘工作矩陣，校正 parent 規劃與 task metadata，不授權啟用 CE cycle、不啟用 feature gate 或流量。
```

---

# P7 Current-State Rebaseline 架構與安全審查分析報告

本報告由前端架構師視角出發，針對 `08-14-p7-current-state-rebaseline` 的規劃方向進行架構決策、邊界安全與遷移路徑審查。

---

## 1. Analysis (當前架構評估)

### 1.1 歷史與現行基線狀態
- **已封存基線 (P3–P6, P7.0–P7.3)**：均為唯讀或已確認之 archived baseline，架構上禁止任何重做 (replay) 或修改。
- **P7.2 Slice C 狀態**：歷史上的 `write-not-committed` no-go 已執行 exact cleanup，此路徑已永久關閉，禁止重試。
- **P7.4 當前狀態**：Parent 任務仍處於 active 狀態。最新完成的 `ORG-CALL-00057` 建立了預設停用 (default-disabled) 的本地唯讀資料平面 (local-only read data plane)，已通過完整測試，但尚未有消費端 (consumer) 接入或流量切換證據。
- **P7.5 前提條件**：目前為確定性的 `no-go`。仍有 70 個 temporary-legacy rows 未遷移，且 legacy 依賴（如 `ToolUtility`、`Microsoft.Xrm.Sdk`）依然存在，CE/host/parity/soak/drain/rollback 仍有顯著缺口。
- **P8 部署閘門**：必須等待 P7.5 的不可變交付 (immutable handoff) 與具名外部部署前提條件滿足後方可啟動。

### 1.2 Rebaseline 規劃方向評估
本次 `08-14-p7-current-state-rebaseline` 的核心目標是**重建權威差距矩陣 (Authoritative Gap Matrix)** 與**校正 P7 parent 規劃文件**。此工作屬於純離線的架構對帳與元數據校正，**未授權**任何 CE 變更、功能啟用、流量切換或 P8 部署。此規劃方向在架構上是安全且必要的，能有效防止開發團隊在缺乏真實證據的情況下盲目推進整合。

---

## 2. Architecture Decision (關鍵設計決策與理由)

### 2.1 權威差距矩陣的必要欄位與驗證規則
為確保矩陣的確定性與機器可讀性，重建的 `authoritative-gap-matrix.json` 必須包含以下欄位，並通過 `build_rebaseline.py` 的嚴格驗證：

| 欄位名稱 | 說明 | 驗證規則 / 限制 |
| :--- | :--- | :--- |
| `callSiteId` | 呼叫點唯一識別碼 | 必須與 P7.0 原始 70 個 call sites 完全一致且唯一。 |
| `operation` | 包含 `id` 與 `kind` | 必須與 C# `OperationIds` 常數及原始矩陣對齊。 |
| `capabilityFamily` | 功能家族分類 | 用於評估特殊資源需求與依賴關係。 |
| `registry` | 註冊表宣告狀態 | `declared` / `not-declared` / `local-only`。 |
| `data8Executor` | Data8 執行器狀態 | `implemented` / `not-implemented` / `local-only-rejected`。 |
| `productClient` | ProductClient 實作狀態 | `implemented` / `not-implemented`。 |
| `consumer` | 消費端遷移狀態 | `migrated-disabled` / `not-migrated`。 |
| `ceEvidence` | CE 執行證據 | 包含 `ce82` 與 `ce91`，未執行時必須為 `evidence-pending`。 |
| `hostEvidence` | 主機環境證據 | 包含 `embedded` 與 `dedicated`，未驗證時為 `evidence-pending`。 |
| `rollout` / `rollback` | 發布與回滾負責人 | 實作完成時指定為 `p7.4-capability-owner`，否則為 `pending`。 |
| `temporaryLegacy` | 暫時性遺留狀態 | 若存在未遷移消費端或 legacy 依賴，必須標記為 `temporary-legacy`。 |
| `specialResourceRequirement` | 特殊資源需求 | `none` / `metadata-cache` / `attachment-stream` / `paging-result`。 |
| `p75RemovalBlocker` | P7.5 移除阻礙因素 | `none` / `mixed` / `special-resource-pending` / `consumer-not-migrated` 等。 |

### 2.2 不可混淆的證據類別 (Evidence Classification)
架構上必須嚴格區分以下三類證據，禁止任何形式的「狀態升格」：
1. **本地程式碼就緒 (Local-only Code Ready)**：僅代表 `registry`、`data8Executor` 與 `productClient` 在本地編譯與單元測試通過。此狀態下，`ceEvidence` 與 `hostEvidence` 必須保持 `evidence-pending` 或 `not-executed`。
2. **消費端已遷移但停用 (Consumer Migrated-Disabled)**：僅代表消費端程式碼已改用 ProductClient 接口，且外層包裹了預設關閉的 feature gate（如 `_package01Enabled`）。這**不代表**流量已切換，亦不代表 CE 部署已完成。
3. **歷史 No-Go 處置 (Historical No-Go Disposition)**：對於已關閉的 P7.2 Slice C，其 `ceEvidence.ce91` 必須永久保持 `no-go-closed`，禁止任何重新啟用的嘗試。

### 2.3 應比對的現行來源 (Source Alignment)
重建矩陣時，必須交叉比對以下現行儲存庫來源，確保無硬編碼字串繞過：
- **`OperationIds.cs`**：提取所有 C# 常數定義，確保矩陣中的 `operation.id` 均有對應的強型別常數。
- **`Package01OperationRegistry.cs`**：驗證註冊表是否已正確宣告該操作。
- **`Data8ProfileOperationExecutor.cs`**：驗證執行器中是否有對應的固定查詢分支。
- **`SpeechMessage.Dynamics.ProductClient`**：驗證是否已建立不可變的 DTO 快照與 stateless client 實作。
- **`SpeechMessageProducts.ChurchReport`**：掃描 `ToolUtility` 與 CRM SDK 的參考次數，作為 P7.5 阻礙因素的計數依據。

---

## 3. Implementation Plan (實施計劃)

### 3.1 P7.4 下一個 Local-Only 候選功能之資格審查流程
在選擇下一個 P7.4 本地候選功能時，必須通過以下資格審查：

```
[選擇矩陣中的候選 Row]
         │
         ▼
┌────────────────────────────────────────┐
│ 1. 是否為 DTO-only 且無 mutable Entity? │ ──(否)──> [排除 (No-Go)]
└────────────────────────────────────────┘
         │(是)
         ▼
┌────────────────────────────────────────┐
│ 2. 是否為 Server-authorized 權限邊界?  │ ──(否)──> [排除 (No-Go)]
│    (禁止前端傳入未驗證之定位器)        │
└────────────────────────────────────────┘
         │(是)
         ▼
┌────────────────────────────────────────┐
│ 3. 是否無共享可變狀態 (Stateless)?     │ ──(否)──> [排除 (No-Go)]
└────────────────────────────────────────┘
         │(是)
         ▼
┌────────────────────────────────────────┐
│ 4. 是否無 Stored Query (FetchXML)?     │ ──(否)──> [排除 (No-Go)]
└────────────────────────────────────────┘
         │(是)
         ▼
┌────────────────────────────────────────┐
│ 5. 是否無寫入相鄰性 (Write Adjacency)? │ ──(否)──> [排除 (No-Go)]
└────────────────────────────────────────┘
         │(是)
         ▼
┌────────────────────────────────────────┐
│ 6. 是否有嚴格的單頁列數與大小限制?     │ ──(否)──> [排除 (No-Go)]
│    (例如 32 rows / 32 KiB 預算)        │
└────────────────────────────────────────┘
         │(是)
         ▼
   [核准為 P7.4 候選功能]
```

### 3.2 Parent 文件與 Task Metadata 最小一致性修正步驟
1. **修正 `08-14-p7-current-state-rebaseline` 任務文件**：
   - 修正 `task.json` 與 `prd.md` 中的亂碼，將其替換為正確的繁體中文描述。
2. **更新 Parent 任務 `gateway-purpose-and-positioning/task.json`**：
   - 在 `currentBaseline` 中加入本次 rebaseline 的時間戳記與驗證結果（70 rows 驗證通過，無 validator 錯誤）。
   - 在 `latestCheckpoint` 中記錄 `ORG-CALL-00057` 的本地資料平面已完成，但消費端遷移仍為 `not-migrated`，且 P7.5/P8 閘門保持關閉。
   - 在 `nextAction` 中，明確指出下一個候選功能必須符合 DTO-only、server-authorized 等安全條件，否則必須記錄 no-go。

---

## 4. Considerations (安全與架構審查意見)

### 4.1 Critical (關鍵審查意見)
- **Cross-User Isolation (跨用戶隔離)**：
  - *風險*：若前端或消費端直接傳入未經伺服器端驗證的 `contactId`，可能導致越權存取（IDOR 漏洞）。
  - *對策*：所有網關 API 契約必須強制使用 Principal-derived 定位器。前端發起請求時，網關必須在伺服器端根據當前已驗證的 Session 身份解析出 `contactId`，禁止信任客戶端傳入的定位器。
- **Shared Mutable State (共享可變狀態)**：
  - *風險*：若權限驗證依賴於全域或 Session 級別的可變狀態（如 `InMemoryContext`、`Session`、`ListManager` 且使用儲存的憑證載入，例如 `ORG-CALL-00031/00032/00033`），會導致並發請求下的權限污染。
  - *對策*：此類功能必須列為 **No-Go**。在建立獨立的、基於請求生命週期 (request-local) 的不可變授權邊界之前，禁止進行任何 ProductClient 遷移。

### 4.2 Warning (警告審查意見)
- **Write Adjacency (寫入相鄰性)**：
  - *風險*：若唯讀操作與寫入操作緊密相鄰（如 `payments.fee.update.after.payment`），且寫入操作尚未建立受控的冪等性、回滾與對帳機制，提前遷移讀取端會導致資料不一致或狀態分裂。
  - *對策*：在寫入家族的控制平面（如 nonce、ledger、preflight、read-back、reconcile 與 deterministic cleanup）未完整實作前，禁止將相鄰的讀取操作標記為已遷移。
- **Stored Query Execution (儲存查詢執行)**：
  - *風險*：若查詢依賴於 CRM 端儲存的 FetchXML 或動態查詢（如 `ORG-CALL-00047`），一旦 CRM 端的查詢被修改，網關的行為與效能預算將失控。
  - *對策*：禁止在網關中執行動態或儲存的 FetchXML。所有查詢必須在網關端以強型別的 `QueryExpression` 固定投影，並在編譯期進行靜態分析。

### 4.3 Info (提示審查意見)
- **Session/Resource Retention (資源留存)**：
  - *風險*：對於大檔案（如圖片流 `ORG-CALL-00028`）或分頁查詢，若未妥善管理生命週期，會導致網關或前端記憶體洩漏。
  - *對策*：ProductClient 必須實現嚴格的生命週期管理（如 `IDisposable`、`CancellationToken`），並在資料傳輸完成後立即釋放連線與緩衝區。
- **P7.5/P8 Scope Drift (範圍漂移)**：
  - *風險*：在 70 個 rows 的遺留依賴完全清除前，提前啟用 feature gate 或進行流量切換會導致系統進入未定義狀態。
  - *對策*：P7.5 移除與 P8 部署閘門必須保持關閉。本次 rebaseline 僅用於對帳，不代表任何上線許可。
