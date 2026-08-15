# P7 MemberInfo Small-Group Snapshot Data-Plane 架構分析報告

本報告針對 `ORG-CALL-00031`（小組描述符，list descriptors）與 `ORG-CALL-00032`（小組成員關係，listmember/contact memberships）設計單一、CE 9.1 專用、本機專用（local-only）、固定組合的快照操作（fixed composed snapshot operation）進行架構與授權邊界分析。

---

## 1. Analysis (分析)

### 當前架構評估
目前 `ORG-CALL-00031` 與 `ORG-CALL-00032` 在權威矩陣（Authoritative Matrix）中被標記為 `temporary-legacy`。其現行 Controller 路徑嚴重依賴於 `Session`、`InMemoryContext`、`ListManager` 等全域或可變狀態，並使用保存的憑證執行 `RetrieveAllEntities` 查詢。這在 Gateway 架構中存在並發請求下的權限污染、資源洩漏以及 TOCTOU (Time-of-Check to Time-of-Use) 安全漏洞。

### Composed Operation 的優勢
相較於由呼叫端（如前端或 Controller）自行組合的兩個獨立操作，設計一個單一的組合快照操作（`memberinfo.smallgroup.snapshot.retrieve.authorized`）具有以下顯著優勢：
1. **強固的授權生命週期收斂**：該操作僅接受不可變的 `MemberInfoTargetAuthorizationScope`。在同一次 Data8 執行過程中，先查詢 descriptors，並**直接使用**該次查詢得到的已驗證 descriptor IDs 作為 membership 查詢的過濾條件。呼叫端無法在兩次呼叫之間篡改或注入未授權的 list ID，消除了授權繞過風險。
2. **資源與 Lease 鎖定**：整個 snapshot 查詢在單一的 Data8 connector lease 內完成，保證了資料的 snapshot 一致性，並在查詢結束或取消時立即釋放 lease，避免連線池資源耗盡。
3. **無狀態與防禦性拷貝**：ProductClient 與 ChurchReport source 之間只傳遞不可變的 DTO，且在邊界進行防禦性拷貝（defensive-copy），防止任何 mutable state 洩漏。

---

## 2. Architecture Decision (架構決策)

### 決策 1：單一 Composed Snapshot 終端
- **決策內容**：建立單一的 Data8 操作 `memberinfo.smallgroup.snapshot.retrieve.authorized`，一次性回傳 descriptors 與 memberships 的聯集。
- **理由**：避免呼叫端自行組合兩次查詢所帶來的授權繞過風險。
- **拒絕的替代方案**：保留兩個獨立的 Data8 查詢。*拒絕原因*：無法在無狀態的 Gateway 中保證兩次呼叫的授權一致性，且會增加一倍的網路往返與 lease 獲取開銷。
- **假設**：`MemberInfoTargetAuthorizationScope` 傳入的 `visibleListIds` 已由 server-owned evidence 驗證，且數量不超過 512。
- **潛在副作用**：若單個小組的成員數量極大，合併查詢可能導致單次 response payload 較大，但已透過 4096 限制進行 fail-closed 截斷。

### 決策 2：動態解析 `contact.customertypecode` 結案狀態
- **決策內容**：在 Data8 內部透過 `RetrieveAttributeRequest` 查詢 `contact` 的 `customertypecode` 屬性，動態解析代表「結案」的唯一 option value，並在查詢 memberships 時將其排除。
- **理由**：避免寫死（hardcode）結案狀態值，同時禁止呼叫端傳入 status 參數，確保業務邏輯的封閉性與安全性。
- **拒絕的替代方案**：由呼叫端傳入結案狀態值。*拒絕原因*：呼叫端可能傳入錯誤或惡意的狀態值，繞過結案過濾邏輯。

### 決策 3：嚴格的 Fail-Closed 限制 (Bounds)
- **決策內容**：Descriptors 上限 512，Memberships 上限 4096。任何超過限制、metadata 解析失敗或 I/O 異常，皆不回傳 partial results，而是直接拋出異常或回傳去識別化的 unavailable 狀態。
- **理由**：防止記憶體溢位與網路傳輸延遲，並確保資料的完整性。

