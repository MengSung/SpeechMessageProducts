ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: start-churchreport-development

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
請審查目前工作區中新增的 ChurchReport PowerShell 開發啟動腳本。

目標：使用者執行一個 .ps1 後，應完成 UTF-8 設定、dotnet 編譯、啟動 ChurchReport、等待 http://localhost:5000/ 可用、開啟預設瀏覽器，並在 Ctrl+C／錯誤／正常結束時清理啟動的網站程序。

請檢查：PowerShell 5.1 與 PowerShell 7 語法相容性、路徑／參數處理、程序樹清理、埠與啟動競態、dotnet 參數正確性、編碼、錯誤處理，以及是否有超出需求的變更。

變更檔案：SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1
請執行 git diff -- SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1，並輸出 Critical/Warning/Info 分級結果；若無問題請明確寫出 No findings。


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