# P7.4 Legacy Admission Boundary 設計審查報告

## 1. Analysis (分析)

本審查針對既有工作樹中 P7.4 child 的規劃進行評估。擬議設計旨在建立一個 host-owned `LegacyToolUtilityDrainController`，僅管理受控 legacy ingress 的 stop/acquire/drain lease 生命週期，以作為 `DynamicsAccess:Package01FeeReadsEnabled` 功能旗標啟用的安全防護網。

然而，現有的 `DonationFeeQueryService` 在 `_package01Enabled` 為 `false` 時，直接呼叫 legacy `ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml`，該呼叫最終對長壽命的 `IOrganizationService` 執行同步的 `RetrieveMultiple`。此架構存在嚴重的同步阻塞與 cancellation 斷裂問題，導致擬議的 controller 無法保證真正的組織級安全（Organization-Level Safety）。

---

## 2. Architecture Decision (架構決策)

*   **決策**: 必須將 `LegacyToolUtilityDrainController` 定義為僅具備「局部計量與排空（Local Metering & Draining）」功能的輔助工具，**絕不能**將其視為「組織級安全閘門（Organization-Level Safety Gate）」。功能旗標的啟用必須依賴外部的 deployment owner 證明，而非僅依賴此 controller 的狀態。
*   **理由**: 
    1. Legacy `ToolUtilityClass` 內部的 SDK 呼叫是完全同步的，不接受 `CancellationToken`，無法在 lease 被取消或 drain timeout 時中斷已經發送給 Dynamics CRM 的請求。
    2. `ToolUtilityClass` / `ToolUtilityFactory` 是 process-wide singleton，且可能被其他服務（如 `DonationPaymentManager`）直接呼叫，這些呼叫並不受此 controller 的控制（存在 Coverage Gap）。
*   **替代方案**: 曾考慮重構 `ToolUtilityClass` 以支援非同步與 cancellation，但因 legacy 代碼風險過高且涉及 P7.2 已凍結的代碼而放棄。
*   **副作用**: 在高併發或 Dynamics CRM 響應緩慢時，host shutdown 可能會因為同步阻塞而超時，導致 `DrainTimeout`。

---

## 3. Findings (審查發現)

### 🔴 Critical (關鍵缺陷)

1.  **同步阻塞呼叫與 Cancellation 斷裂 (Synchronous Blocking Call & Cancellation Disconnect)**
    *   **檔案路徑**: `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
    *   **說明**: 當 `_package01Enabled` 為 `false` 時，系統執行同步的 `_utility.RetrieveDedicationFeeByDateFetchXml`。由於該方法不接受 `CancellationToken`，即使 `LegacyToolUtilityDrainController` 觸發了 cancellation 或 shutdown timeout，底層的 socket/HTTP 請求仍會繼續執行直到完成。這會導致 controller 提早宣告 `drained`（因為呼叫端執行緒可能已因 exception 退出並釋放 lease），但實際的 I/O 還在進行，從而與 Gateway/Data8 產生併發衝突（overlap），破壞了 non-overlap 的安全前提。
2.  **局部計量與組織級安全的錯覺 (Operation-Level Metering vs Organization-Level Safety)**
    *   **說明**: `LegacyToolUtilityDrainController` 僅在 `DonationFeeQueryService` 的 legacy 分支中獲取 lease，這只是 operation-level metering。由於 `ToolUtilityClass` 仍可能被其他未受控的服務直接呼叫，將此 controller 的 `stopped-and-drained` 狀態視為「整個 Organization 級別的 legacy 流量已完全停止」，是一個嚴重的安全錯覺。
3.  **缺乏跨主機的分散式協調 (Lack of Cross-Host Distributed Coordination)**
    *   **說明**: `LegacyToolUtilityDrainController` 是一個 per-host in-memory 的計數器，無法跨多個 IIS/App Host 實例進行協調。如果有多個 host，其中一個 host 宣告 drained，並不代表其他 host 也 drained，更無法與 Gateway/Data8 的 durable SQL coordinator 共享狀態。

### ⚠️ Warning (警告事項)

1.  **Lease 洩漏風險 (Lease Leakage Risk)**
    *   **說明**: 如果呼叫端程式碼在某些異常路徑（例如 DTO 轉換失敗、OverflowException）中沒有正確執行 `finally` 釋放 lease，或者在 `using` 區塊外部發生異常，可能會導致 lease 洩漏，使 active count 永遠大於 0，從而使 `StopIntakeAsync` 永久阻塞。
2.  **計數器 Underflow 與 Race Condition**
    *   **說明**: 在高併發下，如果 `Acquire` 與 `StopIntake` 競爭，或者 lease 被重複 dispose，可能會導致計數器 underflow（變為負數）或狀態混亂。
3.  **Validator 靜態檢查的局限性 (Static Validator Limitations)**
    *   **說明**: Validator 僅檢查靜態的 category 存在性，但無法驗證實際部署拓撲（Deployment Topology）的一致性，例如無法驗證是否所有 active hosts 都已套用相同的 epoch/digest。

### ℹ️ Info (一般資訊)

1.  **設計的無狀態性與安全性 (Statelessness & Security)**
    *   **說明**: 設計中明確要求 controller 不持有 CRM、profile、endpoint、credential、request 或 response，這在設計上避免了 cross-user/profile/session 的資料洩漏，是一個良好的安全設計實踐。
2.  **Runbook 的 Rollback 流程優化 (Rollback Flow Optimization)**
    *   **說明**: 建議在 runbook 中補上從 Gateway 切回 legacy 時，如何確保 Gateway 的 in-flight 寫入已完全排空，且 legacy intake 重新啟用時不會與 Gateway 的殘留寫入產生衝突的步驟。

---

## 4. Implementation Plan & Proposed Diff (實施計劃與建議變更)

### 建議的程式碼變更 (Proposed Diff)

```diff
--- a/SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs
@@ -32,18 +32,21 @@
         private readonly ToolUtilityClass _utility;
         private readonly IPackage01FeeReadClient? _package01FeeReadClient;
         private readonly ProductDynamicsOptions? _dynamicsAccess;
         private readonly bool _package01Enabled;
