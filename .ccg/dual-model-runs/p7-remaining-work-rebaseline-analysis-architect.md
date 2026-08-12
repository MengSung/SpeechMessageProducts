# CCG architect Task: p7-remaining-work-rebaseline-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 remaining capability rebaseline — architecture analysis

## Role

Analyze this high-risk Dynamics Gateway migration planning task. Return a concise, evidence-based report under `Critical`, `Warning`, `Info`, and `Recommended matrix invariants`. Do not propose generic CRM CRUD, direct ChurchReport SDK use, request-time fallback, or a retry of historical CE cycles.

## Current evidence

- P3–P6 are complete; Official Worker live compatibility remains evidence-pending but does not block the Data8-first route.
- Archived P7.0 contains a 70-call-site coverage matrix. It explicitly separates registry declaration, executor implementation, consumer enablement, and CE evidence.
- Archived P7.1 implements six Package01 Data8 typed reads with sanitized CE 9.1 read-only `go`; its product flag remains false.
- Archived P7.2 has Slice C CE `write-not-committed` no-go with exact cleanup, which is permanently closed. D–H only have local-only reducers/plans and executor/consumer false.
- Current source includes Package02 registry, Data8 executor, ProductClients, disabled ChurchReport flags, and remaining ToolUtility/CRM SDK call sites.
- The requested child must build an authoritative 70-row static gap matrix and validator before any remaining capability work. It must expose per row: registry, Data8 executor, typed ProductClient, consumer migration, CE 8.2/9.1, Embedded/Dedicated, rollout/rollback owner, temporary legacy, P7.3 resource need, P7.5 removal blocker.

## Security and lifecycle constraints

- Static analysis only: no CE, browser, credentials, network, feature flags, traffic, Official Worker, or cloud deployment.
- Do not treat local-only contracts, disabled gates, registry entries, or passing tests as CE/consumer success.
- Caller never controls owner, endpoint, credential, connector, organization, or profile; preserve profile/generation isolation and deterministic resource ownership.
- P7.4 cutover must be per capability, disabled by default, no request-time fallback, and require capacity/non-overlap plus rollback evidence.
- P7.5 removal requires all production migration and zero-reference evidence; P8 starts only after immutable P7.5 handoff.

## Output

Identify schema or sequencing defects that could produce false completion, unsafe ToolUtility removal, session/resource leakage, unsupported CE claims, or an unsafe P8 start. State concrete matrix invariants and safe next child boundaries.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
