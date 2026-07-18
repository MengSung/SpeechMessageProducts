# W1 F01B AI agent workflow governance

## Goal

Repair the five approved F01B governance issues through one measured,
reversible Wave 1 contract, without adopting another session's task, tracking
raw CCG/cache data, or allowing lifecycle hooks to block indefinitely.

## Confirmed facts

- The exact canonical subset is F01B-SEC-001, F01B-SEC-002,
  F01B-PERF-001, F01B-PERF-002, and F01B-EXT-001; all have a READY module
  gate and no issue-level block.
- F01C owns the CCG writer. F01B owns the generated CCG store and policy;
  F01A alone owns repository-history rewriting and root Git enrollment.
- The current Python and OpenCode resolvers infer the sole local session when
  identity is missing. The current lifecycle hook runner uses `shell=True`
  and has no timeout.
- The user approved execution of the already-reviewed W1 design on 2026-07-13.

## Requirements

- Freeze `wave_1/{plans,measurements,goals}.md` after review; repairs may not
  weaken or edit those three files.
- Keep raw CCG prompts, model output, diagnostics, and Serena cache files out
  of the Git index while retaining a compact, redacted durable summary.
- Make active-task resolution fail closed without an explicit identity; provide
  a deliberate, warned recovery path for a sole-session selection and prove
  Python/OpenCode conformance with the same fixtures.
- Bound lifecycle hook execution with a default/per-hook timeout, process-tree
  termination, a documented warn/block/ignore policy, and command-name-only
  failure logging.
- Restrict source/test writes to the allowlist in `wave_1/plans.md`; do not
  alter root Git policy, F01C runner redaction, product code, or diagnostic
  source records.

## Acceptance criteria

- [ ] The five selected IDs have baseline evidence, measurable results,
  no-regression evidence, and a rollback boundary in the frozen wave contract.
- [ ] A missing identity returns `source=none`; only explicit recovery selects
  a sole session, and Python/OpenCode fixture results are equivalent.
- [ ] Hook fixtures prove success, nonzero, timeout, child-process cleanup,
  and each failure policy without disclosing command arguments.
- [ ] The index contains no `.ccg/dual-model-runs/**` or `.serena/cache/**`
  path, and the recurrence scanner rejects a staged return of either class.
- [ ] Local validation and Claude-only or read-only Codex fallback review pass
  before a Traditional Chinese commit body is created.

## Out of scope

- F01A history rewrite, root `.gitignore`, CI enrollment, and token rotation.
- F01C CCG pre-write redaction, metadata minimization, and persistent PATH
  repair.
- Any product, deployment, database, UI, or test-subject change outside the
  explicit F01B allowlist.
