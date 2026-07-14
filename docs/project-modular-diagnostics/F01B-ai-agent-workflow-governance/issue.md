# F01B AI Agent and Development Workflow Governance Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F01B
Workspace: F01B-ai-agent-workflow-governance
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Issue document SHA-256: 179f7161db559506b8465112726fe2187769f491f15d6bc8516553bd25d3896c

## Executive Summary

Five confirmed issues survived source reopening. The highest-value finding is
that tracked CCG history contains at least nine distinct 172-character LINE
bearer-token-shaped values across ten matches and full reviewer payloads, while
the runner deliberately persists prompts, stdout, stderr, local paths, and
toolchain metadata without redaction.
The active-task resolver can also adopt another session's task whenever context
identity is missing and exactly one runtime pointer exists; this behavior was
observed in this diagnostic session.

The remaining issues concern tracked ephemeral caches and review transcripts,
unbounded lifecycle hook execution, and duplicated active-task resolution
across Python and OpenCode JavaScript. No issue requires runtime validation to
establish its current diagnosis.

## Ranked Confirmed Issues

### F01B-SEC-001 Tracked CCG Artifacts Retain Credential Material And Operator Metadata

- Category: Security
- Severity: High
- Priority: P0
- Priority score: 86
- Confirmed: true
- Evidence confidence: 20
- Impact score: 25
- Likelihood/frequency score: 15
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F01B
- Cross-module: F01A Git-history response; F01C CCG runner redaction/retention
- Gate blocked: false
- Files:
  - `.ccg/tasks/archive/2026-07/line-messaging-sdk-p0-fixes/review-gemini-after-url-helper-fix.txt:237`
  - `.ccg/dual-model-runs/annotate-richmenu-cs-files-review-input.md:31`
  - `.ccg/dual-model-runs/annotate-richmenu-cs-files-review-input.md:33`
  - `.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json:4`
  - `.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json:7`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:592`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:607`
- Evidence: An encoding-aware, value-redacted scan found ten LINE
  bearer-token-shaped matches on the tracked archived review line. The token
  bodies are 172 characters; SHA-256 comparison found nine distinct bodies,
  with one body appearing twice. The file has a UTF-16LE BOM and mixed byte
  content, so line 237 is based on `Get-Content -Encoding Unicode` reopening.
  Tracked review inputs begin a complete Git diff at line 31. A tracked summary
  records the absolute repository path, user-profile wrapper path, npm path,
  and Python installation paths. The CCG runner writes every backend prompt,
  stdout, and stderr verbatim; no redaction step was found. `.ccg` has no local
  ignore or retention policy.
- Control/data/lifetime flow: Task prompt or Git diff -> CCG backend prompt and
  response -> `.ccg/dual-model-runs` or `.ccg/tasks` -> Git commit/history ->
  every clone and repository reader.
- Impact: Credential material that was removed from product source remains
  recoverable from governance history, and each review can persist additional
  source, machine, session-limit, and operator metadata. Current token validity
  is not claimed, but repository disclosure is already confirmed and deletion
  from the working tree alone cannot remove committed history.
- Why this is necessary: F01B owns the generated review store and must define
  what may be persisted, redacted, ignored, archived, and committed.
- Recommended action: Immediately classify and rotate/revoke the exposed LINE
  tokens; add pre-write secret redaction and metadata minimization; make
  raw prompts/stdout/stderr local-only by default; persist a compact approved
  summary when audit evidence is needed; add secret scanning; coordinate an
  F01A history-remediation decision.
- Validation: Secret scanner finds no high-entropy bearer values in the new
  artifact policy; a fixture prompt containing a fake token is redacted before
  disk write; a fresh review produces only allowed durable records; repository
  history response is documented.
- Rollback boundary: F01B storage/ignore policy, F01C runner redaction, token
  rotation, and F01A history cleanup are separate reversible owner changes.
