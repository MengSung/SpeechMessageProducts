# CCG analyzer Task: p7-2-continuation-root-cause

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 continuation：Slice C 根因與跨使用者隔離分析

請僅分析下列本機程式與任務事實，不要執行 CE、修改檔案、要求或輸出任何 credential、endpoint、CRM ID、名稱或原始例外。

## 已知事實

- 舊 P7.2 Slice C cycle 已歸檔為 `unreleasable-fail-closed`：child 回報 `live-evidence-incomplete` 後 assertion 失敗，parent 僅見 child non-zero；舊 cycle 不得重試。
- `DownloadListManager.GetListManager` 接收 `IOrganizationService`，並可能寫入 factory 取得的 `m_ToolUtilityClass.m_Crm2011OrganizationService` 或 `m_OrganizationService`。
- `DownloadListManager` 與 `ListManager` 有 `throw e;`，可能重設 stack trace。
- 新任務需先通過本機 TDD、隔離／生命週期與 Release build，才可嘗試一次新的 CE cycle。

## 請審查

1. 這個 service 寫回是否構成跨 request／profile 泄漏風險，以及最低風險的 operation-local 介面修正方式。
2. 對 child-to-parent 受控診斷，允許哪些固定分類欄位，才能說明 no-go 又不洩漏 CRM 細節。
3. 必須先寫的最小 TDD 測試與 timeout／Dispose／exception stack 的回歸測試。
4. Slice D–H 本機 capability 可以如何與 CE evidence gate 分離，確保不會提早切流或移除 ToolUtility。

輸出以繁體中文，分 Critical／Warning／Info，逐點給出可驗證的結論與測試建議。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.