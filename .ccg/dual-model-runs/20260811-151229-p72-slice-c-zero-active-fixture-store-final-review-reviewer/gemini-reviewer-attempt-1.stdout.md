# P7.2 Slice C zero-active fixture-store final review 審查報告

## 1. 總體評估與 Verdict

本審查針對 P7.2 Slice C 修正範圍內之週報基數（cardinality）變更、控制面引導（seed bootstrap）、執行診斷分類（diagnostic staging）以及資源生命週期隔離進行了完整程式碼與測試審查。

### Verdict: **PASS**

**評估摘要**：
- **正確性與一致性**：`P72Data8ListManagementFixtureStore` 已成功從「必須剛好一筆週報」的限制，修正為與 Connector 及 Preflight 一致的「零或一筆週報（zero-or-one）」相容邏輯。
- **Fail-Closed 邊界**：在週報或出席紀錄查詢中，若偵測到重複列（Duplicate）、分頁未完（Paging）、格式錯誤（Malformed）或多筆紀錄，皆會在任何變更（Mutation）發生前立即拋出例外並中斷，絕無自動重試或嘗試修補週報之行為。
- **資源與安全隔離**：C# 程式碼中所有敏感認證、連線與 Session 狀態皆無靜態快取或洩漏風險，並在異常路徑上透過嚴格的 `try-finally` 確保資源依反向順序確定釋放。
- **測試品質**：新增的單元測試非套套邏輯（non-tautological），嚴格驗證了查詢條件、讀取次數、變更次數，並完整覆蓋了 zero-active 分支的讀取、衝突拒絕與清理（cleanup）流程。

---

## 2. 審查問題逐項回覆

### Q1. Nullable 週報解析器是否保留精確的 list/state/date 與 `TopCount=2`，且僅在完全零列時回傳 null？
**是**。`ResolveWeeklyReport` 查詢精確保留了目標清單（`WeeklyReportListAttribute`）、啟用狀態（`statecode=0`）、UTC 週日日期（`DateAttribute`），並設定 `TopCount=2`。只有在完全沒有資料列時回傳 `null`，若有重複列或分頁標記則拋出例外 fail closed。

### Q2. Zero-active 出席紀錄查詢是否刻意僅省略週報 lookup 過濾器，同時保留精確的 contact/date/state 與資料列限制，使既有但關聯錯誤的紀錄可見並被拒絕？
**是**。在 `zero-active` 分支中，`ReadPresentRecord` 刻意不加入週報 lookup 條件，但保留了聯絡人、日期、啟用狀態與 `TopCount=2` 的限制。這使得任何既有但關聯錯誤的出席紀錄能被查詢出來，並在後續比對中因 lookup 不符而被拒絕，避免誤判為安全 baseline。

### Q3. 讀回與清理是否使用精確的 nullable 相等性、精確紀錄 ID，以及決定性的有序 rollback，而不猜測或刪除無關資料？
**是**。讀回與清理皆使用精確的 nullable 相等性比對（`observed.WeeklyReportId != weeklyReportId`），並使用預期證明的唯一 `presentRecordId` 進行刪除。Rollback 順序為：刪除出席紀錄 -> 還原聯絡人主清單 -> 還原 Owner -> 還原 Source/Target 成員關係，完全沒有猜測或刪除無關資料。

### Q4. 重複、分頁、格式錯誤、多筆紀錄或錯誤 lookup 狀態是否在變更前被拒絕，且不進行重試或週報修補？
**是**。所有異常狀態（重複、分頁、格式錯誤、多筆紀錄或錯誤關聯）皆在任何寫入/變更操作前拋出例外拒絕，且不進行自動重試或嘗試修補週報。

### Q5. 是否有任何要求/使用者/租戶/設定檔/認證/Session/CRM 實體狀態會透過靜態狀態、快取、日誌、例外、證據或測試替身洩漏？
**否**。所有狀態皆為實例或方法局部變數，無靜態快取。測試替身 `TransferGraphRecordingOrganizationService` 僅在單一測試 scope 內存活並被釋放。例外訊息與日誌皆經過淨化，不包含敏感資訊。

