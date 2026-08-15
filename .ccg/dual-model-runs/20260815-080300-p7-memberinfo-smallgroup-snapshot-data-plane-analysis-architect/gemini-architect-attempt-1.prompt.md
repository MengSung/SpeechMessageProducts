ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-memberinfo-smallgroup-snapshot-data-plane-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 MemberInfo small-group snapshot data-plane architecture analysis

請以 architect 角色，只做本機 source/design analysis，不改檔、不呼叫 CRM/CE。

需求：為 ORG-CALL-00031（list descriptors）與 ORG-CALL-00032（listmember/contact memberships）設計一個單一、CE 9.1-only、local-only、fixed composed snapshot operation。現有 immutable `MemberInfoTargetAuthorizationScope` 有 ChurchWide 或最多 512 assigned list IDs；現行 controller path 的 Session/InMemoryContext/ListManager/saved credentials/IOrganizationService/Entity/RetrieveAllEntities 禁止重用。relation-goal ORG-CALL-00033 不在範圍。

計畫方向：Data8 固定 operation 先解析 contact.customertypecode 的唯一「結案」metadata value，固定 query descriptors，再以同次 validated descriptor IDs 查 listmember/contact memberships，回傳 immutable bounded response union。ChurchReport internal source 將 validated scope 轉為 ProductClient request；無 Controller/gate/traffic/CE runtime mutation。

請審查並提出：
1. 這個 single composed operation 相較兩個 caller-composed operation 是否更能維持 authorization 與 lifecycle；
2. exact parameter/response semantics、scope/metadata/query/paging/bounds/fault union；
3. 必要的 A/B isolation、cancellation、lease ownership、response subset invariant test；
4. 任何阻止開始 implementation 的 Critical 風險；
5. 禁止的 fallback/partial/cache/legacy/CE claims。

輸出 Traditional Chinese 的 Critical/Warning/Info。不得建議 controller cutover、feature enablement、CE request/mutation、P7.5/P8 或 ORG-CALL-00033 migration。


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