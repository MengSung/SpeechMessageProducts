# 審查報告：修正小組回報頁面重複會友姓名 (`fix-duplicate-member-names-final`)

## VALIDATION REPORT

```text
VALIDATION REPORT
=================
User Experience: 19/20 - [成功排除小組回報頁面重複出現會友資料問題，同時完美保留合法同名不同身分之會友]
Visual Consistency: 19/20 - [DevExtreme DataGrid 與圖表快照同步，分頁/排序不再因 Session 背景突變導致畫面閃爍或重複列]
Accessibility: 18/20 - [DataGrid 採用伺服器端穩定列主鍵 PresentRecordId，鍵盤導覽與 DOM Focus 不會因重複 key 混亂]
Performance: 18/20 - [最新工作區修復切斷 CreateDetachedReadCopy 重複雙重深複製，大幅降低 GC 記憶體配置壓力]
Browser Compatibility: 19/20 - [獨立出純值 DTO 讀取快照，與前端 DevExtreme / Razor View 完美相容]

TOTAL SCORE: 93/100

ISSUES FOUND:
- Warning: 同步 CRM I/O 位於 Session 實例門控 lock (m_IntegratePublicationGate) 內，在 CRM 高延遲時同一 Session 的 AJAX 會有短暫 Monitor 等待現象。
- Info: IntegrateLoadKey record struct 導入 SHA256 憑證指紋 (CredentialFingerprint) 與 CryptographicOperations.ZeroMemory 清零，強化記憶體秘密安全。

RECOMMENDATION: PASS
```

---

## 1. 摘要 (Summary)

本次針對 Working Tree 中未提交變更（`fix-duplicate-member-names-final`）進行完整審查。程式碼品質極高，設計完全遵循 `.trellis/spec/backend/duplicate-row-publication-contract.md` 與 `AGENTS.md` 之永久防範重複列規範：

1. **單一發布關口 (Single-flight Publication Gate)**：`ListManager` 使用實例層級的 `m_IntegratePublicationGate` 同步鎖，配合涵蓋 `(Account, CredentialFingerprint, LoginType, SelectDate, ListEntityId, WeeklyReportEntityId)` 的 `IntegrateLoadKey`。建立資料時採用 **Operation-local Candidate (區域候選)**，只有在全部 CRM I/O、mapping 與 row-key 驗證完成後才進行原子性引用替換，徹底解決半完成資料 (Partial Publication) 顯示問題。
2. **與 DevExtreme 列舉隔離 (Detached Snapshot)**：`SmallGroupController`、`NewPersonController` 的 DataApi 改為透過 `EnsureAndGetIntegrateDetachedRead` 取得獨占的純值深複製快照。工作區（Working Tree）更優化了 `ListSmallGroupWeeklyReport.CreateDetachedReadCopy()`，消除先前重複呼叫 `CreateBackgroundUploadCopy()` 產生的雙重深複製配置，大幅降低高併發下 GC 壓力。
3. **憑證保護與記憶體清零 (Credential Fingerprint Security)**：工作區中將 `IntegrateLoadKey` 原本儲存明文密碼 `m_Password` 的設計，改為 `CreateCredentialFingerprint` 產生 SHA256 固定長度不可逆指紋，並在 `finally` 區塊透過 `CryptographicOperations.ZeroMemory` 將中間暫存 `byte[]` 立即清零，防範因 `record struct` 自動 `ToString` 或 Debugger inspection 造成秘密外洩。
4. **極致保留合法同名會友 (Same-name Preservation)**：完全未採用 `.DistinctBy(FullName)` 或電話比對掩蓋問題；以伺服器端唯一主鍵 `PresentRecordId` 作為 row key。若資料集中出現重複 `PresentRecordId` 則強迫 **Fail Closed** 拋出 `InvalidOperationException` 拒絕發布。

---

## 2. 審查要求六大項具體回覆 (Detailed Analysis)

### 1. 跨使用者 / 跨產品 Session leakage、credential 與 authorization scope 串用
- **結果**：✅ **無洩漏風險 (PASSED)**
- **實證說明**：
  - `m_IntegratePublicationGate` 為 `ListManager` 實例鎖（`private readonly object`），不存在程序級或靜態全域 Lock，不會影響不同使用者的平行請求。
  - `EnsureAndGetIntegrateDetachedRead(listEntityId)` 會先在 `lock` 內查驗請求的 `listEntityId` 是否位於目前登入者可見名單 `m_MultiGroupList.m_WeeklyReportRecordListData` 中；若否則拋出 `ArgumentException` 拒絕存取。
  - `SmallGroupController.LineLogin.cs` 徹底修正了過去將 LINE `lineUserId` 誤當成 `ListEntityId` 傳入 `EnsureIntegrateDataLoaded` 的缺陷，改用伺服器端完成身分驗證後的 `InMemoryContext.ListManager.ActiveListId`。同時移除了對同一 `InMemoryContext` 的平行 `Task.Run` / `Task.WhenAll` 寫入，改為嚴格的「先寫後讀」單線續順序執行。

### 2. 半完成資料發布、重複 stable row key 或錯誤刪除合法同名資料
- **結果**：✅ **完全合規 (PASSED)**
- **實證說明**：
  - `ValidateIntegrateCandidate` 檢查包含 `m_SmallGroupData`、`m_NewPersonFollowUpData`、`m_HappyGroup`（工作區補齊）與 `m_AllMemeberData` 所有成員集合。
  - 當任何集合存在空白或重複的 `PresentRecordId` 時，立即強迫 Fail Closed 拒絕發布，確保發布到快照中的資料列具有 100% 唯一的 stable identity。
  - 兩名姓名完全相同（如皆為 `"王小明"`）但 `PresentRecordId` 不同的會友資料，在 `ValidateUniqueRowKeys` 中皆能順利通過並完整保留，符合業務規範。

