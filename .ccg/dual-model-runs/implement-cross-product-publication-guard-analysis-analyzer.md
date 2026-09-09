# CCG analyzer Task: implement-cross-product-publication-guard-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
# 雙模型分析請求：ChurchReport 跨產品資料發布與網路時序防護

請以 analyzer 角色，針對目前 repository 的實際程式與下列規格進行唯讀分析：

- `docs/跨產品資料不重複與網路時序防護實作手冊.md`
- `.trellis/spec/backend/duplicate-row-publication-contract.md`
- `.trellis/tasks/09-09-implement-cross-product-publication-guard/prd.md`
- `.trellis/tasks/09-09-implement-cross-product-publication-guard/design.md`
- `.trellis/tasks/09-09-implement-cross-product-publication-guard/implement.md`

重點檢查 ChurchReport 初始週報從 `ListManager`、Session holder、SmallGroup controller API／Razor 到 DevExtreme Grid 的完整資料流。問題是慢速 Wi-Fi 或防火牆環境下偶發同一資料顯示兩次，但資料庫只有一個 `PresentRecordId`。不得假設兩個不同資料庫 ID，也不得按姓名或內容去重。

請輸出：

1. 實際可造成相同 `PresentRecordId` 重複發布或重複渲染的競態／生命週期缺口，附檔案與符號。
2. 哪些現有防線已經有效，避免重複或破壞既有正確設計。
3. 最小且可測試的後端 consumer-boundary guard 設計。
4. 最小且可測試的前端 single-owner、generation token、bounded refresh、dispose 設計；不得建立第二條取數管線。
5. Session Leakage、Memory Leakage、Resource Leakage 風險與確定 cleanup 要求。
6. TDD 測試矩陣，包含同名不同 ID、相同 ID、回應亂序、重複 mount、A/B Session isolation、取消及 resource drain。
7. 對規劃中任何過度設計、相容性風險或錯誤假設提出修正。

請分 Critical／Warning／Info，所有建議必須以 repository 實際證據為依據，不要直接修改檔案。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.