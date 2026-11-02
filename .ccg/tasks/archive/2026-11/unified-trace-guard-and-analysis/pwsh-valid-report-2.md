# ChurchReport Three-File Trace Analysis Report

- Generated: 2026-08-19 10:58:28 +08:00
- Overall status: **WARN**
- Slow-request threshold: 1000 ms
- Pair/aggregation memory bound: 100,000 entries. Overflow is WARN and cannot become a false PASS.

## Executive Summary

| File | Status | Lines | Size (bytes) | Time range |
|---|---|---:|---:|---|
| Dataverse JSONL | **PASS** | 10 | 1,326 | 2026-08-19 09:00:00.000 to 2026-08-19 09:00:00.000 |
| Trace.log | **WARN** | 6 | 528 | 2026-08-19 09:00:00.100 to 2026-08-19 09:00:00.400 |
| CHURCH_REPORT_TRACE.TXT | **PASS** | 8 | 206 | 2026-08-19 09:00:00.000 to 2026-08-19 09:00:00.000 |

## File Inventory and Read-Only Contract

| File | Path | Exists | Last modified |
|---|---|---|---|
| Dataverse JSONL | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures\dataverse-trace.jsonl` | True | 2026-08-19 10:28:15 |
| Trace.log | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures\Trace.log` | True | 2026-08-19 10:18:13 |
| CHURCH_REPORT_TRACE.TXT | `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures\CHURCH_REPORT_TRACE.TXT` | True | 2026-08-19 10:18:13 |

All inputs are streamed with `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete`; the analyzer does not modify source traces.

## Dataverse Management and Isolation (PASS)

- JSONL: 10 lines, 10 parsed, 0 parse errors
- Request pairing: 0 missing end, 0 orphan end
- Lease pairing: 0 missing return, 0 orphan return
- Request duration: 1 samples, 80 ms average, 80 ms maximum
- Acquire wait: 1 samples, 10 ms average, 10 ms maximum, 0 timeouts
- Lease held: 1 samples, 20 ms average, 20 ms maximum
- Pool: 0 health failures, 1 below-MinSize cleanup snapshots, 0 uncleared caller states, 0 dropped events
- Cleanup interpretation: `idleAfter < minSize` is concurrency-sensitive because a request can lease an idle client after cleanup selection and before the trace snapshot. It is reported as an observation, not a violation, unless independent lease/total-count evidence proves cleanup removed too many live clients.
- User isolation: 1 valid pseudonyms, 0 format violations

### Event Counts

| Event | Count |
|---|---:|
| `request.begin` | 1 |
| `pool.acquire.wait` | 1 |
| `pool.acquire.hit` | 1 |
| `crm.op` | 1 |
| `pool.return` | 1 |
| `pool.health` | 1 |
| `pool.cleanup` | 2 |
| `pool.dispose` | 1 |
| `request.end` | 1 |

- No explicit violation was detected.

- Potential sensitive-data pattern hits: 0

## Application and Performance Trace.log (WARN)

- `[Perf]` 2, `[Perf-N+1]` 1, `[Perf-Gap]` 1, `[Perf-Startup]` 1
- Slow requests 0, startup maximum 45 ms, error/exception 0, warning 1

### Slowest Endpoints (Top 20; query, GUID, and long numbers are masked)

| Endpoint | Hits | Avg total ms | Max total ms | CRM calls | CRM ms | Max crm.n | Avg gap ms | Max gap ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `/Donation/List` | 1 | 900 | 900 | 12 | 500 | 12 | 350 | 350 |
| `/Home/Index` | 1 | 120 | 120 | 1 | 20 | 1 | 80 | 80 |

- [Perf-N+1] indicators were detected.
- [Perf-Gap] indicators were detected.

- Potential sensitive-data pattern hits: 0

## Legacy ToolUtility Trace (PASS)

- Encoding: Big5 (code page 950); 8 lines; 1 StringToProcess entries; 0 error indicators

### Common Safe Categories (Top 20; message text is omitted)

| Category | Count |
|---|---:|
| `[CRM]` | 1 |

- No explicit violation was detected.

- Potential sensitive-data pattern hits: 0

## Cross-File Correlation (WARN)

- Recognizable time ranges do not strictly overlap; cross-file causality needs a single controlled reproduction.
- The analyzer does not guess traceId/endpoint relationships from fuzzy text. Without a shared correlation id, only time-range and aggregate correlation is possible.

## Recommendations and Limitations

- FAIL: repair pairing, pool isolation, parsing, or sensitive-data issues before collecting a new trace.
- WARN: collect all three files from one Debug reproduction and inspect slow endpoints, N+1, Gap, timeout, and dropped indicators.
- This report alone cannot prove absence of memory/session leakage. Release still requires concurrent A/B isolation, handle-release, soak, and resource-baseline checks.
- Files may be appended during analysis. The report is a readable snapshot and may not include later events.
- Sensitive-pattern scanning is conservative. Verify hits in the source environment; raw matching text is intentionally never retained.
