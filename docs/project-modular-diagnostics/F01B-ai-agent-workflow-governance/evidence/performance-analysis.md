# F01B Performance Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Method

The analysis inventoried tracked F01B file counts and blob sizes, reopened Git
context collection and process-runner code, measured current one-shot command
times only as context, and separated static cost from claims requiring a
before/after runtime benchmark.

## Confirmed Finding P1: Ephemeral Artifacts Are Durable Repository Payload

### Static Inventory

- `.ccg/dual-model-runs`: 831 tracked files, including 71 tracked run
  directories and 109 tracked root-level prompt/task files, 4.74 MiB total.
- `.ccg/tasks`: 215 tracked files, 57 task/archive directories, 2.14 MiB.
- `.serena/cache`: two tracked binary caches, about 7.55 MiB.
- Combined raw CCG and Serena subset: 1,048 files, 14.43 MiB at HEAD.
- `.serena/.gitignore:1` says `/cache`, but both cache files remain tracked.
- The Serena cache blobs changed in three commits, storing versions around
  6.7-6.9 MiB and 0.94-0.97 MiB in Git history.

### Cost Flow

Review/index generation -> raw outputs or binary cache -> Git index and object
database -> clone/fetch/checkout/status/backup/review/context operations.

### Current Timing Context

One-shot measurements in the dirty worktree:

- `python ./.trellis/scripts/get_context.py`: about 237 ms.
- `git status --porcelain=v1`: about 45 ms.
- `git ls-files -- .ccg`: about 33 ms.

`.trellis/scripts/common/session_context.py:123-138` executes branch, two status
commands, and log collection. The measurements do not isolate artifact cost, so
no latency percentage is claimed.

### Existing Guards

- `.trellis/.gitignore` excludes runtime sessions, temp files, caches, and
  backups.
- `.trellis/scripts/common/safe_commit.py:6-30` documents and prevents a prior
  wide `git add -f .trellis/` failure mode.
- `.serena/.gitignore` now excludes cache, but tracked files bypass ignore.
- No equivalent `.ccg` raw-run retention/ignore policy exists.

Disposition: retained as F01B-PERF-001.

## Confirmed Finding P2: Lifecycle Hooks Have No Execution Deadline

`.trellis/scripts/common/task_utils.py:218-260` runs each configured hook with
`subprocess.run(..., shell=True, capture_output=True)` and no timeout.
Create/start/finish/archive call the runner through:

- `.trellis/scripts/common/task_store.py:367`
- `.trellis/scripts/task.py:118`
- `.trellis/scripts/task.py:136`
- `.trellis/scripts/task.py:160`
- `.trellis/scripts/common/task_store.py:461`

The current configuration contains only commented hook examples, so frequency
is conditional. Once enabled, a hung command blocks its parent task operation
without a deadline or process-tree cleanup.

Disposition: retained as F01B-PERF-002.

## Rejected Or Non-Standalone Candidates

- CCG retries are runaway: rejected. Defaults are `MaxAttempts=2`,
  `TimeoutSeconds=900`; health uses 420 seconds; timeout kills the process tree.
- Trellis channel workers are unbounded: rejected. Current config uses
  `idle_timeout: 5m` and `max_live_workers: 6`.
- Session-start hooks are necessarily too slow: rejected. Hook-level timeouts
  exist, and no before/after attribution proves material user impact.
- Duplicate skill files are a performance issue: rejected. Their size is small
  and most are generated exact copies; maintainability is treated separately.
- Full Git history rewrite is automatically justified: rejected. Current HEAD
  cleanup is clearly useful, but destructive history work needs an F01A cost/
  benefit and coordination decision.

## Measurement Plan For Future Optimization

1. Record clone size/time, checkout time, status time, and `get_context.py`
   time before cleanup.
2. Untrack/regenerate local caches and raw run artifacts in an isolated change.
3. Repeat measurements on warm and cold filesystems.
4. Add repository budgets for tracked generated files, total bytes, and raw CCG
   run paths.
5. Exercise lifecycle hooks that succeed, fail, hang, and spawn children.
