# B0 + B1 範圍審查報告

本報告針對目前工作樹中尚未提交的 B1 變更進行審查，重點關注 UX、可存取性、設計一致性、效能與資源生命週期。

---

## VALIDATION REPORT
=================
User Experience: 18/20 - 診斷開關的引入能有效減少高頻 Debug 輸出對效能量測的干擾，提升開發與測試體驗。
Visual Consistency: 20/20 - 程式碼結構與既有的 `ProfilingSwitch` 保持高度一致，符合設計模式。
Accessibility: 20/20 - 此為後端與診斷變更，不涉及 UI 可存取性，給予滿分。
Performance: 12/20 - 雖然將 `AutoFlush` 設為 `false` 減少了寫入頻率，但每個請求結束時在 middleware 中同步呼叫 `FlushTraceListener()` 並持有全域鎖 `_traceLock` 進行磁碟 I/O，在高併發下會造成嚴重的執行緒阻塞與效能瓶頸。
Browser Compatibility: 20/20 - 後端變更，不影響瀏覽器相容性。

TOTAL SCORE: 90/100 (因存在 Critical 編譯錯誤，實際推薦為 NEEDS_IMPROVEMENT)

ISSUES FOUND:
- [Critical] 檔案編碼不一致 (Big5/CP950) 導致「許功蓋」字元跳脫引號，引發編譯錯誤。
- [Warning] 請求結束時的同步鎖與磁碟 I/O 瓶頸。
- [Warning] 測試對實體原始碼路徑的依賴性。
- [Info] 使用 Conditional 屬性優化 Release 效能。

RECOMMENDATION: NEEDS_IMPROVEMENT
=================

---

## 1. Summary (總體評估)
本次 B1 變更成功引入了 `SessionDiagnosticsSwitch` 來保護 `InMemoryDataContextSmallGroup.cs` 中原有的 51 個 `Debug.WriteLine` 呼叫，並將 `Program.cs` 的 `AutoFlush` 改為 `false`，這在設計方向上正確地避免了高頻 Session Debug 輸出與同步磁碟 I/O 污染效能量測的問題。

然而，目前變更存在一個**致命的編譯錯誤**：新建立的檔案與部分修改的檔案採用了 **Big5 (CP950)** 編碼，導致繁體中文註解中的特定字元（如「許功蓋」等尾碼為 `0x5C` 的字元）在編譯器以 UTF-8 讀取時被誤判為跳脫字元 `\`，進而跳脫了字串的結束雙引號 `"`，導致程式碼無法編譯。此外，在每個請求結束時同步執行 `Flush()` 並持有全域鎖，在高併發環境下會引入嚴重的效能瓶頸。

---

## 2. Detailed Findings (詳細發現)

### Critical (嚴重問題)

#### 發現 1: 檔案編碼不一致 (Big5/CP950) 導致「許功蓋」字元跳脫引號，引發編譯錯誤
* **檔案路徑與行號**:
  * `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
  * `ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs`
  * `SpeechMessageProducts.ChurchReport/Program.cs` (部分新增/既有註解)
* **技術理由**:
  這些檔案採用了 Big5 (CP950) 編碼（或混合編碼），違反了專案要求的 `UTF-8 without BOM` 規範。當編譯器或讀取器以 UTF-8 讀取這些檔案時，Big5 編碼中尾碼為 `0x5C` (即 `\`) 的繁體中文字（例如「許」、「功」、「蓋」等）會被誤判為跳脫字元，從而將緊隨其後的雙引號 `"` 跳脫（變成 `\"`）。這導致 `SessionDiagnosticsSwitchTests.cs` 第 47 行與第 81 行等處的字串常數未閉合，引發編譯錯誤（如 `CS1010: Newline in constant`），使專案完全無法編譯。
* **具體修正建議**:
  將上述所有 `.cs` 檔案重新儲存為 **UTF-8 without BOM** 編碼，並確保所有中文字元在 UTF-8 下顯示正常，消除 `0x5C` 跳脫字元造成的語法錯誤。

