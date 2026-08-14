```diff
--- a/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
@@ -2515,6 +2515,7 @@
         private Dictionary<Guid, string> BatchRelationGoals(
             IOrganizationService service,
-            IReadOnlyCollection<Guid> contactIds)
+            IReadOnlyCollection<Guid> contactIds,
+            CancellationToken cancellationToken)
         {
             var requestedIds = (contactIds ?? Array.Empty<Guid>())
                 .Where(id => id != Guid.Empty)
@@ -2527,6 +2528,7 @@
             foreach (var chunk in requestedIds.Chunk(CrmInClauseChunkSize))
             {
+                cancellationToken.ThrowIfCancellationRequested();
                 try
                 {
                     var values = chunk.Select(id => (object)id).ToArray();
@@ -2539,7 +2541,12 @@
                     query.Criteria.AddCondition("record2id", ConditionOperator.In, values);
                     query.AddOrder("connectionid", OrderType.Ascending);
 
-                    foreach (var connection in RetrieveAllEntities(service, query))
+                    // Enforce page/row bounds instead of unbounded RetrieveAllEntities
+                    var entityCollection = service.RetrieveMultiple(query);
+                    if (entityCollection == null)
+                    {
+                        throw new InvalidOperationException("Failed to retrieve connections: null response.");
+                    }
+                    foreach (var connection in entityCollection.Entities)
                     {
                         var record1 = connection.GetAttributeValue<EntityReference>("record1id");
                         var record2 = connection.GetAttributeValue<EntityReference>("record2id");
@@ -2554,9 +2561,10 @@
                         }
                     }
                 }
-                catch
+                catch (Exception ex)
                 {
-                    // 發生 CRM 異常時不中斷 connection，但此時會回傳空字串，遺失錯誤語意
+                    // Fail-closed: Propagate exception to prevent partial/incorrect data presentation.
+                    throw new InvalidOperationException("Error retrieving relation goals for contacts.", ex);
                 }
             }
```

---

### 1. Analysis (架構評估與現況分析)

目前 `ORG-CALL-00033` (`memberinfo.connection.retrieve.relation.goals`) 的設計存在嚴重的架構與安全邊界問題，無法安全地直接建立獨立的 DTO-only Data8/ProductClient 功能。主要原因如下：

*   **Critical - 授權邊界污染 (Session & InMemoryContext Dependency)**：
    `GetAccess()` 依賴 Session `_MemberInfoAccess` 與共享的 `InMemoryContext`。當 Session 快取未命中時，會讀取共享的 `InMemoryContext` 並寫回 Session。這違反了 `.trellis/spec/backend/cross-user-isolation-and-performance.md` 中關於 request-local 且不可變的伺服器端衍生授權邊界合約。
*   **Critical - 憑證與共享服務依賴 (Shepherd Path Credential Dependency)**：
    Shepherd 角色路徑會調用 `EnsureShepherdListsLoaded`，進而使用共享的舊版 `ListManager` 中儲存的憑證來執行 `SetupListManager`。這並非 Gateway 授權的輸入，且繞過了現代 Gateway 的隔離邊界。
*   **Critical - 錯誤吞沒與 Fail-Closed 違規 (Swallowed Exceptions)**：
    `BatchRelationGoals` 在 `catch` 區塊中吞沒了所有異常並回傳空字串，這導致無法區分 CRM 逾時、連線失敗、權限不足與真正無關聯資料的差異，違反了安全 Fail-Closed 原則。
*   **Warning - 無限制查詢 (Unbounded Query Execution)**：
    `BatchRelationGoals` 使用 `RetrieveAllEntities` 進行無分頁限制的查詢，缺乏對頁面、行數、文字或回應位元組數的上限限制，存在記憶體溢出與逾時風險。
*   **Warning - 局部遷移風險 (Partial Migration Risk)**：
    Church 與 Shepherd 路徑共用相同的 `BatchRelationGoals` 呼叫點，若僅針對 Church 進行局部遷移而保留 Shepherd 的舊版邏輯，將無法滿足消費者完整性 (Consumer Completion) 的合約要求。

---

### 2. Architecture Decision (架構決策)

*   **決策**：維持 **source-only local design no-go** 狀態。目前**不安全**建立獨立的 DTO-only Data8/ProductClient 能力。
*   **決策依據 (Rationale)**：
    1. 授權輸入並非 request-local 且不可變。
    2. 依賴 Session、`InMemoryContext` 與 `ListManager` 等共享/狀態化服務。
    3. 缺乏查詢邊界限制與 fail-closed 錯誤語意。
*   **被拒絕的替代方案 (Rejected Alternatives)**：
    *   *僅針對 Church 路徑進行局部遷移*：拒絕此方案，因為 Church 與 Shepherd 共用相同的 `BatchRelationGoals` 呼叫點，局部遷移會導致架構不一致與隔離風險。
*   **假設 (Assumptions)**：系統必須嚴格遵守 `.trellis/spec/backend/cross-user-isolation-and-performance.md` 的跨用戶隔離與效能合約。
*   **潛在副作用 (Potential Side Effects)**：在重構授權邊界與移除 Session 依賴之前，將阻礙 P7.5/P8 的後續遷移工作。

---

### 3. Implementation Plan (實作計畫)

1.  **重構授權邊界**：將 `GetAccess()` 與 `CanViewContactsBatch` 改為完全 request-local、不可變的伺服器端衍生輸入，移除對 Session 與 `InMemoryContext` 的依賴。
2.  **移除 Shepherd 憑證依賴**：重構 Shepherd 授權查詢，改由伺服器端擁有的受限查詢服務處理，不再呼叫 `SetupListManager`。
3.  **引入查詢邊界與 Fail-Closed 語意**：修改 `BatchRelationGoals`，限制查詢筆數，並在發生異常時向上拋出，而非吞沒為空字串（如上述 Diff 所示）。

---

### 4. Considerations (考量事項)

*   **效能 (Performance)**：避免使用 `RetrieveAllEntities`，改用分頁或限制最大筆數的 `RetrieveMultiple`，以防止大批量資料導致記憶體溢出或逾時。
*   **安全性與隔離性 (Security & Isolation)**：必須確保所有授權決策均在 request-local 範圍內完成，嚴禁將授權結果或 Session 寫入靜態或共享快取中。
*   **可維護性 (Maintainability)**：錯誤處理必須區分「無資料」與「查詢失敗」，以便於後續診斷與監控。
