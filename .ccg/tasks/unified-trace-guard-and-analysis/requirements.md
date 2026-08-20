# 統一 Trace 兩層保護與三檔綜合分析

## 範圍

- Release 建置必須以編譯期防線強制停用三種檔案 Trace，任何執行期設定都不能重新開啟。
- Debug 建置只使用 `DiagnosticsTrace.Enabled` 與 `DiagnosticsTrace.Directory` 控制三個固定檔名。
- 三個預設輸出為 `D:\除錯追蹤\dataverse-trace.jsonl`、`D:\除錯追蹤\Trace.log` 與 `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT`。
- 建立單一唯讀 PowerShell 分析器，以有界記憶體串流分析三檔並輸出 UTF-8 without BOM Markdown 報告。

## 驗證原則

- 本機測試、Debug/Release 建置、Release 無痕 smoke、fixture 與真實 Trace 分析是完成門檻。
- Gemini 與 Claude 分析／審查只作參考；份額或服務不可用時可降級，但必須如實記錄。
- 不覆寫、不刪除既有 Trace 或使用者未提交變更。