- Extraction contract: CCG review request -> redaction/classification policy ->
  local raw run store -> durable minimal audit summary -> retention/purge job.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude REWRITE; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01B-SEC-002 Missing Context Identity Falls Back To Another Session's Sole Active Task

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 13
- Security urgency score: 12
- Performance gain score: 0
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01B
- Cross-module: false
- Gate blocked: false
- Files:
  - `.trellis/scripts/common/active_task.py:468`
  - `.trellis/scripts/common/active_task.py:475`
  - `.trellis/scripts/common/active_task.py:490`
  - `.trellis/scripts/common/active_task.py:497`
  - `.trellis/scripts/common/active_task.py:519`
  - `.opencode/lib/trellis-context.js:135`
  - `.opencode/lib/trellis-context.js:156`
  - `.opencode/lib/trellis-context.js:169`
  - `.opencode/lib/trellis-context.js:197`
- Evidence: Both implementations deliberately use the only session runtime file
  when no matching context key resolves. During this run, the per-turn workflow
  hook reported `no_task`, while an identity-free CLI resolution returned
  `session-fallback:codex_019f4af0-6343-7792-bdd5-d582429bae84` and adopted
  `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization`.
- Control/data/lifetime flow: Shell/subagent lacks session identity -> resolver
  enumerates `.trellis/.runtime/sessions` -> exactly one unrelated pointer is
  selected -> task requirements, status, and write expectations are injected
  into the wrong execution context.
- Impact: A new window, subagent, or unsupported host can inherit another
  session's task and act under the wrong scope. The guard for two or more files
  prevents ambiguous selection but does not prove ownership when one file
  exists.
- Why this is necessary: Session-scoped task state is a governance isolation
  boundary. Inferring identity from repository cardinality violates that
  boundary and produced conflicting state in the current session.
- Recommended action: Fail closed when context identity is missing. Require an
  explicit inherited `TRELLIS_CONTEXT_ID`, platform-provided session key, or a
  short-lived command ticket bound to the caller and requested operation.
  Expose a deliberate `--use-sole-session` escape hatch only for interactive
  recovery, with a warning naming the selected session.
- Validation: Fixture tests cover zero, one, and multiple runtime files with and
  without explicit context keys; unsupported/no-identity callers return
  `source=none`; parent-authorized subagents resolve only the inherited key.
- Rollback boundary: Keep the current fallback behind an explicit temporary
  compatibility flag while each platform adapter migrates.
- Extraction contract: Platform identity input -> canonical resolver -> exact
  session pointer or fail-closed result -> hook/CLI/subagent consumers.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01B-EXT-001 Active-Task Resolution Is Duplicated Across Python And OpenCode

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 19
- Impact score: 20
- Likelihood/frequency score: 12
- Security urgency score: 5
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01B
- Cross-module: false
- Gate blocked: false
- Files:
  - `.trellis/scripts/common/active_task.py:380`
  - `.trellis/scripts/common/active_task.py:468`
  - `.trellis/scripts/common/active_task.py:497`
  - `.opencode/lib/trellis-context.js:98`
  - `.opencode/lib/trellis-context.js:135`
  - `.opencode/lib/trellis-context.js:165`
  - `.opencode/plugins/inject-workflow-state.js:64`
- Evidence: OpenCode independently reimplements context-key construction,
  runtime-file lookup, stale detection, and the single-session fallback, with
  comments stating it mirrors the Python resolver. Claude, Gemini, and Codex
  hooks call the Python implementation, so a session-isolation correction must
  be implemented and verified in two governance cores.
- Control/data/lifetime flow: Platform event -> platform-specific resolver
  implementation -> active task result -> workflow-state injection. Shared
  semantics are duplicated before the platform adapter boundary.
- Impact: Security and behavior fixes can drift by host. The duplicated unsafe
  fallback in F01B-SEC-002 demonstrates that the duplication covers policy, not
  only syntax adaptation.
- Why this is necessary: Active-task identity is a small, security-relevant
  governance contract with multiple consumers and should have one executable
  definition.
- Recommended action: Add a platform-neutral JSON CLI/API that accepts
  normalized platform input and returns `{taskPath, source, stale}`. Keep Python
  as the canonical resolver and make OpenCode a thin process adapter, or
  generate both implementations from shared conformance fixtures.
