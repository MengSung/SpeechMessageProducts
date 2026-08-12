```diff
--- a/.trellis/tasks/08-12-p7-remaining-work-rebaseline/design.md
+++ b/.trellis/tasks/08-12-p7-remaining-work-rebaseline/design.md
@@ -46,2 +46,108 @@
 隞颱? child ??timeout?mbiguous?ead-back mismatch?leanup uncertainty ??no-go ??甇Ｗ?芾澈 mutation family嚗? Schuyler??銝靘??祆????隞?local-only child ?迫嚗?銝??迂?喲? P7.4嚗7.5 gate??
+
+## P7 Remaining Capability Rebaseline Architecture Analysis Report
+
+### 1. Analysis (當前架構評估)
+* **P3–P6 基礎設施狀態**：Data8 世代專屬連接池、Embedded 模式、Dedicated Gateway 與 Router 均已就緒。Official Worker 的 live 相容性仍為 `evidence-pending`，但這不影響以 Data8 為主的遷移路徑。
+* **P7.0–P7.2 歷史狀態**：
+  - P7.0 建立了 70 個 Dynamics 呼叫點的 coverage matrix。
+  - P7.1 實現了 6 個 Package01 Data8 typed reads，且 CE 9.1 唯讀驗證為 `go`，但 ChurchReport 的 product flag 仍為 false。
+  - P7.2 Slice C 寫入操作在 CE 發生了 `write-not-committed` no-go，已進行嚴格清理並永久關閉。D–H 僅有本機的 plan/reducer，executor 和 consumer 均為 false。
+* **ToolUtility 依賴債務**：`ToolUtility` 是一個 process-wide singleton，被多個 legacy flow 直接讀寫其 service 欄位。`DownloadListManager` 曾將 operation-scoped `IOrganizationService` 寫回共享的 `ToolUtility` 欄位，這會導致嚴重的 Session 交叉污染（Session Bleeding）與資源洩漏。
+
+### 2. Architecture Decision (關鍵設計決策與理由)
+* **決策 1：嚴格的獨立狀態矩陣 (Independent State Matrix)**
+  - *理由*：Registry 宣告、Executor 實現、ProductClient 封裝、Consumer 啟用、CE 證據、Rollout 證據必須是完全獨立的狀態。不能因為本機測試通過或 Registry 宣告了，就宣稱 CE 證據或 Consumer 遷移成功。
+  - *拒絕替代方案*：拒絕使用 request-time fallback 或混合借用與共享 service 的設計。
+  - *假設與副作用*：所有未取得 CE 證據的 capability 必須維持 fail-closed。
+* **決策 2：以 Operation-Local Ownership 替代共享 Singleton**
+  - *理由*：借用的 `IOrganizationService` 必須嚴格限制在單一操作生命週期內，禁止寫入共享的 `ToolUtility`、singleton、static、cache 或跨 request 狀態。
+  - *拒絕替代方案*：在 eviction callback 中 dispose 共享的 `ToolUtilityClass`（這會導致一個 Session 的過期失效了其他 Session 的 CRM 狀態）。
+* **決策 3：逐一 Capability 切流與 Rollback 驗證**
+  - *理由*：P7.4 切流必須是 per-capability 且預設關閉，切流前必須有 rollback 演練證據。P7.5 移除 ToolUtility 必須在所有 70 個呼叫點均無 production 引用時才能進行。
+
+### 3. Concrete Findings (具體發現)
+
+#### Critical (危急)
+1. **Session 交叉污染與資源洩漏風險**：`DownloadListManager` 曾將 operation-scoped `IOrganizationService` 寫回共享的 `ToolUtility` 欄位。若多個 Session 併發呼叫，會導致後一個 request 重用前一個 operation 的可變 CRM service/profile state，造成嚴重的 Session Bleeding。
+2. **Eviction Callback 誤處置共享單例**：`InMemoryDataContextSmallGroup` 的 Session 淘汰回呼（eviction callback）若直接呼叫 `IDisposable.Dispose()` 處置共享的 `ToolUtilityClass`，會導致其他活躍 Session 的 CRM 狀態失效，引發執行期崩潰。
+3. **P8 啟動順序缺陷**：若在 P7.5 移除閘（removal gate）未完全通過（即仍有 production ToolUtility/CRM SDK 引用，或未完成 migration 和 rollback drill）的情況下啟動 P8，會導致生產環境編譯或執行期失敗。P8 必須在 P7.5 交付物完全 immutable 且 zero-reference 的情況下才能啟動。
+
+#### Warning (警告)
+1. **測試專案 Target Framework 不相容**：`ToolUtility.Tests` 目前仍以 `net8.0` 引用 `net10.0` 的 `ToolUtility`，導致 restore 時產生 `NU1201` 錯誤。不能因為此相容性問題而降低 ToolUtility 的 target framework，亦不能跳過隔離測試。
+2. **虛假完成（False Completion）風險**：Slice D–H 目前僅有本機的 plan/reducer，其 executor 和 consumer 均為 false。若僅因本機測試通過就將其標記為完成，會導致 unsupported CE claims。
+
+#### Info (資訊)
+1. **P7.1 唯讀路徑狀態**：六個 Package01 Data8 typed reads 已完成 CE 9.1 唯讀驗證，但其 product flag 仍為 false，處於受控禁用狀態。
+2. **Slice C 狀態**：Slice C 寫入操作已因 `write-not-committed` no-go 而永久關閉，並已完成 exact cleanup。
+
+### 4. Recommended Matrix Invariants (推薦的矩陣不變量)
+1. **CE 證據與啟用狀態不變量**：
+   $$\forall c \in \text{CallSites}, c.\text{ceEvidence} \neq \text{succeeded} \implies c.\text{consumer}.\text{status} = \text{disabled} \lor \text{migrated-disabled}$$
+   *任何未取得 CE 成功證據的呼叫點，其消費者狀態必須強制為禁用，防止未授權的寫入操作進入生產環境。*
+
+2. **ToolUtility 移除阻礙不變量**：
+   $$\forall c \in \text{CallSites}, c.\text{temporaryLegacy} = \text{temporary-legacy} \implies c.\text{p75RemovalBlocker} \neq \text{none}$$
+   *任何仍標記為臨時遺留（temporary legacy）的呼叫點，必須在 P7.5 移除阻礙中列出對應的阻礙原因，嚴禁在有殘留引用的情況下移除 ToolUtility。*
+
+3. **特殊資源隔離不變量**：
+   $$\forall c \in \text{CallSites}, c.\text{specialResourceRequirement} \in \{\text{attachment-stream}, \text{paging-result}\} \implies c.\text{data8Executor}.\text{status} = \text{not-implemented} \lor \text{local-only-rejected}$$
+   *在 P7.3 特殊資源遷移完成前，所有涉及附件流或分頁結果的呼叫點，其 Data8 執行器狀態必須為未實現或本機拒絕，避免資源洩漏。*
+
+### 5. Safe Next Child Boundaries (安全的下一個子任務邊界)
+1. **P7.1/P7.2 剩餘 typed capabilities 遷移**：僅限於實現 Data8 執行器與 ProductClient，且預設為關閉狀態。
+2. **P7.3 特殊資源遷移**：專注於解決附件流（attachment-stream）、分頁結果（paging-result）與元數據快取（metadata-cache）的生命週期與資源釋放。
+3. **P7.4 逐一切流 (Per-Capability Cutover)**：必須在取得完整 CE/parity/rollback 證據後，逐個 capability 啟用，且必須包含 rollback 演練驗證。
+4. **P7.5 ToolUtility 移除閘 (Removal Gate)**：在所有 70 個呼叫點均無 production 引用（zero-reference）時，方可移除對 ToolUtility 與 CRM SDK 的依賴。
```
