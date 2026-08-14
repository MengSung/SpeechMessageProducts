ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-memberinfo-smallgroup-tree-authorization-audit-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 MemberInfo 小組樹授權來源稽核：架構分析

## 範圍

只審查 authoritative matrix 的 ORG-CALL-00031
`memberinfo.smallgroup.retrieve.descriptors` 與 ORG-CALL-00032
`memberinfo.smallgroup.retrieve.memberships` 是否能安全進入新的 Gateway local implementation。

目前來源顯示：
- LoadDistrictTree、SearchDistrictTree、LoadGroupMembers 都先 EnsureCorrectUserData()，再由 GetAccess() 從
  Session `_MemberInfoAccess` 或 InMemoryContext.PersonalInfomationModel / ListManager 推導授權。
- Church branch 以固定 SDK QueryExpression 讀取 app-named 小組 descriptor。
- Shepherd branch GetShepherdListIds() 會 EnsureShepherdListsLoaded()，然後從 InMemoryContext.ListManager
  取得 list assignment；如果未載入，EnsureShepherdListsLoaded() 以 ListManager 保存的帳密呼叫 SetupListManager。
- 後續 descriptor/membership 仍使用 IOrganizationService、Entity、QueryExpression；LoadGroupMembers 的 browser
  listId allowlist 由上述 legacy state 建立。

## 約束

- 此 child 僅可修改 task/CCG 文件；不得改 runtime、matrix、feature gate、CE、fixture、traffic、P7.5 或 P8。
- 不得把 Session、InMemoryContext、ListManager、保存帳密、browser listId、raw CRM SDK object/query 或 caller profile
  作為 Gateway authorization/routing authority。
- 不得只實作 Church branch 而聲稱既有 MemberInfo 小組樹 consumer 已遷移。
- 若無法證明 scope 在 Session/cache/client/CRM I/O 前 server-derived、immutable、request-local，結論必須為
  source-only local design no-go，並列出最小恢復條件。

## 請輸出

以 Critical / Warning / Info 分級，判定：
1. Church 與 Shepherd 是否可安全共用或拆成 capability；
2. 目前 source 是否有 cross-user/profile/credential/authorization 或 resource lifecycle blocker；
3. 是否應建立 runtime child，或應維持 source-only no-go；
4. 若 no-go，提供精確、最小的恢復條件。

只依據上述 source facts 與 repository safety contracts；不要建議 feature enablement、CE、P7.5 或 P8。


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