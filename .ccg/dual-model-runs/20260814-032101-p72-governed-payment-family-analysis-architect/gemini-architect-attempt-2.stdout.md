```diff
--- /dev/null
+++ b/docs/p72-governed-payment-family-analysis.md
@@ -0,0 +1,165 @@
+# P7.2 Governed Recurring Payment-Return Write Family 架構分析報告
+
+## 1. Analysis (當前架構評估與問題回答)
+
+### [Critical] 關鍵設計風險與缺失之不變量 (Critical Design Risks & Missing Invariants)
+* **檔案路徑**：`SpeechMessage.Dynamics.Abstractions/Operations/P72GovernedPaymentCycleAdmission.cs`
+* **風險與缺失**：
+  1. **階段證據狀態矛盾校驗缺失**：傳入的 `P72AdmissionObservation`（或類似的去識別化階段證據）可能存在狀態矛盾。例如，`IsComplete` 為 `false` 但 `Outcome` 卻被設為 `Succeeded`，或者 `HasMatchingProcessedOrder` 與 `IsAwaitingPayment` 同時為 `true`。准入合約必須在最前端進行嚴格的狀態組合校驗，任何不合法的狀態組合必須立即判定為 `NoGo` (fail-closed)，且 `ProhibitsReplay` 必須為 `true`，以防止無效或惡意的重試。
+  2. **可變狀態污染風險**：准入合約必須是純粹的（pure）無副作用函數。如果合約內部或其依賴的 Plan Builder 意外使用了靜態快取（static cache）、執行緒本地變數（ThreadLocal）或任何可變狀態，將會破壞 A/B 測試的隔離性，導致並行請求互相干擾。
+
+### [Critical] 未來受控 CE 執行器必須強制執行的最小規則 (Minimum Executor Rules)
+* **檔案路徑**：未來受控的 CE 執行器（Future Governed CE Executor）
+* **強制規則**：
+  1. **描述符與帳本規則 (Descriptor & Ledger)**：
+     * **Descriptor**：必須包含唯一的、不可變的 `nonce`、`family name`、`fixture marker`（預期前像/後像的雜湊值）以及伺服器授權的 `owner binding`。
+     * **Ledger**：必須是任務專屬的全新帳本（task-owned fresh ledger），僅記錄本次 cycle 內建立的確切實體 ID（例如新建立的 fee ID），嚴禁寫入或污染任何 baseline 生產數據。
+  2. **預檢與白名單規則 (Preflight & Allowlist)**：
+     * **Preflight**：在執行 any mutation 之前，必須進行唯讀預檢（read-only preflight），且預檢結果必須確切為 `go` 才能繼續。
+     * **Allowlist**：必須嚴格限制變更操作，第一階段僅允許 `payments.fee.update.after.payment`，任何不在白名單內的 CRUD 操作必須直接觸發 `NoGo`。
+  3. **讀回與清理規則 (Read-back & Cleanup)**：
+     * **Read-back**：執行 mutation 後，必須使用強型別投影（fixed typed projection）進行精確讀回，驗證欄位值是否與預期後像（postimage）完全一致。
+     * **Cleanup**：不論成功或失敗，必須依據帳本記錄的已知鍵值，以相反順序（reverse-known-key order）進行確定性清理（deterministic cleanup），並透過讀回確認實體已被移除，將系統還原至 baseline 狀態。
+
+### [Critical] 歷史 Slice C 測試基礎設施重用評估 (Reuse of Slice C Infra)
+* **評估結論**：**嚴禁重用任何已關閉的 Slice C 測試基礎設施（Fixture Infra）**。
+* **理由**：歷史上的 P7.2 Slice C 已被永久關閉，其狀態為 `write-not-committed` no-go，且已完成清理。Slice C 的 `nonce`、`ledger`、`descriptor`、`fixture` 及 `evidence` 均已被標記為不可重用且不可重放。為了確保測試家族的隔離性與安全性，未來的受控 CE 執行器必須使用全新生成的、任務專屬的 fresh fixture，絕不能與 Slice C 產生任何關聯。
+
+### [Info] 實作前必須完成的 RED/GREEN 測試案例 (Test Cases Requirements)
+* **檔案路徑**：`SpeechMessage.Dynamics.Tests/P72GovernedPaymentCycleAdmissionTests.cs`
+* **RED 階段測試案例（實作前必須失敗）**：
+  1. **無效描述符測試**：傳入空白或格式錯誤的 `fixtureKey` 時，合約應返回 `NoGo`，且不應生成 any plan。
+  2. **未完成觀察測試**：當 `IsComplete` 為 `false` 時，合約應返回 `NoGo`，且 `FailureCategory` 為 `Unavailable`。
+  3. **狀態矛盾測試**：當輸入矛盾的狀態組合（例如 `Outcome = Succeeded` 且 `HasMatchingProcessedOrder = true` 且 `IsAwaitingPayment = true`）時，合約應返回 `NoGo`。
+  4. **A/B 並行隔離測試**：並行呼叫 `Admit` 時，驗證不同執行緒間的輸入輸出不會互相干擾，確保無靜態狀態殘留。
+* **GREEN 階段測試案例（實作後必須通過）**：
+  1. **全新成功准入測試**：當 `IsComplete = true`、`Outcome = Succeeded`、`HasMatchingProcessedOrder = false` 且 `IsAwaitingPayment = true` 時，合約應返回 `PrepareFutureGovernedDispatch`，且產生的 plan 中 `CeDispatchAllowed` 與 `ProductConsumerAllowed` 均為 `false`。
+  2. **已處理跳過測試**：當 `HasMatchingProcessedOrder = true` 時，合約應返回 `AlreadyProcessed`，且不產生 plan。
+  3. **失敗需對帳測試**：當 `Outcome = Failed` 時，合約應返回 `RequireReconciliation`，且不產生 plan。
+
+### [Critical] 範疇邊界評估 (Scope Boundaries)
+* **評估結論**：**本 slice 的範疇僅限於純本地的 `P72GovernedPaymentCycleAdmission` 合約與相關單元測試**。
+* **理由**：它絕不能修改 `RecurringDonationPaymentProcessor` 等遺留的生產寫入器，也不能啟用 any CE 派遣或消費者（`CeDispatchAllowed` 與 `ProductConsumerAllowed` 必須保持為 `false`）。此設計不會影響 P7.4、P7.5、P8 的部署，也不會影響 ChurchReport 的生產流量或 ToolUtility 的遷移工作。
+
+---
+
+## 2. Architecture Decision (架構決策)
+
+* **決策 1：採用純本地、無副作用的准入合約設計**
+  * **Rationale**：准入合約 `P72GovernedPaymentCycleAdmission` 必須是純粹的（pure）無副作用函數，僅接受去識別化的階段證據，並返回 fail-closed 的處置。這能確保合約易於進行單元測試與形式驗證，且不會對生產環境造成 any mutable 影響。
+  * **Rejected Alternatives**：在准入合約中直接呼叫 CRM SDK 或進行資料庫查詢。此方案被否決，因為這會破壞本地合約的純粹性，增加測試複雜度，且違反安全閘門要求。
+  * **Assumptions**：假設未來的受控 CE 執行器會嚴格遵守准入合約返回的處置，並在執行 mutation 時遵循最小規則。
+  * **Potential Side Effects**：由於准入合約不進行實際的 I/O 操作，若執行器實作有誤，仍可能導致狀態不一致。因此必須透過嚴格的單元測試與整合測試來確保執行器的正確性。
+
+---
+
+## 3. Implementation Plan (實作計畫)
+
+### 步驟 1：建立 `P72GovernedPaymentCycleAdmission` 合約
+在 `SpeechMessage.Dynamics.Abstractions/Operations/` 下建立新檔案 `P72GovernedPaymentCycleAdmission.cs`：
+
+```csharp
+namespace SpeechMessage.Dynamics.Abstractions.Operations;
+
+public enum P72AdmissionDisposition
+{
+    NoGo = 0,
+    PrepareFutureGovernedDispatch = 1,
+    AlreadyProcessed = 2,
+    RequireReconciliation = 3
+}
+
+public enum P72AdmissionFailureCategory
+{
+    None = 0,
+    Unavailable = 1
+}
+
+public sealed class P72AdmissionResult
+{
+    public P72AdmissionDisposition Disposition { get; }
+    public P72AdmissionFailureCategory FailureCategory { get; }
+    public bool ProhibitsReplay => Disposition != P72AdmissionDisposition.PrepareFutureGovernedDispatch;
+
+    public P72AdmissionResult(P72AdmissionDisposition disposition, P72AdmissionFailureCategory failureCategory)
+    {
+        Disposition = disposition;
+        FailureCategory = failureCategory;
+    }
+}
+
+public sealed class P72AdmissionObservation
+{
+    public bool IsComplete { get; init; }
+    public P72DonationPaymentOutcome Outcome { get; init; }
+    public bool HasMatchingProcessedOrder { get; init; }
+    public bool IsAwaitingPayment { get; init; }
+}
+
+public static class P72GovernedPaymentCycleAdmission
+{
+    public static P72AdmissionResult Admit(P72AdmissionObservation? observation, string? fixtureKey)
+    {
+        if (string.IsNullOrWhiteSpace(fixtureKey))
+        {
+            return new P72AdmissionResult(P72AdmissionDisposition.NoGo, P72AdmissionFailureCategory.Unavailable);
+        }
+
+        if (observation is null || !observation.IsComplete)
+        {
+            return new P72AdmissionResult(P72AdmissionDisposition.NoGo, P72AdmissionFailureCategory.Unavailable);
+        }
+
+        // 狀態矛盾校驗
+        if (observation.HasMatchingProcessedOrder && observation.IsAwaitingPayment)
+        {
+            return new P72AdmissionResult(P72AdmissionDisposition.NoGo, P72AdmissionFailureCategory.Unavailable);
+        }
+
+        if (observation.Outcome == P72DonationPaymentOutcome.Failed)
+        {
+            return new P72AdmissionResult(P72AdmissionDisposition.RequireReconciliation, P72AdmissionFailureCategory.None);
+        }
+
+        if (observation.Outcome == P72DonationPaymentOutcome.Succeeded)
+        {
+            if (!observation.HasMatchingProcessedOrder && observation.IsAwaitingPayment)
+            {
+                return new P72AdmissionResult(P72AdmissionDisposition.PrepareFutureGovernedDispatch, P72AdmissionFailureCategory.None);
+            }
+            
+            if (observation.HasMatchingProcessedOrder)
+            {
+                return new P72AdmissionResult(P72AdmissionDisposition.AlreadyProcessed, P72AdmissionFailureCategory.None);
+            }
+        }
+
+        return new P72AdmissionResult(P72AdmissionDisposition.NoGo, P72AdmissionFailureCategory.Unavailable);
+    }
+}
+```
+
+### 步驟 2：建立單元測試 `P72GovernedPaymentCycleAdmissionTests`
+在 `SpeechMessage.Dynamics.Tests/` 下建立新檔案 `P72GovernedPaymentCycleAdmissionTests.cs`，涵蓋上述所有 RED/GREEN 測試案例。
+
+---
+
+## 4. Considerations (考量事項)
+
+* **效能 (Performance)**：由於准入合約為純記憶體運算，不涉及 any I/O、資料庫或網路呼叫，其執行時間應在微秒級別，對系統效能無 any 負面影響。
+* **可維護性 (Maintainability)**：採用「觀察-決策-處置」模式，將業務邏輯與副作用完全隔離，使程式碼結構清晰，易於維護與擴充。
+* **安全性 (Security)**：嚴格的狀態矛盾校驗與 fail-closed 設計，確保在 any 異常或未預期的狀態下，系統均能安全地拒絕執行（NoGo），防止重複寫入或數據污染。
```
