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

When a task requires external CCG analysis or review, do not call Gemini or Claude directly.

Use the project runner:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" `
  -TaskFile ".\.ccg\dual-model-runs\<task>.md" `
  -Role reviewer `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

The runner performs the health check, repairs local PATH/env issues, retries repairable failures,
records all prompts/stdout/stderr/summary files, and distinguishes local failures from provider
quota or session-limit blockers. If `quotaBlocked=true`, report it as an external blocker or use
`-AllowSingleModelWhenQuotaBlocked` only when the task explicitly permits a single-model fallback.
Never report a quota-blocked run as a successful dual-model review.
<!-- CCG-SELF-HEALING:END -->
