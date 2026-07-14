Active task: .trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization

You are the read-only CCG-pending audit peer. Do not edit files, do not invoke
Gemini/Claude/CCG, do not build or test, and do not spawn agents.

Inspect the ledger, the 17 `DEGRADED_REVIEW_PENDING` workspaces, their
`issue.md`, `review-log.md`, prompt files, run folders, and `summary.json`.

Return:

1. The authoritative 17-module list.
2. For each module, whether any prior backend output is usable for the current
   issue content and why.
3. The exact prompt/run artifact to reuse or regenerate.
4. Any issue-document status/schema defect that blocks a trustworthy retry.
5. A safe sequential retry order and batching recommendation under provider
   quota/session limits.
6. The exact evidence required to promote to `APPROVED_DEGRADED` or retain a
   truthful blocked disposition.
