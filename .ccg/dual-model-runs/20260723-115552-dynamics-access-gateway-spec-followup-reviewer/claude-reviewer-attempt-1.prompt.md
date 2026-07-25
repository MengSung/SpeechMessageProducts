ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-access-gateway-spec-followup

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# CCG reviewer task: Dynamics Access Gateway architecture SPEC

## Scope

Review the planning artifacts only. Do not modify production code and do not
review unrelated working-tree changes.

Files to review:

- .trellis/tasks/07-23-dynamics-connection-compatibility/prd.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/design.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/implement.md
- docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md

## User objective

Design a new shared Dynamics 365 Organization access solution for five current
products and future products. It must support CE on-premises 8.2 and 9.1 through
direct HTTP/OData v4 Web API, without CRM SDK DLLs or the GitHub-derived
PowerPlatform.Dataverse.Client implementation. The new solution owns
Connection Pool management. The requested final state forbids every solution
project from referencing or using any DLL under:

D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL

Hard quality requirements:

- centralized Gateway Web Service must be justified, not assumed;
- zero-tolerance release gate for session/profile/token/credential/cache
  leakage and memory/resource leakage;
- high performance using safe connection reuse and bounded concurrency;
- JSON named profiles with secrets resolved by reference;
- safe explicit version routing plus validation/detection that never silently
  changes organization/version;
- products must not hold CRM secrets or use an unrestricted CRM proxy;
- no CRM 2011 OrganizationData.svc/OData v2 fallback;
- the migration plan must recognize broad existing Microsoft.Xrm/
  IOrganizationService coupling rather than pretending it is a single DLL swap.

## Review questions

1. Does the proposed Gateway + private no-SDK WebApi library give a technically
   sound answer for five-to-ten products, and are the Library-only and
   transparent-proxy alternatives rejected for concrete reasons?
2. Are HTTP handler/HttpClient, Windows credentials, OAuth token cache,
   metadata cache, retry/circuit state, queue/concurrency state, and reload
   lifecycle isolated by a sufficient immutable profile-generation key?
3. Does the design leave any path for cross-profile routing, secret leakage,
   caller-provided endpoint/header/profile escape, retention leak, stale
   runtime mutation, or unsafe automatic retry?
4. Are the CE 8.2/9.1 API-version and authentication constraints described
   safely, without assuming on-premise client-secret support or WS-Trust
   fallback?
5. Are performance and high-availability claims bounded, testable, and
   compatible with Dynamics service protection?
6. Are migration scope, no-SDK enforcement checks, and test/release gates
   sufficiently concrete?
7. Identify contradictions, missing explicit decisions, or dangerous
   assumptions. Do not request product decisions that can be safely deferred
   behind a stated feasibility gate.

## Required output

Return a concise report with Critical, Warning, and Info findings. Every
Critical/Warning must cite the relevant file/section and recommend a specific
spec correction. If no finding applies, state why the relevant gate is sound.


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