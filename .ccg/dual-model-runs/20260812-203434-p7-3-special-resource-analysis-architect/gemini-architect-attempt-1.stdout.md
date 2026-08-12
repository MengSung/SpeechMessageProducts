# P7.3 特殊資源能力遷移：架構分析報告

本報告針對現有 Repository 中的 P7.3 五項特殊資源能力（`memberinfo.contact.retrieve.image`、`memberinfo.contact.update.image`、`newperson.contact.update.image`、`metadata.optionset.retrieve.by.attribute`、`stats.meeting.retrieve.by.sunday`）進行架構分析與本機設計規劃。本分析完全基於既有程式碼結構，保持 CE evidence-pending 狀態，不涉及任何實際的 CE 部署、feature flag 切換或流量變更。

---

## 一、 最小安全型別契約與回應鑑別器 (Typed Contract & Response Discriminator)

在既有的 `OperationIds`、`Package01OperationRegistry`、`OperationResponseData` 與 `Data8ProfileOperationExecutor` 模式下，五項 Operation 的最小安全契約設計如下：

### 1. 契約定義與參數限制
*   **`memberinfo.contact.retrieve.image`**
    *   **OperationId**: `"memberinfo.contact.retrieve.image"`
    *   **參數**: `contactId` (guid, Required)
    *   **回應鑑別器**: `OperationResponseKind.ContactImageRetrieve`
    *   **回應 DTO**: `ContactImageRetrieveResponseData` (包含防禦性複製的 `byte[] ImageData` 與 `string MimeType`)
*   **`memberinfo.contact.update.image`**
    *   **OperationId**: `"memberinfo.contact.update.image"`
    *   **參數**: `contactId` (guid, Required), `imageData` (byte-array/base64, Required)
    *   **回應鑑別器**: `OperationResponseKind.ContactImageUpdate`
    *   **回應 DTO**: `ContactImageUpdateResponseData` (包含 `P72ControlledMutationDisposition` 與 `P72ControlledMutationCorrelationCategory`)
*   **`newperson.contact.update.image`**
    *   **OperationId**: `"newperson.contact.update.image"`
    *   **參數**: `contactId` (guid, Required), `imageData` (byte-array/base64, Required)
    *   **回應鑑別器**: `OperationResponseKind.ContactImageUpdate` (可共用或獨立為 `NewPersonContactImageUpdate`)
*   **`metadata.optionset.retrieve.by.attribute`**
    *   **OperationId**: `"metadata.optionset.retrieve.by.attribute"` (即既有的 `OperationIds.MetadataOptionSetByAttribute`)
    *   **參數**: `entityLogicalName` (string, Required, odata-uri-segment), `attributeLogicalName` (string, Required, odata-uri-segment)
    *   **回應鑑別器**: `OperationResponseKind.MetadataOptionSet`
    *   **回應 DTO**: `MetadataOptionSetResponseData` (包含唯讀的 `IReadOnlyList<OptionValueLabelRecord>`，禁止攜帶任何 CRM SDK 原始結構)
*   **`stats.meeting.retrieve.by.sunday`**
    *   **OperationId**: `"stats.meeting.retrieve.by.sunday"`
    *   **參數**: `sundayDate` (date-time, Required), `listId` (guid, Optional)
    *   **回應鑑別器**: `OperationResponseKind.MeetingStatsRetrieve`
    *   **回應 DTO**: `MeetingStatsRetrieveResponseData` (包含唯讀的 `IReadOnlyList<MeetingStatRecord>`)

### 2. 鑑別器安全驗證機制
所有新加入的 `OperationResponseKind` 分支必須在 `OperationResponseData.ValidateSingleSafeBranch` 中進行嚴格驗證，確保**同時只能有一個分支非 null**，以維持封閉聯集 (Closed Union) 的安全邊界，防止 CRM 敏感欄位外洩。

---

## 二、 影像串流處理要點 (Image Stream Handling)

影像資料的讀寫必須遵循極度防禦性的資源管理原則：

