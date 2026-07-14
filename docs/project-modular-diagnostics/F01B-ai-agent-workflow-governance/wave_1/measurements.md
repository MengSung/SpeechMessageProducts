# F01B Wave 1 Measurements

All commands run from the repository root. Baselines are captured before source
edits; result rows are appended by the repair owner without changing the
definitions below.

| Issue | Observation and baseline | Unit/sample | Pass evidence |
|---|---|---|---|
| F01B-SEC-001 | Run `python .trellis/scripts/governance_artifacts.py --scan-fixture` with a fake 172-character bearer token, then `--check-index`. Record durable-token matches and disallowed tracked paths. | Counts; one synthetic fixture plus the full index | 0 durable token matches; 0 tracked raw CCG-run paths; no real token is printed. |
| F01B-SEC-002 | Build temporary runtime-session fixtures for zero, one, and multiple sessions, with and without an explicit key or `--use-sole-session`. | 8 deterministic cases | No identity yields `taskPath=null, source=none`; only explicit recovery returns `session-fallback`. |
| F01B-PERF-001 | Record `git ls-files .ccg/dual-model-runs .serena/cache | Measure-Object`, tracked-byte sum, and five samples each of `git status --short` and `get_context.py`. | File count, bytes, median milliseconds | Raw/cache indexed count is 0; timing result is recorded without claiming a causal target. |
| F01B-PERF-002 | Use fixtures for successful command, exit 7, timed-out parent with child process, and each failure policy. | 5 deterministic fixtures; seconds and child PID liveness | Default timeout is enforced; child is gone; warn/block/ignore result matches the fixture contract; logs omit arguments. |
| F01B-EXT-001 | Serialize the Python CLI and OpenCode results for the SEC-002 fixture matrix and byte-compare normalized `taskPath`, `source`, and `stale`; separately exercise the explicit `Active task:` dispatch hint. | 8 normalized JSON cases plus one hint case | All corresponding results match exactly; the hint resolves only as `prompt-hint`; OpenCode contains no independent sole-session scan. |

## Result capture

The repair owner records commands, timestamps, counts, and medians in the
workspace terminal record in `optimization-blueprint.md`. Raw fixture output
stays in the ignored local CCG directory; only redacted counts and verdicts are
durable.
