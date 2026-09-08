## Persistent ASP.NET Core Engineering Requirements

- Work as a senior ASP.NET Core performance optimization expert with more than
  18 years of experience. Base technical decisions on current Microsoft
  official documentation and the latest applicable .NET version, including
  .NET 10.
- New or modified code must prevent Session Leakage, Memory Leakage, and
  other Resource Leakage. Treat any credible or reproducible leakage as a
  release blocker.
- Optimize for maximum safe sustained performance. Performance improvements
  must never weaken isolation, correctness, deterministic resource cleanup,
  verification, maintainability, or security.

## Persistent Traditional Chinese Documentation and UTF-8 Requirements

- At the beginning of every task, read this complete `AGENTS.md` before
  inspecting, generating, or modifying any `.cs` or `.cshtml` file. Treat these
  documentation, encoding, isolation, lifecycle, and performance requirements
  as release-blocking constraints throughout the task.
- Every `.cs` and `.cshtml` file must contain complete, in-depth, maintainable
  Traditional Chinese documentation appropriate to the code it owns. This rule
  applies immediately to every newly created or substantively modified file and
  to every changed region in an existing file.
- Public and internal types, interfaces, constructors, methods, important
  properties, Razor handlers/helpers, and lifecycle-owning test doubles must use
  the language-appropriate documentation form, including C# XML documentation
  comments and Razor comments where applicable. A translated symbol name,
  one-line restatement, or `<inheritdoc />` alone is not sufficient.
- Non-obvious implementation comments must explain why the implementation is
  required and which invariants must not be broken. When applicable, document
  trust boundaries, input validation, the single resource owner, concurrency and
  race behavior, cancellation and timeout behavior, fail-closed behavior,
  rollback/drain/dispose/cleanup order, performance trade-offs, and memory
  retention limits.
- Code involving Session, identity, token, credential, cache, connection pool,
  queue, timer, subscription, stream, handle, cancellation registration, process,
  IPC, or background work must document the maximum data/resource lifetime, the
  deterministic release path, and how cross-request, cross-user, cross-profile,
  and cross-tenant leakage is prevented.
- Test documentation must state the protected contract, the failure or fault
  injection used, and the decisive assertions, so a future maintainer can tell
  which security, isolation, lifecycle, performance, or compatibility guarantee
  failed.
- All `.cs` and `.cshtml` files must be UTF-8 without BOM, use repository-required
  CRLF line endings, and end with a final CRLF. Invalid UTF-8, BOM, mixed/LF-only
  endings, mojibake, or materially misleading/out-of-date comments are
  review/release blockers. Verify encoding and line endings at byte level before
  reporting completion.

## Permanent Cross-User Isolation and Sustainable Performance Rule

This rule applies to every current and future product line, deployment mode,
service, UI, background worker, test harness, and development tool in this
repository. It is not limited to Dynamics or ChurchReport.

- A request, session, or login for subject A must never reveal, reuse, mutate,
  cache, log, or otherwise retain data, identity, authorization, credentials,
  connection state, token, cookie, response, error detail, or mutable state
  belonging to subject B. Cross-user, cross-tenant, cross-profile, and
  cross-product leakage is a zero-tolerance release blocker.
- Authentication and authorization scope must be derived and validated on the
  server before data access. Caller-provided routing, tenant, identity,
  credential, or profile values are never authority.
- Any cache, pool, queue, singleton, static, background task, retry state, or
  diagnostic buffer must either contain no user-/tenant-/profile-specific data
  or be explicitly partitioned by the complete validated isolation boundary,
  bounded in size and lifetime, and deterministically cleared or disposed.
- Reusable connections and clients may be pooled only when they carry no
  request/user session state and their ownership, fault eviction, drain, and
  disposal paths are proven. A timeout, cancellation, or uncertain transport
  state must not be reused by another request.
- Isolation always wins over throughput. Small, predictable validation,
  partitioning, and cleanup costs are acceptable. Do not introduce a large
  performance regression, such as an unbounded scan, global serialization, or
  a fresh expensive runtime per normal request, merely as a substitute for a
  correct isolation design.
- Before release, focused concurrent A/B isolation tests and lifecycle/soak
  tests must prove that responses, caches, leases, permits, temporary data, and
  resource counters return to their declared safe baseline. See
  `.trellis/spec/backend/cross-user-isolation-and-performance.md` for the
  executable contract and `.trellis/spec/guides/cross-user-isolation-and-performance-review.md`
  for the mandatory review checklist.

## Permanent Duplicate-Row Prevention and Publication Rule

This rule applies permanently to every current and future product line added to
this Solution, including ChurchReport, procurement-association products,
construction-company products, and any later website, API, worker, integration,
test harness, or development tool. It covers every server-rendered table, JSON
collection, API result, grid, tree, report, export, cache projection, background
result, and future UI data source in this repository. Product-specific code,
deployment mode, tenant count, or UI framework never exempts a component from
these requirements.

- Every repeatable row must have a stable, server-owned identity derived from
  the authoritative record or business event. Display text such as name,
  telephone, label, list position, or a client-supplied index is not identity.
