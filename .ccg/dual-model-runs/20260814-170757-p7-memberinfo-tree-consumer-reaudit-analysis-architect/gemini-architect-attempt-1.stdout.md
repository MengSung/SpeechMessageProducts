# P7 MemberInfo Tree Consumer 授權重新稽核報告

本報告針對權威矩陣（Authoritative Matrix）中的 `ORG-CALL-00031`、`ORG-CALL-00032` 與 `ORG-CALL-00033` 進行唯讀架構與授權邊界交叉分析，判定是否能安全建立下一個獨立的、伺服器授權（server-authorized）、請求區域化（request-local）、有界的（bounded）、不可變的（immutable）且僅限 DTO（DTO-only）的資料平面（data-plane）本機實作子任務（child）。

---

## 逐 Row 稽核分析

### Row 1: ORG-CALL-00031 (`memberinfo.smallgroup.retrieve.descriptors`)

1. **Matrix Contract 與實際 Call Chain 一致性**
   * **權威合約**：`memberinfo.smallgroup.retrieve.descriptors`
   * **實際呼叫鏈**：`MemberInfoController.GetVisibleSmallGroupDescriptors` -> `FetchSmallGroupDescriptors`。
   * **查詢行為**：查詢 `list` 實體，篩選條件固定為 `statecode = 0`（作用中）、`purpose = "小組名單"` 且 `new_app_named = 1`，並依 `listname` 升冪排序。
   * **投影欄位**：`listid`, `listname`, `new_area_name`, `new_contact_race_leager_list`, `new_contact_family_leader_list`, `new_group_time`, `new_group_place`, `new_contact_list_arealeader`。
   * **一致性判定**：**完全一致**。

2. **授權 Trust Boundary 是否完整**
   * **判定**：**完整**。
   * **說明**：可完全脫離 legacy 的 `Session`、`InMemoryContext` 與 `ListManager`。新設計可直接注入已封存的 `MemberInfoServerAssignmentEvidenceSource` 取得 `MemberInfoTargetAuthorizationEvidence`：
     * 若 `AccessMode` 為 `ChurchWide`，則允許查詢所有符合條件的 `list`。
     * 若 `AccessMode` 為 `AssignedLists`，則在查詢條件中強制加入 `listid IN (AssignedListIds)` 進行過濾。
     * 授權判定完全在伺服器端（request-local）完成，不依賴瀏覽器傳入的定位器或可變狀態。

3. **設計要求**
   * **Fixed Query/Projection**：固定 QueryExpression/FetchXML，嚴格僅投影上述 8 個欄位，不允許動態拼接非安全條件。
   * **輸出 Boundedness**：限制最大回傳小組數（例如上限 512 筆，對齊 `MaximumVisibleListIds`），超過上限則觸發 fail-closed。
   * **取消與 Fault Union**：必須傳遞 `CancellationToken`。任何 CRM/Data8 傳輸異常或逾時應封裝為 `SourceUnavailable` 失敗，回傳空集合，嚴禁 legacy fallback。
   * **Resource Owner**：由系統/服務帳戶擁有連線與查詢權限，非 caller 指定。
   * **A/B Isolation 與 Rollback**：預設關閉（Feature Gate = false），新舊程式碼路徑完全隔離，可隨時透過 Gate 進行無痛 rollback。

4. **結論**：**可建立 implementation child**。

5. **嚴格禁止事項與最小本機驗證集合**
   * **嚴格禁止**：
     * 嚴格禁止讀取 `Session`、`InMemoryContext` 或 `ListManager`。
     * 嚴格禁止使用保存的帳密進行連線。
     * 嚴格禁止 caller 自行指定 `listId` 繞過 `AssignedListIds` 授權範圍。
   * **最小本機驗證集合**：
     * 驗證 `ChurchWide` 模式下，能正確投影出所有作用中的小組描述元。
     * 驗證 `AssignedLists` 模式下，查詢結果被嚴格限制在 `AssignedListIds` 內，傳入未授權的 ID 必須被過濾。
     * 驗證當 `CancellationToken` 觸發時，查詢能立即中止並釋放資源。

---

### Row 2: ORG-CALL-00032 (`memberinfo.smallgroup.retrieve.memberships`)

1. **Matrix Contract 與實際 Call Chain 一致性**
   * **權威合約**：`memberinfo.smallgroup.retrieve.memberships`
   * **實際呼叫鏈**：`MemberInfoController.FetchGroupMemberships`。
   * **查詢行為**：查詢 `listmember` 實體，並 inner join `contact` 實體。篩選條件為 `listid IN (listIds)`，且聯絡人 `statecode = 0` 且 `customertypecode != closedStatus`。
   * **投影欄位**：`listid`, `entityid` (即 `contactid`)。
   * **一致性判定**：**完全一致**。