+        private readonly ILegacyToolUtilityDrainController? _drainController;
 
         public DonationFeeQueryService(ToolUtilityClass utility)
-            : this(utility, package01FeeReadClient: null, dynamicsAccess: null)
+            : this(utility, package01FeeReadClient: null, dynamicsAccess: null, drainController: null)
         {
         }
 
         public DonationFeeQueryService(
             ToolUtilityClass utility,
             IPackage01FeeReadClient? package01FeeReadClient,
             IOptions<ProductDynamicsOptions>? dynamicsAccess,
-            bool package01FeeReadsEnabled = false)
+            bool package01FeeReadsEnabled = false,
+            ILegacyToolUtilityDrainController? drainController = null)
         {
             _utility = utility ?? throw new ArgumentNullException(nameof(utility));
             _package01FeeReadClient = package01FeeReadClient;
             _dynamicsAccess = dynamicsAccess?.Value;
             _package01Enabled = package01FeeReadsEnabled && package01FeeReadClient is not null;
+            _drainController = drainController;
         }
 
         /// <summary>
@@ -68,14 +71,36 @@
             if (_package01Enabled)
             {
                 await FillFeeListViaPackage01Async(model, contactId, fullName, cancellationToken)
                     .ConfigureAwait(false);
                 return;
             }
 
-            // ---- 舊路徑，同步阻塞呼叫 ----
-            EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
-                fullName,
-                contactId.ToString(),
-                model.QueryStartDate,
-                model.QueryEndDate);
+            if (_drainController != null)
+            {
+                // 獲取 lease，若已停止 intake 則會拋出異常或拒絕
+                using (var lease = await _drainController.AcquireLeaseAsync(
+                    LegacyToolUtilityWorkload.FeeRead, 
+                    cancellationToken).ConfigureAwait(false))
+                {
+                    EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
+                        fullName,
+                        contactId.ToString(),
+                        model.QueryStartDate,
+                        model.QueryEndDate);
+
+                    System.Diagnostics.Trace.WriteLine(
+                        $"[DEDQUERY-LEGACY] ContactId={contactId:D} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");
+
+                    model.TotalAmount = 0;
+                    model.DedicationFeeList = feeEntities.Entities
+                        .Select(MapFee)
+                        .ToList();
+
+                    foreach (var fee in model.DedicationFeeList)
+                    {
+                        model.TotalAmount += fee.Amount;
+                    }
+                }
+            }
+            else
+            {
+                // ---- 舊路徑，同步阻塞呼叫 ----
+                EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
+                    fullName,
+                    contactId.ToString(),
+                    model.QueryStartDate,
+                    model.QueryEndDate);
 
-            System.Diagnostics.Trace.WriteLine(
-                $"[DEDQUERY-LEGACY] ContactId={contactId:D} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");
-
-            model.TotalAmount = 0;
-            model.DedicationFeeList = feeEntities.Entities
-                .Select(MapFee)
-                .ToList();
-
-            foreach (var fee in model.DedicationFeeList)
-            {
-                model.TotalAmount += fee.Amount;
-            }
+                System.Diagnostics.Trace.WriteLine(
+                    $"[DEDQUERY-LEGACY] ContactId={contactId:D} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");
+
+                model.TotalAmount = 0;
+                model.DedicationFeeList = feeEntities.Entities
+                    .Select(MapFee)
+                    .ToList();
+
+                foreach (var fee in model.DedicationFeeList)
+                {
+                    model.TotalAmount += fee.Amount;
+                }
+            }
         }
```
