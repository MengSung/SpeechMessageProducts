# CCG reviewer Task: p71-appnamed-smallgroups-list-catalog-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ORG-CALL-00065 local capability final review

請只做 repository-source review；不得寫檔、不得執行 CE、不得輸出 credential、endpoint、CRM ID、cookie、token、原始 response 或 raw exception。

審查範圍為 `ORG-CALL-00065` / `list.catalog.retrieve.appnamed.smallgroups` 的 local-only、fixed-template、zero-caller-parameter、bounded DTO-only Data8/ProductClient read capability。

必須驗證：

1. operation/template/response union/wire record 與 `ORG-CALL-00014` 完全分離；固定 query 的七欄 projection、四個 filters、排序、單頁 fail-closed、leader 僅為 nullable GUID。
2. non-empty parameter 必須在 profile-router/connector I/O 前回傳固定 invalid-parameters；paged/cookie/超限/schema/UTC/lookup 不符均不得發布 partial response。
3. ProductClient 只以 server-owned profile/workload 建立 request-local immutable DTO snapshots，沒有 caller selector、cache、retry、fallback、timer、background state、Entity rehydration 或跨 A/B request state。
4. 本 child 不可修改或引用 ChurchReport legacy shared EntityCollection consumer、ToolUtility consumer、feature flag、CE、traffic、P7.5 或 P8。
5. matrix 僅可將 registry/Data8 executor/ProductClient 更新為 local implementation；consumer/CE/host/rollout/rollback/temporary legacy 必須維持 pending。

請輸出 `Critical`、`Warning`、`Info` 分級結果。只列出可由實際 source/diff 證實的問題；沒有問題時請明確寫 `No findings`。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
