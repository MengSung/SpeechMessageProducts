ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: churchreport-trace-options-script

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 審查任務：新增 ChurchReport 可調整組態啟動範例

請審查目前工作樹中以下未提交變更，並只針對本次工作範圍回報：

- `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1`
- `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` 的既有變更不可被破壞；本次新功能應主要由新腳本提供。

需求：使用者只需修改新腳本上方的設定區，即可選擇 `Debug`／`Release`、`$true`／`$false` 的 `DiagnosticsTrace:Enabled`、Trace 目錄、是否只編譯及是否開啟瀏覽器。編譯與啟動前要安全停止已在執行的 ChurchReport 網站程序；若占用連接埠的程序無法由命令列確認屬於 ChurchReport，必須拒絕誤殺。Trace 設定只能在目前腳本及子程序有效，結束後還原，不得污染永久或全域環境變數。Release 的產品編譯期 fail-closed 防線不可被腳本繞過。

請檢查：

1. PowerShell 5.1／7 語法與 UTF-8 輸出相容性。
2. 程序停止、PID／連接埠判斷、taskkill、逾時與資源清理是否安全。
3. Debug／Release 編譯與 DiagnosticsTrace 覆寫是否符合現有 Program.cs 契約。
4. 是否有 Critical、Warning 或 Info；只回報可由目前程式碼與 diff 證明的問題。


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