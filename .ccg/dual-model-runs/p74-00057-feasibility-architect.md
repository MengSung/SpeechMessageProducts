# CCG architect Task: p74-00057-feasibility

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00057 bounded-read feasibility analysis

Assess `ORG-CALL-00057` (`list.membership.retrieve.appnamed.by.contact`) as the next independent, local-only P7 capability.

Authoritative inputs:

- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
- `ToolUtility/QueryOperations/RelationshipQueryService.cs`, member `QueryListOfContactManyToMany`
- ChurchReport callers, especially `ContactService.GetContactCurrentGroup` and `DownloadListManager`
- The archived `08-14-p74-contact-current-group-read-boundary` and `08-14-p74-static-list-membership-action-consumer-boundary` tasks.

Constraints:

- Historical P7.2 Slice C is closed and must not be retried.
- No CE, feature-gate, traffic, P7.5, or P8 work is allowed.
- A positive result may only authorize a fresh, disabled-by-default, DTO-only local boundary; it cannot authorize consumer cutover.
- No raw CRM Entity/EntityCollection/IOrganizationService, caller-specified query/profile/endpoint/credential, hidden fallback, shared mutable state, unbounded response, or write-adjacent consumer wiring.

Return a concise report:

1. Is a standalone, server-authorized, bounded DTO-only data-plane child safe to implement now?
2. Exact mandatory input and response cardinality/bounds, including duplicate semantics.
3. Exact prohibited consumers/call paths.
4. Minimum code layers and tests if implementation is safe, or exact recovery conditions if no-go.
5. Critical/Warning/Info findings only.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.