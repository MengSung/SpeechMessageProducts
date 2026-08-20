# 需求

## 目標

依現有 ChurchReport 追蹤檔、分析報告、Dataverse 架構圖與實作程式碼，評估下列面向是否需要改善：

- 效能與連線池行為。
- Session、使用者與租戶隔離。
- Managed／unmanaged memory 與其他資源生命週期。
- 現有 Trace 是否足以支持根因分析，以及應補哪些低成本、高價值事件。

## 範圍

- `D:\除錯追蹤\dataverse-trace.jsonl`
- `D:\除錯追蹤\Trace.log`
- `D:\除錯追蹤\ChurchReport-Trace-Report.md`
- Dataverse Gateway、connection manager、bounded pool、client lease、request profiler 與 trace analyzer。

## 交付

- 提供按優先級排列、附證據與程式位置的繁體中文分析。
- 區分「已由追蹤證明」、「目前未見異常」與「證據不足，不能宣稱安全」。
- 本任務只分析，不修改產品程式碼。
