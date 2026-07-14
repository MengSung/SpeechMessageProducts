# X02B Observability, Health And Logging Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X02B
Workspace: X02B-observability-health-logging
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: f7950b8c7f9e831e7c92a2419e48ae629c9eb509c25da218bfb8b0cd598283a4

## Executive Summary

Static review did not confirm a production X02B defect. Six observations require
runtime validation because diagnostics and session monitoring are DEBUG-only,
Trace logging is configuration-gated, FileLogger registration was not proven,
and the health threshold intent is unknown. Two positive/no-action observations
are kept out of ranked issues.

## Ranked Confirmed Issues

No X02B issue is confirmed by the current static evidence.

## Runtime Validation Pending

### X02B-SEC-001 DEBUG diagnostics may expose sensitive session and identity detail

- Confirmed: false
- Evidence: `DiagnosticsController.cs:46-49` is DEBUG-only and authorized, while
  `DiagnosticsController.cs:92-178` returns session ID, selected session values,
  user/IP fields, trace ID, and identity-audit records.
- Required validation: prove Release exclusion and verify acceptable masking and
  access control in an isolated DEBUG host.

### X02B-SEC-002 Custom logger providers lack a proven redaction boundary

- Confirmed: false
- Evidence: `TraceLoggerProvider.cs:78-109` and
  `FileLoggerProvider.cs:43-79` write formatted state and exception details;
  Trace is configuration-gated and FileLogger registration was not proven.
- Required validation: register each active provider in a controlled host and
  assert token, cookie, password, session, user, phone, and email values are masked.

### X02B-PERF-001 Health memory threshold may diverge from deployment configuration

- Confirmed: false
- Evidence: `Startup.cs:356-370` hard-codes 2048 MB while
  `appsettings.Production.json:29-50` contains memory/health configuration.
- Required validation: confirm the intended `/health` contract and whether X04A
  configuration should drive the threshold.

### X02B-PERF-002 FileLogger would serialize synchronous per-record file writes

- Confirmed: false
- Evidence: `FileLoggerProvider.cs:72-75` calls `File.AppendAllText` under a shared
  lock, but normal startup registration was not found.
- Required validation: first prove a reachable registration and representative log
  volume; only then measure request blocking and allocation/I/O cost.

### X02B-EXT-001 X02B lacks executable operational contract tests

- Confirmed: false
- Evidence: no targeted tests were found for logger redaction, health/diagnostic
  response, provider/hosted-service lifecycle, or session-monitor behavior.
- Required validation: define isolated component tests for these owner contracts
  before extracting X02B host surfaces.

### X02B-EXT-002 Diagnostics performance response needs ownership validation

- Confirmed: false
- Evidence: `DiagnosticsController.cs:195-230` exposes coarse memory/thread/runtime
  and GC counters; X02B excludes X02C request profiling.
- Required validation: keep this response limited to coarse operational health and
  prove no request-profiler behavior or production exposure.

## Deleted Or Rejected Candidates

- X02B-PERF-003 DEBUG-only `SessionMonitoringMiddleware`: no confirmed production
  cost because registration is under DEBUG composition.
- X02B-EXT-003 legacy Trace internals: no action in X02B; X02Q owns legacy Trace
  context while X02B owns only the active host provider boundary.
- `ResetAudit()` unauthenticated mutation: rejected because class authorization and
  action anti-forgery protections apply in a DEBUG-only controller.

## Cross-Module Handoffs

- X04A owns health/logging configuration values; X02B consumes the validated
  contract.
- X02C owns request profiling; X02B retains coarse health and operational signals.
- X02Q owns legacy Trace quarantine.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; run
`20260711-174320-x02b-issue-review-r1-reviewer` returned
`completedBackends=[]` and no usable reviewer findings.
