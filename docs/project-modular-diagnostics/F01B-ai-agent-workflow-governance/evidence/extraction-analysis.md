# F01B Extraction Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Method

The analysis compared platform hooks, skills, template hashes, active-task
resolvers, process runners, and generated-state ownership. A candidate was kept
only when it had a cohesive responsibility, explicit contract, consumers, test
seam, dependency direction, rollback boundary, and optimization-loop value.

## Confirmed Extraction E1: Canonical Active-Task Resolver

### Owning Files

- `.trellis/scripts/common/active_task.py`
- `.opencode/lib/trellis-context.js`
- consumers:
  - `.claude/hooks/session-start.py`
  - `.claude/hooks/inject-workflow-state.py`
  - `.claude/hooks/inject-subagent-context.py`
  - `.codex/hooks/session-start.py`
  - `.codex/hooks/inject-workflow-state.py`
  - `.gemini/hooks/session-start.py`
  - `.gemini/hooks/inject-workflow-state.py`
  - `.opencode/plugins/session-start.js`
  - `.opencode/plugins/inject-workflow-state.js`
  - `.opencode/plugins/inject-subagent-context.js`

### Cohesive Responsibility

Resolve one caller/session identity to one active Trellis task and report source
and stale state without crossing session boundaries.

### Current Duplication

- Python owns environment/platform key resolution, Cursor tickets, runtime
  files, and single-session fallback.
- OpenCode independently mirrors context-key creation, runtime lookup, stale
  detection, and single-session fallback.
- The duplicated policy includes the unsafe fallback retained in
  F01B-SEC-002.

### Proposed Contract

Input:

```json
{
  "platform": "codex|claude|gemini|opencode|...",
  "platformInput": {},
  "environmentContextId": "optional",
  "repositoryRoot": "absolute path"
}
```

Output:

```json
{
  "taskPath": ".trellis/tasks/example or null",
  "source": "session:<key>|ticket:<key>|none",
  "stale": false,
  "diagnosticCode": "optional stable code"
}
```

Dependency direction: platform adapters -> canonical resolver -> runtime session
store. The resolver must not depend on host plugin APIs.

### Test Seam

Temporary runtime-directory fixtures with zero, one, and multiple session
files; explicit and missing context keys; stale task paths; malformed JSON;
Cursor ticket fixtures; equivalent adapter outputs.

### Rollback

Keep existing resolvers behind a compatibility flag until all adapters pass the
same conformance fixtures.

### Loop Leverage

One security fix and one fixture matrix cover every platform. New platforms add
only identity extraction and output adaptation.

Disposition: retained as F01B-EXT-001.

## Companion Boundary: CCG Artifact Store

F01B-SEC-001 and F01B-PERF-001 imply a clean boundary but it is not counted as
a second extraction ISSUE to avoid double-counting.

Proposed responsibility:

- classify raw versus durable records;
- redact secrets and minimize machine metadata;
- store raw runs locally or in an external archive;
- emit a stable compact summary for Git;
- enforce TTL, size budget, and purge.

Consumers: CCG runner, diagnostic workflow, reviewers, F01A repository policy,
and future audit tooling.

## Rejected Extraction Candidates

### Consolidate Every Skill Copy Manually

Rejected. Forty-two files are exact triplicates across `.agents/skills`,
`.claude/skills`, and `.opencode/skills`. The shared hook comment states that
files are written into platform directories at init time, and
`.trellis/.template-hashes.json` records generated variants. Platform copies
are deployment artifacts, not automatically independent sources.

One `trellis-update-spec/SKILL.md` variant differs in platform command wording,
and the template hashes also differ, providing counter-evidence to accidental
drift.

### Extract OpenCode `runScript`

Rejected. `.opencode/lib/trellis-context.js:263-279` is an unsafe-looking shell
helper, but no caller was found. Removal is a cleanup candidate, not a confirmed
reusable boundary.

### Merge All Session-Start Implementations

Rejected as stated. Claude/Gemini copies are identical generated adapters, while
Codex and OpenCode have host-specific lifecycle and persistence requirements.
Only the platform-neutral resolution policy should be centralized.

### Treat All `.ccg` History As One Durable Domain Model

Rejected. Raw prompts, model output, task planning, generated documents, and
approved summaries have different confidentiality and retention contracts.
They should be classified and separated, not wrapped in a larger mixed module.