2. **授權 Trust Boundary 是否完整**
   * **判定**：**完整**。
   * **說明**：雖然此呼叫接收 `listIds` 作為參數，但安全邊界必須在 request-local 進行交叉驗證：
     * 若 `AccessMode` 為 `AssignedLists`，則實際查詢的 `listid` 必須強制取 `listIds` 與 `AssignedListIds` 的**交集**。
     * 如此可確保 caller 無法透過竄改參數來獲取未授權小組的成員名單。

3. **設計要求**
   * **Fixed Query/Projection**：固定查詢 `listmember` 與 `contact`，僅投影 `listid` 與 `entityid`，不允許投影聯絡人敏感個資。
   * **輸出 Boundedness**：限制傳入的 `listIds` 數量（最大 512），且對 `listmember` 進行 chunking（分批，如每批 50 筆）查詢，限制總回傳筆數。
   * **取消與 Fault Union**：支援 `CancellationToken`，異常時 fail-closed。
   * **Resource Owner**：由系統/服務帳戶擁有。
   * **A/B Isolation 與 Rollback**：預設關閉，新舊路徑完全隔離。

4. **結論**：**可建立 implementation child**。

5. **嚴格禁止事項與最小本機驗證集合**
   * **嚴格禁止**：
     * 嚴格禁止直接信任並查詢 caller 傳入的未授權 `listIds`。
     * 嚴格禁止在未經交集過濾的情況下執行 `listmember` 批次查詢。
   * **最小本機驗證集合**：
     * 測試當傳入包含未授權的 `listId` 時，回傳的 membership 集合不包含該未授權小組的任何成員。
     * 測試當 `AssignedListIds` 為空時，查詢應立即回傳空集合而不發送 CRM 請求。

---

### Row 3: ORG-CALL-00033 (`memberinfo.connection.retrieve.relation.goals`)

1. **Matrix Contract 與實際 Call Chain 一致性**
   * **權威合約**：`memberinfo.connection.retrieve.relation.goals`
   * **實際呼叫鏈**：`MemberInfoController.BatchRelationGoals` -> 查詢 `connection` 實體。
   * **查詢行為**：查詢 `connection` 實體，條件為 `record1id IN (contactIds) OR record2id IN (contactIds)`。
   * **投影欄位**：`record1id`, `record2id`, `record1roleid`, `record2roleid`。
   * **一致性判定**：**完全一致**。

2. **授權 Trust Boundary 是否完整**
   * **判定**：**不完整 (No-Go)**。
   * **說明**：
     * 本呼叫的輸入參數為 `contactIds`（聯絡人 ID 列表）。
     * 在 `AssignedLists` 模式下，伺服器端僅持有 `AssignedListIds`（小組 ID 列表），**無法在 request-local 直接判定這些 `contactIds` 是否屬於使用者有權存取的小組**。
     * 若要進行驗證，必須在每次查詢前先執行一次 `listmember` 查詢以確認隸屬關係，這會破壞 DTO-only data-plane 的無狀態與高效能原則；若不驗證，則會產生嚴重的越權漏洞（ID 爬行攻擊，caller 可傳入任意聯絡人 ID 取得其關係目標）。
     * 因此，現有的 `MemberInfoTargetAuthorizationEvidence` 授權憑證**不足以**直接作為 `ORG-CALL-00033` 的安全邊界。

3. **設計要求**
   * **Fixed Query/Projection**：查詢 `connection` 實體。
   * **輸出 Boundedness**：無限制。`connection` 關係可能極為龐大，且缺乏分頁與總量限制，容易造成記憶體過載。
   * **取消與 Fault Union**：缺乏明確的 fail-closed 局部失敗處理。

4. **結論**：**no-go**。

5. **前置條件**：
   * 必須先設計並實作一個能證明 **contact-to-list 隸屬關係** 的唯讀授權邊界快取，或將 `ORG-CALL-00032`（memberships）與 `ORG-CALL-00033`（relation goals）合併為單一的伺服器端聚合查詢服務（在資料庫端直接進行 join 查詢，僅回傳已授權小組成員的關係），否則 `ORG-CALL-00033` 無法作為獨立的 child 安全實作。

---

## 審查結論摘要

| 呼叫點 ID | 合約名稱 | 建議決策 | 關鍵理由 / 前置條件 |
| :--- | :--- | :--- | :--- |
| **ORG-CALL-00031** | `memberinfo.smallgroup.retrieve.descriptors` | **可建立 implementation child** | 可完全透過 `MemberInfoTargetAuthorizationEvidence` 限制 `listid` 查詢範圍，脫離 legacy 狀態。 |
| **ORG-CALL-00032** | `memberinfo.smallgroup.retrieve.memberships` | **可建立 implementation child** | 可透過將傳入的 `listIds` 與 `AssignedListIds` 取交集，在 request-local 建立完整授權邊界。 |
| **ORG-CALL-00033** | `memberinfo.connection.retrieve.relation.goals` | **no-go** | **授權邊界不完整**。無法單憑 `AssignedListIds` 驗證傳入的 `contactIds` 隸屬關係，存在越權風險。需先合併查詢或建立隸屬關係驗證機制。 |
