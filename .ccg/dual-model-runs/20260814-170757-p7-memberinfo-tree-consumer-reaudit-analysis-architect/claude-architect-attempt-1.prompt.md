ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p7-memberinfo-tree-consumer-reaudit-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 MemberInfo tree consumer authorization re-audit

請以 architect/analyzer 角色，僅針對目前工作區的 planning artifacts 與 source evidence 進行交叉分析，不修改任何檔案，不執行 CRM/CE 操作。

目標：重新稽核 authoritative matrix 的 ORG-CALL-00031（small-group descriptors）、00032（small-group memberships）、00033（relation goals），判定是否能建立下一個獨立的 server-authorized、request-local、bounded、immutable DTO-only data-plane child。

已知前置條件：已封存的 P7 memberinfo assignment-source child 提供固定 server-owned Church-wide／assigned-list immutable evidence，但尚未接線 controller。現有 MemberInfo legacy graph 會使用 Session、InMemoryContext、ListManager、保存帳密、IOrganizationService/Entity 與 browser locator，因此這些不可成為新邊界或 fallback。Slice C 已歷史性 closed/no-go，不能重試；本分析不涉及 CE、fixture、feature flag、traffic、P7.5 或 P8。

請逐 row 輸出：
1. matrix contract 與實際 call chain 的一致性；
2. 授權 trust boundary 是否完整；
3. fixed query/projection、輸出 boundedness、取消、fault union、resource owner、A/B isolation 與 rollback 要求；
4. 建議「可建立 implementation child／需前置條件／no-go」其中一項；
5. 若建議 child，列出嚴格不得做的事情與最小本機驗證集合。

不得把 assignment evidence 等同於 relation-goal target authorization；不得建議掃描 CRM、猜選 owner、caller 指定 list/contact、request-time legacy fallback 或切換現有 consumer。若外部模型無法完成，回報可由本機 source evidence 驗證的部分與遺漏。


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