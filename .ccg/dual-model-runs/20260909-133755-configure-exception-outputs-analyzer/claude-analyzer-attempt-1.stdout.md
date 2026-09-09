## 現況分析（Program.cs / ExceptionDiagnostics.cs）

**目前行為**：`ExceptionDiagnostics` 沒有任何開關，`Report()` 固定流程是「同步 flush 落檔 → 成功才 enqueue LINE」（ExceptionDiagnostics.cs:87-113）。`WriteStatus`（線上狀態、佇列滿、LINE 失敗等）一律透過 `Write()` 落檔（第227-230、104、259、297行）。`Program.Main`（Program.cs:58-84）在建立 `WebApplicationBuilder` **之前**就先 new 出 `ExceptionDiagnostics`，目的是連 builder 建置期間的例外都能被捕捉；此時 `builder.Configuration` 尚不存在，是設定開關必須解決的先有雞先有蛋問題。

## 建議最小改動

1. **設定讀取**：在 `Program.Main` 建 `ExceptionDiagnostics` 前，用一個輕量 `ConfigurationBuilder().AddJsonFile("appsettings.json", optional:true).AddEnvironmentVariables()` 只讀 `ExceptionNotifications:WriteExceptionLog` / `SendLine` 兩個布林（預設 true），存成區域變數，作為啟動快照，不再讀取第二次（滿足「startup snapshot」）。不要延後到 `builder.Configuration` 才讀，否則失去 builder 建置期例外的保護。

2. **建構子擴充**：`ExceptionDiagnostics(string directory, bool writeExceptionLog = true, bool sendLine = true, long maximumFileBytes = ...)`，存成 `readonly` 欄位 `_writeLog`、`_sendLine`。

3. **`Report()` 分支**（核心變更）：
   - `_writeLog==true`：維持現狀（flush 成功才 enqueue），但 enqueue 條件再加 `&& _sendLine`。
   - `_writeLog==false && _sendLine==true`（使用者明確授權的 LINE-only 略過落檔）：略過 `Write()`，直接組出通知內容並 `TryWrite` 進佇列；佇列滿或組裝失敗改走一個新的 `EmergencyOnly`（固定訊息、只寫 stderr，不落檔、不遞迴呼叫 `Write`）。
   - 兩者皆 `false`：只維護 `_reported` 去重（避免上游 logger 重複上報），不做任何 I/O，直接 return。

4. **`WriteStatus()` 需要條件化**：新增判斷 `if (!_writeLog) { Emergency(...); return; }`，把 `LineDeliveryFailed`／`LinePendingAtShutdown`／`LineQueueFull` 三處呼叫（第104、259、297行）在 log 關閉時全部改走固定 stderr 訊息，滿足「LINE 錯誤在 log 關閉時只走固定 stderr」。

5. **`StartNotifications` 呼叫端**（Program.cs:69-70）：若快照 `SendLine==false`，直接不建立 `LineExceptionSender`、不呼叫 `StartNotifications`，確保「off 不得有任何網路行為」而非僅內部忽略。

## 風險
- Config 讀取路徑（appsettings + 環境變數）若與 `builder.Configuration` 之後解析結果不一致（例如環境變數覆蓋順序不同），快照值可能與正式設定不符，需在文件中明確標註「僅程序啟動時決定，重啟才生效」。
- `_writeLog=false` 分支繞過落檔直接組 JSON，需重用既有 `Symbol`/`StackSymbols` 序列化邏輯，避免兩套格式漂移。
- `WriteStatus` 條件化容易遺漏呼叫點，需逐一檢查（目前 3 處）。

## 測試建議
- 四種布林組合各一個整合測試：驗證檔案是否被建立/寫入、`sender` 是否被呼叫、stderr 內容格式。
- `WriteLog=false, SendLine=true` 時人為讓 sender 拋例外，斷言 stderr 收到固定訊息且 `Exception.log` 未被建立。
- `Both=true` 情境維持既有 flush-before-enqueue 的併發/輪替測試（回歸）。

---
SESSION_ID: 5e0d1839-28b3-4193-90f1-2f524896c590
