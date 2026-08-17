# 執行備註

## Run 1 期間發現、但刻意不處理的項目

### 1. `ToolUtility/Diagnostics/TraceLogger.cs` 是死碼，且會重複掛 listener

- `ToolUtilityNameSpace.Diagnostics.ITraceLogger` / `TraceLogger` 全專案**零使用**
  （`SpeechMessageProducts.ChurchReport/Logging/TraceLoggerProvider.cs` 內的
  `TraceLogger` 是同名但不同的巢狀 `ILogger`，兩者無關）。
- 該檔第 87 行同樣執行 `Trace.Listeners.Add(listener)`。
- 目前無害（沒有人建立它），但若日後有人啟用，會與
  `FileToolUtilityTracer` 重複掛上 listener，造成日誌重複輸出。

**建議**：另立票刪除該死碼，或明確標記為 obsolete。
本 Run 未處理，因為它不在 Run 1 的檔案白名單內。

### 2. `SpeechMessageProducts.ChurchReport/Program.cs:170` 也有 `Trace.Listeners.Add`

- 屬於應用程式啟動階段的既有行為，與 ToolUtility 的追蹤資源不同來源。
- 未確認兩者是否寫入同一檔案。若是，日誌會有兩份來源。

**建議**：Run 2 的人工回歸時一併觀察追蹤檔是否出現重複行。
本 Run 未處理，同樣不在白名單內。

## Run 0 的調查結論

見 `research/findings-scope-boundaries.md`。三個阻礙中，第 3 項
（`InMemoryDataContextSmallGroup` 以 Session 為鍵快取 `ToolUtilityClass`）
為原規劃未預見，已據此在 `implement.md` 新增 Run 1.5。
