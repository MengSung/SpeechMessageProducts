# Repository-Wide Module Diagnostic Execution Design

## Authoritative Contracts

- Ownership map:
  `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- Diagnostic workflow:
  `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- Persistent run ledger:
  `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/diagnostic-run-ledger.md`

## Execution Model

Each of the 35 leaf workspaces receives exactly one fresh Diagnostic Subagent.
The agent starts without inherited conversation context, may not spawn nested
agents, and owns only its fixed workspace plus uniquely named CCG run artifacts.

The Diagnostic Subagent performs the security, performance, and extraction
analysis itself, produces `issue.md`, runs the project CCG self-healing reviewer,
and resolves verdicts until it reaches a valid terminal or pending state.

Lead Codex is only the dispatcher and process verifier. It does not investigate
module code or author diagnostic findings.

## Concurrency

At most two workspace agents run concurrently. Each pair owns disjoint
workspace paths and uses module-prefixed CCG run titles. A new pair starts only
after both prior agents are complete, their outputs are checked, and the agents
are closed.

If an agent needs corrections, Lead Codex reuses that same agent through
follow-up input. It does not spawn a second agent for the same workspace.

## Write Boundary

Workspace agents may write only:

- their own `docs/project-modular-diagnostics/<fixed-workspace>/**`;
- their own module-prefixed `.ccg/dual-model-runs/**` artifacts.

All product source, configuration, project, solution, CI, test, map, workflow,
other workspace, CCG task, and Trellis task files are read-only to workspace
agents.

## Acceptance Boundary

Lead Codex checks only:

- required workspace files exist and are non-placeholder;
- `review-log.md` records one agent and nested-agent count `0`;
- `issue.md` has a valid final or pending status;
- referenced CCG summary/output artifacts exist and backend state is truthful;
- no new writes occurred outside allowed documentation/CCG paths.