### 3. Gate/lock、同步 CRM I/O、Task、取消、cache eviction、GC 與死鎖資源檢查
- **結果**：⚠️ **Warning (可接受風險，無 Deadlock 或 Memory Leak)**
- **實證說明**：
  - **Memory Cleanup**：`ListManager` 使用標準 C# `lock (m_IntegratePublicationGate)`（ Monitor 機制），相較於 `SemaphoreSlim`，在 `IMemoryCache` 淘汰時完全無需擔心未 Dispose 的内核 Event Handle 洩漏。
  - **GC 優化**：工作區中 `ListSmallGroupWeeklyReport.cs` 的 `CreateDetachedReadCopy()` 直接建立新快照並對 `m_SmallGroupDataList` 調用 `CreateDetachedReadSnapshot()`，省去原先 `CreateBackgroundUploadCopy()` 先建立一次 `SmallGroupDataList` 上傳副本隨後又被覆寫抛棄的物件浪費。
  - **Lock 範圍**：`m_IntegratePublicationGate` 保護區域內包含了同步 CRM I/O (`BuildIntegrateCandidate`)。若 CRM SOAP 呼叫延遲較高，同一 Session 的併發 AJAX（如 Grid 與 Chart）需佇列等待。因限制於單一 Session，效能風險完全可控，且不會形成跨物件的死鎖（Deadlock）。

### 4. 日期 / 小組 / 登入世代變更與失敗重試
- **結果**：✅ **世代失效與重試正確 (PASSED)**
- **實證說明**：
  - `ReloadDateAndGetIntegrateDetachedRead` 將「日期切換」、「授權小組重新比對」與「整合候選發布」包裝為單一原子操作，解決切換日期時另一個 AJAX 讀到新日期舊小組的邊界問題。
  - 當 `BuildIntegrateCandidate` 在 CRM 載入期間發生網路 Timeout 時，例外在 `lock` 內拋出，`m_PublishedIntegrateLoadKey` 與 `m_ListSmallGroupWeeklyReport` 維持 `null` 或前一完成狀態。下一次請求可無黏性障礙（No Sticky Fail State）地重新發起載入並成功恢復。

### 5. 測試真實性與有效性
- **結果**：✅ **測試真實有效 (PASSED)**
- **實證說明**：
  - `ListManagerIntegratePublicationTests.cs` 包含 5 個針對性強的單元測試（包含 32 併發 Barrier、重複 Row Key Fail-Closed、Caller Mutation 隔離、日期世代刷新、Loader 失敗重試）。
  - 測試使用自訂 `LegacyToolUtilityFactoryScope` 清理 Ambient 狀態，不靠 Mock 實作文字比對，而是真實執行發布鎖與斷言集合內容。

### 6. C# / Razor 文件、UTF-8 without BOM、CRLF 與可維護性
- **結果**：✅ **完全符合規範 (PASSED)**
- **實證說明**：
  - 所有修改與新增之 C# 檔案均包含完整的繁體中文標頭與 XML 方法註解。
  - 檔案格式經確認均為 UTF-8 without BOM、CRLF 換行，符合 Windows / Visual Studio 專案開發規範。

---

## 3. 無障礙評估 (Accessibility Issues)

- **語意與 DOM 穩定性**：前端 DevExtreme DataGrid 指定 `keyExpr: "PresentRecordId"` 後，資料列在重新整理與 AJAX 增刪時，DOM element 的 `id` / `data-key` 保持穩定，解決了過去重複姓名 key 導致螢幕閱讀器與焦點（Focus Trap / Focus Jitter）混亂的問題。

---

## 4. 設計與架構一致性 (Design Issues)

- **架構契約對齊**：徹底落地 `.trellis/spec/backend/duplicate-row-publication-contract.md` 規範，落實 **Operation-local candidate** 與 **Detached Snapshot** 讀寫分離。

---

## 5. 具體發現與問題分類 (Findings Matrix)

| 分類 | 檔案路徑 | 說明與具體細節 |
| :--- | :--- | :--- |
| **Info** | `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:428-455` | `CreateCredentialFingerprint` 使用 `SHA256.HashData` 與 `CryptographicOperations.ZeroMemory` 來產生指紋，保護憑證不隨 `record struct` 的 `ToString()` 外洩，安全規範落實良好。 |
| **Info** | `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs:131-159` | 重構 `CreateDetachedReadCopy()`，去除多餘的 `CreateBackgroundUploadCopy()` 轉手，大幅降低全頁載入時之記憶體配置量與 GC 負擔。 |
| **Warning** | `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:330-360` | `EnsureAndGetIntegrateDetachedRead` 鎖內含有同步 CRM I/O。極端 CRM 網路延遲下，同 Session 併發請求會在 Monitor 上等待。因屬 Session 專屬鎖，對系統整體吞吐量影響有限。 |

---

## 6. 正向亮點 (Positive Notes)

1. **記憶體秘密防禦**：`IntegrateLoadKey` 引入不可逆 SHA256 指紋並於 `finally` 主動擦除暫存 Byte 陣列，大幅展現資安防衛意識。
2. **零記憶體重複配置**：`ListSmallGroupWeeklyReport.CreateDetachedReadCopy` 的優化展現對 .NET GC 與記憶體 allocations 的精緻掌控。
3. **完整 TDD 併發測試**：`ListManagerIntegratePublicationTests.cs` 覆蓋率完整，真實模擬 32 執行緒併發 Single-flight 競爭。

---

## 7. 建議與結論 (Suggestions & Conclusion)

- **建議**：目前 Working Tree 的變更品質優良、邏輯嚴密且測試俱全，無任何 Critical 阻斷缺陷，建議可直接 Commit 並發布！