---

### Warning (警告事項)

#### 發現 2: 請求結束時的同步鎖與磁碟 I/O 瓶頸
* **檔案路徑與行號**: `SpeechMessageProducts.ChurchReport/Program.cs` (第 111-121 行, 第 325-342 行)
* **技術理由**:
  在 `Program.cs` 的 middleware 中，每個 HTTP 請求結束時都會在 `finally` 區塊中同步呼叫 `FlushTraceListener()`。該方法內部使用全域鎖 `_traceLock` 並執行 `_traceListener?.Flush()`。這意味著在高併發環境下，所有請求在結束時都必須排隊等待同一個鎖，並同步執行磁碟 I/O 寫入。這會引入嚴重的鎖競爭（Lock Contention）與 I/O 延遲，可能導致執行緒池飢餓，成為新的效能瓶頸。
* **具體修正建議**:
  避免在每個請求結束時同步執行 `Flush()`。可以考慮：
  1. 僅在偵錯模式且併發量極低時才進行同步 Flush。
  2. 使用背景執行緒（如 `Channel<T>` 或環形緩衝區 Ring Buffer）非同步批次寫入與 Flush 診斷日誌。
  3. 增大 `StreamWriter` 的緩衝區，並依賴定時器（Timer）在背景定期 Flush，而非在請求管線中同步執行。

#### 發現 3: 測試對實體原始碼路徑的依賴性
* **檔案路徑與行號**: `ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs` (第 42-82 行)
* **技術理由**:
  `SessionDiagnosticsSwitchTests` 透過 `FindRepositoryRoot()` 在執行期向上尋找實體原始碼目錄並讀取 `.cs` 檔案內容來進行斷言。這種做法在本地開發環境中可行，但在 CI/CD 流程、容器化測試環境或 shadow-copy 測試執行器中，通常無法取得原始碼目錄，會拋出 `DirectoryNotFoundException`，導致測試不穩定或失敗。
* **具體修正建議**:
  如果必須驗證程式碼結構，建議將此測試歸類為靜態分析或編譯期檢查，或者在測試中加入對目錄是否存在的防禦性跳過（例如使用 `Skip` 屬性），避免在無法取得原始碼的環境中導致 CI 流程中斷。

---

### Info (非阻擋建議)

#### 發現 4: 使用 Conditional 屬性優化 Release 效能
* **檔案路徑與行號**: `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` (第 188-197 行)
* **技術理由**:
  `WriteSessionDiagnostic` 方法內部使用了 `#if DEBUG` 包住邏輯，但在 Release 組態下，呼叫端（共 51 處）仍然會產生空的方法呼叫開銷（除非 JIT 進行了內聯優化）。
* **具體修正建議**:
  在 `WriteSessionDiagnostic` 方法宣告上加上 `[System.Diagnostics.Conditional("DEBUG")]` 屬性（需引入 `System.Diagnostics` 命名空間）。這樣在 Release 編譯時，編譯器會直接在編譯期移除所有對該方法的呼叫，達到零執行期開銷。

---

## 3. Positive Notes (做得好的地方)
1. **隔離邊界清晰**：`SessionDiagnosticsSwitch` 的設計完全符合要求，僅在 `#if DEBUG` 下存在，且為 `volatile bool`，不保存任何 request、Session 或使用者狀態，避免了 Session 洩漏風險。
2. **生命週期管理妥當**：`Program.cs` 中對全域 Trace listener 的初始化與清理邏輯非常嚴密，在正常停止時有解除 `UnhandledException` 事件訂閱，避免了記憶體與 handle 洩漏。
3. **註解詳盡**：修改過的程式碼皆附有非常詳盡的繁體中文註解，清楚解釋了隔離邊界、生命週期與清理機制（待編碼修正後即可正常閱讀）。
