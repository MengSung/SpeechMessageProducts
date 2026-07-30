# CCG reviewer Task: valid-unmapped-sid-selector-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review request: valid unmapped SID and named binding-set authorization boundary

## Role

Act as a security-focused code reviewer. Inspect the current working-tree changes only for the bounded files listed below, while reading adjacent production code when necessary to verify data flow and ownership. Report findings as Critical, Warning, or Info, and finish with PASS or FAIL.

## Bounded review scope

- `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
- `SpeechMessage.Dynamics.Gateway/Security/GatewayWorkloadBinding.cs`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `.ccg/tasks/dynamics-connection-compatibility/requirements.md`
- `.ccg/tasks/dynamics-connection-compatibility/plan.md`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`
- `.ccg/tasks/dynamics-connection-compatibility/task.json`

Do not treat unrelated working-tree changes as part of this increment. Do not modify files.

## Required contract

1. A syntactically valid authenticated Windows SID is authoritative. If it is present, authorization performs only the SID lookup. An unmapped valid SID must return `unmapped-principal` and must not fall back to a matching principal name.
2. Exact principal-name fallback remains allowed only when the authenticated principal has no usable SID.
3. Denial must occur before executor request creation, admission permit acquisition, secret/token resolution, or outbound transport work.
4. `ActiveWorkloadBindingSet` selects exactly one direct child under `WorkloadBindingSets` using exact case-insensitive equality. It must not be concatenated into a configuration path.
5. Missing, blank, leading/trailing-whitespace, `*`, `?`, unknown, delimiter-bearing such as `Local:0`, scalar-only, scalar-plus-children, and true childless JSON sets must fail closed before listener traffic. Case-insensitive exact positive selection must remain valid.
6. Request hot-path behavior remains bounded, lock-free frozen lookup. The change must not add shared mutable identity state, principal caches, timers, background tasks, sockets, subscriptions, cancellation registrations, or cleanup owners.
7. All new or substantively modified Production/Test code must contain complete, deep Traditional Chinese documentation explaining trust boundary, owner, concurrency, fail-closed behavior, cancellation/timeout where applicable, cleanup/drain/dispose ordering, and performance/memory tradeoffs.
8. Files must remain UTF-8 without BOM, CRLF-only, and final CRLF.
9. `Package01FeeReadsEnabled=false` remains unchanged. Embedded, Data8, and `PowerPlatform.Dataverse.Client` remain retained. This increment must not claim that Phase 4, Phase 5, Phase 6, real CE 8.2/9.1, OData projection, cross-process capacity, fault/soak/performance, or SDK removal is complete.

## TDD and local evidence to verify, not merely trust

- RED: before the Production fix, the changed regression expected HTTP 403 but received HTTP 200.
- GREEN: the valid-unmapped-SID denial and the no-SID exact-name compatibility case both passed.
- `GatewayWorkloadBoundaryTests`: 31 passed, 0 failed.
- `SpeechMessage.Dynamics.Tests`: 243 passed, 0 failed, 1 ordinary opt-in live SQL skip.
- `ChurchReport.MemberInfo.Tests`: 367 passed, 0 failed.
- `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.

## Review questions

1. Can any valid but unmapped SID still reach a name binding through another path, including the operation catalog?
2. Does invalid or absent SID handling accidentally broaden identity authority?
3. Are selector tests genuine for each provider shape, especially childless JSON versus scalar-only and scalar-plus-children?
4. Is the production change minimal, deterministic, concurrency-safe, allocation-bounded, and cleanup-neutral?
5. Do tests assert behavior rather than mocks, and can any test pass without exercising the intended security boundary?
6. Are SPEC, Phase evidence, Traditional Chinese explanation, and CCG task state consistent without rewriting historical review results or overclaiming completion?
7. Do comments and encoding satisfy the user's hard requirement?

## Confidentiality

Do not print or persist credentials, tokens, passwords, provider session markers, secret-reference values, actual local identities, actual SIDs, private profile paths, or complete private endpoints. Refer only to synthetic test identifiers or redact values.

## Output

Provide:

1. Critical findings
2. Warning findings
3. Info observations
4. Verification of the nine required contracts
5. Final PASS or FAIL recommendation


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
