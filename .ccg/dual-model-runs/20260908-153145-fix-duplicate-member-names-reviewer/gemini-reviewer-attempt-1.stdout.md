# 審查報告：修正小組回報頁面重複會友姓名 (`fix-duplicate-member-names`)

## 1. 綜合評估 (Summary)

本次審查方針與邏輯架構整體方向正確且非常嚴密：
1. **資料流與單一發布關口 (Single-flight Publication Gate)**：採用 `ListManager` 實例級別鎖 `m_IntegratePublicationGate`，並以 `(Account, Credential, LoginType, SelectDate, ListEntityId, WeeklyReportEntityId)` 的 `IntegrateLoadKey` 作為完整 scope key，成功防止半完成快照 (Partial Load) 與重複發布。
2. **與 DevExtreme 讀取隔離 (Detached Snapshot)**：Controller 改用 `EnsureAndGetIntegrateDetachedRead` 取得純值深複製快照後才交給 `DataSourceLoader`，避免 DevExtreme 在無鎖狀態下直接對 Session 的可變集合進行列舉與分頁，防止併發讀寫造成重複列。
3. **LINE 登入競態與授權修正**：移除對同一 `InMemoryContext` 的 `Task.Run`/`Task.WhenAll` 平行寫入，改為順序執行；並修正過去把 LINE `lineUserId` 當成小組 `ListEntityId` 的錯誤，改用伺服器端驗證之 `ActiveListId`。
4. **同名會友與列主鍵防禦 (Fail Closed)**：未採用任何以 `FullName` 去重 Distinct 掩蓋問題的作法，合法同名會友完整保留；若出現重複之非空 `PresentRecordId` 則強迫 Fail Closed 拒絕發布。

**唯目前發現一項阻斷性的 Critical 缺陷**：部分 C# 原始碼檔與新增單元測試檔 (`ListManagerIntegratePublicationTests.cs`, `ListManager.cs`, `SmallGroupController.LineLogin.cs`, `SmallGroupController.Date.cs`) 檔案註解與測試字串常數發生 **Mojibake 亂碼污染**（例如測試字串原本為 `"王小明"` 卻變為 `"???"`），導致測試在執行斷言時會直接失敗，亦違反 `AGENTS.md` 之 UTF-8 規範。

---

## 2. 審查要求六大項具體回覆 (Critical / Warning / Info)

### 🚨 Critical

- **C1: [檔案編碼亂碼與測試斷言失效] C# 原始碼與測試檔案中包含 Mojibake 亂碼，導致測試斷言將於執行期失敗**
  - **受影響檔案**：
    - `ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs` (第 3, 22-30, 43, 70 行等)
    - `SpeechMessageProducts.ChurchReport/Models/ListManager.cs` (第 2, 28-52 行等)
    - `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs` (第 2, 23-50 行等)
    - `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs` (第 2, 85-90 行等)
  - **成因與衝擊**：
    檔案開頭註解與測試資料中出現亂碼（如 `// AI-蝜?銝剜?瑼?閮饷圾`、`"????"`）。更嚴重的是，在 `ListManagerIntegratePublicationTests.cs:43` 的測試 Fixture 中，期望名單 `"王小明"` 被轉碼成 `"????"`，導致同檔第 69 行之斷言 `snapshots.SelectMany(...).Select(m => m.FullName).Should().OnlyContain(name => name == "王小明")` 在執行時必會因字串比對不符合而 **Test Fail**。這違反 `AGENTS.md` UTF-8 without BOM 與「測試必須真能驗證」規範。
  - **修復建議**：將上述檔案以標準 UTF-8 without BOM (CRLF) 重新儲存並還原正確繁體中文註解與測試常數。

---

### ⚠️ Warning

- **W1: [同步 CRM I/O 位於 Lock 內] `m_IntegratePublicationGate` 門控保護期間執行同步 CRM 網絡呼叫，需注意高併發等待**
  - **受影響檔案**：`SpeechMessageProducts.ChurchReport/Models/ListManager.cs` (第 277-318 行)
  - **成因與衝擊**：`EnsureAndGetIntegrateDetachedRead` 於 `lock (m_IntegratePublicationGate)` 內呼叫 `BuildIntegrateCandidate`，其中 `DownloadIntegrateData` 會發起數次同步 CRM SOAP/Dataverse 網絡呼叫 (`RetrieveEntity`, `RetrieveMultiple`)。若 CRM 延遲高，同一 Session 的併發 AJAX 請求（如 Grid + Chart）會依序在 Monitor 上等待。雖然第二個請求進入 Lock 後會命中 `m_PublishedIntegrateLoadKey` 快速路徑而不會重跑 CRM，但在第一筆 CRM I/O 完成前，鎖無法釋放。
  - **建議**：目前為 Session 專屬 lock，影響範圍僅限同一 Session 的數個 AJAX 請求，效能風險可控；建議後續評估是否能將純讀取 CRM 資料（無狀態改變者）放在鎖外，僅將 Candidate 驗證與 Atomic 發布放在鎖內。

- **W2: [Snapshot 深複製範圍] `CreateDetachedReadCopy` 複製 Member 清單容器，但未對個體 Member 屬性進行 Deep Clone**
  - **受影響檔案**：`SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs` (第 131-149 行)
  - **成因與衝擊**：`CreateBackgroundUploadCopy` 新增了 `new List<Member>(...)` 集合，防止 `DataSourceLoader` 列舉時遭遇 `List<T>` 併發修改例外。但元素層級仍指向同一個 `Member` 實例。若 Controller 後續邏輯直接修改 `Member` 的屬性，Session 快照仍可能受影響。目前 Controller 僅傳給 DevExtreme 做 Read-Only 展示，風險為中低度。
  - **建議**：在 `CreateDetachedReadCopy` 中對 `Member` 物件補充屬性級複製（Member-wise clone）。

