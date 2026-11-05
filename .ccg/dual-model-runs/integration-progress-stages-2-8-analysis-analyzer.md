# CCG analyzer Task: integration-progress-stages-2-8-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# 工作

請只讀分析，不要修改任何檔案。對照正式實作計畫 3.3 的 `00_總覽與執行規則.md`、`03_階段2-4_共用送訊_Webhook_金流.md`、`04_階段5-8_Plugin重建_切換_收尾.md`，以及目前的 `實作進度.html` 與 `tools/RepoQuality/Tests/Test-ProgressPage.cjs`，審核將階段 2～8 的工作包完整加入進度頁的資料拆解。

要求：

1. 確認工作包識別碼與數量完整且無重複：階段 2 為 P2-01～P2-12（12 包）、階段 3 為 P3-01～P3-09（9 包）、階段 4 為 P4-01～P4-06（6 包）、階段 5 為 P5-01～P5-13（13 包）、階段 6 為 P6-01～P6-06（6 包）、階段 7 為 P7-01～P7-02（2 包）、階段 8 為 P8-01～P8-05（5 包），合計 53 包。頁面階段總數及全計畫總數須與 P0 18、P1 12、P2～P8 53 相符，即 83 包。
2. 逐包核對頁面名稱、實作範圍、計畫指定測試／驗收證據、明確依賴與外部前置，不得只複製標題或自行增添計畫沒有的承諾。
3. 特別審核安全與停止界線是否保留：P2-04 沙箱 LINE 測試 channel／使用者本人測試帳號與測試憑證前置、禁止記錄秘密；P3-04 必須由使用者提供平台 SQL 並禁止正式記憶體去重；P3-09 測試站與 LINE Developers 使用者操作；P5-02 不修改舊方案來源，P5-03／各模組 goodshepherdback 的 apply／switch／smoke 由使用者執行；P7 禁止 Codex 連正式組織且正式 apply／switch 由使用者審核操作；P8-01 保留同名不同 ID、拒絕重複 ID，不合併名稱；P8-02 遇到以中文文字作比對的既有資料要停止；P8-03 無可靠管理員授權時停止且不信任請求端教會 ID；P8-04 不保存帳密且需人工登入；P8-05 依 v3.3 已完成的 Word 修正，不虛構待辦。
4. 所有階段 2～8 工作包狀態必須是「未開始」，不得影響 P1 的 2/12 已驗收、P0 的 18/18、全計畫已驗收 20/83 與目前階段 1 的統計。
5. 指出進度頁中現有「77 個工作包」是否必須更正；並核實 `Test-ProgressPage.cjs` 的過時 P1-03 警示斷言應以何種精確條件取代，同時不能移除 P1-03 獨立驗收報告斷言。

輸出分 Critical、Warning、Info。每項必須指出計畫章節或工作包編號及具體依據；若無問題，明確列出已核對的階段與包數。此為規劃追蹤頁的完整性分析，不是對未實作工作包的驗收。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.