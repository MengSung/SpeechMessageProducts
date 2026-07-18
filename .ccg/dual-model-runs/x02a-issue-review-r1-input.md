# X02A Issue Review R1

Role: reviewer
Repository: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
Workspace: `docs/project-modular-diagnostics/X02A-shared-cache-foundation/`
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Hard Constraints

You must only review. Do not modify files.

Prohibited actions:

- Do not run `dotnet restore`, `dotnet build`, `dotnet test`, `npm install`, `npm test`, package restore, or any equivalent restore/build/test command.
- Do not run code generation, formatting, migrations, package updates, or lockfile updates.
- Do not write generated files, `bin/**`, `obj/**`, caches, lockfiles, product code, project files, configs, tests, or ledger files.
- Do not write outside `docs/project-modular-diagnostics/X02A-shared-cache-foundation/**` or x02a-prefixed files under `.ccg/dual-model-runs/**`.
- Do not spawn agents or ask another agent/model to review.

If you need evidence, read files only. Prefer the diagnostic packet first, then source files named by the packet.

## Scope

X02A scope: shared cache foundation, cache interface/implementation, cache key, capacity/expiry base rules.

Explicit exclusions:

- Group-specific cache policy except as dependency/consumer context.
- Logging provider/observability except as dependency/consumer context.
- Profiling except as dependency/consumer context.

Primary owner files:

- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/ICacheService.cs`

Excluded context files:

- `SpeechMessageProducts.ChurchReport/Services/Caching/ISmallGroupCacheManager.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/SmallGroupCacheManager.cs`
- Logging/profiling implementation files.

## Review Inputs

Read and review:

- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/issue.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/review-log.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/evidence/scope-manifest.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/evidence/security-analysis.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/evidence/performance-analysis.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/evidence/extraction-analysis.md`
- `docs/project-modular-diagnostics/X02A-shared-cache-foundation/evidence/runtime-validation-plan.md`

Source evidence may be checked read-only in:

- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/ICacheService.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs`

## Issues To Review

1. `X02A-PERF-001` Cache has expiry but no hard capacity baseline.
2. `X02A-SEC-001` Raw identity-bearing cache keys are logged.
3. `X02A-EXT-001` Shared `CacheKeys` mixes reusable primitives with business/group-specific key policy.
4. `X02A-PERF-002` Async cache misses can stampede per key.

## Required Output

Return a concise reviewer report with:

- Overall verdict: `KEEP`, `REWRITE`, `DELETE`, or `NEEDS_RUNTIME_VALIDATION`.
- Per-issue verdict for each listed issue.
- Critical/Warning/Info findings.
- Any ownership boundary corrections.
- Any evidence that is overstated, missing, or outside X02A scope.
- Whether the packet is eligible for `APPROVED`, `APPROVED_DEGRADED`, `RUNTIME_VALIDATION_PENDING`, or `DEGRADED_REVIEW_PENDING`.

Do not recommend direct code changes in this diagnostic workspace. Recommendations must remain diagnostic, validation, or future optimization guidance only.
