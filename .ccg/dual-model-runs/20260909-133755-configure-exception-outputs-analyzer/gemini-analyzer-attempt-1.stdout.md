### 獨立例外輸出配置 (Exception Output Options) 設計分析報告

---

#### **1. 行為矩陣與 UX / 系統影響評估**
在 `Program.cs` 與 `ExceptionDiagnostics.cs` 啟動時快照 `ExceptionNotifications:WriteExceptionLog` 與 `SendLine`（預設皆為 `true`）：
* **雙開 (`WriteExceptionLog=true, SendLine=true`)**：維持既有強契約，完成 `Logs/Exception.log` 寫入與 `Flush(flushToDisk: true)` 後方可推入 LINE 佇列。
* **僅送 LINE (`WriteExceptionLog=false, SendLine=true`)**：使用者授權直送 LINE 繞過日誌。無檔案 I/O；若佇列滿載或 LINE 送出失敗，**僅允許輸出至固定 stderr (`Console.Error`)**，絕不上鎖寫檔。
* **僅寫 Log (`WriteExceptionLog=true, SendLine=false`)**：日誌落檔並 Flush，完全不發送 LINE（零網路請求）。
* **全關 (`WriteExceptionLog=false, SendLine=false`)**：無檔案 I/O、無網路傳輸，記憶體去重後直接返回。

---

#### **2. 最小架構變更 (Minimal Changes)**
1. **`ToolUtility/Diagnostics/ExceptionDiagnostics.cs`**：
   * 建構子新增 `bool writeExceptionLog = true, bool sendLine = true` 唯讀欄位 snapshot。
   * `Report()` 依據 `_writeExceptionLog` 決定是否呼叫 `Write(record)`；依據 `_sendLine` 決定是否推入 Channel。
   * `ConsumeAsync()` 與佇列滿載處理：若 `_writeExceptionLog == false`，發生 LINE 傳送異常時，全數降級為固定 stderr 記錄。
2. **`SpeechMessageProducts.ChurchReport/Program.cs`**：
   * 將 `ExceptionDiagnostics` 初始化移動至 `WebApplication.CreateBuilder(args)` 之後，快照 `builder.Configuration.GetValue(...)` 傳入建構子。

---

#### **3. 分級發現與診斷 (Classified Findings)**

* **[Critical] `Program.cs` 初始化順序風險**
  * **位置**：`SpeechMessageProducts.ChurchReport/Program.cs:60`
  * **分析**：現行程式在 `WebApplication.CreateBuilder` 前即具現化 `ExceptionDiagnostics`。若未調整順序，將無法取得 `appsettings.json` 內的 `ExceptionNotifications` 快照。

* **[Warning] `ExceptionDiagnostics.cs` 不合規寫檔隱患**
  * **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs:104, 235`
  * **分析**：現有 `LineQueueFull` 與 `ConsumeAsync` 失敗時會呼叫 `WriteStatus()` 寫入 Log。當 `WriteExceptionLog=false` 時，若未做分支判斷將導致意外建立 Log 檔案，違反 LINE-only 授權。

* **[Info] 彈性與測試覆蓋建議**
  * **建議測試**：新增 `ExceptionDiagnosticsTests` 覆蓋 4 種組合（`T/T`, `F/T`, `T/F`, `F/F`），重點驗證 `WriteExceptionLog=false` 時零檔案產生、`SendLine=false` 時零網路 Enqueue，以及 `F/T` 失敗時 stderr 正確輸出。
