# CCG architect Task: p111-schema-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀評估P1-11結構部署設計與風險，不修改不spawn。任務.ccg/tasks/p111-schema-deployment，讀requirements.md/plan.md、正式02 P1-11及90附錄A/G、既有Deployment CLI/engine/connector。採現有IOrganizationService單lease/寫鎖與--schema路由，新增完整schema/metadata detached projection/strict JSON/hash/只新增引擎。衝突時不Update/Delete既有欄、DateTime UserLocal存UTC、encrypted欄位安全不改角色；只goodshepherdback、Secret不輸出、plan/export/verify只讀、apply前before snapshot需flush、hash/實際orgid/fingerprint重驗、alternate key需Active才通過。Account五語系+LINE lookup屬P1，sinopacprofile phase4不apply；offeringcategory舊業務lookup等P8。完整export結構含fields/filters/steps但不讀任何業務記錄或設定本文。新schema模式不需Plugin DLL。原P0 62案保留。Hyper-V只讀管理權限不足，未確認隔離/備份/復原，沒有寫入。輸出具體Critical/High/Warning/Info及SDK相容性風險，不把離線測試冒稱實機。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
