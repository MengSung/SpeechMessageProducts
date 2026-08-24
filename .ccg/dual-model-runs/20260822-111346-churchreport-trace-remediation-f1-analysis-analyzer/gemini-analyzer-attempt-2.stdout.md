# CCG 雙模型分析報告：F1 背景上傳狀態隔離與安全設計

本報告針對 `churchreport-trace-remediation-f1-analysis` 任務進行深度分析。在不修改任何檔案的前提下，評估目前 `SaveIntegrate` 背景任務與前景請求共用可變狀態的風險，並提出最小安全設計方案、完整列舉集合使用點、規劃測試優先順序，以及識別範圍外項目與潛在風險。

---

## 1. UX Analysis (使用者影響評估)

- **並發操作導致的隨機崩潰 (Critical)**：
  目前 `SaveIntegrate` 採用 Fire-and-Forget 模式。當背景執行緒在執行 `RemoveTransferredMembers` 清理已轉移成員時，若使用者同時在前端重新整理頁面、切換小組或進行 Crud 操作，前景執行緒會列舉（Enumerate）同一個 `Members` 集合，這將直接引發 `InvalidOperationException`（Collection was modified; enumeration operation may not execute），導致前端畫面顯示錯誤或崩潰，嚴重損害使用者體驗。
- **資料同步狀態不透明 (Warning)**：
  使用者執行「儲存並整合（SaveIntegrate）」後，系統立即回應「資料已送出，正在背景處理中...」（響應時間約 3ms），隨後使用者繼續瀏覽其他頁面。若背景同步在 14 秒的執行過程中因競態條件崩潰，使用者將無法得知同步失敗，導致資料遺失或狀態不一致。
- **跨使用者資料污染風險 (Critical)**：
  若背景執行緒在存取共享狀態時未做好 Session 隔離，可能會導致 A 使用者的操作影響到 B 使用者的 Session 快取，造成嚴重的隱私與資料安全問題。

---

## 2. Design Evaluation (設計系統與模式評估)

- **快照隔離模式 (Snapshot Isolation)**：
  為了在不影響前景讀取效能的前提下消除競態，應採用「寫入時複製（Copy-on-Write / Snapshot）」模式。背景執行緒在啟動前，先對 Session 快取中的 `ListSmallGroupWeeklyReport` 進行深/淺拷貝，取得一個完全隔離的快照。背景的所有上傳與清理操作均在此快照上進行，與前景完全隔離。
- **原子性替換引用 (Atomic Reference Swap)**：
  清理完成後，背景任務必須在同步鎖（`SyncRoot`）的保護下，將清理後的 `Members` 列表引用原子性地替換回共用快取，而不是使用 `Clear() + AddRange()`。這確保了讀取端在任何時刻看到的都是一個完整的列表，避免了短暫的空列表狀態。
- **一致性與 Token 使用**：
  此設計符合專案既有的記憶體快取管理模式，且不需要在全 repo 的讀取端加上繁瑣的鎖，保持了程式碼的簡潔與一致性。

---

## 3. Technical Considerations (技術與架構考量)

### 3.1 目前資料流與共享可變狀態風險
1. **前景捕獲**：`SaveIntegrate` 在主執行緒中捕獲了 `weeklyReportRef`（指向 Session 快取中的 `ListSmallGroupWeeklyReport` 實例）以及 `allMemberData`。
2. **背景執行**：`Task.Run` 啟動後，背景執行緒直接對 `weeklyReportRef.m_SmallGroupDataList.m_SmallGroupData.Members` 執行 `RemoveTransferredMembers`。此列表是直接指向 `IMemoryCache` 中的共享實例。
3. **競態點**：
   - **背景清理**：`RemoveTransferredMembers` 內部使用 `for` 迴圈搭配 `RemoveAt` 就地修改 `List<Member>`。
   - **前景列舉**：`EquipmentController`、`PersonalController`、`NewPersonController` 以及前端 Razor 視圖（如 `_GeneralGroupGrids.cshtml`）會並行遍歷該 `Members` 列表。
   - **結果**：引發 `InvalidOperationException` 或導致 `List<T>` 內部結構損壞。

### 3.2 最小安全設計方案
- **修改檔案邊界**：
  - `SmallGroupDataList.cs`：新增 `private readonly object _syncRoot = new();` 與 `public SmallGroupDataList CreateIsolatedSnapshot()`。
  - `ListSmallGroupWeeklyReport.cs`：新增 `public ListSmallGroupWeeklyReport CreateBackgroundUploadCopy()`。
  - `Member.cs`：新增 `public Member Clone()` 以支援深拷貝。
  - `SmallGroupController.Save.cs`：改寫 `SaveIntegrate`，在 `Task.Run` 前建立快照，並在背景任務結束時於 `SyncRoot` 鎖保護下進行原子性替換。
- **取消與例外處理**：
  - 背景任務的 `Task.Run` 內部必須有完整的 `try-catch`，並使用 `DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload")` 包裹，確保異常被記錄且資源（如 `IServiceScope`）在 `finally` 中被正確 `Dispose`。
  - 背景上傳使用 `CancellationToken.None`，確保即使 HTTP 請求已中斷，背景上傳與清理仍能完整執行完畢。

### 3.3 全 Repo 集合使用點列舉
經檢索，以下為 `m_SmallGroupData.Members`、`m_NewPersonFollowUpData.Members`、`m_AllMemeberData.Members` 的主要使用點：

