# ChurchReport Trace 發現修復 — 設計一致性分析報告

本報告針對 `prd.md`、`design.md`、`implement.md` 以及當前原始碼進行架構與設計一致性審查，評估實作計畫的潛在風險與矛盾。

---

## 1. UX Analysis (使用者影響分析)

* **背景作業穩定性**：`SmallGroupController.SaveIntegrate` 採用背景執行緒（`Task.Run`）進行 Dynamics 365 (Dataverse) 資料同步。若並行請求修改了成員清單，將引發 `InvalidOperationException` 導致背景同步崩潰。修復此問題可確保資料同步的 100% 成功率，避免使用者面臨資料遺失或狀態不一致的窘境。
* **系統可用性 (SLA)**：修正 `InMemoryDataContextSmallGroup` 在無 Session 狀態下產生的 `NOSESSION_*` 快取鍵洩漏，能防止伺服器因長期運行累積無效快取而導致記憶體耗盡 (OOM) 崩潰，直接提升系統的長期穩定性。

---

## 2. Design Evaluation (設計評估)

* **設計與實作計畫衝突**：在處理 F1 缺陷（成員清單並行修改）時，`design.md` 與 `implement.md` 對於 `Member` 的拷貝行為存在直接矛盾：
  * `design.md` 宣稱：僅複製 `List<Member>` 本身（淺拷貝），**不複製 `Member` 物件實例**。
  * `implement.md` 宣稱：對 `Member` 進行**深拷貝**，並要求在 `Member` 類別中實作 `Clone()` 方法。
* **快取模式不一致**：`InMemoryDataContextSmallGroup` 持有 13 個快取屬性，但 `implement.md` 僅規劃修改其中 6 個，這將導致快取管理模式分裂，留下未定義的後備行為。

---

## 3. Technical Considerations (技術考量)

* **AsyncLocal 隔離性**：`DataverseTrace.BeginBackgroundOperation` 透過 `AsyncLocal<RequestContext>` 的 Copy-on-Write 特性，在背景執行緒中建立獨立的 `RequestContext` 與 `RequestStats`。此設計非常優雅，能確保背景 CRM 操作統計（`crm.op`）正確歸屬至背景追蹤 ID，而不會污染或遺漏於主 HTTP 請求的 `request.end` 事件中。
* **原子性發布 (Atomic Publication)**：背景作業完成後，將資料寫回 `InMemoryContext` 時，必須採用**替換 List 參考**（例如 `m_AllMemeberData = newSnapshot`）的原子操作。若使用 `Clear()` 搭配 `AddRange()`，並行讀取的執行緒將在極短時間內讀取到空集合或不完整集合，導致前台 UI 顯示異常。

---

## 4. Options (方案選擇與權衡)

### 針對 F1 成員清單拷貝方式：
* **方案 A：深拷貝 `Member` 實例 (如 `implement.md` 規劃)**
  * *優點*：完全的執行緒隔離，即使背景作業修改了 `Member` 的內部屬性，也不會影響前台讀取。
  * *缺點*：效能開銷較大，且若 `Member` 結構未來新增複雜欄位，維護 `Clone()` 容易遺漏。
* **方案 B：僅複製 `List<Member>` 容器 (如 `design.md` 規劃)**
  * *優點*：極高的效能與極低的記憶體開銷。
  * *缺點*：前提是 `Member` 物件在業務邏輯中必須是**唯讀且不可變的 (Immutable)**。若背景作業（如 `RemoveTransferredMembers`）僅進行清單的篩選與移除，而不修改 `Member` 屬性，則此方案完全足夠。

---

## 5. Recommendation (建議方案)

1. **採用方案 B (淺拷貝 List)**：經評估，背景作業僅對 `List<Member>` 進行過濾與移除操作，並未修改 `Member` 實例本身的屬性。因此，僅需在 `SyncRoot` 鎖定下複製 `List` 容器即可，無需實作複雜的 `Member.Clone()`，以保持程式碼簡潔與高效能。
2. **補齊快取屬性修改**：必須將 `InMemoryDataContextSmallGroup` 的所有 13 個快取屬性一併納入 `TryGetSessionCacheKey` 的後備欄位修改範圍，避免遺漏。

---

## 6. Detailed Findings (詳細審查發現)

### [Critical] Finding 1: `InMemoryDataContextSmallGroup` 快取屬性修改遺漏
* **具體檔案**：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
* **實證/理由**：
  `implement.md` 步驟 3.2 僅規劃修改 6 個屬性（`ListManager`、`SmallGroupDataList`、`WeeklyReportData`、`NewPersonModel`、`PersonalInfomationModel`、`HappyGroupDataManager`）。
  然而，該類別共持有 13 個使用 `IMemoryCache` 的屬性（包括 `ListManagementDataManager`、`EquipmentDataManager`、`FeeList`、`LineBindingViewModel`、`AppointmentsListManager`、`DonationPaymentManager`、`PollManager`）。若未全數修改，當 `CurrentSession == null` 時，未修改的 7 個屬性仍會持續寫入 `NOSESSION_*` 鍵值至 `IMemoryCache`，導致記憶體洩漏風險依然存在。

### [Critical] Finding 2: `Member` 拷貝行為設計矛盾
* **具體檔案**：
  * `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md` (第 48 行)
  * `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md` (第 81-83 行)
* **實證/理由**：
  `design.md` 明確指出「僅複製 `List<Member>` 本身，不複製 `Member` 物件實例」；而 `implement.md` 卻要求「在 `Member` 類別建立深拷貝的 `Clone()` 方法」並進行深拷貝。此矛盾將導致開發人員在實作時無所適從，且影響到 `Member` 類別的修改範圍。

### [Warning] Finding 3: 專案 Markdown 文件存在亂碼 (Mojibake)
* **具體檔案**：
  * `.trellis/tasks/08-22-churchreport-trace-findings-remediation/prd.md`
  * `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md`
  * `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md`
* **實證/理由**：
  從讀取內容可見，上述 Markdown 檔案的中文註釋與說明出現大量亂碼（例如 `撖行葫蝻粹靽桀儔`、`?`）。這通常是由於 UTF-8 與 Big5 編碼衝突所致。由於專案要求嚴格執行 UTF-8 without BOM 與 CRLF 換行格式，必須在實作前使用工具將這些文件的編碼重新轉換並修復亂碼，以防提交損壞的文檔。

### [Info] Finding 4: `BeginBackgroundOperation` 的 AsyncLocal 隔離設計正確
* **具體檔案**：`ToolUtility/Dataverse/DataverseTrace.cs`
* **實證/理由**：
  設計中規劃的 `BeginBackgroundOperation` 透過建立新的 `RequestStats` 並寫入 `_requestContext.Value`，利用了 `AsyncLocal` 在非同步呼叫時的寫入隔離特性。這能確保背景執行緒中的 CRM 操作次數與耗時不會被主執行緒的 `RequestScope.Dispose()` 提前結算，設計符合預期。