---

### ℹ️ Info

- **I1: [隔離與授權安全性 validation 通過] 正確防範跨使用者/跨小組 Session Leakage**
  - **受影響檔案**：`SmallGroupController.LineLogin.cs:98`, `ListManager.cs:282-286`
  - **說明**：LINE 登入流程已不拿用戶輸入的 LINE User ID 當作小組 ID，而是嚴格使用伺服器端推導的 `ActiveListId`；`ListManager` 的 `EnsureAndGetIntegrateDetachedRead` 亦會在門控內查驗 `listEntityId` 是否屬於登入者可見名單 `m_WeeklyReportRecordListData`，若不服則拋出 `ArgumentException` 拒絕存取，確保路由與授權安全。

- **I2: [世代與過期快照失效成功] 日期/小組變更可正確刷新快照與重試**
  - **受影響檔案**：`SmallGroupController.Date.cs:131-144`, `ListManager.cs:119, 129-144`
  - **說明**：日期變更呼叫 `ReloadDateAndGetIntegrateDetachedRead` 時，內部 `SetupListManagerCore` 會將 `m_PublishedIntegrateLoadKey` 歸零 (`null`) 並更新 `m_SelectDate`，確保下一次讀取必定建立新世代快照；且若載入失敗（如 CRM 超時例外），不更新 `m_PublishedIntegrateLoadKey`，下次請求可無黏性障礙地重試。

- **I3: [極致保留合法同名會友] 杜絕 Distinct 掩蓋作法**
  - **受影響檔案**：`ListManager.cs:337-357`, `ListManagerIntegratePublicationTests.cs`
  - **說明**：驗證邏輯僅鎖定穩定 key `PresentRecordId`；同 FullName 但不同 `PresentRecordId` 之兩筆獨立會友資料皆完整予以保留，完全合乎需求與產品規範。

---

## 3. Review Checklist & Scoring Report

### 驗證評分表 (Scoring Format for /ccg:bugfix validation)

```
VALIDATION REPORT
=================
User Experience: 18/20 - [修復重複姓名問題，防止 UI 顯示錯誤與重複點名，保留合法同名會友]
Visual Consistency: 19/20 - [DevExtreme DataGrid 排序/分頁與圖表快照一致，不再產生跨世代混亂]
Accessibility: 18/20 - [符合既有 Razor/DevExtreme 語意結構與鍵盤導覽]
Performance: 16/20 - [成功防止並行 AJAX 重複載入 CRM，但鎖內包覆同步 CRM I/O 在極端連線延遲時有排隊現象]
Browser Compatibility: 19/20 - [後端 API 與深複製隔離修復，不影響前端瀏覽器相容性]

TOTAL SCORE: 90/100

ISSUES FOUND:
- Critical: C# 檔案與單元測試檔包含亂碼 (Mojibake)，導致測試常數與斷言失敗。
- Warning: 鎖內含同步 CRM 網絡呼叫，極端情況下併發請求需等待 CRM 呼叫完成。

RECOMMENDATION: NEEDS_IMPROVEMENT (請先還原檔頭與測試常數之 UTF-8 編碼亂碼)
```

---

## 4. Accessibility Issues (a11y)
- **語意與結構**：本次變更主要集中於後端資料流與 API 控制器，前端 Razor View / DevExtreme DataGrid 結構無破損。
- **鍵盤與焦點**：DataGrid key 指定為獨立穩定 `PresentRecordId` 後，列選擇與鍵盤導覽焦點不會因重複 key 發生混亂。

---

## 5. Design Issues (設計一致性)
- **Token 與樣式**：無內聯樣式污染，遵守專案結構。
- **架構契約一致性**：完整落實 `.trellis/spec/backend/duplicate-row-publication-contract.md` 規範，以操作區域候選 (operation-local candidate) 建構，並於全部 I/O 完成後進行原子發布。

---

## 6. Positive Notes (亮點)
1. **TDD 導向測試**：新增 `ListManagerIntegratePublicationTests.cs` 涵蓋了 32 併發 single-flight、候選隔離、過期日期重建與載入失敗重試等完整的邊界測試。
2. **LINE 登入安全性大幅提升**：移除平行 `Task.Run` 與 `Task.WhenAll` 的 Session 競態，並封堵以 LINE User ID 作為小組 GUID 的轉型漏洞。
3. **主動防禦失敗**：Duplicate row key 直接 Fail Closed，嚴禁以 `DistinctBy(FullName)` 欺騙前端，保障資料嚴謹性。

---

## 7. 建議與後續行動 (Suggestions)
1. **立即修正亂碼檔**：將 `ListManagerIntegratePublicationTests.cs`、`ListManager.cs`、`SmallGroupController.LineLogin.cs` 與 `SmallGroupController.Date.cs` 重新儲存為標準 UTF-8 without BOM (CRLF)，恢復正確的繁體中文註解與測試字串。
2. **驗證測試執行**：執行 `dotnet test ChurchReport.MemberInfo.Tests` 確保所有併發與隔離測試皆呈綠燈。