1.  **大小與維度限制 (Hard Limits)**:
    *   **大小上限**: 嚴格限制單一影像最大為 **512 KiB**，超過此限制的 Request 必須在 Executor 邊界直接拒絕，避免大物件堆 (LOH) 記憶體碎片化。
    *   **維度上限**: 限制最大解析度為 **1024x1024 像素**。
2.  **格式驗證 (Format Validation)**:
    *   僅允許 JPEG (`FF D8 FF`) 與 PNG (`89 50 4E 47`) 的 Magic Bytes 標頭。必須使用輕量級的 Stream 標頭讀取器進行驗證，禁止在未限制大小的情況下將整個 Stream 載入解碼器。
3.  **防禦性複製 (Defensive Copy)**:
    *   **輸入端**: 接收到影像 byte array 後，必須立即進行 `Array.Copy` 或建立唯讀的 `ReadOnlyMemory<byte>`，切斷與 Caller 傳入陣列的參照。
    *   **輸出端**: 回傳給 ProductClient 前，必須對內部 Buffer 進行防禦性複製，防止外部程式碼修改內部快取或傳輸緩衝區。
4.  **取消機制與生命週期 (Cancellation & Ownership)**:
    *   `CancellationToken` 必須完整傳遞至底層的 Data8 Connector 網路 I/O 與 Stream 讀寫操作。
    *   影像 Stream 的 Lease 必須嚴格綁定於 Request Scope，在操作完成或發生 Fault 時，必須在 `finally` 區塊中立即 Dispose，禁止將 Stream 參照保留於任何快取或 Session 中。
5.  **寫入確認與無重試原則 (Read-Back & No-Retry)**:
    *   影像更新操作完成後，Executor 必須透過 Data8 執行一次輕量級的 Read-Back 驗證（例如比對 Image Timestamp 或 Hash），確認寫入成功後才回傳 `Changed`。
    *   若寫入失敗或被取消，**禁止自動重試**，以避免重複傳輸大容量 Payload 導致連線池枯竭。

---

## 三、 元資料快取架構 (Metadata Cache Architecture)

為避免頻繁向 CRM 查詢 OptionSet 定義，必須實作本機快取，但須遵循嚴格的隔離與生命週期規範：

1.  **快取分割 (Partitioning)**:
    *   快取 Key 必須由 `(ProfileAlias, GenerationId, EntityLogicalName, AttributeLogicalName)` 複合組成。
    *   當 Profile 重新載入或 Generation 變更時，舊 Generation 的快取必須完全隔離，避免跨租戶或跨版本讀取到過期的元資料。
2.  **容量與淘汰機制 (TTL / Size / Eviction)**:
    *   **容量上限**: 快取條目上限設定為 **1000 筆**。
    *   **快取時效**: 採用絕對過期時間 (Absolute TTL) **15 分鐘**，不使用滑動過期 (Sliding TTL) 以防過期資料無限延伸。
    *   **淘汰演算法**: 達容量上限時，使用 LRU (Least Recently Used) 演算法進行淘汰。
3.  **主動失效 (Invalidation)**:
    *   當 `IProfileResolver` 偵測到 Profile 正在進行 Drain 或 Dispose 時，必須主動觸發事件清除該 `GenerationId` 下的所有快取條目。
4.  **禁止保留原始 SDK 物件 (No Raw SDK Retention)**:
    *   快取中**絕對禁止**保留 `OptionSetMetadata`、`AttributeMetadata` 等 CRM SDK 原始型別。
    *   僅允許保留經過 Materialize 的自訂唯讀 DTO（如 `OptionValueLabelRecord`），切斷與 SDK 執行階段的任何關聯。

---

## 四、 會議分頁查詢規則 (Meeting Paging Rules)

針對 `stats.meeting.retrieve.by.sunday` 的分頁查詢，必須實作防禦性分頁控制：

1.  **固定查詢 (Server-Owned Query)**:
    *   查詢的 FetchXML 或 QueryExpression 必須完全由 Server 端硬編碼定義，Caller 僅能傳入 `sundayDate` 參數，禁止 Caller 自訂 Filter、Sort 或 Column Set。
