# ChurchReport Three-File Trace Analysis Report

- Generated: 2026-08-19 10:47:25 +08:00
- Overall status: **WARN**
- Slow-request threshold: 1000 ms
- Pair/aggregation memory bound: 100,000 entries. Overflow is WARN and cannot become a false PASS.

## Executive Summary

| File | Status | Lines | Size (bytes) | Time range |
|---|---|---:|---:|---|
| Dataverse JSONL | **WARN** | 0 | 0 | n/a |
| Trace.log | **WARN** | 0 | 0 | n/a |
| CHURCH_REPORT_TRACE.TXT | **WARN** | 0 | 0 | n/a |

## File Inventory and Read-Only Contract

| File | Path | Exists | Last modified |
|---|---|---|---|
| Dataverse JSONL | `D:\除錯追蹤\analyzer-missing-input-20260819\dataverse-trace.jsonl` | False | n/a |
| Trace.log | `D:\除錯追蹤\analyzer-missing-input-20260819\Trace.log` | False | n/a |
| CHURCH_REPORT_TRACE.TXT | `D:\除錯追蹤\analyzer-missing-input-20260819\CHURCH_REPORT_TRACE.TXT` | False | n/a |

All inputs are streamed with `FileMode.Open + FileAccess.Read + FileShare.ReadWrite/Delete`; the analyzer does not modify source traces.

## Dataverse Management and Isolation (WARN)

- JSONL: 0 lines, 0 parsed, 0 parse errors
- Request pairing: 0 missing end, 0 orphan end
- Lease pairing: 0 missing return, 0 orphan return
- Request duration: 0 samples, n/a ms average, 0 ms maximum
- Acquire wait: 0 samples, n/a ms average, 0 ms maximum, 0 timeouts
- Lease held: 0 samples, n/a ms average, 0 ms maximum
- Pool: 0 health failures, 0 below-MinSize cleanup snapshots, 0 uncleared caller states, 0 dropped events
- Cleanup interpretation: `idleAfter < minSize` is concurrency-sensitive because a request can lease an idle client after cleanup selection and before the trace snapshot. It is reported as an observation, not a violation, unless independent lease/total-count evidence proves cleanup removed too many live clients.
- User isolation: 0 valid pseudonyms, 0 format violations

### Event Counts

| Event | Count |
|---|---:|

- File is missing; the three-file evidence set is incomplete.

- Potential sensitive-data pattern hits: 0

## Application and Performance Trace.log (WARN)

- `[Perf]` 0, `[Perf-N+1]` 0, `[Perf-Gap]` 0, `[Perf-Startup]` 0
- Slow requests 0, startup maximum 0 ms, error/exception 0, warning 0

### Slowest Endpoints (Top 20; query, GUID, and long numbers are masked)

| Endpoint | Hits | Avg total ms | Max total ms | CRM calls | CRM ms | Max crm.n | Avg gap ms | Max gap ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|

- File is missing; the three-file evidence set is incomplete.

- Potential sensitive-data pattern hits: 0

## Legacy ToolUtility Trace (WARN)

- Encoding: Big5 (code page 950); 0 lines; 0 StringToProcess entries; 0 error indicators

### Common Safe Categories (Top 20; message text is omitted)

| Category | Count |
|---|---:|

- File is missing; the three-file evidence set is incomplete.

- Potential sensitive-data pattern hits: 0

## Cross-File Correlation (WARN)

- At least one file has no recognizable time range; full event alignment is unavailable.
- The analyzer does not guess traceId/endpoint relationships from fuzzy text. Without a shared correlation id, only time-range and aggregate correlation is possible.

## Recommendations and Limitations

- FAIL: repair pairing, pool isolation, parsing, or sensitive-data issues before collecting a new trace.
- WARN: collect all three files from one Debug reproduction and inspect slow endpoints, N+1, Gap, timeout, and dropped indicators.
- This report alone cannot prove absence of memory/session leakage. Release still requires concurrent A/B isolation, handle-release, soak, and resource-baseline checks.
- Files may be appended during analysis. The report is a readable snapshot and may not include later events.
- Sensitive-pattern scanning is conservative. Verify hits in the source environment; raw matching text is intentionally never retained.
