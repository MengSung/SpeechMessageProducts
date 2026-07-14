# Wave Execution Workflow

## Scope

This workflow applies after the owner activates a specific local workspace
wave. It does not authorize creating a wave, modifying product code, or
expanding the issue subset on its own.

## Directory Contract

For an activated workspace, create exactly:

```text
docs/project-modular-diagnostics/<workspace>/wave_<n>/
  plans.md
  measurements.md
  goals.md
```

The workspace `issue.md` remains the canonical diagnostic source. The three
wave files are the implementation contract for only the selected issue subset.

### plans.md

`plans.md` must state:

- wave ID, workspace, and exact canonical issue IDs;
- allowed product, test, configuration, and consumer paths;
- excluded issue IDs and excluded paths;
- repair approach per selected issue;
- local validation commands and expected evidence;
- rollback boundary for the whole local wave.

### measurements.md

`measurements.md` must state, for each selected issue:

- what is observed and why it represents the issue;
- baseline command, fixture, or reproducible manual procedure;
- sample size or test cases where applicable;
- units and aggregation, such as p95 milliseconds, CRM call count, allocated
  bytes, rejected unauthorized requests, or passing contract cases;
- result capture location and no-regression observation.

### goals.md

`goals.md` is the completion authority. For each selected issue it must state:

- the measurable success target;
- the required behavior that must remain unchanged;
- the local validation result required for success;
- the rollback condition that makes the wave unsuccessful.

For example, a performance goal may require p95 latency at or below a stated
millisecond target and no increase in error rate or CRM calls. A security goal
may require every listed unauthorized case to be rejected while authorized
cases still pass. An extraction goal may require provider and consumer contract
tests to pass without changing observable behavior.

## Per-Workspace Sequence

Each selected workspace runs two subagents in sequence. No subagent may spawn
another agent.

### 1. Planning Subagent

The main session assigns one planning subagent with write access limited to the
workspace's three wave files and its CCG artifacts.

The planning subagent:

1. Reads the owning `issue.md`, the module map, and only the evidence needed to
   define the selected subset.
2. Creates or updates only `plans.md`, `measurements.md`, and `goals.md`.
3. Runs Claude review of those three files.
4. Applies every valid Claude finding to the three files and reruns review until
   approval.
5. If Claude is unavailable or has no usable output, the main session invokes
   one independent, read-only Codex fallback review agent. Gemini is never
   called or probed.
6. Stops with `WAVE_PLAN_APPROVED` only when Claude or the fallback review
   approves the documents without unresolved Critical or Warning findings.

The planning subagent does not modify product code, tests, or existing
diagnostic evidence.

### 2. Zero-Trust Repair Subagent

After `WAVE_PLAN_APPROVED`, the main session assigns a different repair
subagent. It receives the exact workspace, issue subset, wave paths, and file
allowlist from `plans.md`.

The repair subagent:

1. Treats `goals.md` as the outcome contract, `measurements.md` as proof, and
   `plans.md` as the immutable scope boundary.
2. Reads `issue.md` and only source/test/configuration files listed or allowed
   by `plans.md`.
3. Captures the stated baseline before changing product files.
4. Makes the smallest repair needed to satisfy the goals.
5. Runs every listed local validation and captures the stated measurements.
6. Runs Claude review over the wave diff and evidence.
7. Fixes valid review findings and repeats local validation and review until
   approval. If Claude has no usable output, the main session invokes the same
   independent read-only Codex fallback review agent.
8. Commits only after local validation and the Claude or fallback review pass.

The repair subagent may not change `plans.md`, `measurements.md`, or `goals.md`
to weaken a target, broaden scope, or hide a failed measurement. It may not
delegate, create a nested agent, or invoke Gemini.

## Review Fallback

Claude is the sole external reviewer. If its result is unavailable, the main
session starts one Codex review agent through the execution environment. That
fallback agent is read-only and checks the wave documents, allowed diff,
measurements, goals, validation result, and rollback boundary. Its approval is
accepted for the current review gate. Gemini is excluded from both normal and
fallback paths.

## Commit Contract

The repair subagent creates one commit for one local workspace wave. The commit
message body must be Traditional Chinese and include the following fields:

```text
波次: <global Wn / workspace wave_n>
Issue: <canonical issue IDs>
量測: <baseline -> result and target>
驗證: <commands or tests and result>
審核: <Claude 或 Codex fallback approval evidence>
回退: <paths or commit-level rollback boundary>
```

## Global Wave Completion

The main session processes selected workspaces sequentially. It records each
workspace as committed, blocked by an unmet prerequisite, or stopped by a
failed goal. It starts the next workspace only after the current workspace has
a truthful terminal result. A global wave is complete only after every selected
workspace has a recorded terminal result.