- Validation: Run the same fixture matrix against Python and every adapter;
  require byte-equivalent normalized results for identity, fallback, stale, and
  malformed-state cases.
- Rollback boundary: Preserve the current OpenCode resolver behind an adapter
  feature flag until conformance tests pass.
- Extraction contract: normalized platform/session input -> one resolver ->
  structured active-task result -> Claude/Codex/Gemini/OpenCode adapters.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01B-PERF-001 Generated Review History And Serena Caches Are Versioned As Governance Source

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 71
- Confirmed: true
- Evidence confidence: 20
- Impact score: 14
- Likelihood/frequency score: 15
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F01B
- Cross-module: F01A history cleanup
- Gate blocked: false
- Files:
  - `.serena/.gitignore:1`
  - `.serena/cache/csharp/document_symbols.pkl:1`
  - `.serena/cache/csharp/raw_document_symbols.pkl:1`
  - `.trellis/scripts/common/session_context.py:126`
  - `.trellis/scripts/common/session_context.py:129`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:593`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:607`
- Evidence: HEAD tracks 1,048 files and 14.43 MiB under
  `.ccg/dual-model-runs`, `.ccg/tasks`, and `.serena/cache`. The two Serena
  cache files total about 7.55 MiB despite `/cache` in `.serena/.gitignore`;
  three commits have stored successively larger cache versions. CCG history
  contains 831 tracked files: 71 tracked run directories plus 109 tracked
  root-level prompt/task files. Trellis context
  collection executes both `git status --porcelain` and `git status --short`;
  a current one-shot measurement was about 237 ms for full context and 45 ms
  for status, but no causal latency allocation is claimed.
- Control/data/lifetime flow: Tool run/index refresh -> generated cache,
  prompt, stdout, stderr, or summary -> Git index/history -> clone, checkout,
  status, backup, review, and context-collection work.
- Impact: Ephemeral data grows durable repository history, adds binary churn,
  expands every clone and audit surface, and makes normal Git state noisier.
  The exact interactive latency contribution needs measurement after cleanup,
  but the storage and history cost are static and current.
- Why this is necessary: Governance source, durable audit summaries, local
  runtime state, caches, and raw model transcripts have different retention
  requirements and should not share one versioning policy.
- Recommended action: Stop tracking Serena caches; make CCG raw run directories
  local/retained outside Git by default; define a compact durable review record;
  add size/file-count budgets and pruning; coordinate F01A history cleanup when
  the benefit justifies it.
- Validation: Fresh checkout regenerates Serena cache; normal review leaves only
  the approved durable summary; repository checks reject tracked cache/raw-run
  paths; before/after clone size, status time, and context time are recorded.
- Rollback boundary: Ignore/untrack policy, artifact migration, and history
  rewrite are independent changes.
- Extraction contract: Durable governance source | durable audit summary |
  local runtime/cache | external archive, each with explicit owner and TTL.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

### F01B-PERF-002 Task Lifecycle Hooks Execute Shell Commands Without A Timeout

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 8
- Security urgency score: 4
- Performance gain score: 8
- Loop leverage score: 6
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F01B
- Cross-module: false
- Gate blocked: false
- Files:
  - `.trellis/config.yaml:39`
  - `.trellis/config.yaml:41`
  - `.trellis/scripts/common/task_utils.py:218`
  - `.trellis/scripts/common/task_utils.py:238`
  - `.trellis/scripts/common/task_utils.py:240`
  - `.trellis/scripts/common/task_utils.py:249`
  - `.trellis/scripts/task.py:118`
  - `.trellis/scripts/common/task_store.py:367`
  - `.trellis/scripts/common/task_store.py:461`
- Evidence: Repository-configured lifecycle commands run with `shell=True`,
  captured output, and no `timeout`. They execute after create/start/finish/
  archive. The current config has only commented examples, so the defect is
  dormant until hooks are enabled; the execution behavior itself is static and
  confirmed.