---

## 3. Implementation Plan (實作計畫)

### Step 1: Abstractions Registry & Response Contract
在 `OperationIds.cs` 中新增操作 ID，並在 `OperationResponseData.cs` 中新增 `OperationResponseKind.MemberInfoSmallGroupSnapshot` 與對應的 DTO 結構。

```diff
--- a/SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs
+++ b/SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs
@@ -162,3 +162,6 @@ public static class OperationIds
     /// </summary>
     public const string MemberInfoAuthorizationAssignmentEvidence = "memberinfo.authorization.assignment.evidence";
+
+    /// <summary>ORG-CALL-00031/00032 合併的 small-group snapshot 查詢</summary>
+    public const string MemberInfoSmallGroupSnapshotRetrieveAuthorized = "memberinfo.smallgroup.snapshot.retrieve.authorized";
 }
```

### Step 2: Data8 Composed Operation Implementation
在 `SpeechMessage.Dynamics.Connectors.Data8` 下建立 `Package02Data8MemberInfoSmallGroupSnapshotOperations.cs`：
1. 驗證 operation ID 與 ceVersion (必須是 9.1)。
2. 呼叫 `RetrieveAttributeRequest` 解析 `contact.customertypecode` 中 label 為「結案」的 option value。
3. 根據 scope 的 `AccessMode` 查詢 descriptors (最多 512 筆)。
4. 提取 validated descriptor list IDs，若為空則直接回傳空 snapshot。
5. 使用 `In` 條件與 `LinkEntity` 查詢 `listmember` 與 `contact`，過濾 active 且非結案的成員，限制最多 4096 筆。
6. 驗證 Subset Invariant：確保 memberships 中的所有 `ListId` 都存在於 descriptors 中。
7. 包裝成 `OperationResponseData` 回傳。

### Step 3: ProductClient & ChurchReport Source
- 實作 `MemberInfoSmallGroupSnapshotReadClient`，將 `MemberInfoTargetAuthorizationScope` 轉為 Data8 請求。
- 實作 `MemberInfoSmallGroupSnapshotSource`，作為 ChurchReport 的內部 source，只接受 `MemberInfoTargetAuthorizationScope`，不提供 public HTTP endpoint。

---

## 4. Considerations (考量事項)

- **效能 (Performance)**：
  - 使用 `NoLock = true` 進行 QueryExpression 查詢，避免資料庫鎖定。
  - 限制 memberships 最多 4096 筆，防止大數據量導致的記憶體溢位與網路傳輸延遲。
- **無障礙性與相容性 (Accessibility & Compatibility)**：
  - 本次設計為 local-only data-plane，不涉及 UI 變更，但 DTO 輸出必須使用嚴格的 UTF-8 編碼，防止前端解析亂碼。
- **可維護性 (Maintainability)**：
  - 透過單元測試驗證 A/B 隔離性、取消權杖 (cancellation) 的即時釋放，以及 Subset Invariant。

---

## 5. Findings (審查發現)

### Critical
- **無**。目前 `MemberInfoTargetAuthorizationScope` 已經正確實作並封存，且 MaximumVisibleListIds 限制為 512，為本 composed operation 提供了安全的授權基礎。

### Warning
- **OptionSet Metadata 解析風險**：`contact.customertypecode` 的「結案」狀態值依賴於 Dynamics CRM 中的 OptionSet 語意。若環境中該屬性的 label 被修改（例如改為「已結案」或「Closed」），動態解析可能會失敗。建議在解析時使用精確的語意比對，並在解析失敗時立即 fail-closed，絕不能退回到 legacy 預設值。
- **QueryExpression IN 條件限制**：當 `visibleListIds` 達到 512 個時，SQL 查詢的 `In` 條件可能會接近 Dynamics CRM 的效能瓶頸。必須在 Data8 查詢中啟用 `NoLock = true`，並在測試中加入 512 個 list ID 的壓力測試。

### Info
- **relation-goal ORG-CALL-00033 排除**：本設計已明確排除 ORG-CALL-00033，未來若要實作 ORG-CALL-00033，必須基於相同的 `MemberInfoTargetAuthorizationScope` 另立獨立的 capability。
