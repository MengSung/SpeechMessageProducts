# F01B Runtime Validation Plan

Status: NOT_REQUIRED_FOR_DIAGNOSTIC_APPROVAL
Mode: DIAGNOSIS_ONLY
Runtime-pending issue count: 0

All retained issues have sufficient static or directly observed evidence.
The procedures below are implementation acceptance gates, not unresolved
diagnostic hypotheses.

## F01B-SEC-001 Artifact Redaction And Retention

- Method:
  1. Use fixture prompts containing fake LINE-style bearer tokens, fake API
     keys, local user paths, and provider diagnostics.
  2. Run the CCG writer in an isolated repository.
  3. Scan raw and durable outputs before any Git add.
- Required environment: disposable worktree, fake credentials only, both
  successful and failed backend fixtures.
- Safety restrictions: never use production tokens; do not print matched secret
  values; report fingerprints/counts only.
- Success threshold:
  - zero unredacted fixture secrets in durable files;
  - raw local files are ignored or externalized;
  - summaries omit user home and unnecessary toolchain paths;
  - recurrence gate blocks raw-run/cache commits.
- Failure threshold: any fixture secret or disallowed local metadata appears in
  a durable tracked artifact.
- Executor: future F01B implementation owner plus F01C runner owner.
- Verdict effect: confirms implementation closure; does not change current KEEP.

## F01B-SEC-002 And F01B-EXT-001 Session Resolver Conformance

- Method: run the same resolver fixtures through Python, Claude, Codex, Gemini,
  and OpenCode adapters.
- Required cases:
  - explicit valid context key;
  - missing key with zero/one/multiple session files;
  - stale and malformed session files;
  - parent-authorized subagent key;
  - Cursor short-lived ticket;
  - task from another session.
- Safety restrictions: temporary directories and fake task paths only.
- Success threshold: no missing-identity caller selects a session implicitly;
  all adapters return the same normalized result.
- Failure threshold: any adapter returns another session's task without explicit
  authorization or diverges from the canonical result.
- Executor: future F01B implementation owner.
- Verdict effect: validates SEC-002/EXT-001 closure.

## F01B-PERF-001 Repository And Context Budget

- Method: record fresh-clone bytes/time, checkout time, status time,
  `get_context.py` time, tracked generated file count, and tracked generated
  bytes before and after cleanup.
- Required data: cold and warm filesystem runs, same commit content, at least
  five samples for timing medians.
- Safety restrictions: history rewrite only in a disposable clone until F01A
  approves migration.
- Success threshold:
  - Serena cache and raw CCG runs regenerate locally;
  - tracked generated subset stays within the approved file/byte budget;
  - no regression in context correctness.
- Failure threshold: required audit evidence is lost or normal workflows
  recreate tracked raw paths.
- Executor: F01B owner with F01A Git-governance approval.
- Verdict effect: validates PERF-001 closure.

## F01B-PERF-002 Lifecycle Hook Boundaries

- Method: fixture hooks for success, nonzero exit, timeout, process-tree child,
  large stdout/stderr, and sensitive arguments.
- Required environment: disposable task directory and cross-platform process
  fixtures.
- Safety restrictions: no destructive commands; fake secrets only.
- Success threshold:
  - configured deadline enforced;
  - child processes terminated;
  - declared warn/block/ignore policy honored;
  - command output bounded and sensitive arguments redacted;
  - task/archive state remains deterministic.
- Failure threshold: parent command hangs, child survives, or transition state
  disagrees with the configured policy.
- Executor: future F01B implementation owner.
- Verdict effect: validates PERF-002 closure.

## Current Approval Effect

No retained issue is marked `NEEDS_RUNTIME_VALIDATION`. This plan therefore does
not block `APPROVED` or `APPROVED_DEGRADED` if CCG reviewers KEEP the issues and
all reviewer Critical/Warning findings are resolved.
