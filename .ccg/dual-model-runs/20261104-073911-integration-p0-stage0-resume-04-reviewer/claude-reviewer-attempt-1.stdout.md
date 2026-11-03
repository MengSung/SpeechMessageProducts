# 審查結果：SpeechMessage.Dynamics.Plugins（Stage 0 第4批）

範圍 73b6e6cf..ae29cb40 / 安全差異 SHA256 `bf25a8bd...037b`。僅審查本批附帶之安全差異（已遮罩機密欄位，未讀取 appsettings／User Secrets／環境變數／金鑰內容），為確認 `LineChannelCredential` 與 `LinePushClient` 的例外轉譜契約，查閱了兩份既有非機密原始碼（`LineMessagingProcessor.Core/LineChannelCredential.cs`、`LinePushClient.cs`），未開啟 `.ccg/dual-model-runs` 其他歷史輸出。本份為單一分片結論，須與同批其他分份一併確認後才代表全批通過。

## Critical 🔴
無。未發現可驗證的 Critical 問題。

## Warning 🟡

- **`SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs` `Execute_ConcurrentRegistrationReuse_IsolatedAndDrained`（約第 218–240 行）**
  測試缺口：此測試只驗證「兩個不同外掛實例（A／B）各自在單一背景執行緒上依序呼叫 20 次」，`Run(plugin, id)` 內部是 `for` 迴圈、非並行呼叫。沙箱重用模型真正要保證的是「同一個外掛實例」在多個執行緒上被同時（interleave）呼叫 `Execute` 時，仍不會發生 `credential`／`userId`／`RetryKey` 跨請求串話。目前程式碼欄位皆為 `readonly`，理論上執行緒安全，但這條性質並未被任何測試直接驗證——目前測試無法偵測「同一 instance 併發呼叫」下可能出現的問題（例如未來若有人誤把某欄位改為可變狀態，此測試不會抓到）。
  可重現缺口：改為對同一個 `pluginA` 實例以 `Task.WhenAll` 從多個執行緒同時呼叫 `Execute`（而非每個外掛各自單執行緒依序呼叫），並斷言每次呼叫的 `RetryKey`／回應配對不會錯置。

## Info 🟢

- **`SpeechMessage.Dynamics.Plugins/Diagnostics/DiagnosticsPingPlugin.cs:132`（`Task.Run(() => client.PushAsync(...)).GetAwaiter().GetResult()`）**
  此為刻意的 sync-over-async 設計（已有 `RejectingContext` 測試驗證不回捕沙箱 `SynchronizationContext`），邏輯正確。僅提醒：此模式每次呼叫會佔用兩個 ThreadPool 執行緒（呼叫端阻塞 + `Task.Run` 工作執行緒），在目前「診斷用、低頻次單次 ping」情境下可接受；若此外殼之後被複用到高頻率情境，需重新評估 ThreadPool 飽和風險。非本批阻斷項。

- **`SpeechMessage.Dynamics.Plugins/Diagnostics/DiagnosticsPingPlugin.cs:62-63`**
  `tracing`／`context` 透過 `serviceProvider.GetService(...)` 取得後即轉型使用，若日後 SDK 行為改變導致回傳 `null`，`catch` 區塊內再次呼叫 `tracing.Trace(...)` 會丟出新的 `NullReferenceException` 蓋掉原始例外。此為依賴 Xrm SDK 契約保證非 null 的既定寫法，目前不構成可重現缺陷，僅記錄供日後參考，不視為發現。

## 已驗證符合規格之重點
- `Execute` 對 Retryable 保留 `result.TransportException` 作為 `InvalidPluginExecutionException.InnerException`；Permanent 僅追蹤分類、不拋出、不寫 `Exception.log`、不發管理者 LINE（符合設計 10.5/10.9）。
- 例外邊界只追蹤例外「型別名稱」，不寫入訊息／堆疊／收件者／token，`catch` 區塊、`InvalidConfiguration()`、建構子皆未回顯原始輸入。
- 註冊設定嚴格 allowlist＋`DuplicatePropertyNameHandling.Error`＋固定長度上限（4096）＋固定繁中拒絕訊息，無法以非法鍵、重複鍵或超長輸入繞過。
- `LineChannelCredential` 建構子對空白／非可見 ASCII token 僅拋出固定訊息的 `ArgumentException`，不回顯 token 內容；外層 `catch (ArgumentException)` 可正確轉譯，不會洩漏機密。
- 跨步驟（A/B 不同 secure config）以共用、無狀態 `LinePushClient`／`HttpClient` 重用時，`credential`／`userId`／`RetryKey` 皆正確對應各自步驟，無跨教會/跨使用者串話。
- 所有 HTTP 回應內容（`HttpResponseMessage`/`Content`）皆由 Core 的 `using` 確定性釋放，測試以計數斷言驗證歸零，無資源洩漏跡證。
- net48 專案以 `#if NET48` 區分建構子可見度與共用 `HttpClient` 生命週期，行為與既有 `LinePushClient.cs` 中 net10 路徑的例外分類（`OperationCanceledException`/`HttpRequestException` → Retryable）邏輯一致。

## 結論
本分片未發現 Critical 問題；一項 Warning（並行重用測試缺口）建議補測試但不阻斷；兩項 Info 供留意、非缺陷。本分片結論不可單獨代表全批審查通過，仍需與同批其他分份一併確認。

---
SESSION_ID: 746ee31f-e9ef-4c77-aa02-88047a2295eb