2.  **分頁與傳輸上限 (Paging Limits)**:
    *   **最大分頁數**: 限制最多讀取 **4 頁**。
    *   **最大結果筆數**: 累計最多 **1000 筆** 會議記錄。
    *   **最大累計傳輸量**: 累計回應大小上限為 **256 KiB**。一旦超過此限制，必須立即中斷查詢並拋出異常。
3.  **Cookie 生命週期 (Cookie Lifetime)**:
    *   Paging Cookie 僅能在單次 Request Scope 與 Connector Lease 內存活，**禁止跨 Request 快取或重用 Paging Cookie**。
4.  **無部分成功原則 (No-Partial-Success)**:
    *   分頁讀取過程中，若有任何一頁發生逾時、連線中斷或資料損毀，整筆操作必須判定為失敗（Fail-Closed），**禁止回傳部分成功的結果**給 Caller。

---

## 五、 高風險實作陷阱與 TDD 測試策略

### 1. 高風險實作陷阱
*   **記憶體洩漏與 LOH 碎片化**: 未對影像 byte array 進行大小限制，或未及時 Dispose 影像 Stream，導致記憶體耗盡。
*   **快取污染 (Cache Pollution)**: 快取 Key 未綁定 `GenerationId`，導致系統在切換 Profile 時讀取到舊租戶的 OptionSet 標籤。
*   **分頁 Cookie 劫持**: 將 Paging Cookie 暴露於回應中，或在不同使用者的 Request 間重用 Cookie，導致越權存取。
*   **執行緒池飢餓**: 在處理影像 Stream 時使用同步 I/O（如 `Stream.Read` 代替 `ReadAsync`），導致 Gateway 執行緒池枯竭。

### 2. 核心 TDD 測試案例設計
*   **`Should_Fail_When_Image_Exceeds_Size_Limit`**: 驗證當傳入的影像大小超過 512 KiB 時，Executor 立即拒絕並回傳 `InvalidOperationParameters`。
*   **`Should_Fail_When_Image_Format_Is_Invalid`**: 驗證傳入非 JPEG/PNG 標頭的影像資料時，驗證器能正確攔截。
*   **`Should_Isolate_Cache_By_GenerationId`**: 驗證當 `GenerationId` 變更後，舊 Generation 的快取元資料無法被新 Generation 讀取。
*   **`Should_Fail_Entire_Paging_On_Single_Page_Failure`**: 模擬在讀取第 3 頁分頁時發生網路異常，驗證系統不會回傳前 2 頁的資料，而是直接 Fail-Closed。
*   **`Should_Enforce_Cumulative_Byte_Limit_In_Paging`**: 驗證當分頁累計傳輸量超過 256 KiB 時，立即中斷連線並釋放 Lease。

---

## 六、 架構審查發現 (Architecture Review Findings)

### 1. Critical
*   **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
*   **判定理由**: 既有的 `OperationResponseData` 採用封閉聯集設計，其建構子中的 `ValidateSingleSafeBranch` 嚴格限制了可接受的 Response 分支。若要支援影像、元資料與會議統計，必須在此檔案中擴充 `OperationResponseKind` 列舉，並在建構子中加入對應的單一分支非空驗證。若未正確實作此驗證，將導致安全邊界失效，允許任意 JSON 結構穿透 Gateway。

### 2. Warning
*   **檔案路徑**: `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
*   **判定理由**: Executor 負責參數的驗證與正規化（如 `TryCopyValidatedParameters`）。對於影像寫入操作，Executor 必須在進入 Connector Pool Lease 之前完成影像大小與格式的防禦性檢查。若將此檢查延遲至 Connector 內部執行，將導致無效的 Payload 佔用連線池資源，增加系統過載風險。

### 3. Info
*   **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs`
*   **判定理由**: 既有的 `MetadataOptionSetByAttribute` 在 Registry 中被硬編碼對應至 `OperationResponseKind.Unsupported`。在實作 P7.3 時，必須更新此 Registry 的靜態 Allowlist 定義，將其對應至正確的 `OperationResponseKind` 與參數 Schema，以啟用正式路由。
