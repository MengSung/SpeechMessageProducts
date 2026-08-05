# CCG analyzer Task: harden-churchreport-error-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ChurchReport 錯誤復原與 CRM 服務生命週期分析

請審查下列已驗證的程式行為，提出最小且安全的修正範圍與必須具備的回歸測試。不得假設未提供的事實。

## 已觀察到的證據

1. `BaseChurchController.HandleError` 在非 AJAX 分支直接存取 `TempData["ErrorMessage"]`；當 Controller 沒有可用 TempData / HTTP context 時，這會以第二個 `NullReferenceException` 或上下文例外遮蔽原始 CRM 錯誤。
2. AJAX 分支直接將 `exception.Message` 回傳給瀏覽器；非 AJAX 分支也把原始訊息放進 TempData，可能洩漏內部資訊。
3. `ToolUtilityFactory.GetInstance()` 保存靜態 singleton `_instance`；`BaseChurchController.Dispose()` 卻呼叫 `ToolUtility?.Dispose()`。Factory 沒有在每個 Controller 結束後重建 singleton，因此後續請求可能重取已 Dispose 的 CRM client。
4. 登入 `SetupSystemData` 從 `ICrmConnectionPool` 借用 `IOrganizationService`，傳入 `ListManager.SetupListManager` / `DownloadListManager.GetListManager`，最後在 `finally` 歸還。
5. `DownloadListManager.GetListManager` 若傳入 service 且 `m_ToolUtilityClass.m_Crm2011OrganizationService` 為 null，會將該傳入 service 寫入共用的 `ToolUtilityClass` 欄位。之後同檔案改從這些欄位取 service。這是將短生命週期 lease 洩漏至長生命週期 shared object 的風險。
6. `HomeController.DisplayErrorView` 直接讀取 `TempData["ErrorMessage"]`。

## 限制

- 不接觸外部 CE / Web API / SQL / IIS / DNS / ADFS。
- 不可引入可跨使用者、跨組織或跨請求保留的可變 CRM service。
- 不可將原始 exception message、credential、token 或 connection detail 回傳瀏覽器。
- 所有修改過的 C# 檔都要 UTF-8 no BOM、CRLF、末尾 CRLF，並使用深入繁中註解。
- 請區分「已證實根因」與「需要另外驗證的風險」，避免過度重構。

## 輸出

請提供：
1. Critical / Warning / Info 分級；
2. 最小修正建議；
3. 要先寫且應先失敗的 xUnit 回歸測試清單；
4. 任何應明確拒絕的建議。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.