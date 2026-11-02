ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p01-r

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# P0-01 測試修復審查（唯讀）

你是此次外部唯讀審查者，不是 repo 的實作協調者。不要建立任務、修改檔案、呼叫其他模型或啟動 CCG／Trellis 工作流程。
使用者已明確授權僅 Claude 審查；直接回傳 Critical / Warning / Info 分級結果。

本次範圍：git diff 內 11 個測試檔、2 個 CCG 自癒腳本。產品程式未修改。
基準為 .trellis/tasks/11-02-integration-p0/baseline/baseline-summary.json：738 例中 24 失敗。
修復後全方案 Release 738/738、Debug tracer 4/4 已通過。重點：
- 型別反射改查實際產品組件，負向案例不能因組件名稱錯誤而假通過。
- 方案與產品路徑對齊改名後檔案，來源掃描不能漏掉不存在的目錄或跨到父 worktree。
- 一個舊自我比較案例重新命名，依既定移除 QpayManager 別名決策改斷言不存在；案例數不變。
- tracer 保留原有四例；Debug 證明獨佔重開與格式，Release 證明不建檔，fixture 確定清理暫存檔。
- 單模型開關仍由 Start-CcgDualModelRun.ps1 進入，集中健康檢查、逾時、清理、重試及產物；只呼叫 Claude，報告不得稱為雙模型成功。預設路徑保留。
- 請檢查繁中 XML 文件、UTF-8 無 BOM、CRLF；不要要求改動產品行為或提前做 P0-02。

只讀取必要的差異與相關測試。請在一次回覆中完成，列出可驗證的錯誤、檔案位置及最小修復建議。


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
