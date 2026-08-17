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

## Run 1.5 結果

- 工作 A：DONE。`InMemoryDataContextSmallGroup.ToolUtilityClass` 已直接由
  `ToolUtilityFactory` 取得既有單例；Session ID 加上
  `_ToolUtilityClass` 的 IMemoryCache 快取與 `m_ToolUtilityClass` 欄位均已移除。
- 工作 B：DONE。`PersonalController` 與 `SmallGroupController.Save` 的
  fire-and-forget 工作均在 lambda 內建立自己的 DI scope，並由 `using` 在工作
  結束時確定性釋放。resolved service 不會被個別 Dispose。
- A-2 調查：`rg -n '"dirty"|dirty' --glob '*.cs' SpeechMessageProducts.ChurchReport`
  只找到 `session.SetInt32("dirty", 1)` 的寫入端與診斷文字，沒有讀取端；因此
  移除 ToolUtility 快取時一併移除其快取未命中的 `SetSessionDirtyFlag()` 呼叫，
  不會改變任何讀取者的行為。
- 範圍外發現：`ListSmallGroupWeeklyReport` / `UploadIntegrateData` 仍在其內部以
  `ToolUtilityFactory` 建立舊有 ToolUtility。Run 1.5 的白名單不包含這些檔案，
  因此未修改；SmallGroup 背景 scope 已用於取得其自身 ToolUtility 並記錄背景
  失敗。內部上傳器改採 scoped ToolUtility 應在 Run 3 的 Factory 呼叫點遷移中處理。

### 完成判定實際輸出

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤

最終重跑時 IIS Express Worker Process 鎖住既有輸出 DLL，MSBuild 重試後仍建置成功；
該次輸出為 24 個 MSB3026 檔案鎖定警告、0 個錯誤，並非本次程式碼警告。

dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63

dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-build
已通過! - 失敗:     0，通過:     7，略過:     0，總計:     7

grep -n "_ToolUtilityClass\"" SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
（無輸出）

grep -n "m_ToolUtilityClass" SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
（無輸出）

grep -c "CreateScope()" SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs
1

grep -c "CreateScope()" SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
1

ENCODING OK
```

本 Run 的提交雜湊以提交完成後的 `git log -1 --format=%H` 為準。
