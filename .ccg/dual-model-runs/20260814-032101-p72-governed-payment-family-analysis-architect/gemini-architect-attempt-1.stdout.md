```diff
--- /dev/null
+++ b/docs/p72-governed-payment-family-analysis.md
@@ -0,0 +1,165 @@
+# P7.2 Governed Recurring Payment-Return Write Family — 架構分析報告
+
+## 1. 架構評估 (Analysis)
+
+目前專案中關於 P7.2 循環付款退回寫入邊界的設計，正處於從舊有混亂寫入模式向受控寫入（Governed Write）演進的關鍵階段：
+* **歷史 Slice C 狀態**：已永久關閉，其狀態為 `write-not-committed` no-go，且清理工作已完成。其 nonce、ledger、descriptor、fixture 和 evidence 均不可重用或重放。
+* **現有本地決策器**：`P72DonationPaymentLocalDecision` 與 `P72DonationPaymentLocalPlanBuilder` 為純本地、無副作用的合約，且其 `CeDispatchAllowed` 與 `ProductConsumerAllowed` 均被強制設為 `false`，保持 CE 派遣與消費者禁用。
+* **遺留處理器**：`RecurringDonationPaymentProcessor` 混合了多種副作用（如聯絡人更新、費用建立、擁有者分配、預訂更新及通知），缺乏明確的事務與清理邊界。
+* **新准入合約**：`P72GovernedPaymentCycleAdmission` 作為 `payments.fee.update.after.payment` 的准入合約，必須繼承純本地、無副作用的設計，僅接受去識別化的階段證據，並返回 fail-closed 的處置。
+
+---
+
+## 2. 架構決策 (Architecture Decision)
+
+### 決策：採用純同步、無副作用的狀態機准入合約，並實施嚴格的家族隔離與全新夾具規則。
+* **合理性 (Rationale)**：避免在准入階段引入任何 I/O、網路或 CRM SDK 依賴，將決策邏輯與執行邏輯完全解耦。透過全新的描述符與帳本，確保與已關閉的 Slice C 完全隔離，防止歷史數據重放。
+* **拒絕的替代方案 (Rejected Alternatives)**：
+  * *重用 Slice C 的基礎設施*：拒絕。因為 Slice C 已被標記為永久關閉且不可重用，重用會破壞審計追蹤與隔離性。
+  * *在准入合約中直接呼叫 CRM SDK 進行預檢*：拒絕。這會破壞純本地合約的無副作用約束，增加測試複雜度與執行期風險。
+* **假設 (Assumptions)**：假設未來的受控 CE 執行器（Executor）將嚴格遵守准入合約返回的處置，並負責處理實際的 I/O、事務、讀回與清理。
+* **潛在副作用 (Potential Side Effects)**：由於准入合約不執行實際寫入，若未來執行器未正確實作，可能會導致狀態不一致。因此必須在執行器層級強制執行嚴格的帳本與清理規則。
+
+---
+
+## 3. 實作計畫與虛擬碼 (Implementation Plan)
+
+### 步驟 1：建立 `P72GovernedPaymentCycleAdmission` 合約
+在 `SpeechMessage.Dynamics.Abstractions/Operations/` 下建立新檔案，定義輸入證據與處置狀態。
+
+```csharp
+public sealed class P72GovernedPaymentCycleAdmission
+{
+    public static P72AdmissionResult Admit(P72AdmissionObservation observation, string fixtureKey)
+    {
+        if (string.IsNullOrWhiteSpace(fixtureKey))
+        {
+            return P72AdmissionResult.FailClosed(P72AdmissionDisposition.NoGo, "Malformed fixture key");
+        }
+        
+        if (!observation.IsComplete)
+        {
+            return P72AdmissionResult.FailClosed(P72AdmissionDisposition.NoGo, "Observation incomplete");
+        }
+
+        // 僅在全新且成功的交易下允許準備派遣
+        if (observation.Outcome == P72PaymentOutcome.Succeeded && 
+            !observation.HasMatchingProcessedOrder && 
+            observation.IsAwaitingPayment)
+        {
+            return P72AdmissionResult.Success(P72AdmissionDisposition.PrepareFutureGovernedDispatch);
+        }
+
+        if (observation.HasMatchingProcessedOrder)
+        {
+            return P72AdmissionResult.Success(P72AdmissionDisposition.AlreadyProcessed);
+        }
+
+        return P72AdmissionResult.FailClosed(P72AdmissionDisposition.NoGo, "Unhandled state transition");
+    }
+}
+```
+
+### 步驟 2：建立單元測試 `P72GovernedPaymentCycleAdmissionTests`
+在 `SpeechMessage.Dynamics.Tests/` 下建立測試，覆蓋所有邊界條件與併發隔離。
+
+---
+
+## 4. 關鍵分析與問題回覆 (Critical / Warning / Info Findings)
+
+### 1. 新准入合約中的關鍵設計風險或缺失的不變量 (Critical Design Risks & Missing Invariants)
+* **[Critical] 狀態組合爆炸與未定義轉移**：
+  * *風險*：輸入的 `P72DonationPaymentLocalObservation` 屬性（如 `IsComplete`、`Outcome`、`HasMatchingProcessedOrder`、`IsAwaitingPayment`）可能存在矛盾的組合（例如 `Outcome = Succeeded` 但 `IsComplete = false`）。
+  * *不變量要求*：合約必須強制執行「白名單狀態轉移」。任何未明確定義為安全的組合，必須一律判定為 `NoGo` (fail-closed)，且 `ProhibitsReplay` 必須為 `true`。
+* **[Warning] 併發狀態污染**：
+  * *風險*：若准入合約內部或其 Plan Builder 意外使用了靜態快取（static cache）或共享集合，會導致併發請求（A/B 測試）互相干擾。
+  * *不變量要求*：合約必須是執行緒安全的純函數，不保留任何 mutable 狀態。
+
+### 2. 未來受控 CE 執行器必須強制執行的最小規則 (Minimum Executor Rules)
+* **[Critical] 描述符與帳本規則 (Descriptor & Ledger)**：
+  * 必須使用任務專屬的全新描述符（task-owned fresh fixture descriptor），包含唯一的 nonce。
+  * 必須綁定獨立的、唯寫/唯讀的本地帳本（ledger），記錄所有變更的實體 ID（如新建立的 fee ID），嚴禁重用歷史帳本。
+* **[Critical] 預檢與允許可寫清單 (Preflight & Allowlist)**：
+  * 在執行任何 mutation 之前，必須進行唯讀預檢（read-only preflight），且預檢結果必須明確為 `go`。
+  * 允許可寫清單必須嚴格限制在 `payments.fee.update.after.payment`，禁止任何非允許清單內的 CRUD 操作。
+* **[Critical] 精確讀回與確定性清理 (Read-back & Cleanup)**：
+  * 執行 mutation 後，必須進行精確的標量投影讀回（exact scalar projection read-back），比對預期的 postimage。
+  * 清理機制必須按照帳本記錄的相反順序（reverse-known-key order）清除所有已建立的實體，並驗證讀回顯示這些實體已不存在，恢復 baseline 狀態。
+
+### 3. 現有 Slice C 測試夾具基礎設施是否可重用 (Reuse of Slice C Infra)
+* **[Critical] 絕對禁止重用 Slice C 基礎設施**：
+  * *原因*：歷史的 P7.2 Slice C 已經永久關閉，其狀態為 `write-not-committed` no-go，且清理工作已完成。其 nonce、ledger、descriptor、fixture 和 evidence 均不可重用或重放。
+  * *安全做法*：必須為新的 governed child 建立全新的、獨立的 task-owned fresh fixture，避免任何歷史資料殘留或交叉污染。
+
+### 4. 實作前必須為 RED/GREEN 的測試案例 (Test Cases Requirements)
+* **[Info] RED 階段（實作前必須失敗）**：
+  * *無效夾具鍵拒絕*：驗證輸入無效的 fixture key（如空字串或空白）時，必須拒絕並返回 `NoGo`，且不產生任何 plan。
+  * *併發 A/B 隔離測試*：使用多執行緒併發呼叫，傳入不同的去識別化證據，驗證合約不會共享任何 mutable state。
+  * *非 Go 預檢拒絕*：驗證當 preflight 狀態為非 `go`（如超時、未知、衝突）時，合約必須返回 `NoGo` 且 `ProhibitsReplay = true`。
+* **[Info] GREEN 階段（實作後必須通過）**：
+  * *全新成功交易准入*：驗證當輸入為全新的成功交易（`IsComplete = true`, `Outcome = Succeeded`, `HasMatchingProcessedOrder = false`, `IsAwaitingPayment = true`）且 fixture key 有效時，正確返回 `PrepareFutureGovernedDispatch` 處置，且產生的 plan 中 `CeDispatchAllowed` 與 `ProductConsumerAllowed` 均為 `false`。
+  * *已處理交易准入*：驗證當交易已處理（`HasMatchingProcessedOrder = true`）時，返回 `AlreadyProcessed` 處置，且不產生 plan。
+  * *失敗交易准入*：驗證當交易失敗（`Outcome = Failed`）時，返回 `RequireReconciliation` 處置，且不產生 plan。
+
+### 5. 預期範圍是否錯誤地暗示了 P7.4/P7.5/P8/流量或遺留消費者變更 (Scope Boundaries)
+* **[Critical] 嚴格限制範圍，禁止越界變更**：
+  * *說明*：本 slice 的範圍僅限於純本地的 `P72GovernedPaymentCycleAdmission` 合約與相關的單元測試。
+  * *禁止行為*：絕對不應修改 `RecurringDonationPaymentProcessor` 等遺留的生產 CRM 寫入器，也不應涉及任何 feature flags、流量切換（ChurchReport traffic）、CE 8.2、官方 Worker、P7.5 ToolUtility 移除或 P8 部署。
+  * *隔離驗證*：所有 CE 派遣與消費者功能在 plan 中必須保持禁用（`CeDispatchAllowed=false`, `ProductConsumerAllowed=false`），以確保與其他 P7/P8 slice 的完全隔離。
+
+---
+
+## 5. 考量事項 (Considerations)
+
+* **效能 (Performance)**：由於合約為純本地同步計算，無任何 I/O 運算，其執行時間應在微秒級別，對系統效能無負面影響。
+* **可維護性 (Maintainability)**：採用「觀察-決策-處置」模式，使業務邏輯與副作用完全分離，極易進行單元測試與形式驗證。
+* **安全性 (Security)**：嚴格的去識別化輸入與 fail-closed 設計，確保即使在異常情況下，系統也不會誤執行未授權的寫入操作。
```