### Q6. 是否有任何服務、WCF 通道、租約、處理程序、資料流、緩衝區、計時器、取消註冊、背景工作、暫存資料或其他資源在不確定傳輸狀態後洩漏或被重用？
**否**。`OfficialCrmServiceClientFactory` 與 `OfficialCrmServiceClientAdapter` 採用嚴格的 `try-finally` 結構與 `Interlocked.Exchange`，確保 client 與認證資源在失敗或釋放時以反向順序確定釋放。`WorkerEnvelopeCodec` 與 `WorkerFrameCodec` 亦在局部 scope 內管理 Stream 與緩衝區，無資源洩漏風險。

### Q7. 測試是否非套套邏輯，且對查詢形狀、變更計數、剛好一筆行為、zero-active 行為、歧義性、錯誤 lookup 及清理足夠嚴格？
**是**。新增的 5 個單元測試非常嚴格，精確驗證了查詢條件、讀取次數、變更次數（預期為 0 或特定 rollback 次數），並涵蓋了 zero-active、exactly-one、重複/分頁/格式錯誤拒絕、錯誤關聯拒絕以及 zero-active 清理等所有邊界條件。

### Q8. 在複合讀取前將 `probeStage=transfer-read` 是否為正確的受界限診斷行為，且未將該階段轉為重試授權？
**是**。在呼叫 `ReadTransferGraph` 之前將 `probeStage` 設為 `"transfer-read"`，能確保在讀取複合圖表失敗時，診斷階段不會被誤標為前一個 `"contact-owner-read"`。此階段僅用於診斷，不授予任何重試或變更權限。

### Q9. 新增/修改的 C# 區域是否附有實質的繁體中文所有權、隔離、fail-closed、清理與故障注入說明？
**是**。所有新增與修改的 C# 程式碼區段皆附有詳盡的繁體中文 XML 註解，說明其所有權、隔離性、fail-closed 邊界、清理機制與故障注入設計。

### Q10. 實作是否高效：受界限的 O(1) 週報/出席查詢、無無界掃描、無共享可變狀態，且每個普通產品要求無昂貴的新執行期開銷？
**是**。所有查詢皆有 `TopCount=2` 限制與精確索引欄位過濾，時間複雜度為 O(1)，無無界掃描，無共享可變狀態，且對一般產品要求無額外執行期開銷。

---

## 3. 詳細審查發現

### Critical (嚴重)
*無*。未發現任何阻礙發布的嚴重安全性、正確性或資源洩漏問題。

### Warning (警告)
*無*。程式碼結構與測試覆蓋率皆符合專案規範。

### Info (資訊)
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStore.cs`
  * **說明**：`ResolveWeeklyReport` 與 `ReadPresentRecord` 的 nullable 設計與 Connector 實作完全對齊，成功消除了跨層級的基數漂移（cross-layer drift）。
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStoreTests.cs`
  * **說明**：測試替身 `TransferGraphRecordingOrganizationService` 實作了精確的唯讀與變更計數器（如 `WeeklyReportReadCount`、`UnexpectedMutationCount`），能有效防止未來的程式碼修改引入非預期的寫入操作。
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs`
  * **說明**：`probeStage = "transfer-read"` 的位置調整正確解決了診斷階段被誤標的問題，有助於離線排查。

---

## 4. 正面評價

1. **註解詳盡**：新增的繁體中文註解非常清晰，明確闡述了設計意圖、隔離邊界與 fail-closed 邏輯，極具維護價值。
2. **測試嚴謹**：測試中對 `UnexpectedMutationCount` 的斷言（必須為 0）是極佳的防禦性程式設計實踐，能確保唯讀診斷操作不會意外觸發寫入。
3. **資源管理優良**：`OfficialCrmServiceClientFactory` 與 `OfficialCrmServiceClientAdapter` 的資源釋放邏輯非常嚴密，有效防止了 native 與 managed 認證資源的殘留。
