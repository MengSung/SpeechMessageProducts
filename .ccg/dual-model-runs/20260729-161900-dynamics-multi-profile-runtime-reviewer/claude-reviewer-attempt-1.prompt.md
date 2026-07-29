ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-multi-profile-runtime

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review request: Dynamics Multi-Profile Runtime and Gateway readiness

Review the current uncommitted git diff in this repository. This is a high-risk integration/lifecycle change for Local and Central Dynamics Gateway, not a request to redesign unrelated future phases.

## Intended behavior

- Product requests resolve an approved alias before secret, factory, token, admission, or transport work.
- Queue wait retains no runtime/client/handler/token-provider/generation strong reference.
- Admission permit is obtained first; only after dequeue does the manager resolve the current active runtime generation.
- The resolved runtime must still match admission-manager identity, canonical organization key, and configuration digest.
- Runtime publication requires host-slot acquisition before Gateway readiness.
- Per alias: at most one Active plus one Draining generation; a third replacement is rejected before factory allocation.
- Initial catalog publication is all-or-nothing and retryable after failure.
- Runtime, factory, registry, combined lease, and shutdown cleanup must attempt every owned resource even if an earlier cleanup fails.
- The original operation failure must not be hidden by cleanup failures.
- crm82 and crm91 must not share mutable client, handler, token, credential, metadata, or session state. They may share only canonical organization admission authority when they target the same physical organization.
- Readiness outputs bounded non-secret profile/admission data only.
- New production/test lifecycle and routing code requires substantive Traditional Chinese documentation and UTF-8 without BOM plus CRLF.

## Recent regression fixes that need close review

1. `DynamicsProfileRuntimeManager.AcquireAsync`: acquisition may throw after creating a runtime lease; runtime-lease disposal may also throw. The permit must still be released and both original/cleanup failures preserved.
2. `DynamicsProfileRuntimeManager.InitializeCoreAsync`: a later profile factory failure plus an earlier candidate cleanup failure must still reset `_ready` and `_initializationTask`, preserve all causes, and allow retry.
3. Initialization now starts with `await Task.Yield()` so synchronously completing factories/test doubles cannot clear `_initializationTask` before `InitializeAsync` publishes task ownership and then have the outer assignment restore the failed task.

## Evidence already available

- Full `SpeechMessage.Dynamics.Tests`: 155 passed, 0 failed, 0 skipped.
- Focused MultiProfile/Registry/Factory/Readiness/Phase4 soak: 32 passed.
- `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.
- Scoped `dotnet format --verify-no-changes`: passed for changed WebApi/Gateway/Tests C# files.
- Strict UTF-8 without BOM and CRLF check: passed for 32 changed text files.
- NuGet vulnerable-package audit for temporary Data8 project: no known vulnerable package reported.

## Review priorities

1. Cross-session/cross-profile/cross-tenant state leakage.
2. Resource leaks or cleanup ordering failures for permits, runtime leases, handlers, token providers, CTS, registrations, host slots, timers, tasks, streams, and strong references.
3. Deadlock, race, task-publication, cancellation, disposal idempotency, and replace/drain correctness.
4. Capacity multiplication across aliases/generations/organizations or mismatch between canonical key and admission namespace.
5. Readiness correctness and sensitive-data exposure.
6. DI lifetime or shared mutable singleton errors.
7. Regression-test validity: identify tautological tests or fakes that fail to model the production ownership path.
8. Any missing substantive Traditional Chinese lifecycle comments in newly added code.

Do not flag future Phase 5/6 work merely because it remains intentionally incomplete. Do flag current-diff correctness, isolation, security, lifecycle, and test defects.

Output a concise report grouped as Critical / Warning / Info. Every Critical or Warning must cite the exact file and line/construct, explain a reproducible failure path, and recommend the smallest safe correction. If no Critical or Warning remains, say PASS explicitly.


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