- Control/data/lifetime flow: Versioned config command -> task lifecycle event
  -> shell process -> parent CLI waits without a deadline -> task transition or
  archive command cannot return.
- Impact: A hung hook can stall the workflow indefinitely, retain subprocess
  resources, and leave the user uncertain whether task state and auto-commit
  completed. A command string containing secrets would also be printed on
  failure, although no active secret-bearing hook was found.
- Why this is necessary: Lifecycle hooks are an extension boundary and need the
  same bounded execution contract already used by CCG and platform hooks.
- Recommended action: Add per-hook/default timeout, process-tree termination,
  explicit failure policy (`warn`, `block`, `ignore`), command-name-only
  logging, and structured argv support that avoids a shell when possible.
- Validation: Fixture hooks for success, nonzero exit, timeout, child process,
  and sensitive argument redaction; verify task state and archive semantics for
  each failure policy.
- Rollback boundary: Preserve current behavior behind `timeout: 0` only as an
  explicit opt-out.
- Extraction contract: lifecycle event -> validated hook spec -> bounded
  process runner -> structured result -> declared transition policy.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true

## Runtime Validation Pending

None. Current issue confirmation is based on tracked artifacts, executable
control flow, a live active-task resolution observation, and repository object
inventory. Future implementation acceptance measurements are specified in
`evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- CCG runaway retries: rejected. The runner defaults to two attempts, uses a
  900-second backend timeout, a 420-second health timeout, and kills timed-out
  process trees.
- Trellis channel workers are unbounded: rejected. Current configuration sets a
  five-minute idle timeout and six-worker limit.
- Task-name command injection into lifecycle hooks: rejected. Task data is
  passed through `TASK_JSON_PATH`; the executed command comes from trusted
  repository configuration. The remaining issue is the unbounded shell
  extension boundary.
- Serena pickle gives confirmed code execution: rejected. The tracked files are
  opaque pickle caches, but no repository-owned loader or automatic unpickle
  path was proved. Their generated/binary retention remains in PERF-001.
- All duplicated skills need manual consolidation: rejected. Forty-two skill
  files are exact triplicates across `.agents`, `.claude`, and `.opencode`, and
  `.trellis/.template-hashes.json` records generated platform variants. Only
  the independently implemented active-task policy is retained.
- OpenCode `runScript` shell injection: rejected. The helper uses a command
  string but no caller was found.
- Current lifecycle hooks execute malicious commands: rejected. No hooks are
  enabled in `.trellis/config.yaml`.

## Cross-Module Handoffs

1. F01A: decide repository-history cleanup, Git prevention, and durable artifact
   enrollment policy for already committed CCG/cache material.
2. F01C: add CCG pre-write redaction, metadata minimization, retention controls,
   and review the runner's persistent User PATH mutation at
   `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:145`.
3. All future F01B implementation tasks: add governance conformance tests before
   changing session resolution or artifact policy.

## Final CCG Approval

Substantive issue verdict: `APPROVED_DEGRADED`.

- Round 2 submitted SHA-256:
  `B8EC4443207846D87E4428F8F39E8AC26829E2CB8B543C95AFFB8ABCDE900110`.
- Run ID: `20260710-190824-f01b-issue-review-r2-reviewer`.
- Summary:
  `.ccg/dual-model-runs/20260710-190824-f01b-issue-review-r2-reviewer/summary.json`.
- Claude reopened the original files and returned KEEP for all five issues,
  with no unresolved Critical or Warning and no reported write side effects.
- Gemini produced no usable output because provider quota/billing returned
  HTTP 403 insufficient balance.
- The Round 2 summary has `degradedFallback=true`,
  `fallbackAccepted=true`, and `quotaBlocked=true`.
- A post-review self-audit clarified F01B-PERF-001: 180 is the tracked
  top-level entry count, comprising 71 tracked run directories and 109 tracked
  root-level files. This narrows the inventory wording without changing the
  reviewed 831-file count, impact, severity, or KEEP disposition.
- Retained: 5. Deleted: 0. Runtime pending: 0. Cross-module handoff groups: 2.
- No Round 3 approval is claimed.
