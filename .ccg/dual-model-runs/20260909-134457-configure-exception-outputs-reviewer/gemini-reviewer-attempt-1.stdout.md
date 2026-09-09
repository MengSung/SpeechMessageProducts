# 審查結論：PASS

經過詳細程式碼與單元測試審查，對應變更完全符合獨立例外輸出控制（`ExceptionNotifications:WriteExceptionLog` / `SendLine`）之規格授權與架構設計要求。

## 評估摘要

1. **四種組合獨立性與行為規範 (PASS)**
   - `Both=true`: 先完成 `Exception.log` 寫入與 `Flush(flushToDisk: true)` 後才放入 LINE 有界 channel。
   - `Log-Only (true, false)`: 正常落檔 flush，不建立 `LineExceptionSender` 與背景 consumer，無 LINE I/O。
   - `LINE-Only (false, true)`: 入列並發送 LINE；不建立具名 Mutex、不寫檔案 I/O，滿載/失敗狀態改輸出至固定 `stderr` (`Emergency`)。
   - `Off (false, false)`: 不做寫檔與發送，`Report()` 直接回傳 `false`。

2. **生命週期、取消與去重機制 (PASS)**
   - `ExceptionOutputOptions` 採用 Host 啟動快照機制，預設皆為 `true`，無效布林於啟動時拋出 `InvalidOperationException` 拒絕啟動。
   - Host 建立/設定解析前的致命例外，遵循規格退回固定 `stderr` (`Program.Fatal`/`InitializationFailed`)，不擅自猜測設定與寫檔。
   - 繼續保留弱引用去重（`ConditionalWeakTable`）、呼叫端明確取消判斷（`OperationCanceledException`）與確定性 `DisposeAsync` 清理。

3. **測試與文件覆蓋率 (PASS)**
   - `ExceptionOutputOptionsTests.cs` 完整涵蓋 4 種組合、預設值/無效設定拒絕、LINE-only 故障/佇列滿載時不建立日誌檔等場景。
   - `.trellis/spec/backend/error-handling.md`, `logging-guidelines.md`, `AGENTS.md` 文件說明均同步 updates，規格與實作高度一致。
