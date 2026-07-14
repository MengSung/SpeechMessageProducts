# Optimization Blueprint Design

## Objective

Turn the completed 35-workspace diagnostic program into a five-wave
optimization blueprint without authorizing product source changes.

## Authorities

- Workspace `issue.md`: canonical diagnosis, score, gate, and evidence.
- `optimization-issue-inventory.csv`: complete cross-workspace register.
- `optimization-blueprint.md`: global wave placement and activation order.
- `wave-execution-workflow.md`: local wave documentation and execution rules.
- `<workspace>/wave_<n>/{plans,measurements,goals}.md`: activated local-wave
  implementation contract.

## Wave Boundary

Global W1-W5 preserve the ownership/dependency ordering already documented in
the module map. A local wave exists in one primary-owner workspace. Consumer
paths can be listed in `plans.md`, but the wave has no ownerless cross-module
directory. Empty local waves are forbidden.

## Execution Model

For every selected workspace, first use one planning subagent restricted to the
three local wave documents, then use one separate zero-trust repair subagent
restricted by `plans.md`. Workspaces run sequentially inside a global wave;
nested delegation is prohibited.

Claude is the sole external reviewer. If it yields no usable output, the main
session uses one independent, read-only Codex review agent. Gemini is excluded
from all wave review paths. A repair commits only after local verification and
Claude or fallback approval.

## Initial Activation

The first global wave has 12 draft candidates in F01B, F01C, and F01D. No local
wave directory is created until its three-file contract is approved.
