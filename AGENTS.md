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
