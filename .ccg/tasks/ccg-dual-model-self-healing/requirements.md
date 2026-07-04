# CCG Dual Model Self-Healing

## Requirement
When CCG dual-model analysis or review fails, the workflow must not stop immediately. It should run a stable health check, repair known local environment issues when possible, then retry Gemini and Claude through the same runner.

## Scope
- Keep reusable scripts under `docs/scripts`.
- Make `/ccg:analyze` and `/ccg:review` point to the self-healing runner.
- Preserve run artifacts under `.ccg/dual-model-runs` for debugging.
- Treat provider quota/session-limit failures as non-repairable external state and report them clearly.

## Non-goals
- Do not hide genuine reviewer findings.
- Do not fake Claude/Gemini success when a provider is quota-blocked.
- Do not change product code for this tooling task.