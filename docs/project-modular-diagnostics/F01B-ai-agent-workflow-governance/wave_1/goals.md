# F01B Wave 1 Goals

This file is the completion authority for the frozen F01B Wave 1 contract.
The repair may not modify it to turn a failing result into a passing result.

| Issue | Success target | Required no-regression proof | Failure / rollback condition |
|---|---|---|---|
| F01B-SEC-001 | Durable artifact scan reports zero fake bearer values and zero indexed raw CCG-run paths. | Compact review/task records remain readable without raw payloads. | Any durable secret-shaped fixture, raw path, or required audit verdict missing; revert artifact-policy/untracking changes. |
| F01B-SEC-002 | Missing identity never selects any session; the explicit recovery path is the only source of `session-fallback`. | Valid inherited context keys still resolve their own task, including stale-state reporting. | Any no-identity fixture adopts a task or valid keyed caller changes result; revert resolver/adapter changes. |
| F01B-PERF-001 | Serena cache and raw CCG run paths are absent from the Git index and blocked when reintroduced. | Local generation can still create ignored cache/run files; measurements record baseline/result. | Index check accepts either path or local generation fails; revert policy and index-only removals. |
| F01B-PERF-002 | Every configured hook has a bounded default or explicit timeout, process-tree cleanup, and declared warn/block/ignore result. | Legacy string-list hooks preserve warn semantics unless a structured policy overrides them. | Parent/child remains alive, policy mismatches fixture, or command arguments appear in logs; revert hook runner/config changes. |
| F01B-EXT-001 | Python is the sole active-task policy owner and OpenCode returns fixture-equivalent CLI output. | Existing OpenCode workflow-state injection receives the same `{taskPath, source, stale}` shape; an explicit dispatch hint remains `prompt-hint`. | Adapter diverges, Python CLI errors for valid input, or OpenCode/injector retains independent sole-session fallback logic; revert CLI/adapter changes. |

The wave terminal state is `COMMITTED` only when every target and no-regression
proof passes and Claude-only or the main-session read-only Codex fallback
approves the allowlisted diff. Otherwise it is `BLOCKED` or `FAILED_GOAL` and
the next W1 workspace must not start.
