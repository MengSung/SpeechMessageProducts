ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p71-appnamed-list-catalog-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 app-named list catalog typed read：只讀設計分析

請針對下列新 P7.1 child 做只讀架構／安全分析，提出 Critical / Warning / Info 結論。

目標：為權威 matrix 的 `ORG-CALL-00014` 建立 `list.catalog.retrieve.app.named` 之 server-owned、bounded、DTO-only Data8 / ProductClient read capability。legacy 是 `ToolUtility/ListOperations/ListService.RetrieveLists()`；其 static FetchXML 僅讀取 list 的 `listname`、`createdfromcode`、`lastusedon`、`purpose`、`listid`，並固定 `statuscode=0`、`purpose=小組名單`、`new_app_named=1`，按 listname descending 排序。

已證明：

- `ORG-CALL-00014` 與 `ORG-CALL-00065` 是相近但不同 operation/template，不能合併；本 child 只處理 00014。
- 目前 repository 沒有 ToolUtility 外部對 `RetrieveLists()` 的呼叫；因此不作 ChurchReport consumer cutover、不建立 feature gate、不做 CE request。
- `ORG-CALL-00065` 的現有 consumer 使用共享 `EntityCollection` memory cache，另屬未來 P7.4 隔離與 DTO cutover，不在本 child。
- P7.2 Slice C historical cycle 是 write-not-committed 且 cleanup 完成，完全不碰。
- P7.5 deterministic no-go；P8 未建立。

擬定合約：無 caller parameter、固定 operation ID/template、最多 4 page / 64 KiB per page / 256 KiB cumulative / 4096 result items；封閉 ListCatalogRecord DTO 僅傳遞 list ID、名稱、created-from option value、last used UTC time、purpose string。Data8 使用固定 `QueryExpression`、單次或有限 paged `RetrieveMultiple`，不返回 Entity/EntityCollection。ProductClient 接受 deployment-owned profile/workload/cancellation，驗證 operation ID + dedicated branch，將 records 防禦性複製為 request-local DTO；不 cache、retry、fallback 或 rehydrate Entity。

請檢查：資料類型／排序、ListId 必填、null/超限/錯 branch/cancel/fault、A/B profile isolation、shared cache 避免、CE/P7.5/P8 evidence claims。不得建議任何 CE、feature enablement、traffic switch、P7.5 或 P8 action。


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