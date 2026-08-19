ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: analyze-traces-traditional-chinese

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 審查任務：ChurchReport Trace 報告繁體中文化

請審查目前工作樹中 `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1` 的未提交變更。

目標是把產生到 `D:\除錯追蹤\ChurchReport-Trace-Report.md` 的人類可讀文字改為繁體中文，同時保留必要的技術識別名稱，例如檔名、JSONL 事件名稱、Perf 標籤、traceId、端點路徑、編碼名稱與狀態值。請檢查：

1. PowerShell 5.1 與 PowerShell 7 語法是否仍正確。
2. 翻譯是否有遺漏或改壞分析器行為、敏感資料遮罩、WARN/FAIL 狀態與退出碼。
3. UTF-8 BOM、換行與報告 UTF-8 without BOM 契約是否合理。
4. 是否有 Critical、Warning 或 Info 問題。

這是文字輸出修正，不需要擴大到 C#、網站啟動腳本或其他未提交檔案。請只回報可由目前程式碼與 diff 證明的問題。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.