ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-static-list-membership-action-consumer-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 static-list membership action consumer boundary architecture review

Review this repository-local planning result only. The immutable capability matrix says
`list.members.add.many` and `list.members.remove.one` already have registry/Data8/ProductClient foundations,
but the ChurchReport consumer remains legacy.

Evidence from `ListManagementDataManager` shows that the calls coexist in the same user workflow with
ToolUtility Entity retrieve/update for contact primary list and attendance-related mutations. Replacing just the
member actions would create a Gateway-write plus ToolUtility-write composite without a unified transaction,
read-back/reconciliation, reverse-order cleanup, or single rollback owner.

Proposed decision: record a P7.4 local consumer-migration no-go; do not modify runtime/configuration/gates/CE;
retain the matrix temporary-legacy row; require a future independently planned whole-composite typed operation
family before retrying migration. Review for correctness, safety, session/profile isolation, resource lifecycle,
false completion, and missing prerequisites. Do not propose CE or deployment operations.

OUTPUT: Critical/Warning/Info findings and a PASS/FAIL recommendation.


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