#### 1. `m_SmallGroupData.Members`
- **`SmallGroupController.Save.cs`** (行 148, 150, 151, 264, 266, 267) - 背景清理與判斷。
- **`SmallGroupController.DataApi.cs`** (行 124) - API 讀取。
- **`SmallGroupDataList.cs`** (行 174, 206) - 資料初始化與填充。
- **`DownloadIntegrateData.Setup.cs`** (行 136, 142, 293) - 排序、清理與初始化。

#### 2. `m_NewPersonFollowUpData.Members`
- **`SmallGroupController.Save.cs`** (行 155, 157, 158, 271, 273, 274) - 背景清理。
- **`NewPersonController.cs`** (行 119) - CRUD 讀取。
- **`DownloadIntegrateData.Setup.cs`** (行 137, 143, 294) - 排序、清理與初始化。

#### 3. `m_AllMemeberData.Members`
- **`EquipmentController.cs`** (行 231, 251, 335) - 設備管理讀取成員。
- **`PersonalController.cs`** (行 179, 406, 408, 455) - 個人資料維護與讀取。
- **`ListSmallGroupWeeklyReport.cs`** (行 158, 161, 164-171, 378, 380-385) - 週報資料處理與屬性修改。
- **`ListManager.cs`** (行 582) - 成員遍歷。
- **`DownloadIntegrateData.Members.cs`** (行 301, 351, 470, 654) - 下載與填充。
- **`DownloadIntegrateData.Setup.cs`** (行 135, 141, 273, 315) - 排序、清理與初始化。

*潛在缺口說明*：若有透過反射（Reflection）或動態 JSON 序列化存取 `Members` 的地方，靜態 Grep 無法完全覆蓋。因此，在寫入端（`SaveIntegrate`）進行快照隔離是唯一能 100% 保證執行緒安全的方案。

---

## 4. Options (替代方案與權衡)

### 方案 A：全域讀寫鎖 (Global Read-Write Lock)
- **作法**：在所有讀取端與寫入端都加上 `lock (SyncRoot)`。
- **優點**：概念簡單，直接保護共享變數。
- **缺點**：修改範圍極大（超過 30 處），極易遺漏；且會導致前景讀取請求被背景長達 14 秒的上傳操作阻塞，嚴重影響系統響應性。

### 方案 B：快照隔離與原子性替換 (Snapshot Isolation & Atomic Swap) - **推薦**
- **作法**：背景使用快照上傳與清理，完成後在鎖保護下原子性替換引用。
- **優點**：前景讀取端完全無鎖，效能極高；改動範圍侷限於 `SaveIntegrate` 與 `SmallGroupDataList` 內部，風險極低。
- **缺點**：在建立快照時會有微小的記憶體開銷，但對於小組成員數量（通常小於 100 筆）而言微不足道。

---

## 5. Recommendation (推薦方案與理由)

**推薦採用方案 B（快照隔離與原子性替換）**。

### 理由：
1. **影響範圍最小化**：無需修改全 repo 超過 30 處的讀取呼叫點，僅需在 `SmallGroupDataList` 內實作快照複製，並在 `SaveIntegrate` 背景工作中使用該快照，符合高內聚、低耦合的設計原則。
2. **極佳的效能與響應性**：前景讀取端無需等待背景上傳鎖，保持了系統的高吞吐量。
3. **徹底消除 `InvalidOperationException`**：由於背景清理是在隔離的快照上執行，前景列舉舊引用時不會受到任何干擾。

---

## 6. Findings & Risks (發現與風險分類)

### Critical
- **並發修改衝突風險**：
  - **路徑**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
  - **理由**：背景執行緒在 `Task.Run` 中直接修改共享的 `Members` 列表，而前景執行緒在多處並行讀取，極易引發 `InvalidOperationException` 導致系統崩潰。
- **憑證安全風險**：
  - **路徑**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
  - **理由**：背景任務捕獲了 `password` 變數。必須確保此變數絕對不會被寫入 `DataverseTrace` 或任何日誌中，避免敏感資訊外洩。

### Warning
- **原子性替換的空值風險**：
  - **理由**：若在回寫共用快取時使用 `Clear()` 接著 `AddRange()`，在多執行緒環境下，前景讀取執行緒極有可能在 `Clear()` 之後、`AddRange()` 之前讀取到空集合，導致 UI 顯示空白。必須使用**原子性替換引用**（如 `m_SmallGroupData.Members = newIsolatedList;`）來發布更新。

### Info
- **殘留的 Markdown 亂碼**：
  - **路徑**：`SpeechMessageProducts.ChurchReport/文件/修正官網奉獻網頁/HomeController-南崁長老教會.md`
  - **理由**：該備份文件存在編碼不一致導致的亂碼，但不影響系統執行。

---

## 7. 測試優先順序與競態測試規劃

1. **並發讀寫競態測試 (Race Condition Test)**：
   - **測試方法**：啟動背景 `SaveIntegrate` 模擬長達 2 秒的清理過程，同時啟動 10 個執行緒並行讀取 `m_SmallGroupData.Members`。
   - **驗證指標**：無任何執行緒拋出 `InvalidOperationException`。
2. **原子性替換驗證測試 (Atomic Swap Test)**：
   - **測試方法**：前景以極高頻率讀取成員數量，背景執行清理並回寫。
   - **驗證指標**：讀取到的數量絕不能為 0（除非原本就是 0），確保無短暫空狀態。
3. **跨使用者隔離測試 (Session Leakage Test)**：
   - **測試方法**：模擬兩個不同的 SessionId，同時發送 `SaveIntegrate` 請求。
   - **驗證指標**：背景上傳與清理各自獨立，無資料交叉污染。
