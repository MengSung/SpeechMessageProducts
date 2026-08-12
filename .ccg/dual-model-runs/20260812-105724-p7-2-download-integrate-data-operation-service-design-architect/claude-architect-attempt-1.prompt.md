ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p7-2-download-integrate-data-operation-service-design

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# DownloadIntegrateData operation-local CRM service design review

請只做唯讀架構分析，不得修改檔案、執行 CE、讀取任何帳密或輸出 CRM ID、endpoint、token、cookie、原始外部例外。

背景：P7.4／P7.5 必須防止 ChurchReport session-cached `ListManager`、其持有的
`DownloadIntegrateData` 或 Factory singleton `ToolUtility` 跨 request／user／profile 保留
`IOrganizationService`。目前 `ListManager.SetupIntegrateData` 呼叫
`DownloadIntegrateData.SetupIntegrateData`，後者多個 partial flow 直接使用
`m_ToolUtilityClass.m_Crm2011OrganizationService` 或 `m_OrganizationService`，且
`DownloadIntegrateData` 由 ListManager 欄位持有。

已完成：`DownloadListManager` 的 public operation parameter forwarding；動態名單 façade
也已證明不以共享 service 取代呼叫端 service。

請從下列面向提出最小、安全且可逐步驗證的設計，包含檔案與方法群組：

1. public `ListManager.SetupIntegrateData` 到 `DownloadIntegrateData` 的呼叫鏈，如何接收並只以
   method parameter 向下傳遞借用 `IOrganizationService`；不可存於任何 instance/static/cache/
   Factory field，不可改變其 lease／Dispose owner。
2. 如何處理 `DownloadIntegrateData` partial 中直接讀取 ToolUtility service fields 的 list query、
   update、IdentityConverter／metadata cache 與一般 CRUD helper，不要以「暫存欄位」或
   `AsyncLocal` 繞過隔離。
3. 哪些 existing ToolUtility overload 可安全重用、哪些需新增 explicit-service overload；如何避免
   `OrganizationServiceProxy` / generic `IOrganizationService` 類型錯置。
4. 建議的 TDD order：至少 interleaved A/B fake service、exception/fault 後 B 不重用 A、
   cached ListManager 不保留 service reference、以及不由 DownloadIntegrateData Dispose 呼叫端
   service。列出可測 seam。
5. 列出不應該做的快速修正與任何明確的 P7.4/P7.5 blocker。

輸出繁體中文。只回報可由程式碼與上述契約支持的結論，按 Critical / Warning / Info 分類。


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