### 1. Analysis (架構評估與現況分析)

針對 `ORG-CALL-00033` (`memberinfo.connection.retrieve.relation.goals`) 的現有實作進行審查，發現以下架構與安全邊界問題，證實目前**無法安全地**建立獨立的 DTO-only Data8/ProductClient 能力：

*   **Critical - 授權輸入非 Request-Local 且可變**：
    *   `GetAccess()` 依賴 Session `_MemberInfoAccess`。當 Session 快取未命中時，會讀取共享的 `InMemoryContext` 並寫回 Session。這違反了 `.trellis/spec/backend/cross-user-isolation-and-performance.md` 中關於「授權邊界必須是伺服器端衍生且在請求內不可變 (request-local immutable)」的規定。
*   **Critical - 依賴共享服務與保存憑證**：
    *   Shepherd 權限路徑會觸發 `EnsureShepherdListsLoaded`，進而使用保存的舊版憑證呼叫 `SetupListManager`。這使得舊版 `ListManager` 憑證成為實質上的授權來源，而非由 Gateway 統一控管。
*   **Critical - 錯誤語意吞沒 (Fail-Closed 違規)**：
    *   `BatchRelationGoals` 在執行查詢時，使用 `catch { }` 吞沒了所有異常，並統一格式化為空字串。這導致系統無法區分「連線逾時/部分失敗」與「確實無關聯資料」的差異，違反了 fail-closed 原則。
*   **Warning - 無限制查詢 (Unbounded Query)**：
    *   `BatchRelationGoals` 使用 `RetrieveAllEntities` 載入所有關聯，缺乏對分頁、資料筆數及回應位元組數 (response-byte bounds) 的限制，存在記憶體溢出與效能隱憂。
*   **Warning - 局部遷移風險**：
    *   若僅針對 Church 路徑進行局部遷移，而 Shepherd 路徑仍保留舊版邏輯，將導致同一能力 (Capability) 的行為不一致，無法視為完整的消費者遷移。

---

### 2. Architecture Decision (架構決策)

*   **決策**：維持 **source-only local design no-go** 狀態。在解決授權邊界與 Session 依賴前，**不安全**進行獨立的 DTO-only Data8/ProductClient 遷移。
*   **決策依據 (Rationale)**：
    *   授權輸入並非 request-local 且不可變。
    *   依賴 Session、`InMemoryContext` 與 `ListManager` 等共享/狀態化服務。
    *   缺乏查詢邊界限制與 fail-closed 錯誤語意。
*   **被拒絕的替代方案 (Rejected Alternatives)**：
    *   *僅針對 Church 路徑進行局部遷移*：拒絕此方案，因為 Church 與 Shepherd 共用相同的 `BatchRelationGoals` 呼叫點，局部遷移會導致架構不一致與隔離風險。
*   **假設 (Assumptions)**：系統必須嚴格遵守 `.trellis/spec/backend/cross-user-isolation-and-performance.md` 的跨用戶隔離與效能合約。
*   **潛在副作用 (Potential Side Effects)**：在重構授權邊界與移除 Session 依賴之前，將阻礙 P7.5/P8 的後續遷移工作。

---

### 3. Implementation Plan (實作計畫與 Diff)

#### 實作步驟：
1. **重構授權邊界**：將 `GetAccess()` 與 `CanViewContactsBatch` 改為完全 request-local、不可變的伺服器端衍生輸入，移除對 Session 與 `InMemoryContext` 的依賴。
2. **移除 Shepherd 憑證依賴**：重構 Shepherd 授權查詢，改由伺服器端擁有的受限查詢服務處理，不再呼叫 `SetupListManager`。
3. **引入查詢邊界與 Fail-Closed 語意**：修改 `BatchRelationGoals`，限制查詢筆數，並在發生異常時向上拋出，而非吞沒為空字串。

#### 概念性 Diff 修正 (Conceptual Diff)：

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
+                    // 限制查詢邊界，避免無限制的 RetrieveAllEntities
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
+                    // Fail-closed: 向上拋出異常，不吞沒錯誤，確保錯誤語意不遺失
+                    throw new InvalidOperationException("Error retrieving relation goals for contacts.", ex);
                 }
             }
```

---

### 4. Considerations (考量事項)

*   **效能 (Performance)**：避免使用 `RetrieveAllEntities`，改用分頁或限制最大筆數的 `RetrieveMultiple`，以防止大批量資料導致記憶體溢出或逾時。
*   **安全性與隔離性 (Security & Isolation)**：必須確保所有授權決策均在 request-local 範圍內完成，嚴禁將授權結果或 Session 寫入靜態或共享快取中。
*   **可維護性 (Maintainability)**：錯誤處理必須區分「無資料」與「查詢失敗」，以便於後續診斷與監控。
