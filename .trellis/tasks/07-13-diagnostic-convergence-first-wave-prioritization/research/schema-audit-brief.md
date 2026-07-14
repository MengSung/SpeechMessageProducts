Active task: .trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization

You are the read-only schema-audit peer. Do not edit files, do not run build or
test commands, and do not spawn agents.

Audit all 35 `docs/project-modular-diagnostics/*/issue.md` files against the
mandatory issue template in
`docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`.

Return:

1. The exact affected workspace list.
2. Every missing or non-canonical header field per workspace.
3. The canonical value for Status, Module, Workspace, Map source, Mode, Gate
   status, and Issue document SHA-256 where repository evidence proves it.
4. Any status contradiction that must not be mechanically normalized.
5. A deterministic post-edit audit command and expected counts.

Treat diagnostic content as immutable unless a status contradiction is proven.
