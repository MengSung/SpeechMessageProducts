# CCG architect Task: generation-owned-data8-connector-pool-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P3 analysis request

Review the current P3 plan for a generation-owned Data8 connector pool. Inspect the repository contracts, especially IOrganizationAdmissionManager, DispatchEnvelope, ResolvedProfile, OnPremiseClient, and existing lifecycle tests. Identify concrete API, dependency, concurrency, disposal, and test-design risks before implementation. The pool must be SDK-free at the abstraction boundary, keyed by (ProfileAlias, GenerationId), reuse the existing organization admission manager, return healthy leases to the original generation, evict faulted/cancelled/expired leases, drain deterministically, and route only from ResolvedProfile.ConnectorKind. Do not propose Web API, IFD, SQL, or D365APP01 diagnostic work. Return Critical/Warning/Info findings with exact file references and recommended corrections.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.