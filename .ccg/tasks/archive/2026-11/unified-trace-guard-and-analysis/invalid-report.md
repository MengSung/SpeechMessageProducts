# ChurchReport Three-File Trace Analysis Report

- Generated: 2026-08-19 10:39:10 +08:00
- Overall status: **FAIL**
- Slow-request threshold: 1000 ms
- Pair/aggregation memory bound: 100,000 entries. Overflow is WARN and cannot become a false PASS.

## Executive Summary

| File | Status | Lines | Size (bytes) | Time range |
|---|---|---:|---:|---|
| Dataverse JSONL | **FAIL** | 3 | 337 | 2026-08-19 09:00:00.000 to 2026-08-19 09:00:00.010 |
| Trace.log | **PASS** | 1 | 102 | 2026-08-19 09:00:00.100 to 2026-08-19 09:00:00.100 |
| CHURCH_REPORT_TRACE.TXT | **FAIL** | 8 | 218 | 2026-08-19 09:00:00.000 to 2026-08-19 09:00:00.000 |

## File Inventory and Read-Only Contract

| File | Path | Exists | Last modified |
|---|---|---|---|
| Dataverse JSONL | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures-invalid\dataverse-trace.jsonl` | True | 2026-08-19 10:18:13 |
| Trace.log | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures-invalid\Trace.log` | True | 2026-08-19 10:18:13 |
| CHURCH_REPORT_TRACE.TXT | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures-invalid\CHURCH_REPORT_TRACE.TXT` | True | 2026-08-19 10:18:13 |

All inputs are streamed with `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete`; the analyzer does not modify source traces.

## Dataverse Management and Isolation (FAIL)

- JSONL: 3 lines, 2 parsed, 1 parse errors
- Request pairing: 1 missing end, 0 orphan end
- Lease pairing: 1 missing return, 0 orphan return
- Request duration: 0 samples, n/a ms average, 0 ms maximum
- Acquire wait: 0 samples, n/a ms average, 0 ms maximum, 0 timeouts
- Lease held: 0 samples, n/a ms average, 0 ms maximum
- Pool: 0 health failures, 0 below-MinSize cleanup snapshots, 0 uncleared caller states, 0 dropped events
- Cleanup interpretation: `idleAfter < minSize` is concurrency-sensitive because a request can lease an idle client after cleanup selection and before the trace snapshot. It is reported as an observation, not a violation, unless independent lease/total-count evidence proves cleanup removed too many live clients.
- User isolation: 1 valid pseudonyms, 0 format violations

### Event Counts

| Event | Count |
|---|---:|
| `pool.acquire.hit` | 1 |
| `request.begin` | 1 |

- 1 JSONL lines could not be parsed.
- request.begin/request.end events are not fully paired.
- lease acquire/return events are not fully paired.

- Potential sensitive-data pattern hits: 0

## Application and Performance Trace.log (PASS)

- `[Perf]` 1, `[Perf-N+1]` 0, `[Perf-Gap]` 0, `[Perf-Startup]` 0
- Slow requests 0, startup maximum 0 ms, error/exception 0, warning 0

### Slowest Endpoints (Top 20; query, GUID, and long numbers are masked)

| Endpoint | Hits | Avg total ms | Max total ms | CRM calls | CRM ms | Max crm.n | Avg gap ms | Max gap ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `/Fixture/Invalid` | 1 | 15 | 15 | 0 | 0 | 0 | 10 | 10 |

- No explicit violation was detected.

- Potential sensitive-data pattern hits: 0

## Legacy ToolUtility Trace (FAIL)

- Encoding: Big5 (code page 950); 8 lines; 1 StringToProcess entries; 0 error indicators

### Common Safe Categories (Top 20; message text is omitted)

| Category | Count |
|---|---:|
| `[Fixture]` | 1 |

- Potential sensitive-data patterns were found; raw values are omitted.

| Pattern | Matched lines |
|---|---:|
| Sensitive field value | 1 |

## Cross-File Correlation (WARN)

- Recognizable time ranges do not strictly overlap; cross-file causality needs a single controlled reproduction.
- The analyzer does not guess traceId/endpoint relationships from fuzzy text. Without a shared correlation id, only time-range and aggregate correlation is possible.

## Recommendations and Limitations

- FAIL: repair pairing, pool isolation, parsing, or sensitive-data issues before collecting a new trace.
- WARN: collect all three files from one Debug reproduction and inspect slow endpoints, N+1, Gap, timeout, and dropped indicators.
- This report alone cannot prove absence of memory/session leakage. Release still requires concurrent A/B isolation, handle-release, soak, and resource-baseline checks.
- Files may be appended during analysis. The report is a readable snapshot and may not include later events.
- Sensitive-pattern scanning is conservative. Verify hits in the source environment; raw matching text is intentionally never retained.
