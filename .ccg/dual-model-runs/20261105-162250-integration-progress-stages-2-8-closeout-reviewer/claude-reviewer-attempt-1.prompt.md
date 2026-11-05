ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-progress-stages-2-8-closeout

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# 最後增量複核（唯讀）

本任務為把正式計畫 03／04 的 53 個後續工作包加入進度 HTML，全部保持未開始。
前兩輪完整內容審查：
- `20261105-160221-integration-progress-stages-2-8-review-reviewer`
- `20261105-161352-integration-progress-stages-2-8-final-review-reviewer`
均為 `dualModelComplete=true`，沒有已確認 Critical／Warning；請勿重做全方案審查。

這次只複核以下四項增量，目標檔
`文件/規劃設計書/實作計畫-3.3/實作進度.html`：

1. 640px 以下既有 `.wp` 第二欄改 `minmax(0, 1fr)`；
   `.wp > span, ol.timeline li > span` 加 `min-width:0; overflow-wrap:anywhere`；
   `.alert li` 加 `overflow-wrap:anywhere`；時間軸改 `minmax(0,1fr)`。
   真實 Edge 測試原先 320px 頁寬 362→322，修後寬度 320、無水平溢出，
   1440／390／320 三種寬度均通過 Enter／Space 展開收合與 53 todo 包檢查。
   可核對 `artifacts/progress-stages-2-8-browser/result.json`。
2. `P4-05.prerequisite` 現寫「金流回呼依各供應商契約驗簽、驗證重複回呼冪等
   與金額／幣別一致性；不提前停掉 ChurchReport 回呼」；避免與 LINE 簽章／事件
   去重契約混淆。依正式計畫 03 的 P4-05。
3. `P5-01.prerequisite` 現明載影子模式讀取真實服務、寫入／推播只記錄不執行，
   不記秘密或多餘個資，離線測試用合成資料。這修正先前誤把真實影子比對限於
   合成資料的摘要。依正式計畫 04 的 P5-01。
4. 接受上一輪 Claude Info，在 `P5-03.prerequisite` 明載 FileVersion／rollback
   是「階段 5 共同關卡，非 P5-03 專屬驗收」，不變更原規則。

請核對上述增量是否正確、無安全或需求回歸。頁面8/8測試與原數量／狀態保持通過。
禁止修改檔案、禁止連 D365、禁止讀秘密。只輸出最多300字繁體中文，
列 Critical／Warning／Info；無問題就明確寫無，勿建議發布或展開新範圍。


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