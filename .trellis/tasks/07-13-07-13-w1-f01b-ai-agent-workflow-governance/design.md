# F01B Wave 1 Design

## Boundaries

The wave has three independent controls that share one governance boundary:

1. A durable-artifact policy keeps generated CCG/cache data local and checks
   the Git index before commit.
2. Python is the canonical active-task resolver. OpenCode becomes a thin
   process adapter to its JSON CLI, so the missing-identity policy is defined
   once and exercised through one fixture matrix.
3. Hook specifications are normalized by the Python config reader and executed
   by a bounded process runner. Existing string-list hooks remain compatible;
   structured entries enable timeout and failure policy.

## Data flow

`platform input -> active_task_cli -> {taskPath, source, stale} -> OpenCode
workflow injection` must fail closed when identity is absent. The sole-session
path is available only through an explicit `use_sole_session` input and emits a
warning with the selected session key.

The OpenCode subagent injector may accept the explicit `Active task: <path>`
dispatch hint after exact context lookup. It must not use repository session
cardinality as a third identity source.

`hook config -> normalized HookSpec -> subprocess group -> bounded result ->
warn/block/ignore transition` keeps command contents out of logs. A timeout
terminates the spawned process group before the task command returns.

`CCG/cache generator -> local ignored path -> compact durable record -> index
recurrence scan` separates operational payloads from auditable status.

## Compatibility and rollback

- Existing hook string lists retain the current warn behavior, with the new
  default timeout. `timeout_seconds: 0` is an explicit unlimited legacy optout.
- The temporary sole-session recovery switch is opt-in and defaults false.
- The cache/raw-run untracking commit preserves local files; reverting the
  commit restores index enrollment without deleting those files.
- F01C's writer and F01A history remain separate commits and owner decisions.
