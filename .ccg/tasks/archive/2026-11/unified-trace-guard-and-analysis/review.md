# 審查紀錄

## 本機驗證

- `ToolUtility.Dataverse.Tests`: 44/44 通過。
- `ToolUtility.Tests`: 63/63 通過；既有 NU1701、nullable 與 using warnings 保留。
- ChurchReport Debug build: 0 warnings / 0 errors。
- ChurchReport Release build: 0 warnings / 0 errors。
- Debug disabled smoke: 0 trace files。
- Debug enabled smoke: 建立 `Trace.log`，正常停止後可讀。
- Release + `DiagnosticsTrace__Enabled=true`: 0 trace files。
- Analyzer fixture: valid exit 0/WARN；invalid exit 2/FAIL；missing files exit 0/WARN。
- Windows PowerShell 5.1 與 PowerShell 7 fixture 均可產生報告。
- 真實分析器不修改 Dataverse/legacy 原檔；SHA-256、長度與時間均保持不變。

## 外部模型參考

本次透過專案 self-healing runner 執行雙模型 reviewer。Gemini 有輸出，Claude 兩次均為 `no-usable-output`；因此不是完整雙模型 review，也不是 quota fallback 成功。Gemini 僅作 advisory 參考。

- 已採納：PowerShell 7 需註冊 CodePages provider；已加入 reflection-based、兼容 Windows PowerShell 5.1 的註冊邏輯。
- 已採納：GC monitor 不應在同一 `ApplicationStopping` token callback 中先同步等待；已改用專屬 CTS 與回歸測試。
- 已核實為誤判：Gemini 所稱 C# 亂碼／非法預設路徑；實際 UTF-8 bytes、build 與 smoke 均證明 `D:\除錯追蹤` 正確。
- 已核實為誤判：Dataverse CTS race；實作先等待 writer task 完成，再 Dispose CTS。
- 已核實為非問題：RequestScope；唯一呼叫位於 `DataverseTraceMiddleware` 的 `using` 範圍。

## 真實報告現況

`D:\除錯追蹤\ChurchReport-Trace-Report.md` 目前為 FAIL：Dataverse request/lease 配對 PASS，但 `Trace.log` 缺檔，legacy 檔案有敏感模式命中。報告只輸出計數，不重印原始敏感值；這是現有 trace 證據的診斷結果，不是分析器寫入原檔造成。

`pool.cleanup` 的 `idleAfter < minSize` 已改為併發敏感 snapshot observation，不再單獨判定 violation；因 cleanup 選取與 trace snapshot 之間可能有合法 lease acquire。
