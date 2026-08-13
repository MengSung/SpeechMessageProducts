# CCG reviewer Task: p74-memberinfo-present-read-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00026 final local-only code review

Review the current uncommitted diff for the independently gated, local-only migration of
`memberinfo.present.retrieve.by.contact` / `ORG-CALL-00026`.

Required security/correctness properties:
- checked-in base and sub-gates remain false; no CE, traffic, P7.5, P8, push, or PR operation;
- false gate retains legacy ToolUtility route; true path is server-authorized before contact dispatch;
- no browser-owned profile/workload/owner/endpoint/connector authority;
- true path must be DTO-only: no ToolUtility, Entity, QueryExpression, IOrganizationService, fallback, retry, or swallowed cancellation;
- Data8 query must be fixed, CE 9.1, one page only, bounded, and fail closed on MoreRecords/schema/type/duplicate/byte limit errors;
- all response collections must be defensive request-local snapshots; no cross-user/profile or resource leakage;
- contact FullName must keep legacy row semantics via same fixed query, not a second CRM Retrieve.

Run only static/code review. Report Critical, Warning, Info with concrete file/line evidence. Do not suggest CE operations, enabling a gate, traffic switches, P7.5 removal, or P8 work.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.