- Legitimate records that share the same display name must remain separate when
  their stable identities differ. Never use `Distinct`, `GroupBy`, dictionary
  overwrite, or client-side filtering by display name to hide duplicates.
- Before publication, validate stable identities within the exact collection
  consumed by each UI component. A repeated non-empty identity is a data or
  assembly conflict and must fail closed with a diagnosable error; it must not
  be silently dropped, merged, or handed to a UI library with duplicate keys.
- Build mutable results in an operation-local candidate. Publish only after all
  I/O, mapping, authorization, scope checks, and identity validation succeed.
  Readers may observe the previous complete snapshot or the next complete
  snapshot, never an object being populated in place.
- Shared snapshots must be partitioned by the complete validated isolation
  boundary, including the applicable user/contact, tenant/organization,
  authorization role, list/report/business scope, date/version/generation, and
  authentication epoch. A cache hit never replaces server-side authorization.
- Do not expose mutable Session/cache collections directly to serializers,
  grid loaders, background jobs, or callbacks. Return detached immutable values
  or deep-enough request-owned copies, with bounded lifetime and no references
  to Session, HttpContext, credentials, CRM clients, connections, or disposables.
- All writers for the same business event must use one server-generated
  idempotency/alternate key and atomic create-or-update semantics. Application
  locks are not a substitute for database/Dataverse uniqueness across
  processes. Existing conflicts must stop further creation and enter an
  auditable remediation path.
- Every affected feature must add tests for: legitimate same-name records,
  exact duplicate stable identities, concurrent same-scope publication,
  scope/date/user changes, loader failure and retry, caller mutation isolation,
  and resource drain. A known duplicate-key, cross-session, partial-publication,
  or retained-resource failure is a release blocker.
- Duplicate or missing stable row identities, cross-session/cross-user data,
  unbounded retained memory, and unreleased tasks, timers, subscriptions,
  cancellation registrations, streams, handles, connections, leases, or other
  resources are release-blocking defects in every product. They must be fixed
  and verified before delivery; they cannot be deferred as cosmetic UI cleanup
  or accepted in exchange for throughput.

The executable implementation and review contract is
`.trellis/spec/backend/duplicate-row-publication-contract.md`.

<!-- TRELLIS:START -->
# Trellis Instructions

These instructions are for AI assistants working in this project.

This project is managed by Trellis. The working knowledge you need lives under `.trellis/`:

- `.trellis/workflow.md` — development phases, when to create tasks, skill routing
- `.trellis/spec/` — package- and layer-scoped coding guidelines (read before writing code in a given layer)
- `.trellis/workspace/` — per-developer journals and session traces
- `.trellis/tasks/` — active and archived tasks (PRDs, research, jsonl context)

If a Trellis command is available on your platform (e.g. `/trellis:finish-work`, `/trellis:continue`), prefer it over manual steps. Not every platform exposes every command.

If you're using Codex or another agent-capable tool, additional project-scoped helpers may live in:
- `.agents/skills/` — reusable Trellis skills
- `.codex/agents/` — optional custom subagents

Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.

<!-- TRELLIS:END -->

<!-- CCG-SELF-HEALING:START -->
# CCG Gemini + Claude Self-Healing Rule

When a task requires external CCG analysis or review, do not call Gemini, Claude,
or `codeagent-wrapper` directly. Always start from the project auto-recovery
entrypoint below. It creates the UTF-8 task prompt, delegates to the self-healing
runner, records artifacts, retries repairable failures, and returns a structured
summary.

If a direct Gemini / Claude / `codeagent-wrapper` call was attempted and failed,
immediately stop manual debugging and re-run the same analysis or review through
the auto-recovery entrypoint below.

Use the project runner:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "<short-task-name>" `
  -PromptFile ".\.ccg\dual-model-runs\<task>-review-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

`Start-CcgDualModelRun.ps1` calls `Invoke-CcgDualModelWithSelfHealing.ps1`.
The delegated runner performs the health check, repairs local PATH/env issues,
retries repairable failures, records all prompts/stdout/stderr/summary files,
and distinguishes local failures from provider quota or session-limit blockers.
The project owner has approved using `-AllowSingleModelWhenQuotaBlocked` for
provider quota/session fallback when at least one backend completed with usable
output. Treat this as a degraded result, not as full dual-model success. Never
report a quota-blocked run as a successful dual-model review, and never ignore a
Critical finding from the backend that did complete.

Required recovery behavior:

1. Put the analysis/review request into UTF-8 text, preferably a prompt file
   under `.ccg/dual-model-runs/`.
2. Invoke `Start-CcgDualModelRun.ps1` with the correct `-Role`.
3. If the runner exits with `ok=true`, continue the task using both model outputs.
4. If the runner exits with code `2`, inspect the generated run folder, fix the
   local toolchain issue, then run the same entrypoint again instead of switching
   to ad-hoc Gemini/Claude commands.
5. If the runner exits with `quotaBlocked=true`, treat it as provider
   quota/session state, not a local repair failure. Continue only when
   `degradedFallback=true` and at least one backend completed with usable output;
   report that state as single-model fallback, not completed dual-model review.
   If no backend completed, use local verification only and retry external review
   later.
<!-- CCG-SELF-HEALING:END -->
