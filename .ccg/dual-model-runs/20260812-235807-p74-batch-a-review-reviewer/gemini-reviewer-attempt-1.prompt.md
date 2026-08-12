ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-batch-a-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 Batch A review: atomic Package01 fee projection

Review the current uncommitted diff for P7.4 Batch A.

The production change is limited to `DonationFeeQueryService`:

- typed Package01 DTO mapping and amount calculation now complete in request-local locals;
- the existing `DonationPaymentFormModel` is changed only after the entire mapping succeeds;
- a regression test reproduces a malformed typed DTO and proves no partial model mutation.

Verify correctness, null/fault behavior, overflow behavior, async/cancellation semantics,
cross-request isolation, resource ownership, documentation, and scope. Check that no feature
gate enablement, CE request, traffic switch, ToolUtility removal, P7.5, or P8 work was added.

Output Critical / Warning / Info findings with exact paths and line numbers. Treat only actual
code evidence as a finding; do not demand CE activation or SDK Entity compatibility for this
disabled local-only read batch.


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
