# F01B Security Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Method

The analysis reopened executable hooks, task/session resolvers, CCG artifacts,
runner write paths, ignore rules, and representative archived reviews. Secret
values were counted and redacted rather than copied. Findings were tested
against ownership, source/sink, guard, lifetime, and reachable control flow.

## Confirmed Finding S1: Raw CCG Retention Preserves Credential Material

### Source And Sink

- Source: complete Git diffs and model prompts under `.ccg`.
- Sink:
  `.ccg/tasks/archive/2026-07/line-messaging-sdk-p0-fixes/review-gemini-after-url-helper-fix.txt:237`
  contains ten bearer-token-shaped matches with 172-character bodies. A
  value-redacted SHA-256 comparison found nine distinct bodies, with one body
  appearing twice.
- Additional retained payload:
  `.ccg/dual-model-runs/annotate-richmenu-cs-files-review-input.md:31-33`
  begins a complete source diff.
- Local metadata:
  `.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json:4-14`
  records absolute repository, wrapper, npm, and Python paths.

### Reachable Flow

`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:592-607` creates backend
prompts and writes the prompt, stdout, and stderr verbatim. Lines 507-515 and
688-710 persist local paths and toolchain state into summary files. No redaction
or retention filter was found before these writes.

### Guard And Counter-Evidence

- The workflow requires artifacts for auditability, but does not require raw
  credentials or complete operator paths.
- The token values may already be revoked. Current validity was not tested
  and is not required to confirm that credential material is committed.
- The raw values were not copied into any diagnostic file.
- No `.ccg/.gitignore` or equivalent raw/durable partition was found.
- The archived review has a UTF-16LE BOM and mixed byte content. The cited line
  was reopened with `Get-Content -Encoding Unicode`, which produced 270 lines
  and placed the 31,283-character diff payload at line 237. Other decoding
  modes can produce misleading line numbers.

### Ownership

The generated store is F01B. The writer is F01C, so remediation needs separate
owner changes. Git history response belongs to F01A.

Disposition: retained as F01B-SEC-001.

## Confirmed Finding S2: Sole-Session Fallback Violates Session Ownership

### Source And Boundary

`.trellis/scripts/common/active_task.py:468-519` and
`.opencode/lib/trellis-context.js:135-199` select the sole runtime session file
when the caller has no resolvable context key.

### Reachable Flow

No caller identity -> enumerate `.trellis/.runtime/sessions/*.json` -> if count
is one, read `current_task` -> return `source=session-fallback:<other-key>` ->
inject or report that task to hooks, CLIs, and agents.

### Live Observation

With `TRELLIS_CONTEXT_ID` removed, the current worktree resolved:

```text
source=session-fallback:codex_019f4af0-6343-7792-bdd5-d582429bae84
task=.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization
stale=False
```

The same turn's workflow injection reported `no_task`. This proves that task
ownership differs between hook and identity-free CLI contexts.

### Guard And Counter-Evidence

- Zero or two-plus session files fail closed.
- Cursor has a stronger short-lived ticket flow at
  `.trellis/scripts/common/active_task.py:351-377`.
- Neither guard proves that the only file belongs to the caller.
- The active task happened to match the user's explicit request, so no wrong
  write occurred in this run.

Disposition: retained as F01B-SEC-002.

## Rejected Security Candidates

### Task Name Injects Lifecycle Shell

Rejected. `.trellis/scripts/common/task_utils.py:236` passes the task path in an
environment variable. The shell command itself comes from versioned
`.trellis/config.yaml`; no task-field interpolation path was found.

### Serena Pickle Is A Confirmed RCE

Rejected. The two tracked `.pkl` files are opaque generated caches, but no
repository-owned automatic unpickle loader was found. Malicious pickle loading
is a valid external-tool concern, not a confirmed control flow in this scope.

### CCG Provider Retry Is Unbounded

Rejected. The runner defaults to two attempts and kills process trees after
bounded timeouts.

### CCG User PATH Mutation Is F01B-Owned

Rejected as ownership, not risk. The runner persists User PATH changes at
`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:145-149`; that source is
F01C and is recorded as a cross-module handoff.

## Required Security Controls

- Secret classification and redaction before artifact writes.
- Raw local run storage separated from durable summaries.
- Token rotation/revocation and Git-history response.
- Fail-closed session resolution without inferred identity.
- Cross-platform conformance tests for session ownership.
- Sensitive command/diagnostic logging minimization at extension boundaries.
